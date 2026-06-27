using RadioPad.Application.Stt;
using RadioPad.Infrastructure.Audio;
using Whisper.net;

namespace RadioPad.Infrastructure.Providers.Local;

/// <summary>
/// Shared whisper.cpp / Whisper.net decode used by both on-device Whisper engines
/// (the primary <see cref="WhisperNetSttClient"/> and the medical cross-check
/// <see cref="MedicalWhisperSttClient"/>). Keeps the tuned decode in one place:
/// 16 kHz mono resample, multi-threaded beam search (accuracy over greedy), real
/// token probabilities (so the reconciler gets a usable confidence signal), and a
/// domain priming prompt. CPU + system RAM only (per the on-device STT rule).
/// </summary>
internal static class WhisperDecoder
{
    public static async Task<List<SttToken>> DecodeAsync(
        WhisperFactory factory,
        IAudioDecoder decoder,
        byte[] wavBytes,
        string? prompt,
        CancellationToken ct)
    {
        // Decode + resample to 16 kHz mono first. Whisper.net's WAV reader only
        // accepts exactly 16 kHz, so feeding raw mic audio (44.1/48 kHz) straight
        // in throws NotSupportedWaveException. Going through the shared decoder (as
        // the Parakeet engine does) makes the engine accept any input rate.
        var samples = await decoder.DecodeAsync(
            new MemoryStream(wavBytes, writable: false), "audio/wav", ct);

        var builder = factory.CreateBuilder()
            .WithLanguage("en")
            .WithThreads(LocalSttModels.ResolveThreads())
            .WithProbabilities();

        var beam = LocalSttModels.ResolveWhisperBeamSize();
        if (beam > 1)
            builder = builder.WithBeamSearchSamplingStrategy(c => c.WithBeamSize(beam));

        if (!string.IsNullOrWhiteSpace(prompt))
            builder = builder.WithPrompt(prompt);

        var tokens = new List<SttToken>();
        using var processor = builder.Build();
        await foreach (var seg in processor.ProcessAsync(samples, ct))
        {
            var conf = NormalizeProbability(seg.Probability);
            foreach (var t in SttText.Tokenize(seg.Text, conf))
                tokens.Add(t);
        }
        return tokens;
    }

    private static double NormalizeProbability(float p) => p <= 0f ? 0.5 : Math.Clamp(p, 0f, 1f);
}
