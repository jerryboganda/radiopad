using System.Text.RegularExpressions;

namespace RadioPad.Application.Dictation;

/// <summary>Category of a sentinel/consistency warning surfaced for radiologist review.</summary>
public enum SentinelKind
{
    Laterality,
    Negation,
    Gender,
    // F4 — deterministic consistency checks (surfaced through the same warning channel).
    Consistency,
    MeasurementSanity,
}

/// <summary>A single §5.6 sentinel warning surfaced to the radiologist for eye-confirmation.</summary>
public sealed record SentinelWarning(SentinelKind Kind, string Detail);

/// <summary>Result of the §5.6 sentinel.</summary>
public sealed record SentinelResult(IReadOnlyList<SentinelWarning> Warnings)
{
    public bool HasWarnings => Warnings.Count > 0;
}

/// <summary>
/// Brief §5.6 — deterministic check that left/right, presence/absence (negation), and patient sex
/// were not flipped between the transcript and the formatted output. It WARNS (never silently
/// rejects); warnings are surfaced with the <c>.ai-mark</c> "Requires review" treatment.
/// </summary>
public sealed class LateralityNegationSentinel
{
    private static readonly Regex LateralityPattern =
        new(@"\b(left|right|bilateral)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex NegationPattern =
        new(@"\b(no|not|without|absent|absence|negative|denies|denied|none|non)\b",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Strong sex-specific anatomy signals (pronouns are deliberately excluded to avoid noise).
    private static readonly Regex MaleMarkers = new(
        @"\b(prostate|prostatic|testis|testes|testicle|testicular|scrotum|scrotal|penis|penile|seminal\s+vesicle|male)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex FemaleMarkers = new(
        @"\b(uterus|uterine|ovary|ovaries|ovarian|endometri\w+|vagina|vaginal|female)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public SentinelResult Check(
        string sourceTranscript,
        IReadOnlyDictionary<string, string> formattedSections,
        string? patientSex = null)
    {
        var source = sourceTranscript ?? string.Empty;
        var output = string.Join("\n", (formattedSections ?? new Dictionary<string, string>()).Values);
        var warnings = new List<SentinelWarning>();

        CheckLaterality(source, output, warnings);
        CheckNegation(source, output, warnings);
        CheckGender(output, patientSex, warnings);

        return new SentinelResult(warnings);
    }

    private static void CheckLaterality(string source, string output, List<SentinelWarning> warnings)
    {
        var src = LateralityPattern.Matches(source).Select(m => m.Value.ToLowerInvariant()).ToHashSet();
        var outp = LateralityPattern.Matches(output).Select(m => m.Value.ToLowerInvariant()).ToHashSet();

        foreach (var l in outp)
        {
            if (src.Contains(l))
                continue;

            var opposite = l switch { "left" => "right", "right" => "left", _ => null };
            if (opposite is not null && src.Contains(opposite))
                warnings.Add(new SentinelWarning(SentinelKind.Laterality,
                    $"report says '{l}' but the dictation said '{opposite}'"));
            else
                warnings.Add(new SentinelWarning(SentinelKind.Laterality,
                    $"report introduces laterality '{l}' not present in the dictation"));
        }
    }

    private static void CheckNegation(string source, string output, List<SentinelWarning> warnings)
    {
        var srcNeg = NegationPattern.Matches(source).Count;
        var outNeg = NegationPattern.Matches(output).Count;

        if (outNeg < srcNeg)
            warnings.Add(new SentinelWarning(SentinelKind.Negation,
                $"possible dropped negation — dictation had {srcNeg} negation cue(s), report has {outNeg}"));
        else if (outNeg > srcNeg)
            warnings.Add(new SentinelWarning(SentinelKind.Negation,
                $"possible added negation — dictation had {srcNeg} negation cue(s), report has {outNeg}"));
    }

    private static void CheckGender(string output, string? patientSex, List<SentinelWarning> warnings)
    {
        if (string.IsNullOrWhiteSpace(patientSex))
            return;

        var sex = char.ToLowerInvariant(patientSex.Trim()[0]);
        if (sex == 'f' && MaleMarkers.Match(output) is { Success: true } m)
            warnings.Add(new SentinelWarning(SentinelKind.Gender,
                $"male-specific term '{m.Value}' in a female study"));
        else if (sex == 'm' && FemaleMarkers.Match(output) is { Success: true } f)
            warnings.Add(new SentinelWarning(SentinelKind.Gender,
                $"female-specific term '{f.Value}' in a male study"));
    }
}
