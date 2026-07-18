using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace RadioPad.Application.Dictation;

/// <summary>A single deterministic find→replace correction applied BEFORE the LLM (brief §6).</summary>
public sealed record CorrectionRule(string From, string To);

/// <summary>Category of a locked (protected) token extracted from the transcript (brief §5.2).</summary>
public enum LockedTokenKind
{
    Measurement,
    Number,
    Date,
    Laterality,
    Negation,
}

/// <summary>A token the LLM formatter is forbidden to alter or fabricate (brief §5.2/§5.3).</summary>
public sealed record LockedToken(LockedTokenKind Kind, string Text);

/// <summary>Result of the deterministic pass-through (brief §5.2).</summary>
/// <param name="RawTranscript">The unmodified input transcript.</param>
/// <param name="CorrectedTranscript">Dictionary-corrected + number-normalized transcript. This is
/// both what is sent to the formatter AND the fail-safe fallback shown if §5.3 rejects the LLM
/// output.</param>
/// <param name="LockedTokens">Every number / measurement / laterality / negation / date the LLM
/// must reproduce exactly and may not add to.</param>
public sealed record PassThroughResult(
    string RawTranscript,
    string CorrectedTranscript,
    IReadOnlyList<LockedToken> LockedTokens);

/// <summary>
/// Brief §5.2 — deterministic pre-processing that runs BEFORE the LLM formatter. It (1) applies the
/// correction dictionary, (2) normalizes spoken numbers/measurements to digits deterministically
/// (NOT via the LLM — this directly compensates for MedASR's documented weakness on temporal/numeric
/// data), and (3) extracts and locks every number, measurement, laterality term, negation and date
/// so the downstream validation pass (§5.3) can guarantee the LLM neither altered nor fabricated any
/// of them.
/// </summary>
public sealed class DeterministicPassThrough
{
    // ── number-word tables ───────────────────────────────────────────────
    private static readonly Dictionary<string, long> Units = new(StringComparer.Ordinal)
    {
        ["zero"] = 0, ["one"] = 1, ["two"] = 2, ["three"] = 3, ["four"] = 4, ["five"] = 5,
        ["six"] = 6, ["seven"] = 7, ["eight"] = 8, ["nine"] = 9, ["ten"] = 10, ["eleven"] = 11,
        ["twelve"] = 12, ["thirteen"] = 13, ["fourteen"] = 14, ["fifteen"] = 15, ["sixteen"] = 16,
        ["seventeen"] = 17, ["eighteen"] = 18, ["nineteen"] = 19, ["twenty"] = 20, ["thirty"] = 30,
        ["forty"] = 40, ["fifty"] = 50, ["sixty"] = 60, ["seventy"] = 70, ["eighty"] = 80, ["ninety"] = 90,
    };

    private static readonly Dictionary<string, long> Scales = new(StringComparer.Ordinal)
    {
        ["hundred"] = 100, ["thousand"] = 1000, ["million"] = 1_000_000,
    };

    private static readonly Dictionary<string, string> UnitWords = new(StringComparer.Ordinal)
    {
        ["mm"] = "mm", ["millimeter"] = "mm", ["millimeters"] = "mm", ["millimetre"] = "mm", ["millimetres"] = "mm",
        ["cm"] = "cm", ["centimeter"] = "cm", ["centimeters"] = "cm", ["centimetre"] = "cm", ["centimetres"] = "cm",
    };

    private static readonly char[] TrimPunct = { '.', ',', ';', ':', '(', ')', '"', '\'', '!', '?' };

    // ── extraction regexes (operate on the normalized/digit transcript) ───
    private static readonly Regex MultiAxisPattern = new(
        @"(\d+(?:\.\d+)?)\s*[x×]\s*(\d+(?:\.\d+)?)\s*(?:[x×]\s*(\d+(?:\.\d+)?)\s*)?(mm|cm)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex SinglePattern = new(
        @"(\d+(?:\.\d+)?)\s*(mm|cm)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex DatePattern = new(
        @"\b\d{1,2}[/-]\d{1,2}[/-]\d{2,4}\b|\b\d{4}-\d{2}-\d{2}\b|\b(?:January|February|March|April|May|June|July|August|September|October|November|December)\.?\s+\d{1,2}(?:st|nd|rd|th)?,?\s+\d{4}\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex NumberPattern = new(@"\d+(?:\.\d+)?", RegexOptions.Compiled);

    private static readonly Regex LateralityPattern = new(@"\b(left|right|bilateral)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex NegationPattern = new(
        @"\b(no|not|without|absent|absence|negative|denies|denied|none|non)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex TokenPattern = new(@"(\S+)(\s*)", RegexOptions.Compiled);

    // ── public API ────────────────────────────────────────────────────────

    /// <summary>Runs the full deterministic pass-through: corrections → normalization → token lock.</summary>
    public PassThroughResult Process(string rawTranscript, IReadOnlyList<CorrectionRule>? corrections = null)
    {
        var raw = rawTranscript ?? string.Empty;
        if (string.IsNullOrWhiteSpace(raw))
            return new PassThroughResult(raw, string.Empty, Array.Empty<LockedToken>());

        var corrected = ApplyCorrections(raw, corrections ?? Array.Empty<CorrectionRule>());
        corrected = NormalizeSpokenNumbers(corrected);
        var locked = ExtractLockedTokens(corrected);
        return new PassThroughResult(raw, corrected, locked);
    }

    /// <summary>Applies the correction dictionary as ordered, whitespace-tolerant, case-insensitive
    /// whole-phrase replacements (brief §6). The canonical <see cref="CorrectionRule.To"/> form wins.</summary>
    public static string ApplyCorrections(string text, IReadOnlyList<CorrectionRule> corrections)
    {
        if (string.IsNullOrEmpty(text) || corrections is null || corrections.Count == 0)
            return text ?? string.Empty;

        foreach (var rule in corrections)
        {
            if (string.IsNullOrWhiteSpace(rule.From))
                continue;

            var parts = rule.From
                .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
                .Select(Regex.Escape);
            var pattern = @"\b" + string.Join(@"\s+", parts) + @"\b";
            var replacement = (rule.To ?? string.Empty).Replace("$", "$$");
            text = Regex.Replace(text, pattern, replacement, RegexOptions.IgnoreCase);
        }

        return text;
    }

    /// <summary>Converts spoken cardinal numbers, decimals ("point"), and biaxial/triaxial
    /// measurements ("three by four centimeters" → "3 x 4 cm") to digits, normalizing units to
    /// mm/cm. Existing digits are left untouched (brief §5.2 — deterministic, never via the LLM).</summary>
    public static string NormalizeSpokenNumbers(string text)
    {
        if (string.IsNullOrEmpty(text))
            return text ?? string.Empty;

        var tokens = TokenPattern.Matches(text)
            .Select(m => new Tok(m.Groups[1].Value, Core(m.Groups[1].Value), m.Groups[2].Value))
            .ToList();

        var sb = new StringBuilder(text.Length);
        var i = 0;
        while (i < tokens.Count)
        {
            if (IsNumberStart(tokens[i].Core))
            {
                var (digits, used) = ConsumeNumberExpression(tokens, i);
                if (used > 0)
                {
                    sb.Append(digits);
                    sb.Append(tokens[i + used - 1].Sep);
                    i += used;
                    continue;
                }
            }

            sb.Append(tokens[i].Raw);
            sb.Append(tokens[i].Sep);
            i++;
        }

        return sb.ToString();
    }

    /// <summary>Extracts and locks every measurement, date, standalone number, laterality term and
    /// negation from the (already normalized) transcript so §5.3 can verify the LLM output against
    /// them (brief §5.2).</summary>
    public static IReadOnlyList<LockedToken> ExtractLockedTokens(string text)
    {
        var result = new List<LockedToken>();
        if (string.IsNullOrWhiteSpace(text))
            return result;

        var consumed = new List<(int Start, int End)>();

        // 1) Multi-axis measurements (greedy) then single measurements not inside a multi-axis span.
        foreach (Match m in MultiAxisPattern.Matches(text))
        {
            consumed.Add((m.Index, m.Index + m.Length));
            var axes = new List<string> { m.Groups[1].Value, m.Groups[2].Value };
            if (m.Groups[3].Success)
                axes.Add(m.Groups[3].Value);
            result.Add(new LockedToken(LockedTokenKind.Measurement,
                string.Join(" x ", axes) + " " + m.Groups[4].Value.ToLowerInvariant()));
        }

        foreach (Match m in SinglePattern.Matches(text))
        {
            if (Within(consumed, m.Index, m.Index + m.Length))
                continue;
            consumed.Add((m.Index, m.Index + m.Length));
            result.Add(new LockedToken(LockedTokenKind.Measurement,
                m.Groups[1].Value + " " + m.Groups[2].Value.ToLowerInvariant()));
        }

        // 2) Dates (before standalone numbers so date digits are not double-counted).
        foreach (Match m in DatePattern.Matches(text))
        {
            if (Within(consumed, m.Index, m.Index + m.Length))
                continue;
            consumed.Add((m.Index, m.Index + m.Length));
            result.Add(new LockedToken(LockedTokenKind.Date, m.Value.Trim()));
        }

        // 3) Standalone numbers not already part of a measurement or date.
        foreach (Match m in NumberPattern.Matches(text))
        {
            if (Within(consumed, m.Index, m.Index + m.Length))
                continue;
            result.Add(new LockedToken(LockedTokenKind.Number, m.Value));
        }

        // 4) Laterality + negation (directional checks live in the §5.6 sentinel).
        foreach (Match m in LateralityPattern.Matches(text))
            result.Add(new LockedToken(LockedTokenKind.Laterality, m.Value.ToLowerInvariant()));

        foreach (Match m in NegationPattern.Matches(text))
            result.Add(new LockedToken(LockedTokenKind.Negation, m.Value.ToLowerInvariant()));

        return result;
    }

    // ── helpers ──────────────────────────────────────────────────────────

    private readonly record struct Tok(string Raw, string Core, string Sep);

    private static string Core(string raw) => raw.Trim(TrimPunct).ToLowerInvariant();

    private static bool IsNumberStart(string core) => Units.ContainsKey(core) || Scales.ContainsKey(core);

    private static bool IsIntegerWord(string core) => Units.ContainsKey(core) || Scales.ContainsKey(core);

    /// <summary>Parses one or more number groups joined by "by"/"x" plus an optional trailing unit.</summary>
    private static (string Digits, int Used) ConsumeNumberExpression(List<Tok> tokens, int start)
    {
        var i = start;
        var groups = new List<string>();

        while (i < tokens.Count)
        {
            var (num, used) = ConsumeOneNumber(tokens, i);
            if (used == 0)
                break;
            groups.Add(num);
            i += used;

            if (i < tokens.Count
                && (tokens[i].Core == "by" || tokens[i].Core == "x")
                && i + 1 < tokens.Count
                && IsNumberStart(tokens[i + 1].Core))
            {
                i++; // consume the "by"/"x" separator and continue collecting axes
                continue;
            }

            break;
        }

        if (groups.Count == 0)
            return (string.Empty, 0);

        var joined = string.Join(" x ", groups);
        if (i < tokens.Count && UnitWords.TryGetValue(tokens[i].Core, out var unit))
        {
            joined += " " + unit;
            i++;
        }

        return (joined, i - start);
    }

    /// <summary>Parses a single cardinal number (with optional "point" decimals) into a digit string.</summary>
    private static (string Number, int Used) ConsumeOneNumber(List<Tok> tokens, int start)
    {
        long acc = 0, current = 0;
        var saw = false;
        var i = start;

        while (i < tokens.Count)
        {
            var c = tokens[i].Core;
            if (Units.TryGetValue(c, out var v))
            {
                current += v;
                saw = true;
                i++;
            }
            else if (c == "hundred")
            {
                current = (current == 0 ? 1 : current) * 100;
                saw = true;
                i++;
            }
            else if (Scales.TryGetValue(c, out var scale) && scale >= 1000)
            {
                acc += (current == 0 ? 1 : current) * scale;
                current = 0;
                saw = true;
                i++;
            }
            else if (c == "and" && saw && i + 1 < tokens.Count && IsIntegerWord(tokens[i + 1].Core))
            {
                i++; // "one hundred AND twenty"
            }
            else
            {
                break;
            }
        }

        if (!saw)
            return (string.Empty, 0);

        var intVal = (acc + current).ToString(CultureInfo.InvariantCulture);

        // Optional decimal: "point" followed by single-digit words read individually.
        if (i < tokens.Count && tokens[i].Core == "point")
        {
            var j = i + 1;
            var dec = new StringBuilder();
            while (j < tokens.Count && Units.TryGetValue(tokens[j].Core, out var dv) && dv < 10)
            {
                dec.Append(dv);
                j++;
            }

            if (dec.Length > 0)
            {
                intVal = intVal + "." + dec;
                i = j;
            }
        }

        return (intVal, i - start);
    }

    private static bool Within(List<(int Start, int End)> spans, int start, int end)
        => spans.Any(s => start >= s.Start && end <= s.End);
}
