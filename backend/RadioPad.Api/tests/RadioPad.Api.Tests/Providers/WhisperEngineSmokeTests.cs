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
        var dir = Environment.GetEnvironmentVariable("RADIOPAD_STT_WHISPER_SMOKE_DIR");
        if (string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir))
            return; // not configured -> skip

        Environment.SetEnvironmentVariable("RADIOPAD_LOCAL_STT_ENABLED", "1");
        Environment.SetEnvironmentVariable("RADIOPAD_STT_MODEL_DIR", dir);

        var engine = new WhisperNetSttClient(NullLogger<WhisperNetSttClient>.Instance);
        Assert.True(engine.Available, "whisper engine should be Available with the model present");

        var wav = BuildSilentWav(seconds: 1.0);
        var result = await engine.RecognizeAsync(wav, CancellationToken.None);

        // No assertion on text (silence); the point is the native pipeline runs.
        Assert.Equal(WhisperNetSttClient.EngineName, result.EngineId);
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
