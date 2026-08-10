using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RadioPad.Application.Reporting.Dtos;
using RadioPad.Application.Reporting.Services;
using RadioPad.Application.Transcription;
using RadioPad.Domain.Enums;
using RadioPad.Infrastructure.Persistence;
using RadioPad.Infrastructure.Transcription;

namespace RadioPad.Api.Controllers;

/// <summary>
/// Mobile standalone reporting (radiologist logs in on mobile, creates a report, records
/// multi-take audio dictation, pushed to desktop for medical transcription into Positive
/// Findings). Inherits <see cref="TenantedController"/> like every other tenant-scoped
/// controller — this previously had NO authentication or tenant isolation at all (any caller
/// could list/read every tenant's reports, including patient name/age/gender, and upload
/// audio against any report id), which was a live PHI-exposure bug fixed alongside restoring
/// the rest of this feature.
/// </summary>
[ApiController]
[Route("api/v1/reporting/reports")]
public class ReportingController : TenantedController
{
    private readonly IReportingService _reportingService;
    private readonly RadioPadDbContext _db;
    private readonly TranscriptionOrchestrator _orchestrator;
    private readonly IEnumerable<ITranscriptionEngine> _engines;

    public ReportingController(
        IReportingService reportingService,
        RadioPadDbContext db,
        TranscriptionOrchestrator orchestrator,
        IEnumerable<ITranscriptionEngine> engines)
    {
        _reportingService = reportingService;
        _db = db;
        _orchestrator = orchestrator;
        _engines = engines;
    }

    [HttpPost]
    public async Task<IActionResult> CreateReport([FromBody] CreateReportRequestDto dto, CancellationToken ct)
    {
        var (tenant, user) = await ResolveContextAsync(_db, ct);
        var deny = RequirePermission(user, RbacPermission.ReportsDraft);
        if (deny is not null) return deny;

        var report = await _reportingService.CreateReportAsync(dto, tenant.Id, user.Id, ct);
        return CreatedAtAction(nameof(GetReportById), new { id = report.Id }, report);
    }

    [HttpGet]
    public async Task<IActionResult> GetReports([FromQuery] string? search, [FromQuery] string? status, CancellationToken ct)
    {
        var (tenant, user) = await ResolveContextAsync(_db, ct);
        var deny = RequirePermission(user, RbacPermission.ReportsRead);
        if (deny is not null) return deny;

        var reports = await _reportingService.GetReportsAsync(tenant.Id, search, status, ct);
        return Ok(reports);
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetReportById(Guid id, CancellationToken ct)
    {
        var (tenant, user) = await ResolveContextAsync(_db, ct);
        var deny = RequirePermission(user, RbacPermission.ReportsRead);
        if (deny is not null) return deny;

        var report = await _reportingService.GetReportByIdAsync(id, tenant.Id, ct);
        if (report is null)
        {
            return NotFound();
        }
        return Ok(report);
    }

    [HttpPost("{id:guid}/dictations")]
    [RequestSizeLimit(33_554_432)]
    [RequestFormLimits(MultipartBodyLengthLimit = 33_554_432)]
    public async Task<IActionResult> UploadDictation(
        Guid id,
        IFormFile file,
        [FromForm] double durationSeconds,
        CancellationToken ct)
    {
        var (tenant, user) = await ResolveContextAsync(_db, ct);
        var deny = RequirePermission(user, RbacPermission.ReportsEdit);
        if (deny is not null) return deny;

        if (file is null || file.Length == 0)
        {
            return BadRequest(new { error = "No audio file provided.", kind = "validation" });
        }
        if (file.Length > 33_554_432)
        {
            return BadRequest(new { error = "Audio file exceeds the 32 MiB limit.", kind = "validation" });
        }

        try
        {
            await using var stream = file.OpenReadStream();
            var dictation = await _reportingService.AddDictationAudioAsync(id, tenant.Id, stream, file.FileName, durationSeconds, ct);
            return Ok(dictation);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(ex.Message);
        }
    }

    /// <summary>
    /// Dynamic engine list for the mobile/desktop transcription dropdown — enumerated from the
    /// DI-registered <see cref="ITranscriptionEngine"/>s so a future 3rd engine registration
    /// appears automatically on both clients with no hardcoded strings anywhere. The medASR
    /// local engine is always flagged as the default per the product requirement ("by default
    /// the model used for medical transcription will be local MedASR + 6-gram").
    /// </summary>
    [HttpGet("transcription-engines")]
    public async Task<IActionResult> GetTranscriptionEngines(CancellationToken ct)
    {
        var (_, user) = await ResolveContextAsync(_db, ct);
        var deny = RequirePermission(user, RbacPermission.ReportsRead);
        if (deny is not null) return deny;

        var dtos = new List<TranscriptionEngineDto>();
        foreach (var engine in _engines)
        {
            dtos.Add(new TranscriptionEngineDto(
                engine.EngineId,
                engine.DisplayName,
                engine.IsLocal,
                engine.IsAvailable,
                IsDefault: string.Equals(engine.EngineId, MedAsrTranscriptionEngine.DefaultEngineId, StringComparison.OrdinalIgnoreCase)));
        }
        return Ok(dtos);
    }

    /// <summary>
    /// Transcribes (or re-transcribes) one dictation with an optional engine override — this is
    /// what the desktop Positive Findings tab's "Re-transcribe" button and engine dropdown
    /// actually call; previously that button only showed a toast and refetched. Report/dictation
    /// ownership is verified here (not just inside the orchestrator, which deliberately never
    /// accepts a caller-supplied tenant id) so a cross-tenant dictationId can never be
    /// transcribed, and a dictationId that belongs to a DIFFERENT report than the route's
    /// <paramref name="id"/> is rejected too.
    /// </summary>
    [HttpPost("{id:guid}/dictations/{dictationId:guid}/transcribe")]
    [Microsoft.AspNetCore.RateLimiting.EnableRateLimiting("ai")]
    public async Task<IActionResult> TranscribeDictation(
        Guid id, Guid dictationId, [FromBody] TranscribeDictationRequestDto? body, CancellationToken ct)
    {
        var (tenant, user) = await ResolveContextAsync(_db, ct);
        var deny = RequirePermission(user, RbacPermission.ReportsEdit);
        if (deny is not null) return deny;

        var report = await _db.Reports.FirstOrDefaultAsync(r => r.Id == id && r.TenantId == tenant.Id, ct);
        if (report is null) return NotFound();

        var ownsDictation = await _db.DictationAudios
            .AnyAsync(d => d.Id == dictationId && d.ReportId == id, ct);
        if (!ownsDictation) return NotFound();

        try
        {
            var dto = await _orchestrator.ProcessDictationAsync(dictationId, body?.Engine, user.Id, ct);
            return Ok(dto);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(ex.Message);
        }
    }

    /// <summary>
    /// Streams a dictation's stored audio bytes so the desktop/mobile <c>&lt;audio&gt;</c>
    /// player can actually play it — <c>DictationAudioCard.tsx</c> already pointed at exactly
    /// this URL shape, which 404'd because the endpoint didn't exist yet. Range processing is
    /// enabled for scrubbing/seek support.
    /// </summary>
    [HttpGet("{id:guid}/dictations/{dictationId:guid}/audio")]
    public async Task<IActionResult> GetDictationAudio(Guid id, Guid dictationId, CancellationToken ct)
    {
        var (tenant, user) = await ResolveContextAsync(_db, ct);
        var deny = RequirePermission(user, RbacPermission.ReportsRead);
        if (deny is not null) return deny;

        var report = await _db.Reports.FirstOrDefaultAsync(r => r.Id == id && r.TenantId == tenant.Id, ct);
        if (report is null) return NotFound();

        var dictation = await _db.DictationAudios.FirstOrDefaultAsync(d => d.Id == dictationId && d.ReportId == id, ct);
        if (dictation is null || string.IsNullOrWhiteSpace(dictation.StoragePath) || !System.IO.File.Exists(dictation.StoragePath))
        {
            return NotFound();
        }

        var contentType = TranscriptionOrchestrator.InferContentType(dictation.StoragePath);
        var stream = new FileStream(dictation.StoragePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        return File(stream, contentType, enableRangeProcessing: true);
    }
}

/// <summary>Optional engine-id override for <see cref="ReportingController.TranscribeDictation"/>;
/// omitted/null keeps the dictation's own stored engine (defaults to medASR on upload).</summary>
public record TranscribeDictationRequestDto(string? Engine = null);

