using System.Text.RegularExpressions;

namespace RadioPad.Application.Services;

/// <summary>
/// Hardening for UBAG's browser-automation Gemini web session (<see
/// cref="Infrastructure.Providers.Ubag.UbagProviderAdapter"/> — deliberately RadioPad's ONLY
/// report-writing provider; no pinned API model is ever substituted). Because UBAG drives a
/// live web UI rather than a pinned model version, its adherence to the consultant doctrine's
/// structural contract (<see cref="ConsultantReportDoctrine"/> — UPPERCASE heading + "• "
/// bullet FINDINGS layout, numbered IMPRESSION) varies run to run: the same prompt can come
/// back compliant one time and as flowing prose the next.
///
/// <para>This is a deterministic, code-side compliance check with no AI involved — regex over
/// the model's own output — so a doctrine-violating response is CAUGHT before it reaches the
/// radiologist. Callers combine it with <see cref="MaxAttempts"/> + <see
/// cref="BuildReinforcement"/> to re-ask the SAME UBAG provider with the specific violations
/// spelled out, instead of silently shipping prose-paragraph findings or a telegraphic
/// impression.</para>
///
/// <para>Two dimensions are checked, because a lighter web-model run fails in two distinct
/// ways: LAYOUT (headings/bullets/numbering — <see cref="Check"/>) and DEPTH/STYLE (banned
/// hedging filler, telegraphic one-word impressions, a findings "review" that covers a single
/// anatomy group instead of the mandated systematic review — <see cref="CheckStyle"/> /
/// <see cref="CheckSystematicCoverage"/>). A run can be perfectly formatted and still
/// junior-grade; the depth checks catch that. Callers use the composed
/// <see cref="CheckForGeneration"/> / <see cref="CheckForRewrite"/> entry points — they differ
/// deliberately, see those methods.</para>
/// </summary>
public static class ConsultantOutputCompliance
{
    // A heading line: starts with an uppercase letter, is substantially uppercase (anatomy/
    // system names, parentheses, slashes, hyphens allowed), and ends with a colon on its own
    // line. Deliberately generous (2+ chars) so real headings like "GI:" still match.
    private static readonly Regex HeadingLine = new(
        @"^[A-Z][A-Z0-9 /&(),'-]{1,80}:\s*$", RegexOptions.Multiline | RegexOptions.Compiled);

    // A bullet line: the mandated Unicode bullet "• " followed by non-whitespace content.
    private static readonly Regex BulletLine = new(
        @"^•\s+\S", RegexOptions.Multiline | RegexOptions.Compiled);

    // A numbered impression line: digit(s), a period, a space, then content — "1. ...".
    private static readonly Regex NumberedLine = new(
        @"^\d+\.\s+\S", RegexOptions.Compiled);

    // Vague filler / hedging the consultant doctrine explicitly bans — the signature of a
    // junior-grade report even when the layout is technically correct. "cannot rule out" must
    // precede "rule out" in the alternation so the fuller phrase wins the match.
    private static readonly Regex HedgingTerm = new(
        @"\b(?:unremarkable|grossly\s+normal|questionable|cannot\s+rule\s+out|rule\s+out)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// Words in a numbered impression statement below which it reads as a telegraphic fragment
    /// ("1. Gallstone.") rather than the mandated diagnosis-level sentence with qualifiers.
    /// Deliberately low — the goal is to catch the degenerate one-or-two-word case, not to
    /// second-guess a legitimately short conclusion.
    /// </summary>
    private const int MinImpressionStatementWords = 3;

    /// <summary>
    /// Checks the FINDINGS text for the mandated heading+bullet layout. Empty findings are not
    /// flagged here — that is a content question (missing dictation), not a layout one.
    /// </summary>
    public static IReadOnlyList<string> CheckFindings(string? findings)
    {
        var violations = new List<string>();
        if (string.IsNullOrWhiteSpace(findings)) return violations;

        if (!HeadingLine.IsMatch(findings))
            violations.Add(
                "FINDINGS has no UPPERCASE anatomy/system heading line ending in \":\" — it reads as flowing " +
                "prose instead of the required heading-and-bullet layout.");
        if (!BulletLine.IsMatch(findings))
            violations.Add(
                "FINDINGS has no \"• \" bulleted statement — every finding must be a bullet beneath its " +
                "anatomy/system heading, never a paragraph.");

        return violations;
    }

    /// <summary>
    /// Checks the IMPRESSION text: every non-empty line must be a numbered statement
    /// ("1. ", "2. ", ...). Empty impression is not flagged here (a content question).
    /// </summary>
    public static IReadOnlyList<string> CheckImpression(string? impression)
    {
        var violations = new List<string>();
        if (string.IsNullOrWhiteSpace(impression)) return violations;

        var lines = Lines(impression);

        if (lines.Length == 0 || lines.Any(l => !NumberedLine.IsMatch(l)))
            violations.Add(
                "IMPRESSION is not laid out as numbered statements (\"1. \", \"2. \", ...), one complete " +
                "diagnosis-level sentence per line — it reads as an unnumbered fragment instead of a " +
                "synthesised, ordered impression.");

        return violations;
    }

    /// <summary>Combined findings + impression LAYOUT check, in the order they should be reported.</summary>
    public static IReadOnlyList<string> Check(string? findings, string? impression) =>
        CheckFindings(findings).Concat(CheckImpression(impression)).ToArray();

    /// <summary>
    /// DEPTH/STYLE checks that apply to every clinician-facing output, generated or rewritten:
    /// the doctrine's banned hedging filler ("unremarkable", "grossly normal", "questionable",
    /// "cannot rule out", "rule out") and telegraphic numbered impression fragments. These catch
    /// the junior-grade run that is formatted correctly but written shallowly.
    /// </summary>
    public static IReadOnlyList<string> CheckStyle(string? findings, string? impression)
    {
        var violations = new List<string>();

        var findingHedges = DistinctHedges(findings);
        if (findingHedges.Count > 0)
            violations.Add(
                "FINDINGS uses banned vague filler/hedging (" + string.Join(", ", findingHedges) + ") — state " +
                "specifically what is normal (for example \"normal in size, contour, and attenuation\") and " +
                "express any uncertainty as an explicit differential with its likelihood.");

        var impressionHedges = DistinctHedges(impression);
        if (impressionHedges.Count > 0)
            violations.Add(
                "IMPRESSION uses banned vague filler/hedging (" + string.Join(", ", impressionHedges) + ") — " +
                "every conclusion must be a specific diagnosis-level statement; express uncertainty as an " +
                "explicit differential with its likelihood.");

        // Unnumbered lines are the layout check's problem; here only numbered statements are
        // measured for the degenerate telegraphic case ("1. Gallstone.").
        var fragments = Lines(impression)
            .Where(l => NumberedLine.IsMatch(l))
            .Select(l => Regex.Replace(l, @"^\d+\.\s+", ""))
            .Where(body => body.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length < MinImpressionStatementWords)
            .Select(body => "\"" + body + "\"")
            .ToArray();
        if (fragments.Length > 0)
            violations.Add(
                "IMPRESSION contains telegraphic fragments (" + string.Join(" | ", fragments) + ") — each " +
                "numbered statement must be a complete, grammatical diagnosis-level sentence with relevant " +
                "qualifiers (for example \"Non-obstructive right renal calculus\", never \"Calculus.\").");

        return violations;
    }

    /// <summary>
    /// Generation-only depth check: a consultant-grade systematic review covers every structure
    /// routinely assessed on the examination, so generated FINDINGS confined to a single
    /// anatomy/system group are junior-grade regardless of formatting. Deliberately NOT applied
    /// to rewrites — see <see cref="CheckForRewrite"/>.
    /// </summary>
    public static IReadOnlyList<string> CheckSystematicCoverage(string? findings)
    {
        if (string.IsNullOrWhiteSpace(findings)) return Array.Empty<string>();
        var headings = HeadingLine.Matches(findings).Count;
        if (headings >= 2) return Array.Empty<string>();
        return new[]
        {
            "FINDINGS reviews " + (headings == 0 ? "no" : "only one") + " anatomy/system group — a " +
            "consultant-grade systematic review must cover every structure routinely assessed on this " +
            "examination, each group under its own UPPERCASE heading: pertinent negatives for involved " +
            "structures, expected normal appearance for the rest.",
        };
    }

    /// <summary>
    /// Everything that applies to a FROM-SCRATCH generated report: layout + systematic coverage
    /// + style. Generation is authorised to author the full systematic review, so demanding
    /// multi-group coverage is safe and correct here.
    /// </summary>
    public static IReadOnlyList<string> CheckForGeneration(string? findings, string? impression) =>
        Check(findings, impression)
            .Concat(CheckSystematicCoverage(findings))
            .Concat(CheckStyle(findings, impression))
            .ToArray();

    /// <summary>
    /// Everything that applies to a REWRITE of an existing report: layout + style, but NOT the
    /// systematic-coverage floor. A rewrite is bound by the no-fabrication contract — it may
    /// never ADD structures the source report did not mention — so demanding broader anatomy
    /// coverage here would push the model toward fabricating "expected normal" statements, the
    /// exact failure the §5.3 guard exists to prevent.
    /// </summary>
    public static IReadOnlyList<string> CheckForRewrite(string? findings, string? impression) =>
        Check(findings, impression)
            .Concat(CheckStyle(findings, impression))
            .ToArray();

    private static string[] Lines(string? text) =>
        string.IsNullOrWhiteSpace(text)
            ? Array.Empty<string>()
            : text.Replace("\r\n", "\n").Replace('\r', '\n')
                  .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                  .Where(l => l.Length > 0)
                  .ToArray();

    private static IReadOnlyList<string> DistinctHedges(string? text) =>
        string.IsNullOrWhiteSpace(text)
            ? Array.Empty<string>()
            : HedgingTerm.Matches(text)
                .Select(m => "\"" + m.Value.ToLowerInvariant() + "\"")
                .Distinct()
                .ToArray();

    /// <summary>
    /// Bounded attempts against the SAME provider (UBAG only — RadioPad never substitutes a
    /// different/pinned provider on a compliance failure). One retry is enough to correct a
    /// browser-automation model that ignored the layout instructions once; more than that just
    /// adds latency to an already-slow browser-driven call without materially improving the odds,
    /// so callers ship the best available attempt rather than block the clinical workflow.
    /// </summary>
    public const int MaxAttempts = 2;

    /// <summary>
    /// Appends a targeted correction to the ORIGINAL prompt for a same-provider retry: the exact
    /// violations the previous attempt committed, plus an output-contract reminder supplied by the
    /// caller (JSON-only for generate, plain-text-with-headings for rewrite).
    /// </summary>
    public static string BuildReinforcement(IReadOnlyList<string> violations, string outputContractReminder) =>
        "\n\nFORMAT CORRECTION REQUIRED — your previous response violated the mandatory report standard:\n"
        + string.Join("\n", violations.Select(v => "- " + v))
        + $"\nRegenerate the COMPLETE response again, fixing every violation above. {outputContractReminder}";
}
