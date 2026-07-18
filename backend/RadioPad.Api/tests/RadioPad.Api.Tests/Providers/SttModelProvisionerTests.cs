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
    public async Task EnsureFileInDir_ShortCircuits_When_File_Present()
    {
        var dir = TempDir();
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(Path.Combine(dir, "ggml-test.bin"), "x");
            var prov = new SttModelProvisioner(NullLogger<SttModelProvisioner>.Instance);
            var spec = new LocalSttModels.FileSpec(
                Name: "t", FileName: "ggml-test.bin",
                Url: "https://invalid.invalid/should-never-be-fetched.bin",
                SizeBytes: 0, Sha256: "deadbeef");

            Assert.True(await prov.EnsureFileInDirAsync(spec, dir, CancellationToken.None));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    private static void WriteMedAsrBundle(string dir)
    {
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "model.int8.onnx"), "x");
        File.WriteAllText(Path.Combine(dir, "tokens.txt"), "x");
    }

    [Fact]
    public void MedAsr_IsComplete_And_Resolves_When_Model_And_Tokens_Present()
    {
        var dir = TempDir();
        WriteMedAsrBundle(dir);
        try
        {
            Assert.True(LocalSttModels.IsMedAsrComplete(dir));
            var (model, tokens) = LocalSttModels.ResolveMedAsrFiles(dir);
            Assert.NotNull(model);
            Assert.NotNull(tokens);
            Assert.EndsWith("model.int8.onnx", model!);
            Assert.EndsWith("tokens.txt", tokens!);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void MedAsr_IsComplete_False_When_Tokens_Missing()
    {
        var dir = TempDir();
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(Path.Combine(dir, "model.int8.onnx"), "x"); // tokens.txt absent
            Assert.False(LocalSttModels.IsMedAsrComplete(dir));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void MedAsr_Descriptor_Pins_The_Verified_Public_Artifact()
    {
        // Guards the pinned integrity metadata (verified against the HF blob API 2026-07-19).
        Assert.Equal(154106419L, LocalSttModels.MedAsrModel.SizeBytes);
        Assert.Equal(
            "2c20f03265ee6144c566fd18b0f7bbb4f0d005d11ce9440dd641920210f4c33a",
            LocalSttModels.MedAsrModel.Sha256);
        Assert.Contains("sherpa-onnx-medasr-ctc-en-int8", LocalSttModels.MedAsrModel.Url);
        Assert.Equal("model.int8.onnx", LocalSttModels.MedAsrModel.FileName);
        Assert.Equal("tokens.txt", LocalSttModels.MedAsrTokens.FileName);
        // The tiny non-LFS tokens file carries no digest → provisioner skips its verification.
        Assert.Equal("", LocalSttModels.MedAsrTokens.Sha256);
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
