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
/// STT (Parakeet) is actionable today; TTS + an orchestrator brain are
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
    private readonly ILocalSttSettings _settings;
    private readonly ILogger<LocalModelsController> _log;
    private readonly IReadOnlyList<IAiProviderAdapter> _aiAdapters;
    private readonly LlamaServerProcess? _llama;
    private readonly IHttpClientFactory? _http;

    public LocalModelsController(
        ILocalModelCatalog catalog,
        IModelProvisioningStatus status,
        SttModelProvisioner provisioner,
        IEnumerable<ILocalSttEngine> engines,
        ILocalSttSettings settings,
        ILogger<LocalModelsController> log,
        IEnumerable<IAiProviderAdapter>? aiAdapters = null,
        LlamaServerProcess? llama = null,
        IHttpClientFactory? http = null)
    {
        _catalog = catalog;
        _status = status;
        _provisioner = provisioner;
        _engines = engines.ToList();
        _settings = settings;
        _log = log;
        // Orchestrator models (MedGemma) run through an IAiProviderAdapter, not an
        // ILocalSttEngine. Optional so the existing test fixtures that construct this
        // controller with the STT-only set keep compiling.
        _aiAdapters = aiAdapters?.ToList() ?? [];
        _llama = llama;
        _http = http;
    }

    /// <summary>
    /// The visible catalog with per-model status. Works everywhere: <c>enabled</c> is
    /// false on a web/server build (no local engine), driving the UI's "managed in
    /// the desktop app" notice.
    ///
    /// <para>Uses <see cref="ILocalModelCatalog.Visible"/>, not <c>All</c>: entries
    /// flagged <see cref="LocalModelDescriptor.Hidden"/> stay fully functional in the
    /// runtime but are withheld from every UI. The other endpoints deliberately keep
    /// resolving hidden ids via <c>ById</c>, so a workstation that already selected one
    /// keeps working rather than 404-ing on its own saved choice.</para>
    /// </summary>
    [HttpGet]
    public IActionResult List()
    {
        var enabled = LocalSttModels.IsEnabled();
        var models = _catalog.Visible.Select(d => Project(d, enabled)).ToList();
        return Ok(new { enabled, models });
    }

    /// <summary>
    /// Start a download (idempotent). Returns 202 immediately; poll progress.
    ///
    /// <para><paramref name="force"/> re-fetches a model that is already on disk: the
    /// installed files are removed first, because the provisioner short-circuits on a
    /// complete install and would otherwise report success without repairing anything.
    /// This is the recovery path for a corrupt or partial model — the one case where
    /// "it says Ready but it does not work" has no other fix from inside the app.</para>
    /// </summary>
    [HttpPost("{id}/download")]
    public IActionResult Download(string id, [FromQuery] bool force = false)
    {
        var desc = _catalog.ById(id);
        if (desc is null) return NotFound(new { error = "unknown model.", kind = "not_found" });
        if (desc.Placeholder)
            return StatusCode(StatusCodes.Status503ServiceUnavailable,
                new { error = "this model is not available yet.", kind = "coming_soon" });
        if (!LocalSttModels.IsEnabled())
            return StatusCode(StatusCodes.Status503ServiceUnavailable,
                new { error = "on-device models are only available in the RadioPad desktop app.", kind = "stt_unavailable" });

        // Platform speech engines (SAPI / WinRT language pack / Edge) have no hosted
        // artifact to fetch — handle them without the download pipeline.
        if (desc.Provisioning != ModelProvisioning.HostedFile)
            return ProvisionPlatformEngine(desc);

        var current = _status.Get(id);
        if (current is not null && IsInProgress(current.State))
            return Conflict(new { error = "a download is already in progress.", kind = "already_running" });

        if (IsDownloaded(desc))
        {
            if (!force)
            {
                _status.SetState(id, ProvisionState.Ready);
                return Ok(new { id, state = ProvisionState.Ready.ToString(), alreadyInstalled = true });
            }

            if (ClearInstalledFiles(desc) is { } blocked) return blocked;
        }

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

        // Platform speech engines have no downloaded files to remove.
        if (desc.Provisioning != ModelProvisioning.HostedFile)
            return Conflict(new
            {
                error = "this engine is built into Windows or the browser — there are no downloaded files to remove.",
                kind = "not_removable",
            });

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

        // Edge Web Speech recognizes in the WebView, not the sidecar — there is no
        // backend engine to exercise. The frontend runs the real microphone probe.
        if (desc.Provisioning == ModelProvisioning.BrowserWebSpeech)
            return Ok(new
            {
                ok = true,
                engine = desc.Engine,
                inApp = true,
                transcript = (string?)null,
                message = "Microsoft Edge speech is tested in the app window — use the in-app microphone test.",
            });

        // Orchestrator models run on a llama.cpp server, not an ILocalSttEngine. Asking
        // the STT list about them used to return "engine is not available (model not
        // downloaded...)" for a model sitting fully downloaded on disk — a wrong answer
        // that sent people looking for a download button they did not need.
        if (IsOrchestrator(desc))
            return await TestOrchestratorAsync(desc, ct);

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
                hotwordsFile = HasEnv("RADIOPAD_STT_HOTWORDS_FILE"),
            },
            tuning = new
            {
                threads = LocalSttModels.ResolveThreads(),
                provider = LocalSttModels.ResolveProvider(),
                decoding = LocalSttModels.ResolveDecodingMethod(),
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

    /// <summary>
    /// Make a downloaded STT model the primary dictation engine (persisted per
    /// workstation; honored by the ensemble on the next dictation).
    /// </summary>
    [HttpPost("{id}/primary")]
    public IActionResult SetPrimary(string id)
    {
        var desc = _catalog.ById(id);
        if (desc is null) return NotFound(new { error = "unknown model.", kind = "not_found" });
        if (!LocalSttModels.IsEnabled())
            return StatusCode(StatusCodes.Status503ServiceUnavailable,
                new { error = "on-device models are only available in the RadioPad desktop app.", kind = "stt_unavailable" });
        if (desc.Placeholder || desc.Kind != ModelKind.Stt)
            return BadRequest(new { error = "only a speech-to-text model can be the primary engine.", kind = "validation" });
        if (!IsDownloaded(desc))
            return Conflict(new { error = "download the model before making it primary.", kind = "not_downloaded" });

        _settings.SetPrimary(id);
        return Ok(new { id, isPrimary = true });
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
            // How the card behaves in the UI (download vs built-in vs settings vs
            // browser-probe) + an optional note (e.g. the online/PHI warning).
            provisioning = d.Provisioning.ToString(),
            note = d.Note,
            downloaded = IsDownloaded(d),
            available = enabled && IsUsable(d, engine),
            // Only STT models have a "primary dictation engine" concept. Orchestrator
            // models are selected per-report through the provider picker instead, so
            // the card must not offer a primary action the backend would reject.
            supportsPrimary = !d.Placeholder && d.Kind == ModelKind.Stt,
            isPrimary = !d.Placeholder && d.Kind == ModelKind.Stt && _settings.IsPrimary(d.Id),
            // Orchestrator models need a llama.cpp runtime as well as the GGUF. It is
            // fetched with the model but tracked separately, so surface it — otherwise a
            // half-installed chain looks identical to a working one on the card.
            runtime = ProjectRuntime(d),
            progress = ProjectProgress(d.Id, _status.Get(d.Id)),
        };
    }

    /// <summary>
    /// Exercise the offline report formatter end to end: model present → runtime present
    /// → server answering → a real completion comes back. Each failure names the link that
    /// broke, because "it didn't work" on a 2.5 GB feature with a four-step chain is not an
    /// answer anyone can act on.
    /// </summary>
    private async Task<IActionResult> TestOrchestratorAsync(LocalModelDescriptor desc, CancellationToken ct)
    {
        const string engineLabel = LlamaCppProvider.AdapterId;

        if (!LocalSttModels.IsEnabled())
            return StatusCode(StatusCodes.Status503ServiceUnavailable,
                new { error = "on-device models are only available in the RadioPad desktop app.", kind = "stt_unavailable" });

        if (!IsDownloaded(desc))
            return Ok(Failure("The model is not downloaded yet — download it first.", stage: "model"));

        var adapter = _aiAdapters.FirstOrDefault(a => string.Equals(a.Id, LlamaCppProvider.AdapterId, StringComparison.Ordinal));
        if (adapter is null)
            return Ok(Failure("The llama.cpp adapter is not registered in this build.", stage: "adapter"));

        // An operator-pointed server always wins; otherwise we own the process.
        var configured = Environment.GetEnvironmentVariable(LocalMedGemmaFormatter.UrlEnv);
        string endpoint;
        if (!string.IsNullOrWhiteSpace(configured))
        {
            endpoint = configured.Trim();
        }
        else
        {
            if (!LocalRuntimes.IsLlamaServerInstalled(LlamaRuntimeDir()))
                return Ok(Failure(
                    "The llama.cpp runtime that ships with this model is missing. Re-download the model to fetch it.",
                    stage: "runtime"));

            var modelDir = LocalSttModels.ResolveModelDir(desc.Id);
            var modelPath = modelDir is null ? null : Path.Combine(modelDir, desc.FileName ?? string.Empty);
            if (modelPath is null || !System.IO.File.Exists(modelPath))
                return Ok(Failure("Could not resolve the model file on disk.", stage: "model"));

            if (_llama is null)
                return Ok(Failure("No managed llama-server is available in this build.", stage: "runtime"));

            var started = await _llama.EnsureRunningAsync(modelPath, ct);
            if (started is null)
                return Ok(Failure("The llama-server did not start. See the sidecar log for the launch error.", stage: "server"));
            endpoint = started;

            // A cold start loads ~2.5 GB from disk; the first refused connection is normal.
            if (_http is not null
                && !await _llama.WaitUntilHealthyAsync(_http.CreateClient("ai"), TimeSpan.FromMinutes(2), ct))
                return Ok(Failure(
                    "The llama-server started but was still loading after 2 minutes. Try again shortly — the first "
                    + "start of a 2.5 GB model is the slow one.",
                    stage: "server"));
        }

        var provider = new RadioPad.Domain.Entities.ProviderConfig
        {
            Name = "on-device-selftest",
            Adapter = LlamaCppProvider.AdapterId,
            Model = desc.Id,
            EndpointUrl = endpoint,
            Compliance = RadioPad.Domain.Enums.ProviderComplianceClass.LocalOnly,
            Enabled = true,
        };

        // Deliberately not clinical text: this proves the pipeline runs, and a self-test
        // must never put PHI-shaped content through a path the operator is still verifying.
        var request = new AiCompletionRequest(
            Provider: provider,
            SystemPrompt: "You are a health check. Reply with exactly one short word.",
            UserPrompt: "Reply with the single word: ready",
            PromptVersion: "v1.selftest.orchestrator",
            ContainsPhi: false)
        {
            Temperature = 0.0,
        };

        var sw = Stopwatch.StartNew();
        try
        {
            var result = await adapter.CompleteAsync(request, ct);
            sw.Stop();
            var text = result.Text?.Trim() ?? string.Empty;
            return Ok(new
            {
                ok = text.Length > 0,
                engine = engineLabel,
                latencyMs = sw.ElapsedMilliseconds,
                transcript = text,
                sampleSource = "orchestrator_prompt",
                stage = text.Length > 0 ? "ok" : "completion",
                endpoint,
                error = text.Length > 0 ? null : "The server answered but returned no text.",
            });
        }
        catch (Exception ex)
        {
            sw.Stop();
            return Ok(new
            {
                ok = false,
                engine = engineLabel,
                latencyMs = sw.ElapsedMilliseconds,
                transcript = (string?)null,
                sampleSource = "orchestrator_prompt",
                stage = "completion",
                endpoint,
                error = $"{ex.GetType().Name}: {ex.Message}",
                detail = ex.ToString(),
            });
        }

        static object Failure(string error, string stage) => new
        {
            ok = false,
            engine = engineLabel,
            latencyMs = 0,
            transcript = (string?)null,
            sampleSource = "orchestrator_prompt",
            stage,
            error,
        };
    }

    /// <summary>
    /// Is this entry actually usable right now? STT entries answer through their
    /// <see cref="ILocalSttEngine"/>; orchestrator entries have no STT engine at all, so
    /// asking that list would report every one of them as broken forever.
    /// </summary>
    private bool IsUsable(LocalModelDescriptor d, ILocalSttEngine? engine)
    {
        if (d.Placeholder) return false;
        if (IsOrchestrator(d)) return IsDownloaded(d) && LocalRuntimes.IsLlamaServerInstalled(LlamaRuntimeDir());
        return engine?.Available ?? false;
    }

    private static bool IsOrchestrator(LocalModelDescriptor d) =>
        d.Kind == ModelKind.Orchestrator
        && string.Equals(d.Engine, LlamaCppProvider.AdapterId, StringComparison.Ordinal);

    private static string? LlamaRuntimeDir() =>
        LocalRuntimes.ResolveRuntimeDir(LocalRuntimes.LlamaServerId);

    /// <summary>
    /// Runtime state for orchestrator cards (null for everything else). <c>installed</c>
    /// is the on-disk llama-server binary; <c>running</c> is a server this process
    /// started and still owns.
    /// </summary>
    private object? ProjectRuntime(LocalModelDescriptor d)
    {
        if (!IsOrchestrator(d)) return null;
        return new
        {
            id = LocalRuntimes.LlamaServerId,
            installed = LocalRuntimes.IsLlamaServerInstalled(LlamaRuntimeDir()),
            running = _llama?.IsRunning ?? false,
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

    private bool IsDownloaded(LocalModelDescriptor d)
    {
        if (d.Placeholder) return false;
        switch (d.Provisioning)
        {
            // Built into Windows / provided by a Windows language pack — "present"
            // iff the engine reports itself available (recognizer/pack installed).
            case ModelProvisioning.WindowsBuiltIn:
            case ModelProvisioning.WindowsLanguagePack:
                return EngineAvailable(d);
            // Provided by the WebView (Edge) — no backend artifact. Treat as present;
            // the frontend capability probe decides whether it actually works.
            case ModelProvisioning.BrowserWebSpeech:
                return true;
            case ModelProvisioning.HostedFile:
            default:
                var dir = LocalSttModels.ResolveModelDir(d.Id);
                if (dir is null) return false;
                return d.ArchiveKind switch
                {
                    ModelArchiveKind.TarBz2 => LocalSttModels.IsComplete(dir),
                    // The MedASR CTC bundle is two raw files, so neither the Parakeet transducer
                    // check nor the single-FileName check answers "is it installed".
                    ModelArchiveKind.MedAsrCtc => LocalSttModels.IsMedAsrComplete(dir),
                    ModelArchiveKind.RawFile => d.FileName is not null && System.IO.File.Exists(Path.Combine(dir, d.FileName)),
                    _ => false,
                };
        }
    }

    /// <summary>
    /// Remove an installed model's files so a forced re-download actually re-fetches.
    /// Returns null on success, or the error result to send back. Mirrors
    /// <see cref="Delete"/>'s guards: an operator-mounted directory is never ours to clear,
    /// and a model held open by a running engine cannot be replaced until the app restarts.
    /// </summary>
    private IActionResult? ClearInstalledFiles(LocalModelDescriptor d)
    {
        if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("RADIOPAD_STT_MODEL_DIR")))
            return Conflict(new
            {
                error = "model directory is operator-managed (RADIOPAD_STT_MODEL_DIR); re-download is not available.",
                kind = "managed_dir",
            });

        var dir = LocalSttModels.ResolveModelDir(d.Id);
        if (dir is null)
            return StatusCode(StatusCodes.Status500InternalServerError,
                new { error = "could not resolve the model directory.", kind = "no_path" });

        try
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _log.LogWarning(ex, "could not clear model '{Model}' for re-download (in use)", d.Id);
            return Conflict(new
            {
                error = IsOrchestrator(d)
                    ? "the model is loaded by the running llama-server; restart the app, then re-download."
                    : "model is loaded; restart the app to free it, then re-download.",
                kind = "in_use",
                detail = ex.Message,
            });
        }
    }

    private bool EngineAvailable(LocalModelDescriptor d)
    {
        var engine = _engines.FirstOrDefault(e => string.Equals(e.EngineId, d.Engine, StringComparison.Ordinal));
        return engine?.Available ?? false;
    }

    /// <summary>
    /// Provision a platform speech engine (no hosted artifact): SAPI + Edge are
    /// instant no-ops (Ready); the WinRT language-pack opens Windows speech settings
    /// so the user can install/enable the on-device pack, then Test.
    /// </summary>
    private IActionResult ProvisionPlatformEngine(LocalModelDescriptor desc)
    {
        if (desc.Provisioning == ModelProvisioning.WindowsLanguagePack)
        {
            var opened = TryOpenWindowsSpeechSettings();
            _status.SetState(desc.Id, EngineAvailable(desc) ? ProvisionState.Ready : ProvisionState.NotStarted);
            return Ok(new
            {
                id = desc.Id,
                action = "open_settings",
                opened,
                settingsUri = "ms-settings:speech",
                message = "Install or enable a Windows speech language pack in Settings, then return and press Test.",
            });
        }

        // WindowsBuiltIn (SAPI) + BrowserWebSpeech (Edge): nothing to fetch.
        _status.SetState(desc.Id, ProvisionState.Ready);
        return Ok(new { id = desc.Id, state = ProvisionState.Ready.ToString(), alreadyInstalled = true });
    }

    private static bool TryOpenWindowsSpeechSettings()
    {
        if (!OperatingSystem.IsWindows()) return false;
        try
        {
            using var _ = Process.Start(new ProcessStartInfo("ms-settings:speech") { UseShellExecute = true });
            return true;
        }
        catch { return false; }
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
