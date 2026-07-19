using Microsoft.Extensions.Logging.Abstractions;
using RadioPad.Infrastructure.Audio;
using RadioPad.Infrastructure.Providers.Local;
using Xunit;

namespace RadioPad.Api.Tests;

/// <summary>
/// End-to-end smoke for the DEFAULT primary on-device engine (decision D2): loads the sherpa-onnx
/// native library + the downloaded MedASR CTC bundle and transcribes the bundle's own radiology
/// sample WAV. This is the ONLY test that proves <c>OfflineModelConfig.MedAsr</c> is wired
/// correctly at RUNTIME — a compile-clean config can still fail to load a model.
///
/// GATED on <c>RADIOPAD_MEDASR_SMOKE_MODEL_DIR</c> so it runs ONLY where the ~154 MB bundle is
/// present (the dedicated Windows smoke job); normal CI skips via an early return. Setting
/// <c>RADIOPAD_STT_SMOKE_REQUIRE=1</c> turns a missing dir into a hard failure so a mis-wired
/// smoke job can't silently "pass" — see <see cref="SttSmokeGate"/>.
/// </summary>
public class MedAsrEngineSmokeTests
{
    private readonly Xunit.Abstractions.ITestOutputHelper _out;

    public MedAsrEngineSmokeTests(Xunit.Abstractions.ITestOutputHelper output) => _out = output;

    /// <summary>
    /// Content words from test_wavs/0.wav's ground truth (the bundle ships transcript.txt). We
    /// assert on clinically-load-bearing tokens — laterality, anatomy, the pathology — rather than
    /// an exact string match, because CTC output casing/punctuation is not contractually stable and
    /// an exact match would make this a brittle false-negative machine.
    /// </summary>
    private static readonly string[] ExpectedContentWords =
        { "chest", "lobe", "right", "embolus", "pneumothorax" };

    [Fact]
    public async Task MedAsr_Transcribes_Radiology_Sample_OnDevice()
    {
        var modelDir = SttSmokeGate.DirOrSkip("RADIOPAD_MEDASR_SMOKE_MODEL_DIR");
        if (modelDir is null)
            return; // not configured (normal CI) — skip; fails loudly if REQUIRE=1

        Environment.SetEnvironmentVariable("RADIOPAD_LOCAL_STT_ENABLED", "1");
        Environment.SetEnvironmentVariable("RADIOPAD_STT_MODEL_DIR", modelDir);

        var client = new SherpaMedAsrSttClient(
            new WavAudioDecoder(),
            NullLogger<SherpaMedAsrSttClient>.Instance);

        Assert.True(client.Available, "MedASR should be Available with the CTC bundle present");

        // The bundle ships test_wavs/*.wav (16 kHz mono). 0.wav is a radiology dictation.
        var wav = Directory.GetFiles(modelDir, "0.wav", SearchOption.AllDirectories).FirstOrDefault()
                  ?? Directory.GetFiles(modelDir, "*.wav", SearchOption.AllDirectories).FirstOrDefault();
        Assert.NotNull(wav);

        await using var fs = File.OpenRead(wav!);
        var result = await client.TranscribeAsync(fs, "audio/wav", CancellationToken.None);

        // Echo the transcript + latency so the CI smoke job's log shows WHAT was recognised —
        // a bare pass/fail hides accuracy regressions that don't trip the content assertions.
        _out.WriteLine($"[medasr] {result.LatencyMs} ms :: {result.Text}");

        Assert.Equal(SherpaMedAsrSttClient.ProviderName, result.Provider);
        Assert.Equal(LocalSttModels.MedAsrModelName, result.Model);
        Assert.False(string.IsNullOrWhiteSpace(result.Text), "expected a non-empty on-device transcript");

        // Accuracy floor: a model that loads but decodes garbage would still return non-empty text,
        // so assert the clinically-meaningful content actually survived.
        var lower = result.Text.ToLowerInvariant();
        foreach (var word in ExpectedContentWords)
            Assert.True(lower.Contains(word, StringComparison.Ordinal),
                $"expected '{word}' in the MedASR transcript but got: {result.Text}");
    }

    /// <summary>
    /// The ensemble path (<see cref="ILocalSttEngine"/>) must produce tokenized hypotheses too —
    /// this is what feeds the ROVER reconciler, so an empty token list would silently drop MedASR
    /// out of every ensemble vote.
    /// </summary>
    [Fact]
    public async Task MedAsr_Produces_Tokenized_Hypothesis_For_The_Ensemble()
    {
        var modelDir = SttSmokeGate.DirOrSkip("RADIOPAD_MEDASR_SMOKE_MODEL_DIR");
        if (modelDir is null)
            return;

        Environment.SetEnvironmentVariable("RADIOPAD_LOCAL_STT_ENABLED", "1");
        Environment.SetEnvironmentVariable("RADIOPAD_STT_MODEL_DIR", modelDir);

        var client = new SherpaMedAsrSttClient(
            new WavAudioDecoder(),
            NullLogger<SherpaMedAsrSttClient>.Instance);
        Assert.True(client.Available);

        var wav = Directory.GetFiles(modelDir, "*.wav", SearchOption.AllDirectories).FirstOrDefault();
        Assert.NotNull(wav);

        var hypothesis = await client.RecognizeAsync(File.ReadAllBytes(wav!), CancellationToken.None);

        Assert.Equal(SherpaMedAsrSttClient.EngineName, hypothesis.EngineId);
        Assert.NotEmpty(hypothesis.Tokens);
    }
}
