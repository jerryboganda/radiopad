using RadioPad.Infrastructure.Providers.Local;
using Xunit;

namespace RadioPad.Api.Tests;

/// <summary>
/// Dictation brief §2.2 — the optional on-device MedGemma report formatter is pinned in the catalog
/// with the verified Q4_K_M GGUF (URL + SHA-256 + size), download-on-demand (not auto-provisioned).
/// </summary>
public class MedGemmaCatalogTests
{
    [Fact]
    public void Catalog_Pins_MedGemma_As_A_Real_Downloadable_Orchestrator_Model()
    {
        var catalog = new LocalModelCatalog();
        var m = catalog.ById(LocalModelCatalog.MedGemmaId);

        Assert.NotNull(m);
        Assert.Equal(ModelKind.Orchestrator, m!.Kind);
        Assert.False(m.Placeholder);                                  // real, not a coming-soon card
        Assert.Equal(ModelArchiveKind.RawFile, m.ArchiveKind);        // single .gguf, no archive
        Assert.Equal(LocalModelCatalog.MedGemmaFileName, m.FileName);
        Assert.Equal(2489894976L, m.SizeBytes);
        Assert.Equal("b31becdf4f39561800505514cce67681604fe449d04dd35c8c92fd7848c6d7bd", m.Sha256);
        Assert.StartsWith("https://huggingface.co/", m.DownloadUrl);
        Assert.EndsWith(".gguf", m.DownloadUrl);
        Assert.Equal(ModelProvisioning.HostedFile, m.Provisioning);
    }

    [Fact]
    public void MedGemma_Is_Not_The_Auto_Provisioned_First_Run_Model()
    {
        // Only the STT primary (Parakeet) auto-downloads on first run; the formatter is optional.
        Assert.NotEqual(LocalModelCatalog.MedGemmaId, LocalSttModels.Parakeet.Name);
    }
}
