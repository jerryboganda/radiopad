using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RadioPad.Application.Abstractions;
using RadioPad.Application.Dictation;
using RadioPad.Application.Services;
using RadioPad.Domain.Entities;
using RadioPad.Domain.Enums;
using RadioPad.Domain.ValueObjects;
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
/// machine).</para>
///
/// <para>Mirrors <see cref="LocalModelsController"/>'s safety model: not report/tenant-scoped, so it is
/// safe to serve anonymously — the desktop sidecar binds 127.0.0.1 only — and gated on
/// <see cref="LocalSttModels.IsEnabled"/> so a hosted build stays inert (whitelisted in
/// RadioPadBearerMiddleware so that inert result is a clean response, not a 401).</para>
///
/// <para><b>Own prompt, not <see cref="ReportingService.BuildStructuredPrompt"/> (deliberate).</b> That
/// prompt is tuned for and validated against large cloud models (see
/// ConsultantGradeGeneratePromptTests) and asks for heading/bullet-formatted findings purely through
/// prose instructions. Empirically that does NOT work on MedGemma 1.5 4B (Q4_K_M): tested directly
/// against the local llama-server with and without the GBNF grammar, it produced a single
/// undifferentiated prose paragraph either way — a small-model capability gap, not a bug in the
/// grammar or the prompt's content. A short instruction plus a worked example reliably gets it to
/// reproduce the heading+bullet layout instead (verified the same way) — but the example MUST use
/// bracketed placeholders, not concrete clinical values: an earlier version with concrete example
/// values (a specific measurement and organ) fixed the formatting but caused the model to sometimes
/// copy those exact fabricated values into an unrelated case (a phantom kidney stone in a chest CT
/// that never mentioned kidneys) — a hallucination risk that placeholder-only examples eliminated
/// in testing without losing the formatting improvement. A long, exhaustive systematic review can
/// also make a 4B model degenerate into repeating its last line until it exhausts the token budget
/// once it runs out of new content — <see cref="AiCompletionRequest.RepeatPenalty"/> fixes that; a
/// strong penalty (1.3) was tested first and rejected because it also pushed the model to fabricate
/// a different measurement rather than repeat the dictated one verbatim, a moderate one (1.1) did
/// not.</para>
///
/// <para><b>Rulebook scope (v1):</b> this path does not resolve a tenant's rulebook — the frontend has
/// no way to hand it one without a network round trip, which would reintroduce the exact "internet
/// involvement" this endpoint exists to avoid. Tenant-specific rulebook prompt-block overrides are a
/// possible fast-follow, not a v1 requirement.</para>
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

    private const string SystemPrompt =
        "You are a radiologist's report-writing assistant. You write radiology report FINDINGS as a " +
        "list of organ/system headings, each followed by short bullet statements. You NEVER write " +
        "findings as flowing prose paragraphs. Preserve every dictated measurement, laterality, and " +
        "negation exactly; never invent a finding that was not dictated or implied by the systematic " +
        "review of normal anatomy.";

    /// <summary>
    /// Formatting rule stated FIRST and reinforced with a worked example — proven far more effective on
    /// a small model than the equivalent instruction expressed only as abstract prose (see class
    /// remarks). The example uses bracketed PLACEHOLDERS instead of concrete clinical values
    /// deliberately: an earlier version with concrete example values (8 mm calculus, 12 mm gallstone)
    /// measurably fixed the formatting, but the model would sometimes copy those exact fabricated
    /// numbers into an unrelated case's findings (e.g. inventing a phantom kidney stone in a chest CT
    /// report that never mentioned kidneys) — a hallucination risk, not just a cosmetic one. Removing
    /// every concrete, plausible-looking value from the example eliminated that leakage in testing
    /// while still teaching the layout, including the paired-organ-per-side split.
    /// </summary>
    private const string InstructionsTemplate = """
        Write the findings as a list of organ/system headings, each followed by short bullet statements.
        Follow this EXACT layout — the example below shows the LAYOUT ONLY, using bracketed placeholders
        instead of real content, because it is not part of this case:

        EXAMPLE LAYOUT (placeholders in brackets — never copy an organ name, number, or finding from this
        example into your answer; only copy the layout style):
        <ORGAN NAME>:
        • <one short finding for this organ>
        • <a pertinent negative for this organ, if relevant>

        <PAIRED ORGAN> RIGHT:
        • <finding specific to the right side>

        <PAIRED ORGAN> LEFT:
        • <finding specific to the left side, described separately because it differs from the right>

        Rules:
        - One heading per organ/system, in UPPERCASE, alone on its own line, ending with a colon.
        - Each bullet starts with "• " and stays on one line.
        - Leave one blank line between each organ/system group.
        - ONLY include organs and systems that this study's modality/body part actually images. Do not
          mention an organ from the example layout above unless this real case also needs it.
        - When a paired organ (kidneys, lungs, adrenal glands, etc.) has a dictated finding on only one
          side, give it its OWN heading per side (e.g. "RIGHT KIDNEY:" / "LEFT KIDNEY:") and describe the
          actual finding under the affected side — never summarise a paired organ as fully normal
          "bilaterally" when one side has a dictated finding.
        - Cover every organ routinely assessed on THIS study, not only the ones with dictated findings —
          but never an organ outside the field of view of this study's modality and body part.
        - Never write a paragraph of connected sentences. Every line is either a heading or a bullet.

        Give the impression as short numbered lines ("1. ...", "2. ...") synthesising the findings by
        clinical significance. Give recommendations as one or two short sentences, or state that no
        specific follow-up is indicated when none is warranted.
        """;

    private static string BuildUserPrompt(GenerateReportDto dto)
    {
        var age = dto.Age is { } a ? a.ToString() : "Unknown";
        var gender = string.IsNullOrWhiteSpace(dto.Gender) ? "Unknown" : dto.Gender;
        var contrast = string.IsNullOrWhiteSpace(dto.Contrast) ? "Unspecified" : dto.Contrast;
        return $"""
            Modality: {dto.Modality}
            Body part: {dto.BodyPart}
            Contrast: {contrast}
            Patient: age {age}, gender {gender}

            CLINICAL HISTORY / INDICATION:
            {dto.Indication}

            POSITIVE FINDINGS (dictated):
            {dto.Findings}

            INSTRUCTION:
            {InstructionsTemplate}

            Respond with a single JSON object exactly matching this schema:
            {{
              "indication": "",
              "technique": "",
              "findings": "",
              "impression": "",
              "recommendations": ""
            }}
            Return the raw JSON object only. Escape every line break inside a string value as \n; never
            emit a raw newline inside a quoted string.
            """;
    }

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
            SystemPrompt: SystemPrompt,
            UserPrompt: BuildUserPrompt(dto),
            PromptVersion: "local-generate-v2-fewshot",
            ContainsPhi: true)
        {
            Grammar = DictationGrammar.ReportSectionsGbnf,
            // Empirically tuned against the real model — see class remarks for what was tried and why.
            RepeatPenalty = 1.1,
            RepeatLastN = 256,
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
