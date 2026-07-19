using Microsoft.Extensions.Logging.Abstractions;
using RadioPad.Application.Abstractions;
using RadioPad.Application.Stt;
using RadioPad.Infrastructure.Providers.Local;
using Xunit;

namespace RadioPad.Api.Tests;

/// <summary>
/// Which engine actually transcribes when the configured primary cannot run.
///
/// <para>Found by running the real desktop sidecar: with the persisted primary set to
/// <c>edge-webspeech</c> — a FRONTEND-only engine with no backend implementation — every
/// transcription fell back to <c>engines[0]</c>, i.e. whichever engine DI registered first. That
/// routed dictation to Parakeet while MedASR sat installed and available, quietly defeating
/// decision D2. Registration order must never decide which engine transcribes a radiologist.</para>
/// </summary>
public class LocalSttEnsembleFallbackTests
{
    private sealed class FakeEngine : ILocalSttEngine
    {
        public FakeEngine(string id, bool available = true)
        {
            EngineId = id;
            Available = available;
        }

        public string EngineId { get; }
        public bool Available { get; }
        public string? LastError => null;

        public Task<EngineTranscript> RecognizeAsync(byte[] wavBytes, CancellationToken ct) =>
            Task.FromResult(new EngineTranscript(EngineId, new[] { new SttToken(EngineId, 0.9) }));
    }

    private sealed class StubSettings : ILocalSttSettings
    {
        public StubSettings(string primaryEngineId) => PrimaryEngineId = primaryEngineId;
        public string PrimaryModelId => PrimaryEngineId;
        public string PrimaryEngineId { get; }
        public bool IsPrimary(string modelId) => modelId == PrimaryEngineId;
        public void SetPrimary(string modelId) { }
    }

    /// <summary>Build an ensemble whose configured primary is the given ENGINE id.</summary>
    private static LocalSttEnsemble Build(string primaryEngineId, params ILocalSttEngine[] engines) =>
        new(engines, NullLogger<LocalSttEnsemble>.Instance, new StubSettings(primaryEngineId));

    /// <summary>A tiny valid 16 kHz mono WAV; the fakes ignore the bytes.</summary>
    private static byte[] Wav() => new byte[44];

    [Fact]
    public async Task FrontendOnly_Primary_Falls_Back_To_MedAsr_Not_Registration_Order()
    {
        // Parakeet deliberately registered FIRST — the old `engines[0]` fallback picked it.
        var ensemble = Build(
            LocalModelCatalog.EdgeWebSpeechEngine,
            new FakeEngine(SherpaParakeetSttClient.EngineName),
            new FakeEngine(SherpaMedAsrSttClient.EngineName));

        var result = await ensemble.TranscribeAsync(
            new MemoryStream(Wav()), "audio/wav", CancellationToken.None, mode: "single");

        Assert.Equal(SherpaMedAsrSttClient.EngineName, result.Model);
    }

    [Fact]
    public async Task Falls_Back_To_Parakeet_When_MedAsr_Is_Not_Available()
    {
        // MedASR still downloading: the next deliberate choice is Parakeet, not SAPI.
        var ensemble = Build(
            LocalModelCatalog.EdgeWebSpeechEngine,
            new FakeEngine(LocalModelCatalog.WindowsSapiEngine),
            new FakeEngine(SherpaParakeetSttClient.EngineName));

        var result = await ensemble.TranscribeAsync(
            new MemoryStream(Wav()), "audio/wav", CancellationToken.None, mode: "single");

        Assert.Equal(SherpaParakeetSttClient.EngineName, result.Model);
    }

    [Fact]
    public async Task An_Explicitly_Configured_Primary_Still_Wins()
    {
        // The fallback order must not override a real user choice.
        var ensemble = Build(
            SherpaParakeetSttClient.EngineName, // Parakeet, explicitly promoted
            new FakeEngine(SherpaMedAsrSttClient.EngineName),
            new FakeEngine(SherpaParakeetSttClient.EngineName));

        var result = await ensemble.TranscribeAsync(
            new MemoryStream(Wav()), "audio/wav", CancellationToken.None, mode: "single");

        Assert.Equal(SherpaParakeetSttClient.EngineName, result.Model);
    }

    [Fact]
    public async Task Unavailable_Engines_Are_Never_Selected()
    {
        var ensemble = Build(
            LocalModelCatalog.EdgeWebSpeechEngine,
            new FakeEngine(SherpaMedAsrSttClient.EngineName, available: false),
            new FakeEngine(SherpaParakeetSttClient.EngineName));

        var result = await ensemble.TranscribeAsync(
            new MemoryStream(Wav()), "audio/wav", CancellationToken.None, mode: "single");

        Assert.Equal(SherpaParakeetSttClient.EngineName, result.Model);
    }

    [Fact]
    public async Task An_Unknown_Engine_Outside_The_Fallback_List_Is_Still_Used_As_A_Last_Resort()
    {
        // Better to transcribe with something than to fail: a future engine not yet in the
        // fallback order must not make dictation unavailable.
        var ensemble = Build(
            LocalModelCatalog.EdgeWebSpeechEngine,
            new FakeEngine("some-future-engine"));

        var result = await ensemble.TranscribeAsync(
            new MemoryStream(Wav()), "audio/wav", CancellationToken.None, mode: "single");

        Assert.Equal("some-future-engine", result.Model);
    }
}
