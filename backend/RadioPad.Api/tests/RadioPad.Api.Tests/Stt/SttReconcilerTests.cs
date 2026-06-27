using RadioPad.Application.Stt;
using Xunit;

namespace RadioPad.Api.Tests;

/// <summary>
/// Phase 2 — unit tests for the ROVER reconciler: word-alignment + calibrated
/// confidence voting + disagreement / safety flagging. The "counter-detect"
/// mechanism that turns two engines into one reviewed transcript.
/// </summary>
public class SttReconcilerTests
{
    private static SttToken T(string w, double c) => new(w, c);

    private static EngineTranscript Eng(string id, params SttToken[] tokens) => new(id, tokens);

    [Fact]
    public void Identical_Confident_Hypotheses_Produce_No_Flags()
    {
        var a = Eng("parakeet", T("lungs", 0.95), T("are", 0.95), T("clear", 0.95));
        var b = Eng("whisper", T("lungs", 0.92), T("are", 0.92), T("clear", 0.92));

        var r = SttReconciler.Reconcile(a, b);

        Assert.Equal("lungs are clear", r.Text);
        Assert.Equal(0, r.FlaggedCount);
        Assert.All(r.Spans, s => Assert.Equal("both", s.Source));
    }

    [Fact]
    public void Confident_WideMargin_Disagreement_Picks_Winner_Unflagged()
    {
        var a = Eng("parakeet", T("lung", 0.95));
        var b = Eng("whisper", T("long", 0.60));

        var r = SttReconciler.Reconcile(a, b);

        Assert.Equal("lung", r.Text);
        Assert.False(r.Spans[0].Flagged);
        Assert.Equal("parakeet", r.Spans[0].Source);
    }

    [Fact]
    public void Narrow_Disagreement_Is_Flagged()
    {
        var a = Eng("parakeet", T("lung", 0.70));
        var b = Eng("whisper", T("long", 0.65));

        var r = SttReconciler.Reconcile(a, b);

        Assert.Equal("lung", r.Text);
        Assert.True(r.Spans[0].Flagged);
        Assert.Equal("disagreement", r.Spans[0].Reason);
    }

    [Fact]
    public void Insertion_Deletion_Is_Flagged()
    {
        var a = Eng("parakeet", T("the", 0.9), T("lung", 0.9));
        var b = Eng("whisper", T("the", 0.9));

        var r = SttReconciler.Reconcile(a, b);

        Assert.Equal("the lung", r.Text);
        Assert.Equal(1, r.FlaggedCount);
        var flagged = Assert.Single(r.Spans, s => s.Flagged);
        Assert.Equal("lung", flagged.Text);
        Assert.Equal("insert-delete", flagged.Reason);
    }

    [Fact]
    public void Safety_Token_Is_Flagged_Even_When_Engines_Agree()
    {
        var a = Eng("parakeet", T("left", 0.97));
        var b = Eng("whisper", T("left", 0.97));

        var r = SttReconciler.Reconcile(a, b);

        Assert.True(r.Spans[0].Flagged);
        Assert.Equal("safety", r.Spans[0].Reason);
    }

    [Fact]
    public void Numeric_Measurement_Is_Flagged()
    {
        var a = Eng("parakeet", T("1.2", 0.95), T("cm", 0.95));
        var b = Eng("whisper", T("1.2", 0.95), T("cm", 0.95));

        var r = SttReconciler.Reconcile(a, b);

        Assert.Equal(1, r.FlaggedCount);
        Assert.Equal("safety", r.Spans[0].Reason); // "1.2"
        Assert.False(r.Spans[1].Flagged);           // "cm"
    }

    [Fact]
    public void Low_Confidence_Agreement_Is_Flagged()
    {
        var a = Eng("parakeet", T("haze", 0.40));
        var b = Eng("whisper", T("haze", 0.45));

        var r = SttReconciler.Reconcile(a, b);

        Assert.True(r.Spans[0].Flagged);
        Assert.Equal("low-confidence", r.Spans[0].Reason);
    }

    [Fact]
    public void EngineScale_Calibration_Can_Flip_The_Winner()
    {
        // Raw: parakeet 0.9 > whisper 0.8. With a 0.5 scale on the (over-confident)
        // transducer, calibrated parakeet 0.45 < whisper 0.8 -> whisper wins.
        var a = Eng("parakeet", T("alpha", 0.9));
        var b = Eng("whisper", T("beta", 0.8));
        var opts = new ReconcileOptions
        {
            EngineScale = new Dictionary<string, double> { ["parakeet"] = 0.5 },
        };

        var r = SttReconciler.Reconcile(a, b, opts);

        Assert.Equal("beta", r.Text);
        Assert.Equal("whisper", r.Spans[0].Source);
    }

    [Fact]
    public void Word_Equality_Ignores_Case_And_Punctuation()
    {
        var a = Eng("parakeet", T("Clear.", 0.9));
        var b = Eng("whisper", T("clear", 0.9));

        var r = SttReconciler.Reconcile(a, b);

        Assert.False(r.Spans[0].Flagged);
        Assert.Equal("both", r.Spans[0].Source);
    }

    // ---- N-way cross-check (ReconcileMany) -------------------------------

    [Fact]
    public void ReconcileMany_SingleHypothesis_ReturnedVerbatim_NoCorrections()
    {
        var only = Eng("whisper", T("lungs", 0.9), T("clear", 0.9));

        var r = SttReconciler.ReconcileMany(new[] { only });

        Assert.Equal("lungs clear", r.Text);
        Assert.All(r.Spans, s => Assert.Null(s.OriginalText)); // nothing changed
    }

    [Fact]
    public void ReconcileMany_TwoOthersOutvote_LiveDraft_RecordingOriginal()
    {
        // Backbone (live draft) = whisper "long" @0.5; two other engines say "lung".
        var live = Eng("whisper", T("long", 0.50));
        var p = Eng("parakeet", T("lung", 0.90));
        var k = Eng("medical", T("lung", 0.90));

        var r = SttReconciler.ReconcileMany(new[] { live, p, k });

        Assert.Equal("lung", r.Text);
        var span = r.Spans[0];
        Assert.Equal("long", span.OriginalText); // original→corrected diff
        Assert.Equal("lung", span.Text);
        Assert.NotNull(span.Votes);
        Assert.Equal(3, span.Votes!.Count);
    }

    [Fact]
    public void ReconcileMany_TieKeepsBackboneWord()
    {
        // Equal summed confidence → the dictated backbone word wins (no change).
        var live = Eng("whisper", T("alpha", 0.80));
        var p = Eng("parakeet", T("beta", 0.80));

        var r = SttReconciler.ReconcileMany(new[] { live, p });

        Assert.Equal("alpha", r.Text);
        Assert.Null(r.Spans[0].OriginalText);
    }

    [Fact]
    public void ReconcileMany_NeverDeletes_A_Dictated_Word()
    {
        var live = Eng("whisper", T("the", 0.9), T("nodule", 0.9));
        var p = Eng("parakeet", T("the", 0.9));
        var k = Eng("medical", T("the", 0.9));

        var r = SttReconciler.ReconcileMany(new[] { live, p, k });

        Assert.Equal("the nodule", r.Text); // backbone word retained despite no support
    }

    [Fact]
    public void ReconcileMany_InsertsOnlyOnUnanimousAgreement()
    {
        var live = Eng("whisper", T("the", 0.9), T("lung", 0.9));
        var p = Eng("parakeet", T("the", 0.9), T("left", 0.9), T("lung", 0.9));
        var k = Eng("medical", T("the", 0.9), T("left", 0.9), T("lung", 0.9));

        var r = SttReconciler.ReconcileMany(new[] { live, p, k });

        Assert.Equal("the left lung", r.Text);
        var inserted = Assert.Single(r.Spans, s => s.OriginalText == string.Empty);
        Assert.Equal("left", inserted.Text);
        Assert.True(inserted.Flagged);
    }

    [Fact]
    public void ReconcileMany_DropsInsertion_WithoutUnanimousAgreement()
    {
        var live = Eng("whisper", T("the", 0.9), T("lung", 0.9));
        var p = Eng("parakeet", T("the", 0.9), T("left", 0.9), T("lung", 0.9));
        var k = Eng("medical", T("the", 0.9), T("lung", 0.9)); // no "left"

        var r = SttReconciler.ReconcileMany(new[] { live, p, k });

        Assert.Equal("the lung", r.Text);
        Assert.DoesNotContain(r.Spans, s => s.OriginalText == string.Empty);
    }

    [Fact]
    public void ReconcileMany_PreservesSafetyFlag()
    {
        var live = Eng("whisper", T("left", 0.97));
        var p = Eng("parakeet", T("left", 0.97));

        var r = SttReconciler.ReconcileMany(new[] { live, p });

        Assert.True(r.Spans[0].Flagged);
        Assert.Equal("safety", r.Spans[0].Reason);
    }
}
