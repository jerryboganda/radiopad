using System.Collections.Concurrent;
using System.Threading.Channels;

namespace RadioPad.Api.Services;

/// <summary>One unit of durable AI-job work handed from the coordinator's submit path to the runner.</summary>
public sealed record AiJobWork(
    Guid JobId,
    Guid TenantId,
    Guid UserId,
    Guid ReportId,
    string Kind,
    string Mode,
    Guid? ProviderId);

/// <summary>
/// Hosted consumer of the durable AI-job queue. Replaces the submit endpoints' old
/// fire-and-forget <c>_ = Task.Run(...)</c>: work is enqueued onto an unbounded
/// <see cref="Channel{T}"/> by <see cref="AiJobCoordinator.SubmitAsync"/> (so
/// "queued" is a truthful state), dequeued here, and executed with bounded
/// parallelism (<c>AiJobs:MaxConcurrency</c>, default 4).
///
/// <para>Fairness (PR-B2): parallelism is bounded by TWO gates in series — a
/// per-tenant cap (<c>AiJobs:PerTenantMaxConcurrency</c>, default 2, clamped
/// <c>[1, MaxConcurrency]</c>) acquired FIRST, then the global gate. A single
/// flooding tenant can therefore occupy at most <c>PerTenantMaxConcurrency</c>
/// global slots, leaving <c>MaxConcurrency − PerTenantMaxConcurrency</c> slots
/// for everyone else — starvation-free by construction. A per-tenant value
/// &gt;= <c>MaxConcurrency</c> degenerates to the old global-only behaviour, which
/// is allowed (and sensible) for single-tenant deployments.</para>
///
/// <para>Each job runs under a per-job CTS linked to
/// <see cref="IHostApplicationLifetime.ApplicationStopping"/> plus a safety timeout
/// (<c>AiJobs:SafetyTimeoutSeconds</c>, default 600); the coordinator registers that
/// same source with the <see cref="AiJobRegistry"/> so a user cancel can flip it.
/// On graceful shutdown the dequeue loop stops and in-flight jobs unwind through
/// their own CTS into the coordinator's shutdown/timeout errorKind path — nothing is
/// re-enqueued, so a job is never silently re-run (the boot recovery sweep marks any
/// still-queued or interrupted row <c>server_restart</c> instead).</para>
/// </summary>
public class AiJobRunner : BackgroundService
{
    private readonly Channel<AiJobWork> _channel;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IHostApplicationLifetime _lifetime;
    private readonly ILogger<AiJobRunner> _log;
    private readonly SemaphoreSlim _gate;
    private readonly TimeSpan _safetyTimeout;

    // Per-tenant concurrency caps, created lazily on first sight of a tenant. Never
    // disposed: the map is bounded by the number of distinct tenants and an idle
    // SemaphoreSlim with no waiters is ~40 bytes, so leaving them resident is far
    // cheaper (and race-free) than reference-counting and disposing them.
    private readonly ConcurrentDictionary<Guid, SemaphoreSlim> _tenantGates = new();
    private readonly int _perTenantMax;

    public AiJobRunner(
        Channel<AiJobWork> channel,
        IServiceScopeFactory scopeFactory,
        IHostApplicationLifetime lifetime,
        IConfiguration config,
        ILogger<AiJobRunner> log)
    {
        _channel = channel;
        _scopeFactory = scopeFactory;
        _lifetime = lifetime;
        _log = log;

        var max = config.GetValue<int?>("AiJobs:MaxConcurrency") ?? 4;
        if (max < 1) max = 1;
        _gate = new SemaphoreSlim(max, max);

        // Per-tenant cap: default 2, floor 1, ceiling MaxConcurrency. Clamped to the
        // global max so a mis-set value can never claim more slots than exist; a value
        // at (or above) MaxConcurrency degenerates to today's global-only behaviour.
        var perTenant = config.GetValue<int?>("AiJobs:PerTenantMaxConcurrency") ?? 2;
        if (perTenant < 1) perTenant = 1;
        if (perTenant > max) perTenant = max;
        _perTenantMax = perTenant;

        var seconds = config.GetValue<int?>("AiJobs:SafetyTimeoutSeconds") ?? 600;
        if (seconds < 1) seconds = 600;
        _safetyTimeout = TimeSpan.FromSeconds(seconds);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await foreach (var work in _channel.Reader.ReadAllAsync(stoppingToken))
            {
                // Spawn dispatch immediately — do NOT wait on the gates from the dequeue
                // thread. Blocking here would let one saturated tenant head-of-line-block
                // the dispatch of every OTHER tenant's jobs that sit behind it in the
                // channel, which is exactly the unfairness PR-B2 removes. Each job waits
                // on its gates inside DispatchAsync instead.
                _ = Task.Run(() => DispatchAsync(work, stoppingToken), CancellationToken.None);
            }
        }
        catch (OperationCanceledException)
        {
            // Graceful shutdown: stop dequeuing. In-flight jobs are cancelled via
            // their ApplicationStopping-linked CTS and marked terminal by the
            // coordinator; anything still queued is swept as server_restart next boot.
        }
    }

    private async Task DispatchAsync(AiJobWork work, CancellationToken stoppingToken)
    {
        var tenantGate = _tenantGates.GetOrAdd(work.TenantId, _ => new SemaphoreSlim(_perTenantMax, _perTenantMax));

        // Compose graceful shutdown + a user/coordinator cancel now, BEFORE the gate
        // waits, so a shutdown cancels a job still queued behind a gate (the linked
        // ApplicationStopping token flows into the safety-timeout CTS below). The
        // safety timeout itself is armed only AFTER both gates are acquired — see below.
        var jobCts = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.ApplicationStopping);
        var tenantAcquired = false;
        var globalAcquired = false;
        try
        {
            // Acquisition order is TENANT gate first, then GLOBAL gate — this is
            // LOAD-BEARING. If a job took the global gate first and then waited on its
            // tenant cap, a flooding tenant's excess jobs would sit holding global slots
            // while blocked on their own cap, re-introducing the cross-tenant starvation
            // this PR exists to remove. A job never holds two tenant gates, so the fixed
            // tenant->global order can never deadlock.
            await tenantGate.WaitAsync(stoppingToken);
            tenantAcquired = true;
            await _gate.WaitAsync(stoppingToken);
            globalAcquired = true;

            // Both gates held → the job now occupies a global execution slot. Start the
            // safety-timeout budget only NOW: queue-wait must not consume it. (Armed at
            // CTS creation, a job that waited nine minutes behind its tenant cap would
            // get only one minute of runtime.)
            jobCts.CancelAfter(_safetyTimeout);

            await RunJobAsync(work, jobCts);
        }
        catch (OperationCanceledException)
        {
            // Graceful shutdown cancelled a gate wait before this job ever started: the
            // durable row is still "queued", so the boot recovery sweep re-marks it
            // server_restart on the next start. Nothing to do here.
        }
        catch (Exception ex)
        {
            // RunAsync catches its own failures and marks the row terminal; this only
            // fires on an unexpected fault in the dispatch plumbing itself.
            _log.LogError(ex, "AI job {JobId} dispatch task faulted outside the coordinator", work.JobId);
        }
        finally
        {
            // Release in reverse acquisition order (global then tenant) and ONLY what was
            // actually acquired — an acquired-flag guard so a cancellation landing between
            // the two waits can never over-release a semaphore.
            if (globalAcquired) _gate.Release();
            if (tenantAcquired) tenantGate.Release();
            jobCts.Dispose();
        }
    }

    /// <summary>
    /// Runs one dequeued job through a fresh DI scope and the scoped
    /// <see cref="AiJobCoordinator"/>. Isolated as a protected virtual seam purely so the
    /// fairness unit tests can observe gate concurrency without standing up the whole
    /// provider stack; production behaviour is identical to the inline body it replaced.
    /// </summary>
    protected virtual async Task RunJobAsync(AiJobWork work, CancellationTokenSource jobCts)
    {
        using var scope = _scopeFactory.CreateScope();
        var coordinator = scope.ServiceProvider.GetRequiredService<AiJobCoordinator>();
        await coordinator.RunAsync(work, jobCts);
    }
}
