using Microsoft.Extensions.Logging.Abstractions;
using RadioPad.Infrastructure.Audio;
using RadioPad.Infrastructure.Providers.Local;
using Xunit;

namespace RadioPad.Api.Tests;

/// <summary>
/// End-to-end smoke for the real on-device engine: loads the sherpa-onnx native
/// library + the downloaded Parakeet model and transcribes the model bundle's own
/// sample WAV. GATED on <c>RADIOPAD_STT_SMOKE_MODEL_DIR</c> so it runs ONLY in the
/// dedicated Windows smoke job (<c>desktop-stt-smoke.yml</c>) — normal CI (Linux,
/// no model, no win-x64 native lib) skips it via an early return.
/// </summary>
public class SttEngineSmokeTests
{
    [Fact]
    public async Task Engine_Transcribes_Sample_Wav_OnDevice()
    {
        var modelDir = SttSmokeGate.DirOrSkip("RADIOPAD_STT_SMOKE_MODEL_DIR");
        if (modelDir is null)
            return; // not configured (normal CI) — skip; fails loudly if REQUIRE=1

        Environment.SetEnvironmentVariable("RADIOPAD_LOCAL_STT_ENABLED", "1");
        Environment.SetEnvironmentVariable("RADIOPAD_STT_MODEL_DIR", modelDir);

        var client = new SherpaParakeetSttClient(
            new WavAudioDecoder(),
            NullLogger<SherpaParakeetSttClient>.Instance);

        Assert.True(client.Available, "engine should be Available with the model present");

        // The sherpa model bundle ships test_wavs/*.wav (16 kHz mono).
        var wav = Directory.GetFiles(modelDir, "*.wav", SearchOption.AllDirectories).FirstOrDefault();
        Assert.NotNull(wav);

        await using var fs = File.OpenRead(wav!);
        var result = await client.TranscribeAsync(fs, "audio/wav", CancellationToken.None);

        Assert.Equal(SherpaParakeetSttClient.ProviderName, result.Provider);
        Assert.False(string.IsNullOrWhiteSpace(result.Text), "expected a non-empty on-device transcript");
    }
}
