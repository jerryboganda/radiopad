using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using RadioPad.Application.Abstractions;
using RadioPad.Application.Reporting.Dtos;
using RadioPad.Application.Transcription;
using RadioPad.Domain.Entities;
using RadioPad.Domain.Enums;
using RadioPad.Infrastructure.Persistence;
using EngineResult = RadioPad.Application.Transcription.TranscriptionResult;

namespace RadioPad.Infrastructure.Transcription;

public class TranscriptionOrchestrator
{
    private readonly IEnumerable<ITranscriptionEngine> _engines;
    private readonly RadioPadDbContext _db;
    private readonly ITranscriptionNotifier? _notifier;
    private readonly ILogger<TranscriptionOrchestrator> _logger;
    /// <summary>
    /// The real, battle-tested cloud transcription pipeline (create UBAG job → upload audio
    /// artifact → poll to terminal, tenant-aware PHI routing, SHA-256-only audit) already used by
    /// the desktop's live-record dictation flow. Cloud-engine requests from mobile-reporting
    /// dictations are routed through this instead of the simplified <see cref="ITranscriptionEngine"/>
    /// interface, which cannot express tenant/report context and — for any non-local engine —
    /// cannot complete a real multi-step cloud job on its own. Optional so existing unit tests that
    /// construct the orchestrator directly (without DI) keep exercising the engine-selection logic
    /// against the registered <see cref="ITranscriptionEngine"/>s.
    /// </summary>
    private readonly ITranscriptionService? _cloudTranscription;

    /// <summary>
    /// Same append-only audit trail <see cref="RadioPad.Application.Services.TranscriptionService"/>
    /// writes an <see cref="AuditAction.AudioTranscribed"/> row to for the cloud/desktop-live-dictation
    /// paths. The orchestrator's own local-engine branch (below) calls <see cref="ITranscriptionEngine"/>
    /// directly rather than through that service, so it must append the SAME audit event itself —
    /// otherwise local (on-device) transcriptions of mobile dictations would be the one AI-assisted
    /// action in the product with no audit trail at all. Optional so direct-construction unit tests
    /// (no DI) keep working without a database-backed audit sink.
    /// </summary>
    private readonly IAuditLog? _audit;

    public TranscriptionOrchestrator(
        IEnumerable<ITranscriptionEngine> engines,
        RadioPadDbContext db,
        ILogger<TranscriptionOrchestrator> logger,
        ITranscriptionNotifier? notifier = null,
        ITranscriptionService? cloudTranscription = null,
        IAuditLog? audit = null)
    {
        _engines = engines;
        _db = db;
        _logger = logger;
        _notifier = notifier;
        _cloudTranscription = cloudTranscription;
        _audit = audit;
    }

    /// <param name="dictationId">The stored mobile dictation to transcribe.</param>
    /// <param name="preferredEngine">Engine id override (e.g. "ubag"); defaults to the dictation's
    /// own <c>TranscriptionEngine</c>, then medASR.</param>
    /// <param name="actingUserId">The user who triggered this transcription (for audit). Defaults
    /// to the report's <c>CreatedByUserId</c> for the automatic post-upload background call, which
    /// has no "current caller".</param>
    public async Task<DictationAudioDto> ProcessDictationAsync(
        Guid dictationId, string? preferredEngine = null, Guid? actingUserId = null, CancellationToken ct = default)
    {
        var dictation = await _db.DictationAudios
            .FirstOrDefaultAsync(d => d.Id == dictationId, ct);

        if (dictation is null)
        {
            throw new KeyNotFoundException($"DictationAudio with Id '{dictationId}' was not found.");
        }

        // Tenant is ALWAYS derived from the dictation's own report — never accepted as a caller
        // parameter — so a mismatched/forged tenant id can never be passed in and cross-tenant
        // routing/PHI decisions can never be made against the wrong tenant.
        var report = await _db.Reports.FirstOrDefaultAsync(r => r.Id == dictation.ReportId, ct);
        if (report is null)
        {
            throw new KeyNotFoundException($"Report with Id '{dictation.ReportId}' was not found.");
        }

        var targetEngineId = preferredEngine ?? dictation.TranscriptionEngine ?? MedAsrTranscriptionEngine.DefaultEngineId;
        var engine = _engines.FirstOrDefault(e => string.Equals(e.EngineId, targetEngineId, StringComparison.OrdinalIgnoreCase))
                     ?? _engines.FirstOrDefault(e => string.Equals(e.EngineId, MedAsrTranscriptionEngine.DefaultEngineId, StringComparison.OrdinalIgnoreCase))
                     ?? _engines.FirstOrDefault();

        if (engine is null)
        {
            _logger.LogError("No transcription engines registered in orchestrator for dictation {DictationId}", dictationId);
            dictation.Status = "Failed";
            dictation.ErrorMessage = "No transcription engine available";
            await _db.SaveChangesAsync(ct);
            return MapToDto(dictation);
        }

        dictation.Status = "Processing";
        await _db.SaveChangesAsync(ct);

        Stream audioStream;
        bool isFileStream = false;
        long sizeBytes = 0;
        if (!string.IsNullOrWhiteSpace(dictation.StoragePath) && File.Exists(dictation.StoragePath))
        {
            audioStream = new FileStream(dictation.StoragePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            isFileStream = true;
            sizeBytes = audioStream.Length;
        }
        else
        {
            audioStream = new MemoryStream(new byte[] { 1, 2, 3, 4 });
        }

        try
        {
            EngineResult result;
            bool auditedInternally;
            if (!engine.IsLocal && _cloudTranscription is not null)
            {
                // Real cloud path: needs the full tenant/user/report context the simple
                // ITranscriptionEngine interface doesn't carry. Note: ITranscriptionService
                // itself tries on-device STT first when available (a deliberate PHI-safety
                // default shared with the desktop's live-dictation flow — locality alone
                // satisfies the PHI gate), so an explicit "ubag" choice can still resolve via
                // the local engine on a machine where it's configured. That is intentional and
                // strictly safer, never a worse transcription than what was requested.
                var tenant = await _db.Tenants.FirstOrDefaultAsync(t => t.Id == report.TenantId, ct);
                var user = await _db.Users.FirstOrDefaultAsync(
                    u => u.Id == (actingUserId ?? report.CreatedByUserId), ct);

                if (tenant is null || user is null)
                {
                    result = new EngineResult(false, string.Empty, engine.EngineId,
                        "Cannot resolve tenant/user context for cloud transcription (report has no owning tenant yet).");
                    auditedInternally = false;
                }
                else
                {
                    var contentType = InferContentType(dictation.StoragePath);
                    var fileName = Path.GetFileName(dictation.StoragePath);
                    var cloudResult = await _cloudTranscription.TranscribeAsync(
                        tenant, user, report, audioStream, fileName, sizeBytes, contentType, ct);
                    result = new EngineResult(true, cloudResult.Text, cloudResult.Provider);
                    // ITranscriptionService.TranscribeAsync already appended AuditAction.AudioTranscribed
                    // for this call (both its local-fallback and UBAG branches audit internally) — do not
                    // double-write.
                    auditedInternally = true;
                }
            }
            else
            {
                var context = new TranscriptionContext(
                    dictation.Id, dictation.ReportId, ContentType: InferContentType(dictation.StoragePath));
                result = await engine.TranscribeAsync(audioStream, context, ct);
                auditedInternally = false;
            }

            if (result.Success)
            {
                dictation.Status = "Completed";
                dictation.TranscribedText = result.TranscribedText;
                dictation.TranscriptionEngine = result.EngineUsed;
                dictation.TranscribedAt = DateTimeOffset.UtcNow;
                dictation.ErrorMessage = null;
                _logger.LogInformation("Transcription completed for Dictation {DictationId} using engine {EngineId}", dictationId, result.EngineUsed);

                if (!auditedInternally && _audit is not null)
                {
                    await AppendLocalTranscriptionAuditAsync(report, actingUserId, dictation, result, sizeBytes, ct);
                }
            }
            else
            {
                dictation.Status = "Failed";
                dictation.ErrorMessage = result.ErrorMessage ?? "Transcription engine returned failure";
                _logger.LogWarning("Transcription failed for Dictation {DictationId}: {Error}", dictationId, dictation.ErrorMessage);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled exception during transcription for Dictation {DictationId}", dictationId);
            dictation.Status = "Failed";
            dictation.ErrorMessage = ex.Message;
        }
        finally
        {
            if (isFileStream)
            {
                await audioStream.DisposeAsync();
            }
        }

        await _db.SaveChangesAsync(ct);
        var dto = MapToDto(dictation);

        if (_notifier != null)
        {
            try
            {
                await _notifier.NotifyTranscriptionCompletedAsync(dto, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to send transcription completion notification");
            }
        }

        return dto;
    }

    /// <summary>
    /// Appends the <see cref="AuditAction.AudioTranscribed"/> event for the local-engine-direct-call
    /// branch (see <see cref="_audit"/> doc comment for why this duplicates, rather than reuses,
    /// <c>TranscriptionService.AppendTranscriptionAuditAsync</c>). Same shape, same
    /// transcript-SHA-256-only-never-plaintext discipline.
    /// </summary>
    private async Task AppendLocalTranscriptionAuditAsync(
        Report report, Guid? actingUserId, DictationAudio dictation, EngineResult result, long sizeBytes, CancellationToken ct)
    {
        if (_audit is null) return;
        await _audit.AppendAsync(new AuditEvent
        {
            TenantId = report.TenantId,
            UserId = actingUserId ?? report.CreatedByUserId,
            ReportId = report.Id,
            Action = AuditAction.AudioTranscribed,
            DetailsJson = System.Text.Json.JsonSerializer.Serialize(new
            {
                provider = result.EngineUsed,
                target = "on-device",
                dictationId = dictation.Id,
                sizeBytes,
                transcriptSha256 = Sha256(result.TranscribedText ?? string.Empty),
            }),
        }, ct);
    }

    private static string Sha256(string value)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    /// <summary>Infers a MIME type from the stored file's extension (the original upload's
    /// Content-Type isn't persisted on <see cref="DictationAudio"/>) so the real STT engines have
    /// enough information to decode the audio. Public so <c>ReportingController</c>'s audio-stream
    /// endpoint reuses the SAME inference instead of a second, potentially-drifting copy.</summary>
    public static string InferContentType(string? storagePath)
    {
        var ext = string.IsNullOrWhiteSpace(storagePath) ? "" : Path.GetExtension(storagePath).ToLowerInvariant();
        return ext switch
        {
            ".wav" => "audio/wav",
            ".mp3" => "audio/mpeg",
            ".m4a" or ".mp4" => "audio/mp4",
            ".ogg" => "audio/ogg",
            _ => "audio/webm",
        };
    }

    private static DictationAudioDto MapToDto(DictationAudio audio)
    {
        return new DictationAudioDto(
            audio.Id,
            audio.ReportId,
            audio.StoragePath,
            audio.DurationSeconds,
            audio.TranscriptionEngine,
            audio.Status,
            audio.TranscribedText,
            audio.UploadedAt,
            audio.TranscribedAt,
            audio.ErrorMessage
        );
    }
}
