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
/// <para>Each job runs under a per-job CTS linked to
/// <see cref="IHostApplicationLifetime.ApplicationStopping"/> plus a safety timeout
/// (<c>AiJobs:SafetyTimeoutSeconds</c>, default 600); the coordinator registers that
/// same source with the <see cref="AiJobRegistry"/> so a user cancel can flip it.
/// On graceful shutdown the dequeue loop stops and in-flight jobs unwind through
/// their own CTS into the coordinator's shutdown/timeout errorKind path — nothing is
/// re-enqueued, so a job is never silently re-run (the boot recovery sweep marks any
/// still-queued or interrupted row <c>server_restart</c> instead).</para>
/// </summary>
public sealed class AiJobRunner : BackgroundService
{
    private readonly Channel<AiJobWork> _channel;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IHostApplicationLifetime _lifetime;
    private readonly ILogger<AiJobRunner> _log;
    private readonly SemaphoreSlim _gate;
    private readonly TimeSpan _safetyTimeout;

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
                // Throttle: hold the just-dequeued item until a slot frees, so at
                // most MaxConcurrency jobs run at once.
                await _gate.WaitAsync(stoppingToken);

                // Per-job CTS composes graceful shutdown + safety timeout + a
                // user/coordinator cancel (the coordinator registers this same source
                // with the registry so TryRequestCancel flips it).
                var jobCts = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.ApplicationStopping);
                jobCts.CancelAfter(_safetyTimeout);

                _ = Task.Run(async () =>
                {
                    try
                    {
                        using var scope = _scopeFactory.CreateScope();
                        var coordinator = scope.ServiceProvider.GetRequiredService<AiJobCoordinator>();
                        await coordinator.RunAsync(work, jobCts);
                    }
                    catch (Exception ex)
                    {
                        // RunAsync catches its own failures and marks the row; this only
                        // fires on an unexpected fault in the plumbing itself.
                        _log.LogError(ex, "AI job {JobId} runner task faulted outside the coordinator", work.JobId);
                    }
                    finally
                    {
                        jobCts.Dispose();
                        _gate.Release();
                    }
                }, CancellationToken.None);
            }
        }
        catch (OperationCanceledException)
        {
            // Graceful shutdown: stop dequeuing. In-flight jobs are cancelled via
            // their ApplicationStopping-linked CTS and marked terminal by the
            // coordinator; anything still queued is swept as server_restart next boot.
        }
    }
}
