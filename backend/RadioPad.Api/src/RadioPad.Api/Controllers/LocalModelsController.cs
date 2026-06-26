using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RadioPad.Application.Abstractions;
using RadioPad.Infrastructure.Providers.Local;

namespace RadioPad.Api.Controllers;

/// <summary>
/// Manage the on-device AI models — download, delete, test, and diagnose them.
/// STT (Parakeet + Whisper) is actionable today; TTS + an orchestrator brain are
/// roadmap placeholders surfaced as "coming soon".
///
/// Mirrors <see cref="SttController"/>'s safety model: not report-scoped, so there
/// is no tenant/report context to resolve and it is safe to serve anonymously —
/// the desktop sidecar binds 127.0.0.1 only. Every download/delete/test/diagnostic
/// action is gated on <see cref="LocalSttModels.IsEnabled"/>, so on a hosted build
/// it is inert: the catalog listing returns public, non-secret info and the rest
/// 503 or return minimal data. The path is whitelisted in RadioPadBearerMiddleware
/// so a hosted build returns those inert results instead of a 401.
/// </summary>
[ApiController]
[Route("api/local-models")]
[AllowAnonymous]
public sealed class LocalModelsController : ControllerBase
{
    private readonly ILocalModelCatalog _catalog;
    private readonly IModelProvisioningStatus _status;
    private readonly SttModelProvisioner _provisioner;
    private readonly IReadOnlyList<ILocalSttEngine> _engines;
    private readonly ILogger<LocalModelsController> _log;

    public LocalModelsController(
        ILocalModelCatalog catalog,
        IModelProvisioningStatus status,
        SttModelProvisioner provisioner,
        IEnumerable<ILocalSttEngine> engines,
        ILogger<LocalModelsController> log)
    {
        _catalog = catalog;
        _status = status;
        _provisioner = provisioner;
        _engines = engines.ToList();
        _log = log;
    }

    /// <summary>
    /// The full catalog with per-model status. Works everywhere: <c>enabled</c> is
    /// false on a web/server build (no local engine), driving the UI's "managed in
    /// the desktop app" notice.
    /// </summary>
    [HttpGet]
    public IActionResult List()
    {
        var enabled = LocalSttModels.IsEnabled();
        var models = _catalog.All.Select(d => Project(d, enabled)).ToList();
        return Ok(new { enabled, models });
    }

    /// <summary>Start a download (idempotent). Returns 202 immediately; poll progress.</summary>
    [HttpPost("{id}/download")]
    public IActionResult Download(string id)
    {
        var desc = _catalog.ById(id);
        if (desc is null) return NotFound(new { error = "unknown model.", kind = "not_found" });
        if (desc.Placeholder)
            return StatusCode(StatusCodes.Status503ServiceUnavailable,
                new { error = "this model is not available yet.", kind = "coming_soon" });
        if (!LocalSttModels.IsEnabled())
            return StatusCode(StatusCodes.Status503ServiceUnavailable,
                new { error = "on-device models are only available in the RadioPad desktop app.", kind = "stt_unavailable" });

        if (IsDownloaded(desc))
        {
            _status.SetState(id, ProvisionState.Ready);
            return Ok(new { id, state = ProvisionState.Ready.ToString(), alreadyInstalled = true });
        }

        var current = _status.Get(id);
        if (current is not null && IsInProgress(current.State))
            return Conflict(new { error = "a download is already in progress.", kind = "already_running" });

        _status.SetState(id, ProvisionState.Downloading);
        // Detached: respond 202 at once; the multi-minute download must outlive the
        // HTTP request, so use a standalone token rather than HttpContext.RequestAborted.
        _ = Task.Run(async () =>
        {
            try { await _provisioner.EnsureByIdAsync(desc, CancellationToken.None); }
            catch (Exception ex) { _log.LogWarning(ex, "manual download of '{Model}' failed", id); }
        });

        return StatusCode(StatusCodes.Status202Accepted,
            new { id, state = ProvisionState.Downloading.ToString(), startedUtc = DateTimeOffset.UtcNow });
    }

    /// <summary>Poll download/install progress for a model.</summary>
    [HttpGet("{id}/progress")]
    public IActionResult Progress(string id)
    {
        if (_catalog.ById(id) is null) return NotFound(new { error = "unknown model.", kind = "not_found" });
        return Ok(ProjectProgress(id, _status.Get(id)));
    }

    /// <summary>Delete a downloaded model's files.</summary>
    [HttpDelete("{id}")]
    public IActionResult Delete(string id)
    {
        var desc = _catalog.ById(id);
        if (desc is null) return NotFound(new { error = "unknown model.", kind = "not_found" });
        if (!LocalSttModels.IsEnabled())
            return StatusCode(StatusCodes.Status503ServiceUnavailable,
                new { error = "on-device models are only available in the RadioPad desktop app.", kind = "stt_unavailable" });

        // RADIOPAD_STT_MODEL_DIR points at a single operator-mounted dir (not
        // models/<id>), so it isn't ours to remove — refuse rather than nuke a shared mount.
        if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("RADIOPAD_STT_MODEL_DIR")))
            return Conflict(new { error = "model directory is operator-managed (RADIOPAD_STT_MODEL_DIR); remove it manually.", kind = "managed_dir" });

        var dir = LocalSttModels.ResolveModelDir(id);
        if (dir is null)
            return StatusCode(StatusCodes.Status500InternalServerError,
                new { error = "could not resolve the model directory.", kind = "no_path" });

        try
        {
            if (Directory.Exists(dir))
                Directory.Delete(dir, recursive: true);
            _status.SetState(id, ProvisionState.NotStarted);
            return Ok(new { id, deleted = true });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Singleton engines keep native handles open on Windows; a loaded model
            // can't be deleted until the app restarts.
            _log.LogWarning(ex, "could not delete model '{Model}' (in use)", id);
            return Conflict(new
            {
                error = "model is loaded; restart the app to free it, then delete.",
                kind = "in_use",
                detail = ex.Message,
            });
        }
    }

    /// <summary>
    /// Run a self-test: transcribe a known sample through the engine and report
    /// transcript + latency, or full error detail (for IT hand-off) on failure.
    /// </summary>
    [HttpPost("{id}/test")]
    public async Task<IActionResult> Test(string id, CancellationToken ct)
    {
        var desc = _catalog.ById(id);
        if (desc is null) return NotFound(new { error = "unknown model.", kind = "not_found" });
        if (desc.Placeholder)
            return StatusCode(StatusCodes.Status503ServiceUnavailable,
                new { error = "this model is not available yet.", kind = "coming_soon" });

        var engine = _engines.FirstOrDefault(e => string.Equals(e.EngineId, desc.Engine, StringComparison.Ordinal));
        if (engine is null || !engine.Available)
            return StatusCode(StatusCodes.Status503ServiceUnavailable,
                new { error = "engine is not available (model not downloaded, or running on a web build).", kind = "stt_unavailable" });

        var sample = SelfTestAudio.Resolve(LocalSttModels.ResolveModelDir(id));
        var sw = Stopwatch.StartNew();
        try
        {
            var transcript = (await engine.RecognizeAsync(sample.Wav, ct)).Text?.Trim() ?? string.Empty;
            sw.Stop();
            // A real speech sample must produce text; a synthesized tone only needs
            // to exercise the pipeline without throwing.
            var ok = sample.Source != "model_sample" || transcript.Length > 0;
            return Ok(new
            {
                ok,
                engine = engine.EngineId,
                latencyMs = sw.ElapsedMilliseconds,
                transcript,
                sampleSource = sample.Source,
            });
        }
        catch (Exception ex)
        {
            sw.Stop();
            return Ok(new
            {
                ok = false,
                engine = engine.EngineId,
                latencyMs = sw.ElapsedMilliseconds,
                transcript = (string?)null,
                sampleSource = sample.Source,
                error = $"{ex.GetType().Name}: {ex.Message}",
                detail = ex.ToString(),
            });
        }
    }

    /// <summary>
    /// Machine-readable diagnostics for IT hand-off: resolved paths + per-file
    /// presence, engine availability + last error, non-secret env presence flags,
    /// resolved tuning, and host info. Minimal (<c>{enabled:false}</c>) off-desktop
    /// so no server paths leak.
    /// </summary>
    [HttpGet("{id}/diagnostics")]
    public IActionResult Diagnostics(string id)
    {
        var desc = _catalog.ById(id);
        if (desc is null) return NotFound(new { error = "unknown model.", kind = "not_found" });
        if (!LocalSttModels.IsEnabled())
            return Ok(new { enabled = false });

        var dir = LocalSttModels.ResolveModelDir(id);
        var engine = _engines.FirstOrDefault(e => string.Equals(e.EngineId, desc.Engine, StringComparison.Ordinal));

        object files;
        if (desc.ArchiveKind == ModelArchiveKind.TarBz2 && dir is not null)
        {
            var (enc, dec, join, tok) = LocalSttModels.ResolveFiles(dir);
            files = new { encoder = PathInfo(enc), decoder = PathInfo(dec), joiner = PathInfo(join), tokens = PathInfo(tok) };
        }
        else
        {
            var bin = dir is not null && desc.FileName is not null ? Path.Combine(dir, desc.FileName) : null;
            files = new { bin = PathInfo(bin) };
        }

        return Ok(new
        {
            enabled = true,
            model = new { desc.Id, kind = desc.Kind.ToString(), engine = desc.Engine, desc.DisplayName, desc.License },
            paths = new { modelDir = dir, files },
            engineState = new { available = engine?.Available ?? false, lastError = engine?.LastError },
            env = new
            {
                localSttEnabled = HasEnv("RADIOPAD_LOCAL_STT_ENABLED"),
                ensemble = HasEnv("RADIOPAD_STT_ENSEMBLE"),
                modelDirOverride = HasEnv("RADIOPAD_STT_MODEL_DIR"),
                model = HasEnv("RADIOPAD_STT_MODEL"),
                decoding = HasEnv("RADIOPAD_STT_DECODING"),
                threads = HasEnv("RADIOPAD_STT_THREADS"),
                whisperBeam = HasEnv("RADIOPAD_STT_WHISPER_BEAM"),
                hotwordsFile = HasEnv("RADIOPAD_STT_HOTWORDS_FILE"),
            },
            tuning = new
            {
                threads = LocalSttModels.ResolveThreads(),
                provider = LocalSttModels.ResolveProvider(),
                decoding = LocalSttModels.ResolveDecodingMethod(),
                whisperBeam = LocalSttModels.ResolveWhisperBeamSize(),
            },
            system = new
            {
                os = RuntimeInformation.OSDescription,
                arch = RuntimeInformation.OSArchitecture.ToString(),
                processArch = RuntimeInformation.ProcessArchitecture.ToString(),
                processors = Environment.ProcessorCount,
                totalMemoryBytes = SafeTotalMemory(),
                appVersion = typeof(LocalModelsController).Assembly.GetName().Version?.ToString(),
            },
            progress = ProjectProgress(id, _status.Get(id)),
        });
    }

    // ── helpers ─────────────────────────────────────────────────────────────

    private object Project(LocalModelDescriptor d, bool enabled)
    {
        var engine = _engines.FirstOrDefault(e => string.Equals(e.EngineId, d.Engine, StringComparison.Ordinal));
        return new
        {
            id = d.Id,
            displayName = d.DisplayName,
            kind = d.Kind.ToString(),
            engine = d.Engine,
            sizeBytes = d.SizeBytes,
            license = d.License,
            placeholder = d.Placeholder,
            downloaded = IsDownloaded(d),
            available = enabled && (engine?.Available ?? false),
            progress = ProjectProgress(d.Id, _status.Get(d.Id)),
        };
    }

    private static object ProjectProgress(string id, ModelProvisionSnapshot? s) => new
    {
        id,
        state = (s?.State ?? ProvisionState.NotStarted).ToString(),
        bytesDownloaded = s?.BytesDownloaded ?? 0,
        totalBytes = s?.TotalBytes ?? 0,
        error = s?.Error,
    };

    private static bool IsDownloaded(LocalModelDescriptor d)
    {
        if (d.Placeholder) return false;
        var dir = LocalSttModels.ResolveModelDir(d.Id);
        if (dir is null) return false;
        return d.ArchiveKind switch
        {
            ModelArchiveKind.TarBz2 => LocalSttModels.IsComplete(dir),
            ModelArchiveKind.RawFile => d.FileName is not null && System.IO.File.Exists(Path.Combine(dir, d.FileName)),
            _ => false,
        };
    }

    private static bool IsInProgress(ProvisionState s) =>
        s is ProvisionState.Downloading or ProvisionState.Verifying
            or ProvisionState.Extracting or ProvisionState.Installing;

    private static object? PathInfo(string? path) =>
        path is null ? null : new { path, exists = System.IO.File.Exists(path) };

    private static bool HasEnv(string name) =>
        !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(name));

    private static long SafeTotalMemory()
    {
        try { return GC.GetGCMemoryInfo().TotalAvailableMemoryBytes; }
        catch { return 0; }
    }
}
