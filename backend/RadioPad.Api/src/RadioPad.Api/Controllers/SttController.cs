using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RadioPad.Application.Abstractions;
using RadioPad.Application.Stt;

namespace RadioPad.Api.Controllers;

/// <summary>
/// Stateless, on-device dictation transcription.
///
/// The RadioPad desktop is a thin client over the hosted production API for
/// everything EXCEPT dictation: dictation audio (which may contain PHI) is
/// transcribed entirely on the clinician's machine and never leaves it. The
/// desktop ships a loopback-bound sidecar with <c>RADIOPAD_LOCAL_STT_ENABLED</c>
/// set; the webview posts the recorded 16 kHz mono WAV here, gets back the
/// transcript (plus per-word ensemble review spans), and then saves the
/// resulting de-identified text to the production report through the normal API.
///
/// Unlike <c>POST /api/reports/{id}/dictation/transcribe</c>, this endpoint is
/// NOT report-scoped — the on-device engine needs only the audio, so there is no
/// tenant/report context to resolve. It is therefore safe to serve anonymously:
/// the sidecar binds 127.0.0.1 only. On the hosted production server it is inert
/// twice over: the production bearer gate rejects an anonymous request (401)
/// before it reaches this controller, and even if it did, the on-device engine
/// is unconfigured there (<see cref="ILocalSttClient.Available"/> == false) so it
/// would return 503. Either way the hosted API never transcribes here.
///
/// Nothing is persisted: the transcript is returned to the caller and never
/// written to any store (the report-scoped endpoint is what records the
/// SHA-256 audit event when the radiologist adopts the text).
/// </summary>
[ApiController]
[Route("api/stt")]
[AllowAnonymous]
public sealed class SttController : ControllerBase
{
    /// <summary>Hard cap on a single dictation upload (32 MiB) — mirrors the report-scoped path.</summary>
    private const long MaxAudioBytes = 33_554_432;

    private static readonly string[] AllowedContentTypes =
        { "audio/webm", "audio/wav", "audio/mpeg", "audio/mp4", "audio/ogg" };

    private readonly ILocalSttClient _localStt;
    private readonly ICrossCheckService _crossCheck;
    private readonly ICrossCheckJobStore _jobs;

    public SttController(ILocalSttClient localStt, ICrossCheckService crossCheck, ICrossCheckJobStore jobs)
    {
        _localStt = localStt;
        _crossCheck = crossCheck;
        _jobs = jobs;
    }

    /// <summary>
    /// Transcribe a recorded-audio buffer fully on-device. Returns the transcript
    /// and, for the multi-engine ensemble, per-word review spans (the disagreement
    /// / safety-critical tokens the radiologist must eye-confirm). Returns 503 when
    /// no on-device engine is configured (e.g. a web/server build).
    /// </summary>
    [HttpPost("transcribe")]
    [RequestSizeLimit(MaxAudioBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = MaxAudioBytes)]
    public async Task<IActionResult> Transcribe(
        [FromForm] IFormFile? audio,
        CancellationToken ct,
        [FromForm] string? mode = null)
    {
        // Validate the request shape FIRST (a malformed upload is a 400 regardless
        // of engine state), then gate on the on-device engine being present.
        if (audio is null || audio.Length <= 0)
            return BadRequest(new { error = "audio file is required.", kind = "validation" });
        if (audio.Length > MaxAudioBytes)
            return BadRequest(new { error = "audio file exceeds the 32 MiB limit.", kind = "validation" });

        var contentType = (audio.ContentType ?? string.Empty).Split(';')[0].Trim();
        if (!AllowedContentTypes.Contains(contentType, StringComparer.OrdinalIgnoreCase))
            return BadRequest(new { error = $"unsupported audio content type '{audio.ContentType}'.", kind = "validation" });

        if (!_localStt.Available)
            return StatusCode(StatusCodes.Status503ServiceUnavailable,
                new { error = "On-device transcription is not available on this build.", kind = "stt_unavailable" });

        await using var stream = audio.OpenReadStream();
        var result = await _localStt.TranscribeAsync(stream, contentType, ct, mode);

        return Ok(new
        {
            transcript = result.Text,
            provider = result.Provider,
            model = result.Model,
            latencyMs = result.LatencyMs,
            // Per-word ensemble review spans (null for single-engine). Flagged
            // spans are rendered as .ai-mark review marks in the editor.
            spans = result.Spans?.Select(s => new
            {
                text = s.Text,
                flagged = s.Flagged,
                reason = s.Reason,
                source = s.Source,
            }),
        });
    }

    /// <summary>
    /// Kick off a manual cross-check of an already-dictated section: re-run the
    /// retained audio through the on-device engines, reconcile against the live
    /// draft, and return a job id to poll. Async because the multi-engine pass
    /// (and the later LLM step) can take seconds; the UI shows a processing badge
    /// meanwhile. 503 when no on-device engine is available.
    /// </summary>
    [HttpPost("crosscheck")]
    [RequestSizeLimit(MaxAudioBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = MaxAudioBytes)]
    public async Task<IActionResult> CrossCheck(
        [FromForm] IFormFile? audio,
        [FromForm] string? liveTranscript,
        CancellationToken ct,
        [FromForm] string? sectionKey = null,
        [FromForm] bool useUbag = false)
    {
        if (audio is null || audio.Length <= 0)
            return BadRequest(new { error = "audio file is required.", kind = "validation" });
        if (audio.Length > MaxAudioBytes)
            return BadRequest(new { error = "audio file exceeds the 32 MiB limit.", kind = "validation" });
        if (string.IsNullOrWhiteSpace(liveTranscript))
            return BadRequest(new { error = "liveTranscript is required.", kind = "validation" });

        var contentType = (audio.ContentType ?? string.Empty).Split(';')[0].Trim();
        if (!AllowedContentTypes.Contains(contentType, StringComparer.OrdinalIgnoreCase))
            return BadRequest(new { error = $"unsupported audio content type '{audio.ContentType}'.", kind = "validation" });

        if (!_crossCheck.Available)
            return StatusCode(StatusCodes.Status503ServiceUnavailable,
                new { error = "On-device cross-check is not available on this build.", kind = "stt_unavailable" });

        // Buffer the audio now — the request stream won't survive the background task.
        byte[] bytes;
        await using (var stream = audio.OpenReadStream())
        using (var ms = new MemoryStream())
        {
            await stream.CopyToAsync(ms, ct);
            bytes = ms.ToArray();
        }

        var job = _jobs.Create();
        job.State = CrossCheckState.Running;
        job.Stage = "re-running engines";
        _jobs.Update(job);

        var options = new CrossCheckOptions { SectionKey = sectionKey, UseUbag = useUbag };
        _ = Task.Run(async () =>
        {
            try
            {
                var result = await _crossCheck.RunAsync(bytes, liveTranscript!, options, CancellationToken.None);
                job.Result = result;
                job.State = CrossCheckState.Completed;
                job.Stage = "done";
                _jobs.Update(job);
            }
            catch (Exception ex)
            {
                job.Error = ex.Message;
                job.State = CrossCheckState.Failed;
                job.Stage = "failed";
                _jobs.Update(job);
            }
        });

        return Accepted(new { jobId = job.Id });
    }

    /// <summary>Poll a cross-check job started by <see cref="CrossCheck"/>.</summary>
    [HttpGet("crosscheck/{jobId}")]
    public IActionResult CrossCheckStatus(string jobId)
    {
        var job = _jobs.Get(jobId);
        if (job is null) return NotFound(new { error = "unknown or expired job.", kind = "not_found" });
        return Ok(ProjectJob(job));
    }

    private static object ProjectJob(CrossCheckJob job) => new
    {
        jobId = job.Id,
        status = job.State.ToString().ToLowerInvariant(),
        stage = job.Stage,
        error = job.Error,
        transcript = job.Result?.Transcript,
        engineIds = job.Result?.EngineIds,
        latencyMs = job.Result?.LatencyMs,
        corrections = job.Result?.Corrections.Select(c => new
        {
            id = c.Id,
            sectionKey = c.SectionKey,
            originalText = c.OriginalText,
            correctedText = c.CorrectedText,
            startOffset = c.StartOffset,
            endOffset = c.EndOffset,
            reason = c.Reason,
            category = c.Category,
            source = c.Source,
            confidence = c.Confidence,
            severity = c.Severity,
        }),
    };
}
