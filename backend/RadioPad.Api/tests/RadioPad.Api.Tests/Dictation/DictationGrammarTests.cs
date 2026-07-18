using RadioPad.Application.Dictation;
using Xunit;

namespace RadioPad.Api.Tests;

/// <summary>Brief §5.4 — GBNF grammar generation that forces valid report-section JSON structure.</summary>
public class DictationGrammarTests
{
    [Fact]
    public void BuildSectionGrammar_Emits_Keys_In_Order_With_String_Rule()
    {
        var g = DictationGrammar.BuildSectionGrammar(new[] { "findings", "impression" });

        Assert.Contains("\"\\\"findings\\\":\"", g);   // GBNF literal for  "findings":
        Assert.Contains("\"\\\"impression\\\":\"", g);
        Assert.True(g.IndexOf("findings") < g.IndexOf("impression"));
        Assert.Contains("string ::=", g);
        Assert.Contains("root ::=", g);
    }

    [Fact]
    public void BuildSectionGrammar_Empty_Falls_Back_To_Default_Five_Sections()
    {
        var g = DictationGrammar.BuildSectionGrammar(System.Array.Empty<string>());
        Assert.Contains("indication", g);
        Assert.Contains("technique", g);
        Assert.Contains("findings", g);
        Assert.Contains("impression", g);
        Assert.Contains("recommendations", g);
    }

    [Fact]
    public void Default_Grammar_Is_The_Five_Section_Report()
    {
        Assert.Contains("recommendations", DictationGrammar.ReportSectionsGbnf);
        Assert.Contains("ws ::=", DictationGrammar.ReportSectionsGbnf);
    }
}
