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
/// radiologist. Callers combine this with <see cref="ConsultantRetryPolicy"/> to re-ask the
/// SAME UBAG provider with the specific violations spelled out, instead of silently shipping
/// prose-paragraph findings or a telegraphic impression.</para>
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

        var lines = impression
            .Replace("\r\n", "\n").Replace('\r', '\n')
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(l => l.Length > 0)
            .ToArray();

        if (lines.Length == 0 || lines.Any(l => !NumberedLine.IsMatch(l)))
            violations.Add(
                "IMPRESSION is not laid out as numbered statements (\"1. \", \"2. \", ...), one complete " +
                "diagnosis-level sentence per line — it reads as an unnumbered fragment instead of a " +
                "synthesised, ordered impression.");

        return violations;
    }

    /// <summary>Combined findings + impression check, in the order they should be reported.</summary>
    public static IReadOnlyList<string> Check(string? findings, string? impression) =>
        CheckFindings(findings).Concat(CheckImpression(impression)).ToArray();

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
        "\n\nFORMAT CORRECTION REQUIRED — your previous response violated the mandatory layout:\n"
        + string.Join("\n", violations.Select(v => "- " + v))
        + $"\nRegenerate the COMPLETE response again, fixing every violation above. {outputContractReminder}";
}
