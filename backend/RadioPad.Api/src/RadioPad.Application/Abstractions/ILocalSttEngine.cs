using RadioPad.Application.Stt;

namespace RadioPad.Application.Abstractions;

/// <summary>
/// A single on-device ASR engine that produces a word-level hypothesis from a
/// 16 kHz mono WAV buffer. Multiple engines (e.g. Parakeet + Whisper) are run in
/// parallel and combined by the ensemble orchestrator via the ROVER reconciler.
/// Implementations self-disable (<see cref="Available"/> == false) when their
/// model is absent, so the orchestrator can skip them gracefully.
/// </summary>
public interface ILocalSttEngine
{
    /// <summary>Stable id used as the hypothesis EngineId and the calibration key.</summary>
    string EngineId { get; }

    /// <summary>True when the engine is enabled and its model is present.</summary>
    bool Available { get; }

    /// <summary>Recognize a complete 16 kHz mono WAV buffer into a word-level hypothesis.</summary>
    Task<EngineTranscript> RecognizeAsync(byte[] wavBytes, CancellationToken ct);
}
