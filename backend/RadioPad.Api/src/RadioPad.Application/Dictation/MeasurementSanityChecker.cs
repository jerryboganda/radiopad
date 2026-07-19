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

        // 2) consistency — a measurement in the Impression must also appear in the Findings.
        //    Compared on a unit-normalized key, not the raw text: restating a lesion in the other
        //    unit ("8 mm" in Findings, "0.8 cm" in the Impression) is ordinary dictation, and
        //    flagging it produced a safety warning for something that had not gone wrong.
        var findings = Set(sections, "findings");
        foreach (var m in Measurements(Get(sections, "impression")))
        {
            if (!findings.Contains(CanonicalKey(m)))
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
        => Measurements(Get(s, key)).Select(CanonicalKey).ToHashSet(StringComparer.Ordinal);

    /// <summary>
    /// Comparison key for a measurement, with every dimension converted to millimetres, so "8 mm",
    /// "0.8 cm", "8mm" and "30 x 40 mm" / "3 x 4 cm" are recognised as the same measurement.
    ///
    /// Only the equality comparison is normalized — warning text still quotes what the radiologist
    /// actually dictated. A measurement carrying no recognisable unit falls back to its trimmed,
    /// lower-cased text so it can still match itself.
    /// </summary>
    private static string CanonicalKey(string measurement)
    {
        var unit = UnitPattern.Match(measurement);
        if (!unit.Success)
            return measurement.Trim().ToLowerInvariant();

        var toMillimetres = unit.Value.ToLowerInvariant() == "cm" ? 10.0 : 1.0;
        var dimensions = new List<string>();
        foreach (Match n in NumberPattern.Matches(measurement))
        {
            if (double.TryParse(n.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
                // Rounded so 0.8 cm and 8 mm agree exactly rather than differing in the last bit.
                dimensions.Add(Math.Round(v * toMillimetres, 3).ToString(CultureInfo.InvariantCulture));
        }

        return dimensions.Count == 0
            ? measurement.Trim().ToLowerInvariant()
            : string.Join("x", dimensions) + "mm";
    }

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
