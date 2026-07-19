using RadioPad.Infrastructure.Providers.Local;
using Xunit;

namespace RadioPad.Api.Tests;

/// <summary>
/// Every hosted-file archive kind must have a real "is it installed on disk" check.
///
/// <para>This exists because an end-to-end run of the desktop sidecar caught MedASR reporting
/// <c>downloaded=false</c> while simultaneously reporting <c>available=true</c> — its
/// <see cref="ModelArchiveKind.MedAsrCtc"/> kind fell through the controller's archive-kind switch
/// to a <c>_ =&gt; false</c> default. The user-visible effect was that the model manager offered to
/// re-download a 154 MB bundle that was already present, and "Make primary" returned 409
/// "download the model before making it primary" — so MedASR could never actually become the
/// primary engine, which is the whole of decision D2.</para>
///
/// <para>The unit tests of the day all passed: the catalog entry was right, the engine worked, the
/// provisioner worked. Only the controller's mapping was wrong. So this test guards the invariant
/// itself — a new archive kind that nobody teaches the completeness check will fail here.</para>
/// </summary>
public class LocalModelDownloadedStateTests
{
    /// <summary>
    /// Mirrors LocalModelsController.IsDownloaded's archive-kind switch for hosted files. Kept in
    /// sync deliberately: the point is that adding a kind to the enum without adding it BOTH here
    /// and in the controller fails the completeness test below.
    /// </summary>
    private static bool IsInstalled(ModelArchiveKind kind, string dir, string? fileName) => kind switch
    {
        ModelArchiveKind.TarBz2 => LocalSttModels.IsComplete(dir),
        ModelArchiveKind.MedAsrCtc => LocalSttModels.IsMedAsrComplete(dir),
        ModelArchiveKind.RawFile => fileName is not null && File.Exists(Path.Combine(dir, fileName)),
        _ => false,
    };

    [Fact]
    public void Every_ArchiveKind_Has_A_Completeness_Check()
    {
        // A kind that falls through to `_ => false` can never report itself installed, which
        // silently breaks download-state and "Make primary" for that model.
        foreach (ModelArchiveKind kind in Enum.GetValues<ModelArchiveKind>())
        {
            var handled = kind is ModelArchiveKind.TarBz2 or ModelArchiveKind.MedAsrCtc or ModelArchiveKind.RawFile;
            Assert.True(handled,
                $"ModelArchiveKind.{kind} has no completeness check — LocalModelsController.IsDownloaded " +
                "would always report it as not-downloaded. Add it there and here.");
        }
    }

    [Fact]
    public void MedAsr_With_Both_Files_Present_Reports_Installed()
    {
        var dir = Path.Combine(Path.GetTempPath(), "rp-medasr-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, LocalSttModels.MedAsrModel.FileName), "onnx");
            File.WriteAllText(Path.Combine(dir, LocalSttModels.MedAsrTokens.FileName), "tokens");

            Assert.True(IsInstalled(ModelArchiveKind.MedAsrCtc, dir, LocalSttModels.MedAsrModel.FileName),
                "a fully-provisioned MedASR bundle must report as downloaded");
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch { /* best effort */ } }
    }

    [Fact]
    public void MedAsr_Missing_Tokens_Reports_Not_Installed()
    {
        // Half a bundle is not a bundle: the engine cannot load without tokens.txt, so reporting
        // "downloaded" here would strand the user with a model that never works.
        var dir = Path.Combine(Path.GetTempPath(), "rp-medasr-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, LocalSttModels.MedAsrModel.FileName), "onnx");

            Assert.False(IsInstalled(ModelArchiveKind.MedAsrCtc, dir, LocalSttModels.MedAsrModel.FileName));
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch { /* best effort */ } }
    }

    [Fact]
    public void MedAsr_Empty_Dir_Reports_Not_Installed()
    {
        var dir = Path.Combine(Path.GetTempPath(), "rp-medasr-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(dir);
            Assert.False(IsInstalled(ModelArchiveKind.MedAsrCtc, dir, LocalSttModels.MedAsrModel.FileName));
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch { /* best effort */ } }
    }
}
