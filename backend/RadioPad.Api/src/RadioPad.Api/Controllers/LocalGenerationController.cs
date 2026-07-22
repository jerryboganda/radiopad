using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RadioPad.Application.Abstractions;
using RadioPad.Application.Dictation;
using RadioPad.Application.Services;
using RadioPad.Domain.Entities;
using RadioPad.Domain.Enums;
using RadioPad.Infrastructure.Providers.Local;

namespace RadioPad.Api.Controllers;

/// <summary>
/// Whole-report generation against the on-device MedGemma model, entirely on this workstation.
///
/// <para>The normal <c>/api/reports/{id}/generate</c> path always runs through the hosted API — fine
/// for cloud providers, but wrong for an on-device model: the radiologist's whole point in picking
/// MedGemma is that nothing leaves the workstation, and a hosted container has neither the GGUF file
/// nor a running llama-server on its own loopback. This endpoint gives the desktop frontend a way to
/// run generation locally end to end when an on-device provider is selected, calling
/// <see cref="LlamaCppProvider"/> directly (bypassing <see cref="IAiGateway"/> — no tenant/PHI-policy
/// registry lookup is needed, since there is only ever one possible target: the model on this
/// machine) and reusing <see cref="ReportingService.BuildStructuredPrompt"/> so the prompt text is
/// byte-identical to the hosted path's default (no rulebook fetch here — see class remarks below).</para>
///
/// <para>Mirrors <see cref="LocalModelsController"/>'s safety model: not report/tenant-scoped, so it is
/// safe to serve anonymously — the desktop sidecar binds 127.0.0.1 only — and gated on
/// <see cref="LocalSttModels.IsEnabled"/> so a hosted build stays inert (whitelisted in
/// RadioPadBearerMiddleware so that inert result is a clean response, not a 401).</para>
///
/// <para><b>Rulebook scope (v1):</b> this path does not resolve a tenant's rulebook — the frontend has
/// no way to hand it one without a network round trip, which would reintroduce the exact "internet
/// involvement" this endpoint exists to avoid. It builds the same default consultant-grade prompt
/// <see cref="ReportingService.GenerateStructuredAsync"/> uses when a report has no rulebook bound,
/// via <c>BuildStructuredPrompt(report, rulebook: null, overrides: null)</c>. Tenant-specific rulebook
/// prompt-block overrides are a possible fast-follow, not a v1 requirement.</para>
/// </summary>
[ApiController]
[Route("api/local-generation")]
[AllowAnonymous]
public sealed class LocalGenerationController : ControllerBase
{
    private readonly IReadOnlyList<IAiProviderAdapter> _adapters;
    private readonly ILogger<LocalGenerationController> _log;

    public LocalGenerationController(
        IEnumerable<IAiProviderAdapter> adapters,
        ILogger<LocalGenerationController> log)
    {
        _adapters = adapters.ToList();
        _log = log;
    }

    public record GenerateReportDto(
        string? Modality, string? BodyPart, string? Contrast, int? Age, string? Gender,
        string? Indication, string? Findings);

    public record GeneratedReportSections(
        string Indication, string Technique, string Findings, string Impression, string Recommendations,
        string Provider, string Model, int LatencyMs);

    [HttpPost("report")]
    public async Task<IActionResult> GenerateReport([FromBody] GenerateReportDto dto, CancellationToken ct)
    {
        if (!LocalSttModels.IsEnabled())
            return StatusCode(StatusCodes.Status503ServiceUnavailable,
                new { error = "On-device generation is only available in the RadioPad desktop app.", kind = "stt_unavailable" });

        var llama = _adapters.FirstOrDefault(a => a.Id == LlamaCppProvider.AdapterId);
        if (llama is null)
            return StatusCode(StatusCodes.Status503ServiceUnavailable,
                new { error = "The on-device AI adapter is not registered.", kind = "adapter_unavailable" });

        var report = new Report
        {
            Study = new StudyContext
            {
                Modality = dto.Modality ?? "",
                BodyPart = dto.BodyPart ?? "",
                Contrast = dto.Contrast ?? "",
                Age = dto.Age,
                Gender = dto.Gender ?? "",
            },
            Indication = dto.Indication ?? "",
            Findings = dto.Findings ?? "",
        };

        // No rulebook: see class remarks — resolving one would require a network round trip.
        var (system, _, userPrompt) = ReportingService.BuildStructuredPrompt(report, rulebook: null, overrides: null);

        var provider = new ProviderConfig
        {
            Name = "local-medgemma",
            Adapter = LlamaCppProvider.AdapterId,
            Model = LocalModelCatalog.MedGemmaId,
            EndpointUrl = LlamaServerProcess.BaseUrl,
            Compliance = ProviderComplianceClass.LocalOnly,
            Enabled = true,
        };

        var request = new AiCompletionRequest(
            Provider: provider,
            SystemPrompt: system,
            UserPrompt: userPrompt,
            PromptVersion: "local-generate-v1",
            ContainsPhi: true)
        {
            // Report generation's hosted path enforces its JSON shape purely by prompt engineering;
            // the on-device path can do better and constrain decoding structurally, exactly like the
            // dictation formatter already does for the same model/runtime.
            Grammar = DictationGrammar.ReportSectionsGbnf,
        };

        AiResult result;
        try
        {
            result = await llama.CompleteAsync(request, ct);
        }
        catch (ProviderTransportException ex)
        {
            _log.LogWarning(ex, "On-device report generation failed to reach the local llama-server.");
            return StatusCode(StatusCodes.Status502BadGateway, new { error = ex.Message, kind = "provider_transport" });
        }

        var sections = ReportSectionJson.Parse(result.Text);
        return Ok(new GeneratedReportSections(
            Indication: sections.GetValueOrDefault("indication", string.Empty),
            Technique: sections.GetValueOrDefault("technique", string.Empty),
            Findings: ReportingService.FormatGeneratedFindings(sections.GetValueOrDefault("findings", string.Empty)),
            Impression: sections.GetValueOrDefault("impression", string.Empty),
            Recommendations: sections.GetValueOrDefault("recommendations", string.Empty),
            Provider: result.Provider,
            Model: result.Model,
            LatencyMs: result.LatencyMs));
    }
}
