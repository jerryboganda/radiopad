using RadioPad.Application.Dictation;
using Xunit;

namespace RadioPad.Api.Tests;

/// <summary>
/// §5.3 must discard the formatter's output for FABRICATION and only for fabrication.
///
/// <para>Found end-to-end: MedGemma formatted a real dictation into a clean report with zero
/// sentinel warnings, and the pipeline threw it away because the radiologist had not dictated a
/// "recommendations" section. The model was penalised for correctly refusing to invent content —
/// the single most important thing it is asked to do. Since most dictations omit at least one
/// required section, this made the offline formatter fall back on nearly every real input: the
/// feature appeared broken while every component behaved "correctly".</para>
///
/// <para>These tests pin the distinction in both directions, because getting it wrong in the other
/// direction — letting a fabricated measurement through — is a patient-safety failure.</para>
/// </summary>
public class ValidationBlockingTests
{
    private static PassThroughResult Source(string text) =>
        new(text, text, DeterministicPassThrough.ExtractLockedTokens(text));

    private static readonly string[] RequiredSections =
        { "indication", "technique", "findings", "impression", "recommendations" };

    [Fact]
    public void A_Section_The_Radiologist_Never_Dictated_Does_Not_Discard_The_Report()
    {
        var source = Source("There is a 3.2 cm nodule in the right upper lobe. No pneumothorax.");
        var sections = new Dictionary<string, string>
        {
            ["indication"] = "Shortness of breath",
            ["technique"] = "CT chest with contrast",
            ["findings"] = "There is a 3.2 cm nodule in the right upper lobe. No pneumothorax.",
            ["impression"] = "3.2 cm right upper lobe nodule.",
            // 'recommendations' deliberately absent — none were dictated, so inventing one would be
            // the actual safety failure.
        };

        var result = new DictationValidationService().Validate(source, sections, RequiredSections);

        Assert.True(result.Accepted, "omitting an undictated section must not discard a sound report");
        // ...but it is still reported, so the radiologist is told what to fill in.
        Assert.Contains(result.Violations, v => v.Reason == ValidationRejectReason.MissingRequiredSection);
        Assert.All(result.Violations, v => Assert.False(v.IsBlocking));
    }

    [Fact]
    public void A_Fabricated_Measurement_Still_Discards_The_Report()
    {
        var source = Source("There is a nodule in the right upper lobe.");
        var sections = new Dictionary<string, string>
        {
            ["indication"] = "Screening",
            ["technique"] = "CT chest",
            ["findings"] = "There is a 2.4 cm nodule in the right upper lobe.", // never dictated
            ["impression"] = "Nodule.",
            ["recommendations"] = "Follow up.",
        };

        var result = new DictationValidationService().Validate(source, sections, RequiredSections);

        Assert.False(result.Accepted);
        Assert.Contains(result.Violations, v => v.Reason == ValidationRejectReason.AddedMeasurement && v.IsBlocking);
        Assert.Equal(source.CorrectedTranscript, result.FallbackText);
    }

    [Fact]
    public void A_Fabricated_Date_Still_Discards_The_Report()
    {
        var source = Source("Compared to the prior study, the nodule is stable.");
        var sections = new Dictionary<string, string>
        {
            ["findings"] = "Compared to the prior study of 03/14/2024, the nodule is stable.",
        };

        var result = new DictationValidationService().Validate(source, sections, Array.Empty<string>());

        Assert.False(result.Accepted);
        Assert.Contains(result.Violations, v => v.Reason == ValidationRejectReason.AddedDate && v.IsBlocking);
    }

    [Fact]
    public void Fabrication_Wins_Even_When_A_Section_Is_Also_Missing()
    {
        // A blocking violation must not be masked by the presence of advisory ones.
        var source = Source("There is a nodule.");
        var sections = new Dictionary<string, string>
        {
            ["findings"] = "There is a 9.9 cm nodule.",
        };

        var result = new DictationValidationService().Validate(source, sections, RequiredSections);

        Assert.False(result.Accepted);
        Assert.Contains(result.Violations, v => v.IsBlocking);
        Assert.Contains(result.Violations, v => !v.IsBlocking);
    }

    [Fact]
    public void A_Faithful_Complete_Report_Is_Accepted_Cleanly()
    {
        var source = Source("There is a 3.2 cm nodule in the right upper lobe.");
        var sections = new Dictionary<string, string>
        {
            ["indication"] = "Screening",
            ["technique"] = "CT chest",
            ["findings"] = "There is a 3.2 cm nodule in the right upper lobe.",
            ["impression"] = "3.2 cm right upper lobe nodule.",
            ["recommendations"] = "Routine follow-up.",
        };

        var result = new DictationValidationService().Validate(source, sections, RequiredSections);

        Assert.True(result.Accepted);
        Assert.Empty(result.Violations);
    }

    [Fact]
    public void A_Measurement_Echoed_Into_Several_Sections_Is_Not_Fabrication()
    {
        // Set membership, not multiset: a value dictated once and legitimately repeated in both
        // Findings and Impression must not read as an addition.
        var source = Source("There is a 3.2 cm nodule in the right upper lobe.");
        var sections = new Dictionary<string, string>
        {
            ["findings"] = "There is a 3.2 cm nodule in the right upper lobe.",
            ["impression"] = "3.2 cm right upper lobe nodule.",
        };

        var result = new DictationValidationService().Validate(source, sections, Array.Empty<string>());

        Assert.True(result.Accepted);
        Assert.Empty(result.Violations);
    }
}
