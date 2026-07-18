using System.Collections.Concurrent;
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
    private readonly IModelProvisioningStatus _status;

    // One gate per model id so the startup auto-download and a manual /download
    // call can't race on the same temp/extract dirs.
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new(StringComparer.Ordinal);

    public SttModelProvisioner(ILogger<SttModelProvisioner> log, IModelProvisioningStatus? status = null)
    {
        _log = log;
        _status = status ?? NullModelProvisioningStatus.Instance;
    }

    private SemaphoreSlim LockFor(string id) => _locks.GetOrAdd(id, _ => new SemaphoreSlim(1, 1));

    /// <summary>Ensure the default (Parakeet) model is installed.</summary>
    public Task<bool> EnsureAsync(CancellationToken ct) => EnsureAsync(LocalSttModels.Parakeet, ct);

    /// <summary>
    /// Ensure the MedASR CTC bundle (the default primary STT, D2) is installed — its two raw files
    /// (model.int8.onnx + tokens.txt) into the MedASR model dir. Idempotent; a fast no-op once both
    /// are present. The repo is public/ungated, so this needs no credentials.
    /// </summary>
    public async Task<bool> EnsureMedAsrAsync(CancellationToken ct)
    {
        var dir = LocalSttModels.ResolveModelDir(LocalSttModels.MedAsrModelName);
        if (dir is null)
        {
            _log.LogWarning("No local-app-data dir resolvable; cannot provision MedASR.");
            return false;
        }
        if (LocalSttModels.IsMedAsrComplete(dir))
        {
            _status.SetState(LocalSttModels.MedAsrModelName, ProvisionState.Ready);
            return true;
        }
        var okModel = await EnsureFileInDirAsync(LocalSttModels.MedAsrModel, dir, ct);
        var okTokens = await EnsureFileInDirAsync(LocalSttModels.MedAsrTokens, dir, ct);
        return okModel && okTokens && LocalSttModels.IsMedAsrComplete(dir);
    }

    /// <summary>
    /// Ensure the model described by <paramref name="desc"/> is installed,
    /// dispatching by archive kind. Used by the manual download endpoint so any
    /// catalog model (STT today; future kinds) provisions through one entry point.
    /// </summary>
    public Task<bool> EnsureByIdAsync(LocalModelDescriptor desc, CancellationToken ct)
    {
        if (desc.Placeholder)
            return Task.FromResult(false);
        return desc.ArchiveKind switch
        {
            ModelArchiveKind.TarBz2 => EnsureAsync(
                new LocalSttModels.ModelSpec(desc.Id, desc.DownloadUrl, desc.SizeBytes, desc.Sha256), ct),
            ModelArchiveKind.RawFile => EnsureFileAsync(
                new LocalSttModels.FileSpec(
                    desc.Id,
                    desc.FileName ?? throw new InvalidOperationException("raw-file model requires a file name"),
                    desc.DownloadUrl, desc.SizeBytes, desc.Sha256), ct),
            ModelArchiveKind.MedAsrCtc => EnsureMedAsrAsync(ct),
            _ => Task.FromResult(false),
        };
    }

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
        {
            _status.SetState(spec.Name, ProvisionState.Ready);
            return true; // already installed — fast path on every subsequent boot
        }

        var gate = LockFor(spec.Name);
        await gate.WaitAsync(ct);
        try
        {
            if (LocalSttModels.IsComplete(dir))
            {
                _status.SetState(spec.Name, ProvisionState.Ready);
                return true; // another caller finished while we waited
            }

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

                _status.SetState(spec.Name, ProvisionState.Downloading);
                await DownloadAsync(spec.Name, spec.Url, tmpArchive, ct);

                _status.SetState(spec.Name, ProvisionState.Verifying);
                await VerifySha256Async(tmpArchive, spec.Sha256, ct);

                _status.SetState(spec.Name, ProvisionState.Extracting);
                Directory.CreateDirectory(tmpExtract);
                Extract(tmpArchive, tmpExtract);

                if (!LocalSttModels.IsComplete(tmpExtract))
                    throw new InvalidOperationException(
                        "extracted STT bundle is missing encoder/decoder/joiner/tokens");

                _status.SetState(spec.Name, ProvisionState.Installing);
                // Atomic publish: move the verified extract into final place.
                if (Directory.Exists(dir))
                    Directory.Delete(dir, recursive: true);
                Directory.Move(tmpExtract, dir);

                _status.SetState(spec.Name, ProvisionState.Ready);
                _log.LogInformation("On-device STT model '{Model}' installed at {Dir}", spec.Name, dir);
                return true;
            }
            catch (OperationCanceledException)
            {
                _status.SetState(spec.Name, ProvisionState.NotStarted);
                throw;
            }
            catch (Exception ex)
            {
                _status.SetState(spec.Name, ProvisionState.Failed, ex.Message);
                _log.LogError(ex, "Failed to provision on-device STT model '{Model}'", spec.Name);
                return false;
            }
            finally
            {
                TryDelete(tmpArchive);
                TryDeleteDir(tmpExtract);
            }
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>Ensure a single model FILE (a raw .bin/.onnx download — no archive).</summary>
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
        if (File.Exists(dest))
        {
            _status.SetState(spec.Name, ProvisionState.Ready);
            return true;
        }

        var gate = LockFor(spec.Name);
        await gate.WaitAsync(ct);
        try
        {
            if (File.Exists(dest))
            {
                _status.SetState(spec.Name, ProvisionState.Ready);
                return true; // another caller finished while we waited
            }

            Directory.CreateDirectory(dir);
            var tmp = dest + $".download-{Guid.NewGuid():N}";
            try
            {
                _log.LogInformation(
                    "Downloading on-device STT model file '{File}' (~{SizeMB} MB).",
                    spec.FileName, spec.SizeBytes / (1024 * 1024));
                _status.SetState(spec.Name, ProvisionState.Downloading);
                await DownloadAsync(spec.Name, spec.Url, tmp, ct);
                _status.SetState(spec.Name, ProvisionState.Verifying);
                // Tiny non-LFS config files (e.g. tokens.txt) carry no pinned digest — an empty
                // Sha256 skips content verification. Real model weights always pin a digest.
                if (!string.IsNullOrWhiteSpace(spec.Sha256))
                    await VerifySha256Async(tmp, spec.Sha256, ct);
                _status.SetState(spec.Name, ProvisionState.Installing);
                if (File.Exists(dest)) File.Delete(dest);
                File.Move(tmp, dest);
                _status.SetState(spec.Name, ProvisionState.Ready);
                _log.LogInformation("On-device STT model file '{File}' installed at {Dir}", spec.FileName, dir);
                return true;
            }
            catch (OperationCanceledException)
            {
                _status.SetState(spec.Name, ProvisionState.NotStarted);
                throw;
            }
            catch (Exception ex)
            {
                _status.SetState(spec.Name, ProvisionState.Failed, ex.Message);
                _log.LogError(ex, "Failed to provision on-device STT model file '{File}'", spec.FileName);
                return false;
            }
            finally
            {
                TryDelete(tmp);
            }
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task DownloadAsync(string modelId, string url, string destPath, CancellationToken ct)
    {
        using var http = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        using var resp = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        resp.EnsureSuccessStatusCode();

        var total = resp.Content.Headers.ContentLength ?? 0L;
        if (total > 0) _status.SetTotal(modelId, total);

        await using var src = await resp.Content.ReadAsStreamAsync(ct);
        await using var dst = File.Create(destPath);

        var buffer = new byte[1 << 20];
        long done = 0;
        long lastReported = 0;
        const long reportEvery = 4L << 20; // throttle progress writes to ~4 MiB
        int read;
        while ((read = await src.ReadAsync(buffer, ct)) > 0)
        {
            await dst.WriteAsync(buffer.AsMemory(0, read), ct);
            done += read;
            if (done - lastReported >= reportEvery)
            {
                _status.ReportBytes(modelId, done);
                lastReported = done;
            }
        }
        _status.ReportBytes(modelId, done);
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
