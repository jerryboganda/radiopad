using Microsoft.Extensions.Logging.Abstractions;
using RadioPad.Infrastructure.Providers.Local;
using Xunit;

namespace RadioPad.Api.Tests;

/// <summary>
/// Phase 1 activation — unit tests for the on-device STT model catalog/resolver
/// (<see cref="LocalSttModels"/>) and the first-run provisioner short-circuit
/// (<see cref="SttModelProvisioner"/>). Deterministic: no network, no env, no
/// native model — the download path is exercised only in the desktop smoke test.
/// </summary>
public class SttModelProvisionerTests
{
    private static string TempDir() =>
        Path.Combine(Path.GetTempPath(), "rp-stt-test-" + Guid.NewGuid().ToString("N"));

    private static void WriteBundle(string dir)
    {
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "encoder.int8.onnx"), "x");
        File.WriteAllText(Path.Combine(dir, "decoder.int8.onnx"), "x");
        File.WriteAllText(Path.Combine(dir, "joiner.int8.onnx"), "x");
        File.WriteAllText(Path.Combine(dir, "tokens.txt"), "x");
    }

    [Fact]
    public void ResolveFiles_Finds_Int8_Recursively_Through_Archive_Subdir()
    {
        var root = TempDir();
        // Mirror the archive layout: files live under a nested top-level folder.
        var nested = Path.Combine(root, "sherpa-onnx-nemo-parakeet-tdt-0.6b-v3-int8");
        WriteBundle(nested);
        try
        {
            Assert.True(LocalSttModels.IsComplete(root));
            var (enc, dec, join, tok) = LocalSttModels.ResolveFiles(root);
            Assert.NotNull(enc);
            Assert.NotNull(dec);
            Assert.NotNull(join);
            Assert.NotNull(tok);
            Assert.Contains("int8", enc!, StringComparison.OrdinalIgnoreCase);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void IsComplete_Is_False_When_A_File_Is_Missing()
    {
        var dir = TempDir();
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(Path.Combine(dir, "encoder.int8.onnx"), "x");
            File.WriteAllText(Path.Combine(dir, "decoder.int8.onnx"), "x");
            File.WriteAllText(Path.Combine(dir, "joiner.int8.onnx"), "x");
            // tokens.txt absent
            Assert.False(LocalSttModels.IsComplete(dir));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void IsComplete_Is_False_For_Missing_Dir()
    {
        Assert.False(LocalSttModels.IsComplete(TempDir())); // never created
    }

    [Fact]
    public async Task EnsureInDir_ShortCircuits_Without_Downloading_When_Model_Present()
    {
        var dir = TempDir();
        WriteBundle(dir);
        try
        {
            var prov = new SttModelProvisioner(NullLogger<SttModelProvisioner>.Instance);
            // A deliberately invalid URL/hash proves the download path is never
            // taken when the bundle is already complete.
            var spec = new LocalSttModels.ModelSpec(
                Name: "test",
                Url: "https://invalid.invalid/should-never-be-fetched.tar.bz2",
                SizeBytes: 0,
                Sha256: "deadbeef");

            var ok = await prov.EnsureInDirAsync(spec, dir, CancellationToken.None);
            Assert.True(ok);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }
}
