using Microsoft.Extensions.Logging.Abstractions;
using RadioPad.Infrastructure.Providers.Local;
using Xunit;

namespace RadioPad.Api.Tests;

/// <summary>
/// Gated Windows smoke for the Whisper engine: loads the real whisper.cpp native
/// library + GGML model and runs the recognizer over a synthesized 16 kHz mono
/// WAV — proving the native pipeline runs end-to-end. GATED on
/// <c>RADIOPAD_STT_WHISPER_SMOKE_DIR</c> so normal CI (Linux, no model/native lib)
/// skips it via an early return.
/// </summary>
public class WhisperEngineSmokeTests
{
    [Fact]
    public async Task Whisper_Loads_Model_And_Runs_OnDevice()
    {
        var dir = SttSmokeGate.DirOrSkip("RADIOPAD_STT_WHISPER_SMOKE_DIR");
        if (dir is null)
            return; // not configured -> skip; fails loudly if REQUIRE=1

        Environment.SetEnvironmentVariable("RADIOPAD_LOCAL_STT_ENABLED", "1");
        Environment.SetEnvironmentVariable("RADIOPAD_STT_MODEL_DIR", dir);

        var engine = new WhisperNetSttClient(NullLogger<WhisperNetSttClient>.Instance);
        Assert.True(engine.Available, "whisper engine should be Available with the model present");

        // Prefer a REAL speech WAV (set by the CI job to a sample from the Parakeet
        // bundle) so we prove actual transcription, not just that the native
        // pipeline doesn't throw. Fall back to silence when none is provided.
        var wavPath = Environment.GetEnvironmentVariable("RADIOPAD_STT_SMOKE_WAV");
        var haveSpeech = !string.IsNullOrWhiteSpace(wavPath) && File.Exists(wavPath);
        // When the CI job demands a real run, refuse to quietly fall back to
        // silence — a missing speech WAV must fail, not pass on an empty assertion.
        if (!haveSpeech && SttSmokeGate.IsRequired())
            Assert.Fail($"RADIOPAD_STT_SMOKE_WAV unset or missing ('{wavPath}') but RADIOPAD_STT_SMOKE_REQUIRE=1.");
        var wav = haveSpeech ? await File.ReadAllBytesAsync(wavPath!) : BuildSilentWav(seconds: 1.0);

        var result = await engine.RecognizeAsync(wav, CancellationToken.None);

        Assert.Equal(WhisperNetSttClient.EngineName, result.EngineId);
        if (haveSpeech)
            Assert.False(string.IsNullOrWhiteSpace(result.Text),
                "expected a non-empty on-device Whisper transcript for real speech");
    }

    private static byte[] BuildSilentWav(double seconds)
    {
        const int rate = 16000;
        int frames = (int)(rate * seconds);
        int dataLen = frames * 2;
        using var ms = new MemoryStream();
        var w = new BinaryWriter(ms);
        w.Write(System.Text.Encoding.ASCII.GetBytes("RIFF"));
        w.Write(36 + dataLen);
        w.Write(System.Text.Encoding.ASCII.GetBytes("WAVE"));
        w.Write(System.Text.Encoding.ASCII.GetBytes("fmt "));
        w.Write(16);
        w.Write((short)1);
        w.Write((short)1);
        w.Write(rate);
        w.Write(rate * 2);
        w.Write((short)2);
        w.Write((short)16);
        w.Write(System.Text.Encoding.ASCII.GetBytes("data"));
        w.Write(dataLen);
        for (int i = 0; i < frames; i++) w.Write((short)0);
        w.Flush();
        return ms.ToArray();
    }
}
