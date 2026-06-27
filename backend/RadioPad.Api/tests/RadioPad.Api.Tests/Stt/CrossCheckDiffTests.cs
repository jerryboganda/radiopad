using RadioPad.Application.Stt;
using Xunit;

namespace RadioPad.Api.Tests;

/// <summary>
/// Unit tests for the cross-check diff: turning a reconciled N-way result into the
/// editor's original→corrected list, anchored to char ranges in the live draft.
/// </summary>
public class CrossCheckDiffTests
{
    private static SpanVote V(string id, string text, double c) => new(id, text, c);

    [Fact]
    public void Tokenize_Returns_Char_Ranges()
    {
        var toks = CrossCheckDiff.Tokenize("the left lung");
        Assert.Equal(3, toks.Count);
        Assert.Equal(("left", 4, 8), toks[1]);
    }

    [Fact]
    public void Changed_Word_Becomes_A_Correction_Anchored_To_The_Draft()
    {
        const string live = "the left lung is clear";
        var spans = new[]
        {
            new ReconciledSpan("the", false, null, "live"),
            new ReconciledSpan("right", true, "disagreement", "parakeet", OriginalText: "left",
                Votes: new[] { V("live", "left", 0.5), V("parakeet", "right", 0.9), V("medical", "right", 0.9) }),
            new ReconciledSpan("lung", false, null, "live"),
            new ReconciledSpan("is", false, null, "live"),
            new ReconciledSpan("clear", false, null, "live"),
        };
        var reconciled = new ReconciledResult("the right lung is clear", spans);

        var corrections = CrossCheckDiff.BuildCorrections(live, reconciled, "findings");

        var c = Assert.Single(corrections);
        Assert.Equal("left", c.OriginalText);
        Assert.Equal("right", c.CorrectedText);
        Assert.Equal(4, c.StartOffset);
        Assert.Equal(8, c.EndOffset);
        Assert.Equal("findings", c.SectionKey);
        Assert.Equal("warning", c.Severity);
        Assert.Equal("asr_disagreement", c.Category);
    }

    [Fact]
    public void Safety_Change_Is_Severity_Safety()
    {
        var spans = new[]
        {
            new ReconciledSpan("no", true, "safety", "parakeet", OriginalText: "known",
                Votes: new[] { V("live", "known", 0.5), V("parakeet", "no", 0.95) }),
            new ReconciledSpan("acute", false, null, "live"),
            new ReconciledSpan("findings", false, null, "live"),
        };
        // backbone built from "known acute findings" so offsets map to the draft
        var corrections = CrossCheckDiff.BuildCorrections("known acute findings",
            new ReconciledResult("no acute findings", spans));

        var c = Assert.Single(corrections);
        Assert.Equal("safety", c.Severity);
        Assert.Equal("safety", c.Category);
        Assert.Equal(0, c.StartOffset);
        Assert.Equal(5, c.EndOffset); // "known"
    }

    [Fact]
    public void Inserted_Word_Is_A_ZeroWidth_Correction()
    {
        const string live = "the lung";
        var spans = new[]
        {
            new ReconciledSpan("the", false, null, "live"),
            new ReconciledSpan("left", true, "insert", "parakeet", OriginalText: "",
                Votes: new[] { V("parakeet", "left", 0.9), V("medical", "left", 0.9) }),
            new ReconciledSpan("lung", false, null, "live"),
        };
        var corrections = CrossCheckDiff.BuildCorrections(live, new ReconciledResult("the left lung", spans));

        var c = Assert.Single(corrections);
        Assert.Equal("", c.OriginalText);
        Assert.Equal("left", c.CorrectedText);
        Assert.Equal(4, c.StartOffset); // before "lung"
        Assert.Equal(4, c.EndOffset);
        Assert.Equal("insertion", c.Category);
    }

    [Fact]
    public void Unchanged_Transcript_Produces_No_Corrections()
    {
        const string live = "lungs are clear";
        var spans = new[]
        {
            new ReconciledSpan("lungs", false, null, "live"),
            new ReconciledSpan("are", false, null, "live"),
            new ReconciledSpan("clear", false, null, "live"),
        };
        var corrections = CrossCheckDiff.BuildCorrections(live, new ReconciledResult(live, spans));
        Assert.Empty(corrections);
    }

    // ---- LLM medical-review parse + anchor ------------------------------

    [Fact]
    public void ParseLlmCorrections_Reads_A_Json_Array()
    {
        const string body = """
            [{"original":"left","corrected":"right","reason":"laterality","category":"laterality","severity":"safety"}]
            """;
        var items = CrossCheckDiff.ParseLlmCorrections(body);
        var it = Assert.Single(items);
        Assert.Equal("left", it.Original);
        Assert.Equal("right", it.Corrected);
        Assert.Equal("safety", it.Severity);
    }

    [Fact]
    public void ParseLlmCorrections_Strips_Code_Fences_And_Reads_Wrapped_Object()
    {
        const string body = "```json\n{\"corrections\":[{\"original\":\"no\",\"corrected\":\"known\"}]}\n```";
        var items = CrossCheckDiff.ParseLlmCorrections(body);
        Assert.Single(items);
        Assert.Equal("known", items[0].Corrected);
    }

    [Fact]
    public void ParseLlmCorrections_Tolerates_Free_Text()
    {
        Assert.Empty(CrossCheckDiff.ParseLlmCorrections("no corrections needed"));
        Assert.Empty(CrossCheckDiff.ParseLlmCorrections(""));
    }

    [Fact]
    public void AnchorLlmCorrections_Locates_Original_And_Maps_Severity()
    {
        var items = new[]
        {
            new LlmCorrectionItem("left", "right", "laterality", "laterality", "safety"),
        };
        var corrections = CrossCheckDiff.AnchorLlmCorrections("the left lung", items, "findings");

        var c = Assert.Single(corrections);
        Assert.Equal(4, c.StartOffset);
        Assert.Equal(8, c.EndOffset);
        Assert.Equal("llm", c.Source);
        Assert.Equal("safety", c.Severity);
        Assert.Equal("findings", c.SectionKey);
    }

    [Fact]
    public void AnchorLlmCorrections_Drops_Unfindable_Or_Empty_Items()
    {
        var items = new[]
        {
            new LlmCorrectionItem("nonexistent", "x", null, null, null),
            new LlmCorrectionItem("lung", null, null, null, null), // no corrected
        };
        Assert.Empty(CrossCheckDiff.AnchorLlmCorrections("the left lung", items, null));
    }
}
