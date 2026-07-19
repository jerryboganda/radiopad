using System.Collections.Generic;
using RadioPad.Application.Dictation;
using Xunit;

namespace RadioPad.Api.Tests;

/// <summary>
/// F4 — deterministic measurement sanity + findings/impression consistency checks surfaced through
/// the sentinel-warning channel for radiologist review.
/// </summary>
public class MeasurementSanityCheckerTests
{
    private static Dictionary<string, string> Sections(string findings = "", string impression = "") =>
        new() { ["findings"] = findings, ["impression"] = impression };

    [Fact]
    public void Flags_Implausible_Measurement()
    {
        var w = MeasurementSanityChecker.Check(Sections(findings: "300 cm mass in the liver"));
        Assert.Contains(w, x => x.Kind == SentinelKind.MeasurementSanity);
    }

    [Fact]
    public void Does_Not_Flag_Normal_Measurement()
    {
        var w = MeasurementSanityChecker.Check(Sections(findings: "3.2 cm nodule in the right lobe"));
        Assert.DoesNotContain(w, x => x.Kind == SentinelKind.MeasurementSanity);
    }

    [Fact]
    public void Flags_Impression_Measurement_Absent_From_Findings()
    {
        var w = MeasurementSanityChecker.Check(
            Sections(findings: "Nodule in the right lobe.", impression: "3.2 cm nodule."));
        Assert.Contains(w, x => x.Kind == SentinelKind.Consistency);
    }

    [Fact]
    public void Does_Not_Flag_When_Impression_Measurement_Is_In_Findings()
    {
        var w = MeasurementSanityChecker.Check(
            Sections(findings: "3.2 cm nodule in the right lobe.", impression: "3.2 cm right lobe nodule."));
        Assert.DoesNotContain(w, x => x.Kind == SentinelKind.Consistency);
    }

    /// <summary>
    /// The consistency check compared measurement strings for equality, so the same lesion restated
    /// in the other unit — a routine dictation habit, millimetres in Findings and centimetres in the
    /// Impression — was reported as an inconsistency that did not exist. A safety warning that cries
    /// wolf on ordinary reporting is worse than no warning: radiologists learn to dismiss the banner,
    /// and the real laterality and fabrication warnings ride in the same channel.
    /// </summary>
    [Theory]
    [InlineData("8 mm nodule in the right lower lobe.", "0.8 cm nodule, unchanged.")]
    [InlineData("0.8 cm nodule in the right lower lobe.", "8 mm nodule, unchanged.")]
    [InlineData("3.2 cm nodule.", "3.2cm nodule.")] // spacing only
    [InlineData("A 30 x 40 mm mass.", "A 3 x 4 cm mass.")]
    public void Does_Not_Flag_The_Same_Measurement_Expressed_In_Another_Unit(string findings, string impression)
    {
        var w = MeasurementSanityChecker.Check(Sections(findings: findings, impression: impression));
        Assert.DoesNotContain(w, x => x.Kind == SentinelKind.Consistency);
    }

    /// <summary>Normalizing units must not blunt the check: a genuinely different size still flags.</summary>
    [Theory]
    [InlineData("8 mm nodule.", "9 mm nodule.")]
    [InlineData("8 mm nodule.", "8 cm nodule.")]
    public void Still_Flags_A_Genuinely_Different_Measurement(string findings, string impression)
    {
        var w = MeasurementSanityChecker.Check(Sections(findings: findings, impression: impression));
        Assert.Contains(w, x => x.Kind == SentinelKind.Consistency);
    }

    [Fact]
    public void Empty_Sections_Yield_No_Warnings()
    {
        Assert.Empty(MeasurementSanityChecker.Check(new Dictionary<string, string>()));
    }
}
