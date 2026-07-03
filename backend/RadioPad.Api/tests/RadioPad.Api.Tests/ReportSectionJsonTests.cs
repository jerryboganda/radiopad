using RadioPad.Application.Services;
using Xunit;

namespace RadioPad.Api.Tests;

/// <summary>
/// Unit coverage for <see cref="ReportSectionJson.Parse"/> — the shared parser
/// behind both dictation cleanup and full-report generation. Guards the JSON-fence
/// stripping and the free-text fallback that keep a stray model response from
/// wiping the editor.
/// </summary>
public class ReportSectionJsonTests
{
    [Fact]
    public void Parses_A_Plain_Section_Object()
    {
        var map = ReportSectionJson.Parse(
            """{"indication":"headache","findings":"No acute infarct.","impression":"Normal study."}""");

        Assert.Equal("headache", map["indication"]);
        Assert.Equal("No acute infarct.", map["findings"]);
        Assert.Equal("Normal study.", map["impression"]);
    }

    [Fact]
    public void Section_Keys_Are_Case_Insensitive()
    {
        var map = ReportSectionJson.Parse("""{"Findings":"x"}""");
        Assert.Equal("x", map["findings"]);
        Assert.Equal("x", map["FINDINGS"]);
    }

    [Fact]
    public void Strips_A_Json_Code_Fence()
    {
        var body = "```json\n{\"impression\":\"clear\"}\n```";
        var map = ReportSectionJson.Parse(body);
        Assert.Equal("clear", map["impression"]);
    }

    [Fact]
    public void Strips_A_Bare_Code_Fence()
    {
        var body = "```\n{\"findings\":\"y\"}\n```";
        var map = ReportSectionJson.Parse(body);
        Assert.Equal("y", map["findings"]);
    }

    [Fact]
    public void Non_Json_Falls_Back_To_Findings()
    {
        const string prose = "Large left pleural effusion. No pneumothorax.";
        var map = ReportSectionJson.Parse(prose);

        Assert.Equal(prose, map["findings"]);
        Assert.False(map.ContainsKey("impression"));
    }

    [Fact]
    public void Ignores_Non_String_Properties()
    {
        var map = ReportSectionJson.Parse("""{"findings":"ok","confidence":0.9,"flags":[]}""");
        Assert.Equal("ok", map["findings"]);
        Assert.False(map.ContainsKey("confidence"));
        Assert.False(map.ContainsKey("flags"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Empty_Input_Returns_Empty_Map(string? body)
    {
        Assert.Empty(ReportSectionJson.Parse(body));
    }
}
