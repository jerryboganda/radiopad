using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RadioPad.Application.Abstractions;

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

    public SttController(ILocalSttClient localStt) => _localStt = localStt;

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
}
