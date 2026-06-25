using System.Security.Cryptography;
using ICSharpCode.SharpZipLib.BZip2;
using ICSharpCode.SharpZipLib.Tar;
using Microsoft.Extensions.Logging;

namespace RadioPad.Infrastructure.Providers.Local;

/// <summary>
/// First-run provisioner for the on-device STT model. Downloads the pinned
/// sherpa-onnx Parakeet bundle, verifies its SHA-256, and extracts it into the
/// model directory — entirely on-device, idempotent, and safe to call on every
/// startup (a fast no-op once the model is present). Models are NEVER shipped in
/// the MSI; this keeps the installer small while still guaranteeing a fully
/// offline engine after the first download.
/// </summary>
public sealed class SttModelProvisioner
{
    private readonly ILogger<SttModelProvisioner> _log;

    public SttModelProvisioner(ILogger<SttModelProvisioner> log) => _log = log;

    /// <summary>Ensure the default (Parakeet) model is installed.</summary>
    public Task<bool> EnsureAsync(CancellationToken ct) => EnsureAsync(LocalSttModels.Parakeet, ct);

    /// <summary>Ensure <paramref name="spec"/> is installed at its resolved model dir.</summary>
    public Task<bool> EnsureAsync(LocalSttModels.ModelSpec spec, CancellationToken ct)
    {
        var dir = LocalSttModels.ResolveModelDir(spec.Name);
        if (dir is null)
        {
            _log.LogWarning("No local-app-data dir resolvable; cannot provision on-device STT model.");
            return Task.FromResult(false);
        }
        return EnsureInDirAsync(spec, dir, ct);
    }

    /// <summary>
    /// Ensure <paramref name="spec"/> is installed into <paramref name="dir"/>.
    /// Returns true when the complete bundle is present afterwards. Exposed for
    /// tests (deterministic, no env / no network when the model already exists).
    /// </summary>
    public async Task<bool> EnsureInDirAsync(LocalSttModels.ModelSpec spec, string dir, CancellationToken ct)
    {
        if (LocalSttModels.IsComplete(dir))
            return true; // already installed — fast path on every subsequent boot

        var parent = Path.GetDirectoryName(dir);
        if (!string.IsNullOrEmpty(parent))
            Directory.CreateDirectory(parent);

        var tmpArchive = dir + $".download-{Guid.NewGuid():N}.tar.bz2";
        var tmpExtract = dir + $".extract-{Guid.NewGuid():N}";
        try
        {
            _log.LogInformation(
                "Downloading on-device STT model '{Model}' (~{SizeMB} MB) — dictation uses the cloud until this completes.",
                spec.Name, spec.SizeBytes / (1024 * 1024));

            await DownloadAsync(spec.Url, tmpArchive, ct);
            await VerifySha256Async(tmpArchive, spec.Sha256, ct);

            Directory.CreateDirectory(tmpExtract);
            Extract(tmpArchive, tmpExtract);

            if (!LocalSttModels.IsComplete(tmpExtract))
                throw new InvalidOperationException(
                    "extracted STT bundle is missing encoder/decoder/joiner/tokens");

            // Atomic publish: move the verified extract into final place.
            if (Directory.Exists(dir))
                Directory.Delete(dir, recursive: true);
            Directory.Move(tmpExtract, dir);

            _log.LogInformation("On-device STT model '{Model}' installed at {Dir}", spec.Name, dir);
            return true;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Failed to provision on-device STT model '{Model}'", spec.Name);
            return false;
        }
        finally
        {
            TryDelete(tmpArchive);
            TryDeleteDir(tmpExtract);
        }
    }

    /// <summary>Ensure the default (Whisper) model file is installed.</summary>
    public Task<bool> EnsureWhisperAsync(CancellationToken ct) => EnsureFileAsync(LocalSttModels.Whisper, ct);

    /// <summary>Ensure a single model FILE (e.g. a Whisper GGML .bin — no archive).</summary>
    public Task<bool> EnsureFileAsync(LocalSttModels.FileSpec spec, CancellationToken ct)
    {
        var dir = LocalSttModels.ResolveModelDir(spec.Name);
        if (dir is null)
        {
            _log.LogWarning("No local-app-data dir resolvable; cannot provision file '{File}'.", spec.FileName);
            return Task.FromResult(false);
        }
        return EnsureFileInDirAsync(spec, dir, ct);
    }

    /// <summary>
    /// Download + SHA-256-verify a single model file into <paramref name="dir"/>,
    /// atomically. Exposed for tests (fast no-op when present; no network).
    /// </summary>
    public async Task<bool> EnsureFileInDirAsync(LocalSttModels.FileSpec spec, string dir, CancellationToken ct)
    {
        var dest = Path.Combine(dir, spec.FileName);
        if (File.Exists(dest)) return true;

        Directory.CreateDirectory(dir);
        var tmp = dest + $".download-{Guid.NewGuid():N}";
        try
        {
            _log.LogInformation(
                "Downloading on-device STT model file '{File}' (~{SizeMB} MB).",
                spec.FileName, spec.SizeBytes / (1024 * 1024));
            await DownloadAsync(spec.Url, tmp, ct);
            await VerifySha256Async(tmp, spec.Sha256, ct);
            if (File.Exists(dest)) File.Delete(dest);
            File.Move(tmp, dest);
            _log.LogInformation("On-device STT model file '{File}' installed at {Dir}", spec.FileName, dir);
            return true;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Failed to provision on-device STT model file '{File}'", spec.FileName);
            return false;
        }
        finally
        {
            TryDelete(tmp);
        }
    }

    private static async Task DownloadAsync(string url, string destPath, CancellationToken ct)
    {
        using var http = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        using var resp = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        resp.EnsureSuccessStatusCode();
        await using var src = await resp.Content.ReadAsStreamAsync(ct);
        await using var dst = File.Create(destPath);
        await src.CopyToAsync(dst, 1 << 20, ct);
    }

    private static async Task VerifySha256Async(string path, string expectedHex, CancellationToken ct)
    {
        await using var fs = File.OpenRead(path);
        var hash = await SHA256.HashDataAsync(fs, ct);
        var actual = Convert.ToHexString(hash).ToLowerInvariant();
        var expected = expectedHex
            .Replace("sha256:", "", StringComparison.OrdinalIgnoreCase)
            .Trim()
            .ToLowerInvariant();
        if (!string.Equals(actual, expected, StringComparison.Ordinal))
            throw new InvalidOperationException($"SHA-256 mismatch (expected {expected}, got {actual})");
    }

    private static void Extract(string archivePath, string targetDir)
    {
        using var fileIn = File.OpenRead(archivePath);
        using var bz = new BZip2InputStream(fileIn);
        using var tar = TarArchive.CreateInputTarArchive(bz, System.Text.Encoding.UTF8);
        tar.ExtractContents(targetDir);
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch (IOException) { /* best-effort */ }
    }

    private static void TryDeleteDir(string path)
    {
        try { if (Directory.Exists(path)) Directory.Delete(path, recursive: true); } catch (IOException) { /* best-effort */ }
    }
}
