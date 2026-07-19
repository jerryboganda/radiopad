using System.Linq;
using RadioPad.Application.Dictation;
using Xunit;

namespace RadioPad.Api.Tests;

/// <summary>
/// Brief §5.2 — deterministic pre-processing that runs BEFORE the LLM formatter.
/// These are SAFETY-CRITICAL (brief §8): failures are release blockers.
/// </summary>
public class DeterministicPassThroughTests
{
    // ── Spoken-number normalization ──────────────────────────────────────

    [Fact]
    public void Normalize_Spoken_Decimal_With_Unit()
    {
        Assert.Equal("3.2 cm", DeterministicPassThrough.NormalizeSpokenNumbers("three point two centimeters"));
    }

    [Fact]
    public void Normalize_British_And_American_Unit_Spellings()
    {
        Assert.Equal("12 mm", DeterministicPassThrough.NormalizeSpokenNumbers("twelve millimetres"));
        Assert.Equal("12 mm", DeterministicPassThrough.NormalizeSpokenNumbers("twelve millimeters"));
        Assert.Equal("5 cm", DeterministicPassThrough.NormalizeSpokenNumbers("five centimetre"));
    }

    [Fact]
    public void Normalize_Biaxial_Measurement_Uses_x()
    {
        Assert.Equal("3 x 4 cm", DeterministicPassThrough.NormalizeSpokenNumbers("three by four centimeters"));
        Assert.Equal("12 x 8 x 6 mm", DeterministicPassThrough.NormalizeSpokenNumbers("twelve by eight by six millimeters"));
    }

    [Fact]
    public void Normalize_Compound_Numbers()
    {
        Assert.Equal("120", DeterministicPassThrough.NormalizeSpokenNumbers("one hundred and twenty"));
        Assert.Equal("45", DeterministicPassThrough.NormalizeSpokenNumbers("forty five"));
    }

    [Fact]
    public void Normalize_Leaves_Existing_Digits_Untouched()
    {
        Assert.Equal("the lesion measures 3.2 cm", DeterministicPassThrough.NormalizeSpokenNumbers("the lesion measures 3.2 cm"));
    }

    [Fact]
    public void Normalize_Does_Not_Touch_Non_Number_Words()
    {
        // "no" is a negation, not a number — must not be converted to 0.
        Assert.Equal("no acute abnormality", DeterministicPassThrough.NormalizeSpokenNumbers("no acute abnormality"));
    }

    [Fact]
    public void Normalize_Number_In_Sentence_Context()
    {
        Assert.Equal(
            "a 3.2 cm nodule in the right upper lobe",
            DeterministicPassThrough.NormalizeSpokenNumbers("a three point two centimeter nodule in the right upper lobe"));
    }

    // ── Correction dictionary (find-replace before the LLM) ──────────────

    [Fact]
    public void Corrections_Apply_Case_Insensitively_Preserving_Canonical_Form()
    {
        var rules = new[] { new CorrectionRule("hypo dense", "hypodense") };
        Assert.Equal("the lesion is hypodense", DeterministicPassThrough.ApplyCorrections("the lesion is Hypo Dense", rules));
    }

    /// <summary>
    /// Parity anchor for the frontend port (<c>frontend/lib/dictation/resolveCorrections.ts</c>),
    /// which the microphone path now applies client-side — the mic used to insert its transcript
    /// without any correction dictionary at all.
    ///
    /// The two implementations must agree, including where they are LIMITED: a source phrase ending
    /// in punctuation never matches, because both wrap it in <c>\b…\b</c> and a word boundary cannot
    /// anchor after a trailing '.'. Pinned on both sides so neither can drift into correcting more
    /// than the other; if this is ever relaxed, relax it in the same change.
    /// </summary>
    [Fact]
    public void Corrections_Do_Not_Match_A_Source_Phrase_Ending_In_Punctuation()
    {
        var rules = new[] { new CorrectionRule("c.t.", "CT") };
        Assert.Equal("c.t. of the chest", DeterministicPassThrough.ApplyCorrections("c.t. of the chest", rules));

        // The escaping itself is sound: the '.' is literal, so it does not match an arbitrary char.
        var interior = new[] { new CorrectionRule("c.t", "CT") };
        Assert.Equal("CT of the chest", DeterministicPassThrough.ApplyCorrections("c.t of the chest", interior));
        Assert.Equal("cot of the chest", DeterministicPassThrough.ApplyCorrections("cot of the chest", interior));
    }

    [Fact]
    public void Corrections_Are_Applied_In_Order()
    {
        var rules = new[]
        {
            new CorrectionRule("gall bladder", "gallbladder"),
            new CorrectionRule("gallbladder wall", "GB wall"),
        };
        Assert.Equal("thick GB wall", DeterministicPassThrough.ApplyCorrections("thick gall bladder wall", rules));
    }

    // ── Token locking / extraction (feeds §5.3 validation) ───────────────

    [Fact]
    public void Extract_Locks_Measurements_Numbers_Laterality_Negation()
    {
        var tokens = DeterministicPassThrough.ExtractLockedTokens("3.2 cm nodule in the right upper lobe, no effusion");

        Assert.Contains(tokens, t => t.Kind == LockedTokenKind.Measurement && t.Text == "3.2 cm");
        Assert.Contains(tokens, t => t.Kind == LockedTokenKind.Laterality && t.Text == "right");
        Assert.Contains(tokens, t => t.Kind == LockedTokenKind.Negation && t.Text == "no");
    }

    [Fact]
    public void Extract_Locks_Standalone_Numbers_And_Dates()
    {
        var tokens = DeterministicPassThrough.ExtractLockedTokens("compared to 01/02/2024, segment 8 lesion");

        Assert.Contains(tokens, t => t.Kind == LockedTokenKind.Date && t.Text == "01/02/2024");
        Assert.Contains(tokens, t => t.Kind == LockedTokenKind.Number && t.Text == "8");
    }

    // ── End-to-end pass-through ──────────────────────────────────────────

    [Fact]
    public void Process_Applies_Corrections_Then_Normalizes_Then_Locks()
    {
        var pt = new DeterministicPassThrough();
        var rules = new[] { new CorrectionRule("hypo dense", "hypodense") };

        var result = pt.Process("three point two centimeter hypo dense lesion in the right lobe", rules);

        Assert.Contains("3.2 cm", result.CorrectedTranscript);
        Assert.Contains("hypodense", result.CorrectedTranscript);
        Assert.Contains(result.LockedTokens, t => t.Kind == LockedTokenKind.Measurement && t.Text == "3.2 cm");
        Assert.Contains(result.LockedTokens, t => t.Kind == LockedTokenKind.Laterality && t.Text == "right");
    }

    [Fact]
    public void Process_Empty_Transcript_Yields_Empty_Result()
    {
        var pt = new DeterministicPassThrough();
        var result = pt.Process("   ", null);
        Assert.Equal(string.Empty, result.CorrectedTranscript);
        Assert.Empty(result.LockedTokens);
    }
}
