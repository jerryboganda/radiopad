using RadioPad.Application.Stt;

namespace RadioPad.Application.Abstractions;

/// <summary>
/// Runs the manual cross-check pass: re-runs retained dictation audio through the
/// available on-device ASR engines, reconciles them against the live draft via the
/// N-way ROVER voter, then (when configured) an LLM medical-accuracy pass, and
/// returns the resulting original→corrected list. CPU/system-RAM only, per the
/// on-device STT rule.
/// </summary>
public interface ICrossCheckService
{
    /// <summary>True when at least one on-device ASR engine is available.</summary>
    bool Available { get; }

    /// <summary>
    /// Cross-check <paramref name="liveTranscript"/> against the engines' hypotheses
    /// of <paramref name="wavAudio"/> (16 kHz mono WAV).
    /// </summary>
    Task<CrossCheckResult> RunAsync(
        byte[] wavAudio, string liveTranscript, CrossCheckOptions options, CancellationToken ct);
}
