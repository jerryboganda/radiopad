using RadioPad.Infrastructure.Providers.Local;
using Xunit;

namespace RadioPad.Api.Tests;

/// <summary>
/// Persisted "primary STT model" selection: defaults to MedASR (decision D2, when SAPI isn't the
/// installed default), derives the engine from the chosen id, and round-trips to disk.
/// Deterministic — uses a temp prefs file (no env, no models).
/// </summary>
public class LocalSttSettingsTests
{
    private static string TempFile() =>
        Path.Combine(Path.GetTempPath(), "rp-stt-prefs-" + Guid.NewGuid().ToString("N") + ".json");

    [Fact]
    public void Defaults_To_MedAsr_Primary()
    {
        // RADIOPAD_LOCAL_STT_ENABLED is unset in tests, so the SAPI branch is skipped and the
        // out-of-box sherpa default is MedASR (D2), mapping to the "medasr" engine.
        var s = new LocalSttSettings(TempFile()); // file absent → defaults
        Assert.Equal(LocalSttModels.MedAsrModelName, s.PrimaryModelId);
        Assert.Equal(SherpaMedAsrSttClient.EngineName, s.PrimaryEngineId);
        Assert.True(s.IsPrimary(LocalSttModels.MedAsrModelName));
    }

    [Fact]
    public void Parakeet_Selection_Maps_To_The_Parakeet_Engine()
    {
        var path = TempFile();
        try
        {
            var s = new LocalSttSettings(path);
            s.SetPrimary(LocalSttModels.DefaultModelName); // promote Parakeet
            Assert.Equal(SherpaParakeetSttClient.EngineName, s.PrimaryEngineId);
        }
        finally { try { File.Delete(path); } catch { /* best effort */ } }
    }

    [Fact]
    public void SetPrimary_Updates_Selection_And_Persists()
    {
        var path = TempFile();
        try
        {
            var s = new LocalSttSettings(path);
            s.SetPrimary(LocalModelCatalog.WindowsSapiId);

            Assert.Equal(LocalModelCatalog.WindowsSapiEngine, s.PrimaryEngineId);
            Assert.True(s.IsPrimary(LocalModelCatalog.WindowsSapiId));
            Assert.False(s.IsPrimary(LocalSttModels.DefaultModelName));

            // Choice survives a reload from disk.
            var reloaded = new LocalSttSettings(path);
            Assert.Equal(LocalModelCatalog.WindowsSapiId, reloaded.PrimaryModelId);
        }
        finally { try { File.Delete(path); } catch { /* best effort */ } }
    }
}
