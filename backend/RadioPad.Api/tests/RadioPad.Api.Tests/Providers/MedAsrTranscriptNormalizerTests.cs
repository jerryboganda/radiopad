using RadioPad.Infrastructure.Providers.Local;
using Xunit;

namespace RadioPad.Api.Tests;

/// <summary>
/// MedASR emits its own markup — <c>{period}</c>-style punctuation markers and <c>[FINDINGS]</c>
/// section tags — rather than plain punctuation. These fixtures are the VERBATIM outputs captured
/// from the real engine over the bundle's test_wavs (see MedAsrEngineSmokeTests), so the normalizer
/// is tested against what the model actually produces rather than what we assume it produces.
/// Deterministic: pure string transformation, no model or network.
/// </summary>
public class MedAsrTranscriptNormalizerTests
{
    // Verbatim MedASR output for test_wavs/0.wav (a radiology PE dictation).
    private const string RealPeDictation =
        "[EXAM TYPE] CT chest PE protocol {period} [INDICATION] 54-year-old female, shortness of " +
        "breath, evaluate for PE {period} [TECHNIQUE] Standard protocol {period} [FINDINGS] {colon} " +
        "Pulmonary vasculature {colon} The main PA is patent {period} There are filling defects in " +
        "the segmental branches of the right lower lobe {comma} compatible with acute PE {period} " +
        "No saddle embolus {period} Lungs {colon} No pneumothorax {period} Small bilateral " +
        "effusions {comma} right greater than left {period} {new paragraph} [IMPRESSION] {colon} " +
        "Acute segmental PE, right lower lobe {period}";

    [Fact]
    public void Converts_Punctuation_Markers_To_Real_Punctuation()
    {
        var result = MedAsrTranscriptNormalizer.Normalize(RealPeDictation);

        // No marker survives — the whole point: these would otherwise reach a signed report.
        Assert.DoesNotContain("{", result, StringComparison.Ordinal);
        Assert.DoesNotContain("}", result, StringComparison.Ordinal);
        Assert.Contains("compatible with acute PE.", result, StringComparison.Ordinal);
        Assert.Contains("right lower lobe, compatible", result, StringComparison.Ordinal);
    }

    [Fact]
    public void Converts_Section_Tags_To_Heading_Lines()
    {
        var result = MedAsrTranscriptNormalizer.Normalize(RealPeDictation);

        Assert.DoesNotContain("[", result, StringComparison.Ordinal);
        Assert.DoesNotContain("]", result, StringComparison.Ordinal);
        Assert.Contains("FINDINGS:", result, StringComparison.Ordinal);
        Assert.Contains("IMPRESSION:", result, StringComparison.Ordinal);
        // "[FINDINGS] {colon}" must not leave a doubled colon — including one split across the
        // newline the heading substitution inserts ("FINDINGS:\n : Pulmonary"), which a plain
        // "::" check does not catch.
        Assert.DoesNotContain("::", result, StringComparison.Ordinal);
        Assert.DoesNotMatch(@":[ \t\r\n]*:", result);
        Assert.Contains("FINDINGS: Pulmonary vasculature:", result, StringComparison.Ordinal);
    }

    [Fact]
    public void Preserves_Every_Clinical_Word()
    {
        var result = MedAsrTranscriptNormalizer.Normalize(RealPeDictation);

        // The safety-critical property: normalization is punctuation-only. Losing or altering a
        // clinical term here would be a silent fabrication upstream of the §5.2 token lock.
        foreach (var word in new[]
                 {
                     "54-year-old", "pneumothorax", "embolus", "segmental", "right", "left",
                     "bilateral", "patent", "Pulmonary", "vasculature",
                 })
            Assert.Contains(word, result, StringComparison.Ordinal);
    }

    [Fact]
    public void Leaves_Plain_Prose_Untouched()
    {
        // MedASR emits ordinary punctuation when none was dictated (test_wavs/1.wav) — the
        // normalizer must be a no-op there, so it is safe to apply to every result.
        const string prose =
            "Biopsy is a medical procedure in which a sample of tissue is removed from the body " +
            "for examination. Osteoporosis is a condition in which bones become weak and brittle.";

        Assert.Equal(prose, MedAsrTranscriptNormalizer.Normalize(prose));
    }

    [Fact]
    public void Is_Idempotent()
    {
        var once = MedAsrTranscriptNormalizer.Normalize(RealPeDictation);
        Assert.Equal(once, MedAsrTranscriptNormalizer.Normalize(once));
    }

    [Fact]
    public void Unknown_Marker_Survives_Verbatim_Rather_Than_Being_Guessed()
    {
        // Fail-visible over fail-silent: a marker we don't know must reach the radiologist intact,
        // never be dropped or guessed into punctuation.
        var result = MedAsrTranscriptNormalizer.Normalize("The lesion {frobnicate} measures 3 mm.");

        Assert.Contains("{frobnicate}", result, StringComparison.Ordinal);
        Assert.Contains("3 mm", result, StringComparison.Ordinal);
    }

    [Fact]
    public void Spurious_MidSentence_Tag_Does_Not_Lose_Words()
    {
        // Observed on test_wavs/5.wav: MedASR emitted "The [DIAGNOSES] Includes ...". We cannot
        // treat a tag as an authoritative boundary, but we must never drop the surrounding words.
        var result = MedAsrTranscriptNormalizer.Normalize(
            "The [DIAGNOSES] Includes cholecystitis, gastritis, and acid reflux.");

        Assert.Contains("cholecystitis", result, StringComparison.Ordinal);
        Assert.Contains("gastritis", result, StringComparison.Ordinal);
        Assert.Contains("acid reflux", result, StringComparison.Ordinal);
        Assert.Contains("The", result, StringComparison.Ordinal);
    }

    [Fact]
    public void New_Paragraph_Marker_Becomes_A_Blank_Line()
    {
        var result = MedAsrTranscriptNormalizer.Normalize("Findings here {new paragraph} Impression here");
        Assert.Contains("\n", result, StringComparison.Ordinal);
        Assert.DoesNotContain("new paragraph", result, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Empty_Input_Yields_Empty_String(string? input)
    {
        Assert.Equal(string.Empty, MedAsrTranscriptNormalizer.Normalize(input));
    }

    [Fact]
    public void Removes_The_Space_Left_Before_Substituted_Punctuation()
    {
        var result = MedAsrTranscriptNormalizer.Normalize("No pneumothorax {period} Lungs are clear {period}");
        Assert.Contains("No pneumothorax.", result, StringComparison.Ordinal);
        Assert.DoesNotContain(" .", result, StringComparison.Ordinal);
    }
}
