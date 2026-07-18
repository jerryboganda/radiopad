using System.Collections.Generic;
using System.Linq;
using RadioPad.Application.Dictation;
using Xunit;

namespace RadioPad.Api.Tests;

/// <summary>
/// Brief §5.3 — validation pass that runs AFTER the LLM formatter, before display. Rejects output
/// that fabricates numbers/measurements/dates or drops required sections, falling back to the
/// dictionary-corrected raw transcript (fail-safe, never fail-silent). SAFETY-CRITICAL (brief §8).
/// </summary>
public class DictationValidationServiceTests
{
    private static PassThroughResult Source(string transcript) =>
        new DeterministicPassThrough().Process(transcript, null);

    private static Dictionary<string, string> Sections(
        string findings = "", string impression = "", string technique = "", string indication = "") =>
        new()
        {
            ["indication"] = indication,
            ["technique"] = technique,
            ["findings"] = findings,
            ["impression"] = impression,
        };

    private static readonly string[] Required = { "findings", "impression" };

    [Fact]
    public void Accepts_Faithful_Output()
    {
        var svc = new DictationValidationService();
        var source = Source("3.2 cm nodule in the right upper lobe, no effusion");
        var output = Sections(
            findings: "3.2 cm nodule in the right upper lobe. No effusion.",
            impression: "Right upper lobe nodule.");

        var result = svc.Validate(source, output, Required);

        Assert.True(result.Accepted);
        Assert.Empty(result.Violations);
    }

    [Fact]
    public void Allows_Legitimate_Repetition_Across_Sections()
    {
        // Measurement dictated once but echoed into both Findings and Impression must NOT be rejected.
        var svc = new DictationValidationService();
        var source = Source("3.2 cm nodule right upper lobe");
        var output = Sections(
            findings: "3.2 cm nodule in the right upper lobe.",
            impression: "3.2 cm right upper lobe nodule.");

        var result = svc.Validate(source, output, Required);

        Assert.True(result.Accepted);
    }

    [Fact]
    public void Rejects_Fabricated_Measurement_And_Falls_Back()
    {
        var svc = new DictationValidationService();
        var source = Source("nodule in the right upper lobe");
        var output = Sections(
            findings: "2.5 cm nodule in the right upper lobe.",  // measurement never dictated
            impression: "Nodule.");

        var result = svc.Validate(source, output, Required);

        Assert.False(result.Accepted);
        Assert.Contains(result.Violations, v => v.Reason == ValidationRejectReason.AddedMeasurement);
        Assert.Equal(source.CorrectedTranscript, result.FallbackText);
    }

    [Fact]
    public void Rejects_Fabricated_Number()
    {
        var svc = new DictationValidationService();
        var source = Source("lesion in segment eight");   // → "segment 8"
        var output = Sections(findings: "Lesion in segment 7.", impression: "Segment 7 lesion.");

        var result = svc.Validate(source, output, Required);

        Assert.False(result.Accepted);
        Assert.Contains(result.Violations, v => v.Reason == ValidationRejectReason.AddedNumber);
    }

    [Fact]
    public void Rejects_Unit_Flip_As_Fabricated_Measurement()
    {
        var svc = new DictationValidationService();
        var source = Source("3.2 cm lesion");
        var output = Sections(findings: "3.2 mm lesion.", impression: "Lesion.");   // cm → mm

        var result = svc.Validate(source, output, Required);

        Assert.False(result.Accepted);
        Assert.Contains(result.Violations, v => v.Reason == ValidationRejectReason.AddedMeasurement);
    }

    [Fact]
    public void Rejects_Fabricated_Date()
    {
        var svc = new DictationValidationService();
        var source = Source("no prior imaging available");
        var output = Sections(
            findings: "Compared to 01/02/2024, stable.",
            impression: "Stable.");

        var result = svc.Validate(source, output, Required);

        Assert.False(result.Accepted);
        Assert.Contains(result.Violations, v => v.Reason == ValidationRejectReason.AddedDate);
    }

    [Fact]
    public void Rejects_When_Required_Section_Missing()
    {
        var svc = new DictationValidationService();
        var source = Source("right upper lobe nodule");
        var output = Sections(findings: "Right upper lobe nodule.", impression: "");  // impression dropped

        var result = svc.Validate(source, output, Required);

        Assert.False(result.Accepted);
        Assert.Contains(result.Violations,
            v => v.Reason == ValidationRejectReason.MissingRequiredSection && v.Detail.Contains("impression"));
    }
}
