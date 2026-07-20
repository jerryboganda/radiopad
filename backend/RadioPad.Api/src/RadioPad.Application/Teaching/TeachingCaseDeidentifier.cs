using System.Text.RegularExpressions;
using RadioPad.Application.Security;

namespace RadioPad.Application.Teaching;

/// <summary>
/// PRD §14.14 TF-002 — auto-anonymisation of report text, dates, and study
/// identifiers on the way into the teaching file.
///
/// This is a **content** scrubber, deliberately distinct from
/// <see cref="PhiRedactor"/> (which masks log lines and exception breadcrumbs).
/// The difference matters: a log line can be destroyed without cost, whereas a
/// teaching case must stay clinically readable — "45-year-old male, 3-day
/// history of RLQ pain" is the whole point of the case and must survive, while
/// "MRN 00481234" and "Accession ACC-2026-0031" must not.
///
/// Design rules:
/// <list type="bullet">
/// <item>Fail safe. Every pattern errs toward over-redaction; a scrubbed
/// teaching point is a nuisance, a leaked identifier is a breach.</item>
/// <item>Known identifiers are removed <em>literally</em> first
/// (<see cref="Scrub(string?, string?[])"/> takes the accession number and
/// patient reference of the source study), so an identifier survives even when
/// its shape does not match any generic pattern.</item>
/// <item><see cref="PhiRedactor"/> runs last as the generic backstop for
/// name-, SSN-, and MRN-shaped runs, so the two stay in sync: a pattern added
/// there is automatically enforced here too.</item>
/// </list>
///
/// The scrubber is NOT a substitute for the schema guarantee — see
/// <c>TeachingCase</c>, which has no column an identifier could be stored in.
/// It exists for identifiers embedded in free narrative.
/// </summary>
public static class TeachingCaseDeidentifier
{
    /// <summary>What every redacted span is replaced with. Visible on purpose: a
    /// reader must be able to see that something was removed.</summary>
    public const string Placeholder = "[de-identified]";

    /// <summary>Shortest literal identifier we will strip. Below this a literal
    /// match is more likely to be an ordinary word ("CT", "US") than an id.</summary>
    private const int MinLiteralLength = 4;

    private static readonly RegexOptions Opts =
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;

    /// <summary>
    /// Labelled identifiers: everything from the label to the end of the field
    /// (comma, semicolon, or line break) is dropped. "Patient" alone is NOT in
    /// this list — "Patient: 45-year-old male" is clinical content, and
    /// name-shaped values after it are caught by <see cref="PhiRedactor"/>.
    /// </summary>
    private static readonly Regex LabelledIdentifier = new(
        @"\b(MRN|M\.R\.N\.|Medical\s+Record(?:\s+(?:No\.?|Number|#))?|Accession(?:\s+(?:No\.?|Number|#))?|Acc\s*#|Study\s+(?:UID|Instance\s+UID)|Patient\s+(?:Name|ID|Identifier)|Name|DOB|D\.O\.B\.|Date\s+of\s+Birth|SSN|NHS\s+(?:No\.?|Number))\s*[:#=]\s*[^,;\r\n]*",
        Opts);

    /// <summary>
    /// Accession-shaped tokens ("ACC-2026-0031", "A1234567"): a short letter
    /// prefix followed by a run of at least FOUR digits, optionally continuing
    /// in hyphen-separated groups. The four-digit floor is what keeps anatomy
    /// out of the net — "L4-L5", "T12", and "C1" all stay intact.
    /// </summary>
    private static readonly Regex AccessionShaped = new(
        @"\b[A-Za-z]{1,4}[-_]?\d{4,}(?:[-_]\d+)*\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>Month-name dates: "5 January 2024", "January 5, 2024", "Jan 2024".</summary>
    private static readonly Regex TextualDate = new(
        @"\b(?:\d{1,2}\s+)?(?:Jan(?:uary)?|Feb(?:ruary)?|Mar(?:ch)?|Apr(?:il)?|May|Jun(?:e)?|Jul(?:y)?|Aug(?:ust)?|Sep(?:t(?:ember)?)?|Oct(?:ober)?|Nov(?:ember)?|Dec(?:ember)?)\.?\s+\d{1,2}(?:st|nd|rd|th)?(?:,)?\s*\d{4}\b|\b(?:Jan(?:uary)?|Feb(?:ruary)?|Mar(?:ch)?|Apr(?:il)?|May|Jun(?:e)?|Jul(?:y)?|Aug(?:ust)?|Sep(?:t(?:ember)?)?|Oct(?:ober)?|Nov(?:ember)?|Dec(?:ember)?)\.?\s+\d{4}\b",
        Opts);

    /// <summary>Numeric dates with any common separator: 05/01/2024, 5-1-2024, 2024.01.05.</summary>
    private static readonly Regex NumericDate = new(
        @"\b\d{1,4}[./-]\d{1,2}[./-]\d{2,4}\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>Honorific-led personal names: "Dr. John Smith", "Dr Smith".</summary>
    private static readonly Regex HonorificName = new(
        @"\b(?:Dr\.?|Doctor|Prof\.?|Professor|Mr\.?|Mrs\.?|Ms\.?)\s+[A-Z][A-Za-z'\-]+(?:\s+[A-Z][A-Za-z'\-]+){0,2}",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>Collapses runs of adjacent placeholders left by overlapping patterns.</summary>
    private static readonly Regex PlaceholderRun = new(
        @"(?:\[de-identified\])(?:[\s,;]*\[de-identified\])+",
        Opts);

    /// <summary>
    /// De-identify one free-text field. <paramref name="literalIdentifiers"/>
    /// carries identifiers already known for the source study (accession
    /// number, patient reference); each is removed verbatim, case-insensitively,
    /// before the generic patterns run.
    /// </summary>
    public static string Scrub(string? input, params string?[] literalIdentifiers)
    {
        if (string.IsNullOrWhiteSpace(input)) return "";

        var s = input;

        // 1. Known identifiers, verbatim. Longest first so "ACC-2026-0031"
        //    is removed whole rather than leaving "-0031" behind.
        if (literalIdentifiers is { Length: > 0 })
        {
            foreach (var raw in literalIdentifiers
                         .Where(x => !string.IsNullOrWhiteSpace(x))
                         .Select(x => x!.Trim())
                         .Where(x => x.Length >= MinLiteralLength)
                         .Distinct(StringComparer.OrdinalIgnoreCase)
                         .OrderByDescending(x => x.Length))
            {
                s = Regex.Replace(s, Regex.Escape(raw), Placeholder,
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            }
        }

        // 2. Labelled identifiers (MRN:, Accession:, DOB:, Name:).
        s = LabelledIdentifier.Replace(s, m => $"{m.Groups[1].Value}: {Placeholder}");

        // 3. Dates — textual before numeric so "January 5, 2024" is taken whole.
        s = TextualDate.Replace(s, Placeholder);
        s = NumericDate.Replace(s, Placeholder);

        // 4. Names behind an honorific.
        s = HonorificName.Replace(s, Placeholder);

        // 5. Accession-shaped tokens that carried no label.
        s = AccessionShaped.Replace(s, Placeholder);

        // 6. Generic backstop — "Patient: Jane Doe", SSNs, long digit runs.
        s = PhiRedactor.Redact(s).Replace("<redacted:phi>", Placeholder);

        return PlaceholderRun.Replace(s, Placeholder).Trim();
    }

    /// <summary>
    /// True when <paramref name="text"/> still contains any of the supplied
    /// identifiers. Used as a post-condition assertion at the controller
    /// boundary so a scrubber regression cannot silently persist PHI.
    /// </summary>
    public static bool ContainsAny(string? text, params string?[] identifiers)
    {
        if (string.IsNullOrEmpty(text) || identifiers is null) return false;
        return identifiers
            .Where(x => !string.IsNullOrWhiteSpace(x) && x!.Trim().Length >= MinLiteralLength)
            .Any(x => text.Contains(x!.Trim(), StringComparison.OrdinalIgnoreCase));
    }
}
