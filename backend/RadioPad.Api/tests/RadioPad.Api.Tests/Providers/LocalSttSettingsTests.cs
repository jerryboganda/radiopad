using RadioPad.Infrastructure.Providers.Local;
using Xunit;

namespace RadioPad.Api.Tests;

/// <summary>
/// Persisted "primary STT model" selection: defaults to Parakeet, derives the
/// engine + active whisper model from the chosen id, and round-trips to disk.
/// Deterministic — uses a temp prefs file (no env, no models).
/// </summary>
public class LocalSttSettingsTests
{
    private static string TempFile() =>
        Path.Combine(Path.GetTempPath(), "rp-stt-prefs-" + Guid.NewGuid().ToString("N") + ".json");

    [Fact]
    public void Defaults_To_Parakeet_Primary()
    {
        var s = new LocalSttSettings(TempFile()); // file absent → defaults
        Assert.Equal(LocalSttModels.DefaultModelName, s.PrimaryModelId);
        Assert.Equal(SherpaParakeetSttClient.EngineName, s.PrimaryEngineId);
        // Parakeet primary → whisper engine still defaults to turbo for the ensemble.
        Assert.Equal(LocalSttModels.WhisperModelName, s.ActiveWhisperModelId);
        Assert.True(s.IsPrimary(LocalSttModels.DefaultModelName));
    }

    [Fact]
    public void SetPrimary_Whisper_Updates_Engine_ActiveModel_And_Persists()
    {
        var path = TempFile();
        try
        {
            var s = new LocalSttSettings(path);
            s.SetPrimary(LocalSttModels.WhisperSmallEnModelName);

            Assert.Equal(WhisperNetSttClient.EngineName, s.PrimaryEngineId);
            Assert.Equal(LocalSttModels.WhisperSmallEnModelName, s.ActiveWhisperModelId);
            Assert.True(s.IsPrimary(LocalSttModels.WhisperSmallEnModelName));
            Assert.False(s.IsPrimary(LocalSttModels.DefaultModelName));

            // Choice survives a reload from disk.
            var reloaded = new LocalSttSettings(path);
            Assert.Equal(LocalSttModels.WhisperSmallEnModelName, reloaded.PrimaryModelId);
        }
        finally { try { File.Delete(path); } catch { /* best effort */ } }
    }
}
