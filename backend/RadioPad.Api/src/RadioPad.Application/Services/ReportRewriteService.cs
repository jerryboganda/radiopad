using RadioPad.Application.Abstractions;
using RadioPad.Domain.Entities;
using RadioPad.Domain.ValueObjects;

namespace RadioPad.Application.Services;

/// <summary>
/// Iter-30 (RPT-007) — Report rewrite modes. Calls <see cref="IAiGateway"/>
/// with mode-specific system prompts and returns the rewritten section text.
/// Output is NOT persisted — the frontend lets the radiologist accept or
/// reject. PHI policy + audit chain are unchanged from the underlying gateway
/// (which audits <see cref="Domain.Enums.AuditAction.AiRequest"/>/<see
/// cref="Domain.Enums.AuditAction.AiResponse"/>).
/// </summary>
public interface IReportRewriteService
{
    /// <summary>
    /// Run a rewrite. <paramref name="sections"/> is an optional whitelist
    /// of sections to include in the user prompt — when null, every populated
    /// section is included. Throws <see cref="ProviderPolicyException"/> when
    /// PHI policy blocks the request (gateway already audits the block).
    /// </summary>
    Task<ReportRewriteResult> RewriteAsync(
        Tenant tenant,
        Report report,
        ProviderConfig provider,
        ReportRewriteMode mode,
        IReadOnlyCollection<string>? sections,
        CancellationToken ct);
}

public enum ReportRewriteMode
{
    Concise = 0,
    Formal = 1,
    PatientFriendly = 2,
    ReferringSummary = 3,
}

public sealed record ReportRewriteResult(
    string Text,
    string Provider,
    string Model,
    int LatencyMs,
    string PromptVersion,
    ReportRewriteMode Mode);

public sealed class ReportRewriteService : IReportRewriteService
{
    private readonly IAiGateway _gateway;

    public ReportRewriteService(IAiGateway gateway)
    {
        _gateway = gateway;
    }

    public const string PromptVersion = "rewrite-v1.iter30";

    public async Task<ReportRewriteResult> RewriteAsync(
        Tenant tenant,
        Report report,
        ProviderConfig provider,
        ReportRewriteMode mode,
        IReadOnlyCollection<string>? sections,
        CancellationToken ct)
    {
        var (system, body) = BuildPrompt(report, mode, sections);
        var containsPhi = ReportingService.ContainsPhi(report) || ReportingService.ContainsPhiText(system, body);
        var result = await _gateway.RouteAsync(tenant, new AiCompletionRequest(
            Provider: provider,
            SystemPrompt: system,
            UserPrompt: body,
            PromptVersion: PromptVersion,
            ContainsPhi: containsPhi), ct);
        return new ReportRewriteResult(
            result.Text, result.Provider, result.Model, result.LatencyMs, result.PromptVersion, mode);
    }

    private static (string system, string userPrompt) BuildPrompt(
        Report report,
        ReportRewriteMode mode,
        IReadOnlyCollection<string>? sections)
    {
        var system = mode switch
        {
            ReportRewriteMode.Concise => "You are a board-certified radiologist's editorial assistant. " +
                "Rewrite the report to be more concise without changing clinical meaning, negations, " +
                "measurements, or laterality. Preserve all numeric values and section structure.",
            ReportRewriteMode.Formal => "You are a board-certified radiologist's editorial assistant. " +
                "Rewrite the report in a formal clinical tone suitable for a teaching hospital. Do not " +
                "change clinical meaning, negations, measurements, or laterality.",
            ReportRewriteMode.PatientFriendly => "You are a clinical communicator. Rewrite the IMPRESSION " +
                "for a non-clinical patient at an 8th-grade reading level. Do not provide diagnostic " +
                "advice, do not invent findings, and never include PHI. Preserve laterality and " +
                "measurements where clinically meaningful.",
            ReportRewriteMode.ReferringSummary => "You are summarising a radiology report for the " +
                "referring clinician. Produce ONE paragraph (no bullets) that highlights the impression " +
                "and any actionable findings. Do not change clinical meaning or invent findings.",
            _ => "Echo the input unchanged.",
        };

        var include = sections is null
            ? new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                { "Indication", "Technique", "Comparison", "Findings", "Impression", "Recommendations" }
            : new HashSet<string>(sections, StringComparer.OrdinalIgnoreCase);

        var sb = new System.Text.StringBuilder();
        sb.Append("Modality: ").Append(report.Study.Modality).Append('\n');
        sb.Append("Body part: ").Append(report.Study.BodyPart).Append('\n');
        sb.Append("Indication: ").Append(report.Study.Indication).Append("\n\n");
        Append(sb, "INDICATION", report.Indication, include);
        Append(sb, "TECHNIQUE", report.Technique, include);
        Append(sb, "COMPARISON", report.Comparison, include);
        Append(sb, "FINDINGS", report.Findings, include);
        Append(sb, "IMPRESSION", report.Impression, include);
        Append(sb, "RECOMMENDATIONS", report.Recommendations, include);
        sb.Append("\nINSTRUCTION: Rewrite the report under the rules in the system prompt. ");
        sb.Append("Output the rewritten report as plain text with the same section headings. ");
        sb.Append("Do not sign the report.");

        return (system, sb.ToString());
    }

    private static void Append(System.Text.StringBuilder sb, string heading, string content, HashSet<string> include)
    {
        // The whitelist uses Title-cased section names; map heading back to that form.
        var canonical = heading switch
        {
            "INDICATION" => "Indication",
            "TECHNIQUE" => "Technique",
            "COMPARISON" => "Comparison",
            "FINDINGS" => "Findings",
            "IMPRESSION" => "Impression",
            "RECOMMENDATIONS" => "Recommendations",
            _ => heading,
        };
        if (!include.Contains(canonical)) return;
        if (string.IsNullOrWhiteSpace(content)) return;
        sb.Append(heading).Append(":\n").Append(content.Trim()).Append("\n\n");
    }
}
