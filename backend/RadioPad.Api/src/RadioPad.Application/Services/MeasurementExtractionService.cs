using System.Text.RegularExpressions;
using RadioPad.Domain.Entities;
using RadioPad.Domain.ValueObjects;

namespace RadioPad.Application.Services;

/// <summary>
/// Regex-based extraction of structured measurement data from free-text
/// radiology findings. Powers PRD Beta #7 "Measurements extraction."
/// </summary>
public class MeasurementExtractionService
{
    // Biaxial / triaxial: "5 x 3 mm", "12 × 8 × 6 cm"
    private static readonly Regex MultiAxisPattern = new(
        @"(\d+(?:\.\d+)?)\s*[x×]\s*(\d+(?:\.\d+)?)\s*(?:[x×]\s*(\d+(?:\.\d+)?)\s*)?(mm|cm)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Single: "3.2 cm", "15 mm"
    private static readonly Regex SinglePattern = new(
        @"(\d+(?:\.\d+)?)\s*(mm|cm)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Finding types near measurement
    private static readonly Regex FindingPattern = new(
        @"\b(nodule|mass|lesion|cyst|calcification|lymph\s*node|abscess|collection|effusion|hematoma|aneurysm|pseudoaneurysm)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Anatomical locations
    private static readonly Regex AnatomyPattern = new(
        @"\b(liver|kidney|spleen|pancreas|lung|lobe|segment|adrenal|thyroid|prostate|ovary|uterus|breast|brain|cerebellum)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Laterality
    private static readonly Regex LateralityPattern = new(
        @"\b(left|right|bilateral)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>Window (characters) around a measurement match to search for context.</summary>
    private const int ContextWindow = 80;

    /// <summary>
    /// Extract all measurements from a single text section.
    /// </summary>
    public List<ExtractedMeasurement> Extract(string text, string section)
    {
        if (string.IsNullOrWhiteSpace(text))
            return new List<ExtractedMeasurement>();

        var results = new List<ExtractedMeasurement>();
        var consumed = new HashSet<(int Start, int End)>();

        // Pass 1: multi-axis measurements (greedy — captures biaxial + triaxial)
        foreach (Match m in MultiAxisPattern.Matches(text))
        {
            consumed.Add((m.Index, m.Index + m.Length));
            var context = GetContext(text, m.Index, m.Index + m.Length);

            results.Add(new ExtractedMeasurement(
                Value: double.Parse(m.Groups[1].Value),
                Unit: m.Groups[4].Value.ToLowerInvariant(),
                SecondValue: m.Groups[2].Value,
                ThirdValue: m.Groups[3].Success ? m.Groups[3].Value : null,
                AnatomicalLocation: MatchNearby(AnatomyPattern, context),
                Finding: MatchNearby(FindingPattern, context),
                Laterality: MatchNearby(LateralityPattern, context),
                Section: section,
                StartIndex: m.Index,
                EndIndex: m.Index + m.Length));
        }

        // Pass 2: single measurements that were not already part of a multi-axis match
        foreach (Match m in SinglePattern.Matches(text))
        {
            if (consumed.Any(c => m.Index >= c.Start && m.Index + m.Length <= c.End))
                continue;

            var context = GetContext(text, m.Index, m.Index + m.Length);

            results.Add(new ExtractedMeasurement(
                Value: double.Parse(m.Groups[1].Value),
                Unit: m.Groups[2].Value.ToLowerInvariant(),
                SecondValue: null,
                ThirdValue: null,
                AnatomicalLocation: MatchNearby(AnatomyPattern, context),
                Finding: MatchNearby(FindingPattern, context),
                Laterality: MatchNearby(LateralityPattern, context),
                Section: section,
                StartIndex: m.Index,
                EndIndex: m.Index + m.Length));
        }

        return results;
    }

    /// <summary>
    /// Convenience — extract measurements from every text section of a report.
    /// </summary>
    public Dictionary<string, List<ExtractedMeasurement>> ExtractFromReport(Report report)
    {
        var result = new Dictionary<string, List<ExtractedMeasurement>>();

        void Add(string section, string text)
        {
            var measurements = Extract(text, section);
            if (measurements.Count > 0)
                result[section] = measurements;
        }

        Add("findings", report.Findings);
        Add("impression", report.Impression);
        Add("indication", report.Indication);
        Add("technique", report.Technique);
        Add("comparison", report.Comparison);
        Add("recommendations", report.Recommendations);

        return result;
    }

    // ── helpers ──────────────────────────────────────────────────────────

    private static string GetContext(string text, int matchStart, int matchEnd)
    {
        var start = Math.Max(0, matchStart - ContextWindow);
        var end = Math.Min(text.Length, matchEnd + ContextWindow);
        return text[start..end];
    }

    private static string? MatchNearby(Regex pattern, string context)
    {
        var m = pattern.Match(context);
        return m.Success ? m.Groups[1].Value.ToLowerInvariant() : null;
    }
}
