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
    public void Strips_A_Bare_Language_Label_Line()
    {
        // The UBAG gateway's DOM scraper flattens a ```json block from the Gemini
        // web UI into the language tag + fenced body: "JSON\n{ ... }". The old
        // parser only handled ``` fences, so this dumped the whole blob (label and
        // all) into findings and left the other sections empty.
        var body = "JSON\n{\"indication\":\"headache\",\"impression\":\"Normal study.\"}";
        var map = ReportSectionJson.Parse(body);
        Assert.Equal("headache", map["indication"]);
        Assert.Equal("Normal study.", map["impression"]);
        Assert.False(map.ContainsKey("json"));
    }

    [Fact]
    public void Strips_A_Lowercase_Label_Line()
    {
        var map = ReportSectionJson.Parse("json\n{\"findings\":\"y\"}");
        Assert.Equal("y", map["findings"]);
    }

    [Fact]
    public void Extracts_Object_From_Surrounding_Prose()
    {
        var body = "Here is the report:\n{\"findings\":\"No acute abnormality.\"}\nLet me know if you need changes.";
        var map = ReportSectionJson.Parse(body);
        Assert.Equal("No acute abnormality.", map["findings"]);
    }

    [Fact]
    public void Real_Gemini_Web_Payload_With_Escaped_Newlines_Parses()
    {
        // Shape captured live from the UBAG gemini_web lane: bare "JSON" label,
        // multi-line findings with \n-escaped bullets across headings.
        var body = "JSON\n{\n\"technique\": \"Unenhanced CT KUB.\",\n"
            + "\"findings\": \"KIDNEYS:\\n\\u2022 Right lower pole calculus 5.5 x 7.1 mm (686 HU).\\n\\nBLADDER:\\n\\u2022 Normal.\",\n"
            + "\"impression\": \"1. Non-obstructive right nephrolithiasis.\",\n"
            + "\"recommendations\": \"No specific follow-up is indicated.\"\n}";
        var map = ReportSectionJson.Parse(body);
        Assert.Equal("Unenhanced CT KUB.", map["technique"]);
        Assert.Contains("KIDNEYS:", map["findings"]);
        Assert.Contains("\n\n", map["findings"]); // escaped \n became a real newline
        Assert.StartsWith("1. ", map["impression"]);
        Assert.False(string.IsNullOrEmpty(map["recommendations"]));
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
    public void Label_Like_Prose_Without_An_Object_Stays_Free_Text()
    {
        // A real report that merely begins with a short word must NOT be mistaken
        // for a dropped language label — with no JSON object it falls back whole.
        const string prose = "Normal\nchest radiograph with clear lung fields.";
        var map = ReportSectionJson.Parse(prose);
        Assert.Equal(prose, map["findings"]);
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
