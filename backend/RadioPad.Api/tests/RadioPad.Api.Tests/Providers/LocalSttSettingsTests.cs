using RadioPad.Infrastructure.Providers.Local;
using Xunit;

namespace RadioPad.Api.Tests;

/// <summary>
/// Persisted "primary STT model" selection: defaults to MedASR (decision D2), derives the engine
/// from the chosen id, round-trips to disk, and refuses to stay pinned to an engine that has been
/// hidden from the UI. Deterministic — uses a temp prefs file (no env, no models).
/// </summary>
public class LocalSttSettingsTests
{
    private static string TempFile() =>
        Path.Combine(Path.GetTempPath(), "rp-stt-prefs-" + Guid.NewGuid().ToString("N") + ".json");

    [Fact]
    public void Defaults_To_MedAsr_Primary()
    {
        // The out-of-box default is MedASR (D2), mapping to the "medasr" engine. There is no
        // longer a SAPI branch here: that engine is hidden from the UI, so defaulting to it
        // would point the "Primary" badge at a card the radiologist cannot see.
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
            s.SetPrimary(LocalSttModels.DefaultModelName); // Parakeet — a visible engine

            Assert.Equal(SherpaParakeetSttClient.EngineName, s.PrimaryEngineId);
            Assert.True(s.IsPrimary(LocalSttModels.DefaultModelName));
            Assert.False(s.IsPrimary(LocalSttModels.MedAsrModelName));

            // Choice survives a reload from disk.
            var reloaded = new LocalSttSettings(path);
            Assert.Equal(LocalSttModels.DefaultModelName, reloaded.PrimaryModelId);
        }
        finally { try { File.Delete(path); } catch { /* best effort */ } }
    }

    [Fact]
    public void A_Saved_Primary_That_Is_Now_Hidden_Falls_Back_To_The_Default()
    {
        // Regression guard for the engine-hiding change: a workstation that had already
        // selected Windows SAPI (or Edge) before those cards were withheld must not stay
        // pinned to an engine the radiologist can no longer see — there would be no UI
        // left to switch away from it. Reloading coerces the selection back to the default.
        var path = TempFile();
        try
        {
            var s = new LocalSttSettings(path);
            s.SetPrimary(LocalModelCatalog.WindowsSapiId); // still settable in-process
            Assert.True(s.IsPrimary(LocalModelCatalog.WindowsSapiId));

            var reloaded = new LocalSttSettings(path);
            Assert.Equal(LocalSttModels.MedAsrModelName, reloaded.PrimaryModelId);
            Assert.Equal(SherpaMedAsrSttClient.EngineName, reloaded.PrimaryEngineId);
        }
        finally { try { File.Delete(path); } catch { /* best effort */ } }
    }

    [Fact]
    public void The_Hidden_Engines_Are_Withheld_From_The_Ui_But_Still_Resolvable()
    {
        // The three platform speech engines are hidden, not deleted: absent from what the
        // UI lists, still present in All/ById so the runtime and any saved state keep working.
        // Flipping Hidden back to false in LocalModelCatalog is all that re-enabling takes.
        var cat = new LocalModelCatalog();
        string[] hidden =
        {
            LocalModelCatalog.WindowsSapiId,
            LocalModelCatalog.WindowsWinRtId,
            LocalModelCatalog.EdgeWebSpeechId,
        };

        foreach (var id in hidden)
        {
            Assert.DoesNotContain(cat.Visible, m => m.Id == id);
            Assert.Contains(cat.All, m => m.Id == id);
            Assert.NotNull(cat.ById(id));
            Assert.True(LocalModelCatalog.IsHiddenId(id));
        }

        // The engines radiologists actually use are unaffected.
        Assert.Contains(cat.Visible, m => m.Id == LocalSttModels.MedAsrModelName);
        Assert.Contains(cat.Visible, m => m.Id == LocalSttModels.DefaultModelName);
    }
}
