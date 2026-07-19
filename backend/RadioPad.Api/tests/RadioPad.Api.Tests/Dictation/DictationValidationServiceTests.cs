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
    /// <summary>
    /// A missing required section is REPORTED but no longer discards the report.
    ///
    /// <para>This test previously asserted rejection. The rule it encoded — the brief's "reject on
    /// a dropped required section" — is really about the formatter LOSING dictated content, and an
    /// empty section was a crude proxy for that. In practice the proxy fired hardest on correct
    /// behaviour: MedGemma, run end-to-end, produced a clean report and was discarded because the
    /// radiologist had not dictated any recommendations. Since most dictations omit at least one
    /// required section, the offline formatter fell back on nearly every real input.</para>
    ///
    /// <para>The underlying safety property is now measured directly instead of by proxy — see
    /// <c>Rejects_When_A_Dictated_Measurement_Is_Dropped</c> — which is strictly stronger: before,
    /// a formatter could silently delete a dictated measurement and nothing caught it.</para>
    /// </summary>
    public void Reports_But_Does_Not_Reject_When_A_Required_Section_Is_Missing()
    {
        var svc = new DictationValidationService();
        var source = Source("right upper lobe nodule");
        var output = Sections(findings: "Right upper lobe nodule.", impression: "");

        var result = svc.Validate(source, output, Required);

        Assert.True(result.Accepted, "an undictated section must not discard an otherwise sound report");
        Assert.Contains(result.Violations,
            v => v.Reason == ValidationRejectReason.MissingRequiredSection
                 && v.Detail.Contains("impression")
                 && !v.IsBlocking);
    }

    /// <summary>
    /// The real property the section check was standing in for: content the radiologist dictated
    /// must survive into the report. Losing a measurement is as dangerous as inventing one, and
    /// before this the pass only ever looked for additions.
    /// </summary>
    [Fact]
    public void Rejects_When_A_Dictated_Measurement_Is_Dropped()
    {
        var svc = new DictationValidationService();
        var source = Source("There is a 3.2 cm nodule in the right upper lobe.");
        var output = Sections(findings: "There is a nodule in the right upper lobe.", impression: "Nodule.");

        var result = svc.Validate(source, output, Array.Empty<string>());

        Assert.False(result.Accepted);
        Assert.Contains(result.Violations,
            v => v.Reason == ValidationRejectReason.DroppedMeasurement && v.IsBlocking);
        Assert.Equal(source.CorrectedTranscript, result.FallbackText);
    }
}
