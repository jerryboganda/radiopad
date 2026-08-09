using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace RadioPad.Application.Transcription;

public record TranscriptionContext(
    Guid DictationId,
    Guid ReportId,
    string? Prompt = null
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
    Task<TranscriptionResult> TranscribeAsync(Stream audioStream, TranscriptionContext context, CancellationToken ct = default);
}
