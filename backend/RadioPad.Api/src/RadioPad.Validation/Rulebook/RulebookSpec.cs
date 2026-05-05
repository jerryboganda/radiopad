using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace RadioPad.Validation.Rulebook;

/// <summary>
/// Strongly-typed in-memory representation of a YAML rulebook.
/// Mirrors the schema described in §10.4 of the enterprise PRD.
/// </summary>
public class RulebookSpec
{
    public string RulebookId { get; set; } = "";
    public string Name { get; set; } = "";
    public string Version { get; set; } = "0.0.1";
    public string Owner { get; set; } = "";
    public string Status { get; set; } = "draft";
    public AppliesToSpec AppliesTo { get; set; } = new();
    public StyleSpec Style { get; set; } = new();
    public List<string> RequiredSections { get; set; } = new();
    public List<RuleSpec> Rules { get; set; } = new();
    public Dictionary<string, string> PromptBlocks { get; set; } = new();
    public OutputSchemaSpec? OutputSchema { get; set; }

    public class AppliesToSpec
    {
        public List<string> Modalities { get; set; } = new();
        public List<string> BodyParts { get; set; } = new();
        public List<string> ReportTypes { get; set; } = new();
    }

    public class StyleSpec
    {
        public string Tone { get; set; } = "concise_clinical";
        public int ImpressionMaxBullets { get; set; } = 5;
        public List<string> AvoidTerms { get; set; } = new();
        /// <summary>
        /// Iter-32 AI-008 — allow-list of follow-up phrases. Any AI-suggested
        /// or radiologist-typed recommendation that is not equal (case- and
        /// whitespace-insensitive) to one of these phrases is flagged by the
        /// <c>unauthorized_followup</c> validation rule. Empty list disables
        /// the rule entirely (back-compat for un-curated rulebooks).
        /// </summary>
        public List<string> ApprovedFollowups { get; set; } = new();
    }

    public class RuleSpec
    {
        public string Id { get; set; } = "";
        public string Severity { get; set; } = "warning";
        public string Description { get; set; } = "";
    }

    public class OutputSchemaSpec
    {
        public string Type { get; set; } = "object";
        public List<string> Required { get; set; } = new();
        public Dictionary<string, OutputSchemaPropertySpec> Properties { get; set; } = new();
    }

    public class OutputSchemaPropertySpec
    {
        public string Type { get; set; } = "string";
        public List<string> Enum { get; set; } = new();
    }

    private static readonly IDeserializer YamlReader = new DeserializerBuilder()
        .WithNamingConvention(UnderscoredNamingConvention.Instance)
        .IgnoreUnmatchedProperties()
        .Build();

    public static RulebookSpec FromYaml(string yaml)
    {
        var spec = YamlReader.Deserialize<RulebookSpec>(yaml) ?? new RulebookSpec();
        foreach (var rule in spec.Rules)
        {
            rule.Severity = NormalizeSeverity(rule.Severity);
        }
        return spec;
    }

    private static string NormalizeSeverity(string? severity) =>
        (severity ?? "").Trim().ToLowerInvariant() switch
        {
            "blocker" => "Blocker",
            "warning" => "Warning",
            "info" => "Info",
            _ => string.IsNullOrWhiteSpace(severity) ? "Warning" : severity.Trim(),
        };
}
