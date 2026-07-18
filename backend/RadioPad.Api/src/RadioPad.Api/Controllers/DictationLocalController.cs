using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RadioPad.Application.Dictation;

namespace RadioPad.Api.Controllers;

/// <summary>
/// Stateless, on-device dictation FORMATTING (dictation brief §4.2, optional local path).
///
/// Mirrors <see cref="SttController"/>: the desktop is a thin client over the hosted API for
/// everything except on-device work. When the offline MedGemma formatter is enabled
/// (<c>RADIOPAD_LOCAL_FORMATTER_ENABLED</c>), the loopback sidecar runs the FULL safety pipeline
/// locally — §5.2 deterministic pass-through → MedGemma (llama-server, LocalOnly) → §5.3
/// validation-diff → §5.6 sentinel → §5.7 local audit — so the transcript (PHI) never leaves the
/// machine. It is NOT report-scoped: the report context + corrections come in the body, so no
/// tenant/DB lookup is needed and it is safe to serve anonymously (the sidecar binds 127.0.0.1).
///
/// Inert on the hosted server twice over: the production bearer gate rejects an anonymous request
/// (401) before it reaches here, and the local formatter is unconfigured there
/// (<see cref="ILocalReportFormatter.Available"/> == false) so it returns 503.
/// </summary>
[ApiController]
[Route("api/dictation")]
[AllowAnonymous]
public sealed class DictationLocalController : ControllerBase
{
    public record CorrectionDto(string From, string To);

    public record DraftLocalDto(
        string RawDictation,
        string? Modality,
        string? BodyPart,
        string? Indication,
        string? PatientSex,
        List<CorrectionDto>? Corrections);

    private readonly DictationEngineService _engine;
    private readonly ILocalReportFormatter _localFormatter;
    private readonly IDictationAuditStore _audit;

    public DictationLocalController(
        DictationEngineService engine,
        ILocalReportFormatter localFormatter,
        IDictationAuditStore audit)
    {
        _engine = engine;
        _localFormatter = localFormatter;
        _audit = audit;
    }

    /// <summary>
    /// Format a raw dictation into a safety-checked, editable draft entirely on-device. 503 when the
    /// local formatter is not configured (web/server, or the desktop offline formatter is off).
    /// </summary>
    [HttpPost("draft-local")]
    public async Task<IActionResult> DraftLocal([FromBody] DraftLocalDto dto, CancellationToken ct)
    {
        if (dto is null || string.IsNullOrWhiteSpace(dto.RawDictation))
            return BadRequest(new { error = "rawDictation is required.", kind = "validation" });

        if (!_localFormatter.Available)
            return StatusCode(StatusCodes.Status503ServiceUnavailable,
                new { error = "On-device MedGemma formatting is not available on this build.", kind = "formatter_unavailable" });

        var corrections = (dto.Corrections ?? new List<CorrectionDto>())
            .Where(c => !string.IsNullOrWhiteSpace(c.From) && !string.IsNullOrWhiteSpace(c.To))
            .Select(c => new CorrectionRule(c.From, c.To))
            .ToList();

        var context = new DictationFormatContext(
            Modality: dto.Modality ?? string.Empty,
            BodyPart: dto.BodyPart ?? string.Empty,
            Indication: dto.Indication ?? string.Empty,
            SectionKeys: DictationGrammar.DefaultSections,
            Grammar: DictationGrammar.ReportSectionsGbnf);

        DictationDraft draft;
        try
        {
            draft = await _engine.RunAsync(dto.RawDictation, context, corrections, dto.PatientSex, _localFormatter, ct);
        }
        catch (Exception ex)
        {
            return StatusCode(StatusCodes.Status502BadGateway,
                new { error = $"On-device formatting failed: {ex.Message}", kind = "formatter_transport" });
        }

        // §5.7 — record the run in the on-device audit store (PHI stays local).
        await _audit.AppendAsync(new DictationAuditRecord(
            ReportId: "local",
            RawTranscript: draft.RawTranscript,
            CorrectedTranscript: draft.CorrectedTranscript,
            FinalSections: draft.DraftSections,
            Diff: string.Empty,
            TemplateId: null,
            SttModel: "on-device",
            FormatterProvider: draft.Provider,
            FormatterModel: draft.Model,
            Accepted: draft.Accepted,
            TimestampUtc: DateTime.UtcNow.ToString("o")), ct);

        return Ok(new
        {
            sections = draft.DraftSections,
            accepted = draft.Accepted,
            usedFallback = draft.UsedFallback,
            requiresReview = draft.RequiresReview,
            violations = draft.Violations.Select(v => new { reason = v.Reason.ToString(), detail = v.Detail }),
            sentinelWarnings = draft.SentinelWarnings.Select(w => new { kind = w.Kind.ToString(), detail = w.Detail }),
            provider = draft.Provider,
            model = draft.Model,
            latencyMs = draft.LatencyMs,
        });
    }
}
