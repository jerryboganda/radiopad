using System.Text;

namespace RadioPad.Application.Dictation;

/// <summary>
/// Brief §5.4 — grammar-constrained decoding. Builds a GBNF grammar (llama.cpp format) that forces
/// the local MedGemma formatter to emit a valid report-section JSON object and nothing else. This
/// makes the model structurally unable to emit malformed output; tolerant JSON parsing remains the
/// secondary net. GBNF is preferred over an OpenAI-style JSON schema for small local models.
/// </summary>
public static class DictationGrammar
{
    /// <summary>The canonical ordered section keys of a dictated report draft.</summary>
    public static readonly IReadOnlyList<string> DefaultSections =
        new[] { "indication", "technique", "findings", "impression", "recommendations" };

    /// <summary>The default five-section report grammar matching the dictation formatter schema.</summary>
    public static readonly string ReportSectionsGbnf = BuildSectionGrammar(DefaultSections);

    // JSON string/whitespace sub-rules shared by every generated grammar (raw literal — no escaping).
    private const string SharedRules = """
        string ::= "\"" char* "\""
        char ::= [^"\\] | "\\" (["\\/bfnrt] | "u" hex hex hex hex)
        hex ::= [0-9a-fA-F]
        ws ::= [ \t\n]*
        """;

    /// <summary>
    /// Builds a GBNF grammar for a JSON object with exactly the given ordered string-valued keys.
    /// Falls back to the default five report sections when no keys are supplied.
    /// </summary>
    public static string BuildSectionGrammar(IReadOnlyList<string> sectionKeys)
    {
        var keys = sectionKeys is { Count: > 0 } ? sectionKeys : DefaultSections;

        const char dq = '"';
        const string bs = "\\";
        string KeyLit(string key) => $"{dq}{bs}{dq}{key}{bs}{dq}:{dq}"; // GBNF literal for  "key":

        var sb = new StringBuilder();
        sb.Append("root ::= \"{\" ws");
        for (var i = 0; i < keys.Count; i++)
        {
            sb.Append(' ').Append(KeyLit(keys[i])).Append(" ws string");
            sb.Append(i < keys.Count - 1 ? " \",\" ws" : " ws");
        }
        sb.Append(" \"}\"");
        sb.Append('\n');
        sb.Append(SharedRules);
        sb.Append('\n');
        return sb.ToString();
    }
}
