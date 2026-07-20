using Microsoft.Extensions.Logging.Abstractions;
using RadioPad.Application.Abstractions;
using RadioPad.Application.Stt;
using RadioPad.Infrastructure.Providers.Local;
using Xunit;

namespace RadioPad.Api.Tests;

/// <summary>
/// Phase 2c — the ensemble orchestrator: runs the engines and either reconciles
/// two hypotheses (ensemble on, ≥2 engines) or transcribes single-engine. Uses
/// fake engines; the real engines are tested separately.
/// </summary>
[Collection(RadioPad.Api.Tests.Infrastructure.EnvironmentVariableCollection.Name)]
public class LocalSttEnsembleTests
{
    private sealed class FakeEngine : ILocalSttEngine
    {
        private readonly EngineTranscript _t;
        public FakeEngine(string id, bool available, params SttToken[] toks)
        {
            EngineId = id;
            Available = available;
            _t = new EngineTranscript(id, toks);
        }
        public string EngineId { get; }
        public bool Available { get; }
        public string? LastError => null;
        public Task<EngineTranscript> RecognizeAsync(byte[] wavBytes, CancellationToken ct) => Task.FromResult(_t);
    }

    private sealed class StubSettings : ILocalSttSettings
    {
        private string _primary;
        public StubSettings(string primaryModelId) => _primary = primaryModelId;
        public string PrimaryModelId => _primary;
        public string PrimaryEngineId => _primary == LocalModelCatalog.WindowsSapiId
            ? LocalModelCatalog.WindowsSapiEngine : SherpaParakeetSttClient.EngineName;
        public bool IsPrimary(string id) => id == _primary;
        public void SetPrimary(string id) => _primary = id;
    }

    private static SttToken T(string w, double c) => new(w, c);
    private static MemoryStream Audio() => new(new byte[] { 1, 2, 3, 4 });
    private static LocalSttEnsemble Build(params FakeEngine[] engines)
        => new(engines, NullLogger<LocalSttEnsemble>.Instance);

    [Fact]
    public async Task ConfiguredPrimary_Single_Uses_Configured_Engine()
    {
        await WithEnsemble(false, async () =>
        {
            var ensemble = new LocalSttEnsemble(
                new ILocalSttEngine[]
                {
                    new FakeEngine("parakeet", true, T("para", 0.9)),
                    new FakeEngine(LocalModelCatalog.WindowsSapiEngine, true, T("sapi", 0.9)),
                },
                NullLogger<LocalSttEnsemble>.Instance,
                new StubSettings(LocalModelCatalog.WindowsSapiId)); // SAPI is primary
            var res = await ensemble.TranscribeAsync(Audio(), "audio/wav", CancellationToken.None);
            Assert.Equal(LocalModelCatalog.WindowsSapiEngine, res.Model); // single-engine path picked the configured primary
        });
    }

    private static async Task WithEnsemble(bool on, Func<Task> body)
    {
        var prev = Environment.GetEnvironmentVariable("RADIOPAD_STT_ENSEMBLE");
        Environment.SetEnvironmentVariable("RADIOPAD_STT_ENSEMBLE", on ? "1" : null);
        try { await body(); }
        finally { Environment.SetEnvironmentVariable("RADIOPAD_STT_ENSEMBLE", prev); }
    }

    [Fact]
    public async Task EnsembleOff_Uses_Single_Engine_With_No_Spans()
    {
        await WithEnsemble(false, async () =>
        {
            var svc = Build(
                new FakeEngine("parakeet", true, T("lungs", 0.9), T("clear", 0.9)),
                new FakeEngine("secondary", true, T("lungs", 0.9), T("clear", 0.9)));

            var r = await svc.TranscribeAsync(Audio(), "audio/wav", CancellationToken.None);

            Assert.Equal("local", r.Provider);
            Assert.Equal("parakeet", r.Model);
            Assert.Equal("lungs clear", r.Text);
            Assert.Null(r.Spans);
        });
    }

    [Fact]
    public async Task EnsembleOn_Reconciles_Two_Engines_And_Flags_Disagreement()
    {
        await WithEnsemble(true, async () =>
        {
            var svc = Build(
                new FakeEngine("parakeet", true, T("lungs", 0.7), T("clear", 0.5)),
                new FakeEngine("secondary", true, T("lungs", 0.65), T("clean", 0.5)));

            var r = await svc.TranscribeAsync(Audio(), "audio/wav", CancellationToken.None);

            Assert.Equal("local_ensemble", r.Provider);
            Assert.Equal("parakeet+secondary", r.Model);
            Assert.NotNull(r.Spans);
            Assert.Contains(r.Spans!, s => s.Flagged); // the clear/clean disagreement
        });
    }

    [Fact]
    public async Task EnsembleOn_With_Only_One_Engine_Available_Falls_Back_To_Single()
    {
        await WithEnsemble(true, async () =>
        {
            var svc = Build(
                new FakeEngine("parakeet", true, T("lungs", 0.9)),
                new FakeEngine("secondary", available: false, T("ignored", 0.9)));

            var r = await svc.TranscribeAsync(Audio(), "audio/wav", CancellationToken.None);

            Assert.Equal("local", r.Provider);
            Assert.Equal("parakeet", r.Model);
            Assert.Null(r.Spans);
        });
    }

    [Fact]
    public async Task Mode_Ensemble_Override_Reconciles_Even_When_Default_Off()
    {
        await WithEnsemble(false, async () =>
        {
            var svc = Build(
                new FakeEngine("parakeet", true, T("lungs", 0.7), T("clear", 0.5)),
                new FakeEngine("secondary", true, T("lungs", 0.65), T("clean", 0.5)));

            var r = await svc.TranscribeAsync(Audio(), "audio/wav", CancellationToken.None, "ensemble");

            Assert.Equal("local_ensemble", r.Provider);
            Assert.NotNull(r.Spans);
        });
    }

    [Fact]
    public async Task Mode_Single_Override_Uses_One_Engine_Even_When_Default_On()
    {
        await WithEnsemble(true, async () =>
        {
            var svc = Build(
                new FakeEngine("parakeet", true, T("lungs", 0.9)),
                new FakeEngine("secondary", true, T("lungs", 0.9)));

            var r = await svc.TranscribeAsync(Audio(), "audio/wav", CancellationToken.None, "single");

            Assert.Equal("local", r.Provider);
            Assert.Null(r.Spans);
        });
    }

    [Fact]
    public void Available_Is_True_When_Any_Engine_Available()
    {
        Assert.True(Build(new FakeEngine("a", true)).Available);
        Assert.False(Build(new FakeEngine("a", false)).Available);
    }
}
