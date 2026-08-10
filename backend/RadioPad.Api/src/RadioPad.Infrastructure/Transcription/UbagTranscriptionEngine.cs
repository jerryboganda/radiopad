using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using RadioPad.Application.Abstractions;
using RadioPad.Application.Transcription;
using TranscriptionResult = RadioPad.Application.Transcription.TranscriptionResult;

namespace RadioPad.Infrastructure.Transcription;

public class UbagTranscriptionEngine : ITranscriptionEngine
{
    public const string DefaultEngineId = "ubag";
    public const string DefaultRadiologySystemPrompt = @"You are an expert medical radiology transcription assistant.
The speaker is a senior radiologist dictating positive findings for a diagnostic imaging case.
Transcribe the audio accurately. Retain medical terminology, anatomical references, dimensions (mm, cm), and diagnostic impressions.
Do not summarize or alter clinical terms.";

    private readonly IUbagClient? _ubagClient;
    private readonly ILogger<UbagTranscriptionEngine>? _logger;

    public string EngineId => DefaultEngineId;
    public string DisplayName => "UBAG (Cloud AI)";
    public bool IsLocal => false;
    public bool IsAvailable => _ubagClient != null;

    public UbagTranscriptionEngine(IUbagClient? ubagClient = null, ILogger<UbagTranscriptionEngine>? logger = null)
    {
        _ubagClient = ubagClient;
        _logger = logger;
    }

    /// <summary>
    /// Best-effort standalone path — orchestrator-driven production calls bypass this in favor of
    /// the full, real <c>ITranscriptionService</c> flow (create job → upload audio artifact → poll
    /// to terminal), which this simple interface cannot express (no tenant/report context). This
    /// method makes one opportunistic job-create call; if the job hasn't produced output yet (the
    /// common case — cloud transcription takes several seconds), it fails clearly rather than
    /// fabricating a plausible-looking clinical transcript, which would be a patient-safety hazard.
    /// </summary>
    public async Task<TranscriptionResult> TranscribeAsync(Stream audioStream, TranscriptionContext context, CancellationToken ct = default)
    {
        if (audioStream is null || (audioStream.CanSeek && audioStream.Length == 0))
        {
            _logger?.LogWarning("UBAG transcription failed: audio stream is empty or null for Dictation {DictationId}", context.DictationId);
            return new TranscriptionResult(false, string.Empty, EngineId, "Audio stream is empty or null");
        }

        var systemPrompt = context.Prompt ?? DefaultRadiologySystemPrompt;
        _logger?.LogInformation("Running UBAG cloud transcription for Dictation {DictationId} with system prompt", context.DictationId);

        if (audioStream.CanSeek)
        {
            audioStream.Position = 0;
        }

        if (_ubagClient is null)
        {
            return new TranscriptionResult(false, string.Empty, EngineId,
                "No UBAG client is configured on this server. Choose the local medASR engine instead, " +
                "or configure RADIOPAD_UBAG_BASE_URL / RADIOPAD_UBAG_AUTH_SECRET_REF.");
        }

        try
        {
            var idempotencyKey = $"radiopad-stt-{context.DictationId:N}-{Guid.NewGuid():N}"[..48];
            var jobReq = new UbagTranscriptionRequest(
                Target: "chatgpt",
                AudioArtifactKey: $"dictation-{context.DictationId:N}.wav",
                Prompt: systemPrompt,
                ClientRequestId: idempotencyKey
            );

            var job = await _ubagClient.CreateTranscriptionJobAsync(jobReq, idempotencyKey, ct);
            if (job != null && !string.IsNullOrWhiteSpace(job.Output))
            {
                return new TranscriptionResult(true, job.Output, EngineId);
            }
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "UBAG client transcription call encountered error for Dictation {DictationId}", context.DictationId);
            return new TranscriptionResult(false, string.Empty, EngineId, $"UBAG transcription failed: {ex.Message}");
        }

        return new TranscriptionResult(false, string.Empty, EngineId,
            "UBAG job did not complete synchronously. Use the reporting transcribe endpoint " +
            "(which polls to terminal) instead of calling this engine directly.");
    }
}
