using Microsoft.Extensions.Logging.Abstractions;
using RadioPad.Application.Abstractions;
using RadioPad.Infrastructure.Audio;
using RadioPad.Infrastructure.Providers.Local;
using Xunit;

namespace RadioPad.Api.Tests;

/// <summary>
/// End-to-end smoke for the REAL ensemble: loads both on-device engines
/// (sherpa-onnx Parakeet + whisper.cpp/Whisper.net) from a combined model dir,
/// runs them in parallel over a real speech WAV, and reconciles the two
/// hypotheses through the ROVER <see cref="LocalSttEnsemble"/>. This is the only
/// test that proves the full Phase-2 path actually works on real native libs +
/// real models — the unit tests use fakes.
///
/// GATED on <c>RADIOPAD_STT_ENSEMBLE_SMOKE_DIR</c> (a directory holding BOTH the
/// Parakeet bundle and the Whisper .bin), so normal CI skips it. The Windows
/// smoke job sets <c>RADIOPAD_STT_SMOKE_REQUIRE=1</c> so a mis-wired path fails
/// loudly instead of silently passing.
/// </summary>
public class LocalSttEnsembleSmokeTests
{
    [Fact]
    public async Task Ensemble_Reconciles_Both_Real_Engines_OnDevice()
    {
        var dir = SttSmokeGate.DirOrSkip("RADIOPAD_STT_ENSEMBLE_SMOKE_DIR");
        if (dir is null)
            return; // not configured (normal CI) — skip; fails loudly if REQUIRE=1

        Environment.SetEnvironmentVariable("RADIOPAD_LOCAL_STT_ENABLED", "1");
        Environment.SetEnvironmentVariable("RADIOPAD_STT_ENSEMBLE", "1");
        // Both clients resolve their model files (recursively) from this one dir.
        Environment.SetEnvironmentVariable("RADIOPAD_STT_MODEL_DIR", dir);

        var parakeet = new SherpaParakeetSttClient(
            new WavAudioDecoder(), NullLogger<SherpaParakeetSttClient>.Instance);
        var whisper = new WhisperNetSttClient(
            new WavAudioDecoder(), NullLogger<WhisperNetSttClient>.Instance);

        Assert.True(parakeet.Available, "Parakeet should be Available in the combined model dir");
        Assert.True(whisper.Available, "Whisper should be Available in the combined model dir");

        var ensemble = new LocalSttEnsemble(
            new ILocalSttEngine[] { parakeet, whisper },
            NullLogger<LocalSttEnsemble>.Instance);

        // Real speech WAV (16 kHz mono) shipped by the Parakeet bundle.
        var wavPath = Environment.GetEnvironmentVariable("RADIOPAD_STT_SMOKE_WAV");
        Assert.True(!string.IsNullOrWhiteSpace(wavPath) && File.Exists(wavPath),
            "RADIOPAD_STT_SMOKE_WAV must point at a real speech WAV for the ensemble smoke");

        await using var fs = File.OpenRead(wavPath!);
        var result = await ensemble.TranscribeAsync(fs, "audio/wav", CancellationToken.None, "ensemble");

        Assert.Equal("local_ensemble", result.Provider);
        Assert.Equal("parakeet+whisper", result.Model);
        Assert.False(string.IsNullOrWhiteSpace(result.Text),
            "expected a non-empty reconciled transcript from the real ensemble");
        Assert.NotNull(result.Spans); // ROVER alignment spans are always emitted in ensemble mode
    }
}
