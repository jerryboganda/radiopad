using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace RadioPad.Application.Transcription;

public record TranscriptionContext(
    Guid DictationId,
    Guid ReportId,
    string? Prompt = null,
    /// <summary>MIME type of the stored audio (e.g. "audio/wav"), inferred from the file
    /// extension at upload time — needed by real STT engines that decode the audio.</summary>
    string? ContentType = null,
    /// <summary>Optional on-device ensemble mode passthrough ("auto" | "single" | "ensemble");
    /// see <see cref="RadioPad.Application.Abstractions.ILocalSttClient.TranscribeAsync"/>.</summary>
    string? SttMode = null
);

public record TranscriptionResult(
    bool Success,
    string TranscribedText,
    string EngineUsed,
    string? ErrorMessage = null
);

public interface ITranscriptionEngine
{
    string EngineId { get; }

    /// <summary>Human-readable label for the engine picker dropdown (e.g. "medASR (Local, 6-gram LM)").</summary>
    string DisplayName { get; }

    /// <summary>True when this engine runs entirely on-device (audio never leaves the machine).</summary>
    bool IsLocal { get; }

    /// <summary>True when this engine is actually usable right now (model loaded / client configured).
    /// Surfaced by the "list transcription engines" endpoint so the dropdown can grey out an engine
    /// that is registered but not currently runnable, instead of letting the user pick it and fail.</summary>
    bool IsAvailable { get; }

    Task<TranscriptionResult> TranscribeAsync(Stream audioStream, TranscriptionContext context, CancellationToken ct = default);
}
