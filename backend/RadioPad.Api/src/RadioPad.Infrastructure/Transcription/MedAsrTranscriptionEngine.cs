using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using RadioPad.Application.Abstractions;
using RadioPad.Application.Transcription;
using EngineResult = RadioPad.Application.Transcription.TranscriptionResult;

namespace RadioPad.Infrastructure.Transcription;

/// <summary>
/// Delegates to the real on-device speech-to-text engine (<see cref="ILocalSttClient"/>,
/// which resolves to the MedASR/6-gram sherpa-onnx bundle by default — see
/// <c>SherpaMedAsrSttClient</c>) instead of returning canned text. Previously this class
/// returned the SAME fabricated "Lungs are clear..." findings regardless of what was actually
/// dictated — a patient-safety hazard in a clinical product, not a placeholder anyone could
/// safely ship. When the local engine is not configured/available (e.g. a server-only backend
/// with no bundled model), this now fails clearly instead of inventing findings.
/// </summary>
public class MedAsrTranscriptionEngine : ITranscriptionEngine
{
    public const string DefaultEngineId = "medASR-6gram";
    private readonly ILocalSttClient? _localStt;
    private readonly ILogger<MedAsrTranscriptionEngine>? _logger;

    public string EngineId => DefaultEngineId;
    public string DisplayName => "medASR (Local, 6-gram LM)";
    public bool IsLocal => true;
    public bool IsAvailable => _localStt?.Available ?? false;

    public MedAsrTranscriptionEngine(ILocalSttClient? localStt = null, ILogger<MedAsrTranscriptionEngine>? logger = null)
    {
        _localStt = localStt;
        _logger = logger;
    }

    public async Task<EngineResult> TranscribeAsync(Stream audioStream, TranscriptionContext context, CancellationToken ct = default)
    {
        if (audioStream is null || (audioStream.CanSeek && audioStream.Length == 0))
        {
            _logger?.LogWarning("MedASR transcription failed: audio stream is empty or null for Dictation {DictationId}", context.DictationId);
            return new EngineResult(false, string.Empty, EngineId, "Audio stream is empty or null");
        }

        if (_localStt is not { Available: true })
        {
            _logger?.LogWarning("MedASR transcription failed: local engine unavailable for Dictation {DictationId}", context.DictationId);
            return new EngineResult(false, string.Empty, EngineId,
                "The local MedASR engine is not available on this server (RADIOPAD_LOCAL_STT_ENABLED / model bundle missing). " +
                "Choose a cloud transcription engine instead, or enable on-device STT.");
        }

        if (audioStream.CanSeek)
        {
            audioStream.Position = 0;
        }

        _logger?.LogInformation("Running medASR-6gram local transcription for Dictation {DictationId}", context.DictationId);

        var result = await _localStt.TranscribeAsync(audioStream, context.ContentType ?? "audio/webm", ct, context.SttMode);
        return new EngineResult(true, result.Text, EngineId);
    }
}
