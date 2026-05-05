using Microsoft.Extensions.Logging;
using RadioPad.Application.Abstractions;
using RadioPad.Domain.Entities;
using RadioPad.Validation.Rulebook;

namespace RadioPad.Application.Services;

/// <summary>
/// Iter-31 AI-001 — dictation cleanup pipeline. Routes through
/// <see cref="IAiGateway"/> using the <c>dictation_cleanup</c> prompt block
/// from the active rulebook (or tenant override), falling back to a
/// clinically conservative default. The model is asked to emit a strict
/// section-keyed JSON object so the editor can adopt one section at a
/// time without re-parsing free text.
/// </summary>
public class DictationCleanupService : IDictationCleanupService
{
    private const string PromptVersion = "v1.iter31.dictation_cleanup";

    private readonly IAiGateway _gateway;
    private readonly IRulebookStore _rulebooks;
    private readonly IProviderRouter _router;
    private readonly IPromptOverrideStore? _overrides;
    private readonly ILogger<DictationCleanupService> _log;

    public DictationCleanupService(
        IAiGateway gateway,
        IRulebookStore rulebooks,
        IProviderRouter router,
        ILogger<DictationCleanupService> log,
        IPromptOverrideStore? overrides = null)
    {
        _gateway = gateway;
        _rulebooks = rulebooks;
        _router = router;
        _log = log;
        _overrides = overrides;
    }

    public async Task<DictationCleanupResult> CleanupAsync(
        Tenant tenant, User user, Report report, string rawDictation, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(rawDictation))
            throw new ArgumentException("rawDictation is required.", nameof(rawDictation));

        var containsPhi = ReportingService.ContainsPhi(report)
            || LooksLikePhi(rawDictation);

        var provider = await _router.SelectAsync(tenant, containsPhi, ct)
            ?? throw new ProviderPolicyException(
                "No enabled provider matches the tenant's PHI / compliance requirements.");

        var rulebookEntity = report.RulebookId is null
            ? null
            : (await _rulebooks.ListAsync(tenant.Id, ct))
                .FirstOrDefault(r => r.Id == report.RulebookId.Value);
        var rulebook = rulebookEntity is null ? null : RulebookSpec.FromYaml(rulebookEntity.SourceYaml);
        IReadOnlyDictionary<string, string> overrides = _overrides is null || rulebookEntity is null
            ? new Dictionary<string, string>()
            : await _overrides.LoadAsync(tenant.Id, rulebookEntity.RulebookId, ct);

        var instructions = (overrides.TryGetValue("dictation_cleanup", out var ov) && !string.IsNullOrWhiteSpace(ov))
            ? ov
            : rulebook?.PromptBlocks.GetValueOrDefault("dictation_cleanup")
            ?? DefaultInstruction;

        var system = rulebook?.PromptBlocks.GetValueOrDefault("system")
            ?? "You are assisting a board-certified radiologist. Never invent findings. " +
               "Preserve every measurement, laterality, and negation exactly as dictated. " +
               "Output only the requested JSON object — no preface, no trailing prose.";

        var userPrompt = $$"""
            Modality: {{report.Study.Modality}}
            Body part: {{report.Study.BodyPart}}
            Indication: {{report.Study.Indication}}

            DICTATION:
            {{rawDictation}}

            INSTRUCTION:
            {{instructions}}

            Respond with a single JSON object exactly matching this schema (use empty strings for sections not dictated):
            {
              "indication": "",
              "technique": "",
              "findings": "",
              "impression": "",
              "recommendations": ""
            }
            """;

        var result = await _gateway.RouteAsync(tenant, new AiCompletionRequest(
            Provider: provider,
            SystemPrompt: system,
            UserPrompt: userPrompt,
            PromptVersion: PromptVersion,
            ContainsPhi: containsPhi), ct);

        var sections = ParseSections(result.Text);
        return new DictationCleanupResult(
            Indication: sections.GetValueOrDefault("indication", string.Empty),
            Technique: sections.GetValueOrDefault("technique", string.Empty),
            Findings: sections.GetValueOrDefault("findings", string.Empty),
            Impression: sections.GetValueOrDefault("impression", string.Empty),
            Recommendations: sections.GetValueOrDefault("recommendations", string.Empty),
            Provider: result.Provider,
            Model: result.Model,
            LatencyMs: result.LatencyMs,
            PromptVersion: PromptVersion);
    }

    private const string DefaultInstruction =
        "Rewrite the dictation into clean grammatical prose for each report section. " +
        "Do not add findings the radiologist did not dictate. Preserve all numbers, sides, " +
        "negations, and clinical hedging. If a section was not dictated, return an empty string for it.";

    private static bool LooksLikePhi(string blob)
    {
        if (string.IsNullOrEmpty(blob)) return false;
        if (System.Text.RegularExpressions.Regex.IsMatch(blob, @"\bMRN[:\s]*\d{4,}\b",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase)) return true;
        if (System.Text.RegularExpressions.Regex.IsMatch(blob, @"\b\d{1,2}/\d{1,2}/\d{2,4}\b")) return true;
        if (System.Text.RegularExpressions.Regex.IsMatch(blob, @"\b(Mr|Mrs|Ms|Dr)\.\s+[A-Z][a-z]+\b")) return true;
        return false;
    }

    private static Dictionary<string, string> ParseSections(string? body)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(body)) return map;
        var trimmed = body.Trim();
        // Strip code fences if the model wrapped JSON in ```json ... ```
        if (trimmed.StartsWith("```"))
        {
            var nl = trimmed.IndexOf('\n');
            if (nl >= 0) trimmed = trimmed[(nl + 1)..];
            if (trimmed.EndsWith("```")) trimmed = trimmed[..^3];
            trimmed = trimmed.Trim();
        }
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(trimmed);
            foreach (var prop in doc.RootElement.EnumerateObject())
            {
                if (prop.Value.ValueKind == System.Text.Json.JsonValueKind.String)
                {
                    map[prop.Name] = prop.Value.GetString() ?? string.Empty;
                }
            }
        }
        catch (System.Text.Json.JsonException)
        {
            // Model returned free text — surface it as the Findings section so
            // the radiologist can still see something useful, but leave the
            // remaining sections empty so the editor does not overwrite them.
            map["findings"] = body;
        }
        return map;
    }
}
