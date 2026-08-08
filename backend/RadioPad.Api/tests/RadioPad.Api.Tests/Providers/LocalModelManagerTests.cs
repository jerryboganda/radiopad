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
    public void Catalog_Surfaces_Tts_As_A_Placeholder()
    {
        var cat = new LocalModelCatalog();
        // TTS is still a roadmap placeholder.
        Assert.Contains(cat.All, m => m.Kind == ModelKind.Tts && m.Placeholder);
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
