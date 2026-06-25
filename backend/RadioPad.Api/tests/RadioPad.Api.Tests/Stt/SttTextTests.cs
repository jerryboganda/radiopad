using RadioPad.Application.Stt;
using Xunit;

namespace RadioPad.Api.Tests;

public class SttTextTests
{
    [Fact]
    public void Tokenize_Splits_On_Whitespace_With_Confidence()
    {
        var toks = SttText.Tokenize("  lungs are   clear ", 0.7);
        Assert.Equal(3, toks.Count);
        Assert.Equal("lungs", toks[0].Text);
        Assert.Equal("are", toks[1].Text);
        Assert.Equal("clear", toks[2].Text);
        Assert.All(toks, t => Assert.Equal(0.7, t.Confidence));
    }

    [Fact]
    public void Tokenize_Empty_Or_Null_Yields_Nothing()
    {
        Assert.Empty(SttText.Tokenize("", 0.9));
        Assert.Empty(SttText.Tokenize("   ", 0.9));
        Assert.Empty(SttText.Tokenize(null, 0.9));
    }
}
