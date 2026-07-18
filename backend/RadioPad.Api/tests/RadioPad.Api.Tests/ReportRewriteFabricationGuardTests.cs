using RadioPad.Application.Dictation;
using RadioPad.Application.Services;
using Xunit;

namespace RadioPad.Api.Tests;

/// <summary>
/// F12 / brief §5.3 — the hard guard on free-text ("custom") rewrites. An instruction-driven rewrite
/// may rephrase freely, but it must not be able to introduce a measurement, number, or date that was
/// not in the source. These are safety-critical: a failure here would let AI invent a clinical value.
/// </summary>
public class ReportRewriteFabricationGuardTests
{
    [Fact]
    public void Clean_Rephrase_Has_No_Violations()
    {
        var original = "Findings: There is a 2.5 cm mass in the right upper lobe.";
        var rewritten = "Findings: A 2.5 cm mass is present in the right upper lobe.";

        var violations = ReportRewriteService.CheckNoFabrication(original, rewritten);

        Assert.Empty(violations);
    }

    [Fact]
    public void Adding_A_Measurement_Is_Rejected()
    {
        var original = "Findings: There is a mass in the right upper lobe.";
        var rewritten = "Findings: There is a 3.2 cm mass in the right upper lobe.";

        var violations = ReportRewriteService.CheckNoFabrication(original, rewritten);

        Assert.Contains(violations, v => v.Reason == ValidationRejectReason.AddedMeasurement);
    }

    [Fact]
    public void Changing_A_Measurement_Value_Is_Rejected()
    {
        var original = "Findings: 2.5 cm nodule.";
        var rewritten = "Findings: 4.0 cm nodule."; // the model altered the number

        var violations = ReportRewriteService.CheckNoFabrication(original, rewritten);

        Assert.Contains(violations, v => v.Reason == ValidationRejectReason.AddedMeasurement);
    }

    [Fact]
    public void Adding_A_Date_Is_Rejected()
    {
        var original = "Comparison: None available.";
        var rewritten = "Comparison: Compared to the prior study of 01/02/2026.";

        var violations = ReportRewriteService.CheckNoFabrication(original, rewritten);

        Assert.Contains(violations, v => v.Reason == ValidationRejectReason.AddedDate);
    }

    [Fact]
    public void Adding_Only_NonQuantitative_Prose_Is_Allowed()
    {
        var original = "Impression: 2.5 cm mass, likely benign.";
        // Radiologist asked to soften tone / add a caveat with no new numbers — permitted.
        var rewritten = "Impression: A 2.5 cm mass, most likely benign; clinical correlation is advised.";

        var violations = ReportRewriteService.CheckNoFabrication(original, rewritten);

        Assert.Empty(violations);
    }
}
