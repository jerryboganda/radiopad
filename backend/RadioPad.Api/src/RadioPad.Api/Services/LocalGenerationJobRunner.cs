using System.Collections.Concurrent;
using RadioPad.Api.Controllers;
using RadioPad.Application.Abstractions;
using RadioPad.Application.Services;
using RadioPad.Infrastructure.Providers.Local;

namespace RadioPad.Api.Services;

/// <summary>
/// Runs the desktop sidecar's on-device (MedGemma / llama.cpp) whole-report generation jobs
/// asynchronously, detached from the HTTP request that submitted them
/// (<see cref="LocalGenerationController"/>). This is what makes local generation non-blocking: the
/// submit endpoint returns a job id in milliseconds, this runner does the minutes-long work, and the
/// client polls for the result.
///
/// <para><b>Strictly serialised.</b> The backing llama-server is a single-request server, so a second
/// local job must queue behind the first, never interleave. A single-slot
/// <see cref="SemaphoreSlim"/> enforces that: submit two jobs back-to-back and the second sits with
/// <c>stage: "queued"</c> until the first releases the slot. (SemaphoreSlim is approximately FIFO,
/// which is sufficient here — the desktop is single-user and rarely has more than one or two local
/// jobs in flight.)</para>
///
/// <para><b>In-memory only, by doctrine.</b> Like <see cref="AiJobRegistry"/> on this path, nothing is
/// persisted — the sidecar's SQLite is throwaway and a sidecar restart kills the llama-server child
/// anyway. Jobs legitimately vanish on restart; the widget rehydrates from whatever the registry
/// still holds via <c>GET /api/local-generation/jobs</c>.</para>
/// </summary>
public sealed class LocalGenerationJobRunner
{
    public const string StageQueued = "queued";
    public const string StageModelLoading = "model-loading";
    public const string StageGenerating = "generating";

    // Longer than the hosted 10-min ceiling on purpose: a cold job pays a multi-GB model load (up to
    // ~3 min) BEFORE it can decode a token, then generates for CPU-bound minutes, and may sit queued
    // behind a predecessor for that whole span first.
    private static readonly TimeSpan SafetyTimeout = TimeSpan.FromMinutes(20);

    private readonly AiJobRegistry _registry;
    private readonly IReadOnlyList<IAiProviderAdapter> _adapters;
    private readonly IHostApplicationLifetime _lifetime;
    private readonly ILogger<LocalGenerationJobRunner> _log;
    private readonly IHttpClientFactory? _http;
    private readonly LlamaServerProcess? _server;

    // Single slot — see the class remarks. A second job's WaitAsync is what surfaces as "queued".
    private readonly SemaphoreSlim _gate = new(1, 1);

    // Live per-job stage, read by the controller's poll (kept off the immutable AiJobState record).
    private readonly ConcurrentDictionary<Guid, string> _stages = new();

    public LocalGenerationJobRunner(
        AiJobRegistry registry,
        IEnumerable<IAiProviderAdapter> adapters,
        IHostApplicationLifetime lifetime,
        ILogger<LocalGenerationJobRunner> log,
        IHttpClientFactory? http = null,
        LlamaServerProcess? server = null)
    {
        _registry = registry;
        _adapters = adapters.ToList();
        _lifetime = lifetime;
        _log = log;
        _http = http;
        _server = server;
    }

    /// <summary>Current live stage of a tracked job, or null once it has left the runner (terminal).</summary>
    public string? StageOf(Guid jobId) => _stages.TryGetValue(jobId, out var stage) ? stage : null;

    /// <summary>
    /// Launch a job. Deliberately synchronous up to the first real await: it records the "queued"
    /// stage and registers the cancellation source on the calling thread, so a poll fired immediately
    /// after the controller's 202 already observes the job. Everything after the semaphore wait runs
    /// detached on the thread pool. Fire-and-forget — the returned task always completes by writing a
    /// terminal outcome to the registry, and never faults (all paths are caught).
    /// </summary>
    public Task RunAsync(Guid jobId, LocalGenerationController.GenerateReportDto dto, CancellationToken outerCt = default)
    {
        _stages[jobId] = StageQueued;
        // Detached from the request: cancellation comes only from an explicit cancel (via the
        // registry flipping this CTS), app shutdown, or the safety ceiling — never a dropped fetch.
        var cts = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.ApplicationStopping, outerCt);
        cts.CancelAfter(SafetyTimeout);
        _registry.RegisterCancellation(jobId, cts);
        return ExecuteAsync(jobId, dto, cts);
    }

    private async Task ExecuteAsync(Guid jobId, LocalGenerationController.GenerateReportDto dto, CancellationTokenSource cts)
    {
        var ct = cts.Token;
        try
        {
            // Blocks here — and stays "queued" — while an earlier local job holds the single slot.
            // If a cancel is requested while queued, this throws before any provider work happens.
            await _gate.WaitAsync(ct);
            try
            {
                // A cancel may have raced in between acquiring the slot and starting; honour it so a
                // cancelled-while-queued job never invokes the model.
                if (_registry.WasCancelRequested(jobId))
                {
                    _registry.Cancel(jobId);
                    return;
                }

                var llama = _adapters.FirstOrDefault(a => a.Id == LlamaCppProvider.AdapterId);
                if (llama is null)
                {
                    _registry.Fail(jobId, "The on-device AI adapter is not registered.", "adapter_unavailable");
                    return;
                }

                using var stageTracking = BeginStageTracking(jobId, ct);

                // AI-013 — feed the llama-server token stream into the registry's progress side-map so
                // the poll (and the /events SSE) can expose live token counts + partial text for the
                // desktop preview. SynchronousProgress invokes inline in arrival order (never
                // System.Progress, which posts to the ThreadPool and can reorder chunks). Percent stays
                // null: n_predict=1024 is a ceiling MedGemma rarely reaches, so tokens/n_predict would
                // be a fake bar (design §3.10) — honest indeterminate progress instead.
                var onStream = new SynchronousProgress<AiStreamChunk>(chunk =>
                {
                    // The first streamed token is itself a strong "generating" signal — flip the stage
                    // even if the /health watcher missed the model-loading→generating transition.
                    // Advance-only (TryUpdate matches ONLY while still model-loading), so it is a no-op
                    // on every later chunk and never regresses a stage the watcher already moved.
                    _stages.TryUpdate(jobId, StageGenerating, StageModelLoading);
                    _registry.UpdateProgress(jobId, chunk.OutputTokens, percent: null, partialDelta: chunk.Delta);
                });

                var result = await llama.CompleteAsync(LocalGenerationController.BuildCompletionRequest(dto, onStream), ct);
                // Structured-section post-processing still runs at end-of-stream, unchanged: the
                // streamed partials are a live preview; BuildSections parses the complete model text.
                _registry.Complete(jobId, LocalGenerationController.BuildSections(result));
            }
            finally
            {
                _gate.Release();
            }
        }
        catch (ProviderTransportException ex)
        {
            _log.LogWarning(ex, "On-device generation job {JobId} failed to reach the local llama-server.", jobId);
            _registry.Fail(jobId, ex.Message, "provider_transport");
        }
        catch (OperationCanceledException)
        {
            // Distinguish a deliberate cancel from the safety timeout firing the same exception —
            // the widget renders "you cancelled this" vs "it timed out" differently.
            if (_registry.WasCancelRequested(jobId))
                _registry.Cancel(jobId);
            else
                _registry.Fail(jobId, "On-device generation timed out or the sidecar is shutting down.", "timeout");
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "On-device generation job {JobId} failed unexpectedly.", jobId);
            _registry.Fail(jobId, "Unexpected error during on-device generation.", "server_error");
        }
        finally
        {
            _stages.TryRemove(jobId, out _);
            cts.Dispose();
        }
    }

    /// <summary>
    /// Sets the job's stage for the generation span and returns a handle that stops any background
    /// tracking. When the llama-server is already warm the model is resident and generation starts
    /// immediately, so the stage is "generating". When cold, the first call blocks on a multi-GB
    /// model load, so the stage is "model-loading" until the server answers /health, then flips to
    /// "generating". Best-effort and self-correcting: if it can't observe the transition the stage
    /// simply stays "model-loading" until the job reaches a terminal status, which is harmless. With
    /// no server handle at all (e.g. unit tests) the load is unobservable, so it reports "generating".
    /// </summary>
    private IDisposable BeginStageTracking(Guid jobId, CancellationToken ct)
    {
        var cold = _server is not null && !_server.IsRunning && _http is not null;
        if (!cold)
        {
            _stages[jobId] = StageGenerating;
            return NullDisposable.Instance;
        }

        _stages[jobId] = StageModelLoading;
        var watcherCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _ = WatchUntilServingAsync(jobId, watcherCts.Token);
        return new CancelOnDispose(watcherCts);
    }

    private async Task WatchUntilServingAsync(Guid jobId, CancellationToken ct)
    {
        try
        {
            var client = _http!.CreateClient("ai-local");
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    using var resp = await client.GetAsync($"{LlamaServerProcess.BaseUrl}/health", ct);
                    if (resp.IsSuccessStatusCode)
                    {
                        // Advance forward only — never regress a stage the caller may have moved on.
                        _stages.TryUpdate(jobId, StageGenerating, StageModelLoading);
                        return;
                    }
                }
                catch (HttpRequestException) { /* not listening yet — still loading */ }
                await Task.Delay(TimeSpan.FromSeconds(1), ct);
            }
        }
        catch (OperationCanceledException) { /* job finished or shutting down */ }
        catch (Exception ex)
        {
            _log.LogDebug(ex, "Stage watcher for on-device job {JobId} stopped early.", jobId);
        }
    }

    private sealed class NullDisposable : IDisposable
    {
        public static readonly NullDisposable Instance = new();
        public void Dispose() { }
    }

    private sealed class CancelOnDispose(CancellationTokenSource cts) : IDisposable
    {
        public void Dispose()
        {
            try { cts.Cancel(); } catch (ObjectDisposedException) { }
            cts.Dispose();
        }
    }
}
