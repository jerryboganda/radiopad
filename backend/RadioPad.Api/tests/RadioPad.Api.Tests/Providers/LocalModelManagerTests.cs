using System.Linq;
using RadioPad.Infrastructure.Providers.Local;
using Xunit;

namespace RadioPad.Api.Tests;

/// <summary>
/// On-device model manager — the generalized catalog (seeded from the pinned STT
/// specs + roadmap placeholders) and the in-memory download-progress tracker that
/// backs the LocalModelsController polling surface. Deterministic: no network.
/// </summary>
public class LocalModelCatalogTests
{
    [Fact]
    public void Catalog_Includes_Stt_Models_Whose_Ids_And_Engines_Match_The_Pinned_Specs()
    {
        var cat = new LocalModelCatalog();

        var parakeet = cat.ById(LocalSttModels.Parakeet.Name);
        Assert.NotNull(parakeet);
        Assert.Equal(ModelKind.Stt, parakeet!.Kind);
        Assert.Equal(SherpaParakeetSttClient.EngineName, parakeet.Engine);
        Assert.Equal(ModelArchiveKind.TarBz2, parakeet.ArchiveKind);
        Assert.False(parakeet.Placeholder);
        // Id MUST equal the model-dir name the engine resolves, or status/delete
        // look in the wrong folder.
        Assert.Equal(LocalSttModels.Parakeet.Sha256, parakeet.Sha256);
    }

    [Fact]
    public void Catalog_Surfaces_Tts_As_Placeholder_And_MedGemma_As_A_Real_Orchestrator()
    {
        var cat = new LocalModelCatalog();
        // TTS is still a roadmap placeholder.
        Assert.Contains(cat.All, m => m.Kind == ModelKind.Tts && m.Placeholder);
        // The orchestrator kind is now a REAL model (MedGemma), not a placeholder.
        Assert.Contains(cat.All, m => m.Kind == ModelKind.Orchestrator && !m.Placeholder);
    }

    [Fact]
    public void Catalog_Lists_MedAsr_As_The_Primary_Stt_Engine()
    {
        var cat = new LocalModelCatalog();
        var medasr = cat.ById(LocalSttModels.MedAsrModelName);
        Assert.NotNull(medasr);
        Assert.Equal(ModelKind.Stt, medasr!.Kind);
        Assert.Equal(SherpaMedAsrSttClient.EngineName, medasr.Engine);
        Assert.Equal(ModelArchiveKind.MedAsrCtc, medasr.ArchiveKind);
        Assert.False(medasr.Placeholder);
        Assert.Equal(LocalSttModels.MedAsrModel.Sha256, medasr.Sha256);
    }

    [Fact]
    public void Catalog_Ids_Are_Unique()
    {
        var cat = new LocalModelCatalog();
        Assert.Equal(cat.All.Count, cat.All.Select(m => m.Id).Distinct().Count());
    }

    [Fact]
    public void Unknown_Id_Resolves_To_Null()
    {
        Assert.Null(new LocalModelCatalog().ById("nope"));
    }
}

public class ModelProvisioningStatusTests
{
    [Fact]
    public void Unknown_Model_Has_No_Snapshot()
    {
        Assert.Null(new ModelProvisioningStatus().Get("m"));
    }

    [Fact]
    public void Tracks_State_Total_And_Bytes()
    {
        var s = new ModelProvisioningStatus();

        s.SetState("m", ProvisionState.Downloading);
        s.SetTotal("m", 1000);
        s.ReportBytes("m", 250);

        var snap = s.Get("m");
        Assert.NotNull(snap);
        Assert.Equal(ProvisionState.Downloading, snap!.State);
        Assert.Equal(1000, snap.TotalBytes);
        Assert.Equal(250, snap.BytesDownloaded);

        s.SetState("m", ProvisionState.Ready);
        Assert.Equal(ProvisionState.Ready, s.Get("m")!.State);
    }

    [Fact]
    public void Failed_State_Records_Error_Message()
    {
        var s = new ModelProvisioningStatus();
        s.SetState("m", ProvisionState.Failed, "boom");

        var snap = s.Get("m");
        Assert.Equal(ProvisionState.Failed, snap!.State);
        Assert.Equal("boom", snap.Error);
    }

    [Fact]
    public void NotStarted_Resets_Progress_Counters()
    {
        var s = new ModelProvisioningStatus();
        s.SetState("m", ProvisionState.Downloading);
        s.SetTotal("m", 1000);
        s.ReportBytes("m", 500);

        s.SetState("m", ProvisionState.NotStarted);

        var snap = s.Get("m");
        Assert.Equal(0, snap!.BytesDownloaded);
        Assert.Equal(0, snap.TotalBytes);
    }
}
