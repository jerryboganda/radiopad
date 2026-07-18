using System.Globalization;
using System.Text.RegularExpressions;

namespace RadioPad.Application.Dictation;

/// <summary>
/// F4 — deterministic post-format checks over the drafted sections: (1) implausible measurements
/// (a single dimension beyond a sane bound), and (2) a measurement cited in the Impression that does
/// not appear in the Findings. Both are surfaced as <see cref="SentinelWarning"/>s (never silently
/// rejected) for the radiologist to eye-confirm. Reuses the §5.2 measurement extractor.
/// </summary>
public static class MeasurementSanityChecker
{
    // Implausible upper bounds for a single lesion/structure dimension.
    private const double MaxCm = 60.0;
    private const double MaxMm = 600.0;

    private static readonly Regex UnitPattern =
        new(@"(mm|cm)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex NumberPattern =
        new(@"\d+(?:\.\d+)?", RegexOptions.Compiled);

    public static IReadOnlyList<SentinelWarning> Check(IReadOnlyDictionary<string, string> sections)
    {
        var warnings = new List<SentinelWarning>();
        if (sections is null || sections.Count == 0)
            return warnings;

        // 1) implausible measurements across every section
        foreach (var kv in sections)
        {
            foreach (var m in Measurements(kv.Value))
            {
                if (IsImplausible(m))
                    warnings.Add(new SentinelWarning(SentinelKind.MeasurementSanity,
                        $"implausible measurement '{m}' in {kv.Key}"));
            }
        }

        // 2) consistency — a measurement in the Impression must also appear in the Findings
        var findings = Set(sections, "findings");
        foreach (var m in Measurements(Get(sections, "impression")))
        {
            if (!findings.Contains(m))
                warnings.Add(new SentinelWarning(SentinelKind.Consistency,
                    $"impression cites measurement '{m}' not present in findings"));
        }

        return warnings;
    }

    private static string Get(IReadOnlyDictionary<string, string> s, string key)
        => s.TryGetValue(key, out var v) ? v ?? string.Empty : string.Empty;

    private static IEnumerable<string> Measurements(string? text)
        => DeterministicPassThrough.ExtractLockedTokens(text ?? string.Empty)
            .Where(t => t.Kind == LockedTokenKind.Measurement)
            .Select(t => t.Text);

    private static HashSet<string> Set(IReadOnlyDictionary<string, string> s, string key)
        => Measurements(Get(s, key)).ToHashSet(StringComparer.OrdinalIgnoreCase);

    private static bool IsImplausible(string measurement)
    {
        var unit = UnitPattern.Match(measurement);
        if (!unit.Success)
            return false;
        var bound = unit.Value.ToLowerInvariant() == "cm" ? MaxCm : MaxMm;

        foreach (Match n in NumberPattern.Matches(measurement))
        {
            if (double.TryParse(n.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) && v > bound)
                return true;
        }

        return false;
    }
}
