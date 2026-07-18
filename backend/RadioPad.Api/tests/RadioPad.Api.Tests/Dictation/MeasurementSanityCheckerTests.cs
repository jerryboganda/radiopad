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

    [Fact]
    public void Empty_Sections_Yield_No_Warnings()
    {
        Assert.Empty(MeasurementSanityChecker.Check(new Dictionary<string, string>()));
    }
}
