using Microsoft.EntityFrameworkCore;
using RadioPad.Infrastructure.Persistence;

namespace RadioPad.Api.Services;

/// <summary>
/// Boot-time recovery for durable AI jobs. A generation is DETACHED background
/// work with no HTTP request holding it open, so a process that dies mid-run
/// leaves <c>queued</c>/<c>running</c> rows that no live task will ever finish.
/// On startup this sweeps them to <c>error</c> / errorKind <c>server_restart</c>
/// once — it NEVER re-enqueues them.
///
/// <para>Not re-running is deliberate: a browser-driven generation costs real
/// provider time with nobody watching, and a generate re-run could write sections
/// onto a report the radiologist is now editing. The widget surfaces "Interrupted
/// by a restart — Retry" so a human decides.</para>
///
/// <para>Registered BEFORE <see cref="AiJobRunner"/> so its <c>StartAsync</c> (fully
/// awaited by the host) completes before the runner begins consuming queued work.
/// This does NOT guarantee the sweep beats every inbound HTTP request — Kestrel is
/// already accepting connections while hosted services start, so a submit landing
/// in that narrow boot window could have its brand-new <c>queued</c> row swept to
/// <c>server_restart</c> before the runner ever sees it. Self-healing (the row is
/// simply never picked up; nothing runs twice), just not the airtight guarantee an
/// earlier version of this comment claimed.</para>
/// </summary>
public sealed class AiJobRecoveryHostedService : IHostedService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<AiJobRecoveryHostedService> _log;

    public AiJobRecoveryHostedService(IServiceScopeFactory scopeFactory, ILogger<AiJobRecoveryHostedService> log)
    {
        _scopeFactory = scopeFactory;
        _log = log;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            var reattachCandidates = new List<Guid>();
            var appStopping = CancellationToken.None;

            using (var scope = _scopeFactory.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<RadioPadDbContext>();
                // GetService (not GetRequiredService): the unit-level recovery test builds a bare
                // ServiceProvider with no host, and only rows carrying a ProviderJobId ever need
                // ApplicationStopping — those never occur in that test.
                appStopping = scope.ServiceProvider.GetService<IHostApplicationLifetime>()?.ApplicationStopping
                    ?? CancellationToken.None;

                // Load + save (rather than ExecuteUpdate) so the SQLite DateTimeOffset⇒ticks
                // value converter is honoured for CompletedAt/UpdatedAt. Runs once at boot on
                // a table that only ever holds a handful of non-terminal rows.
                var orphans = await db.AiJobs
                    .Where(j => j.Status == "queued" || j.Status == "running")
                    .ToListAsync(cancellationToken);
                if (orphans.Count == 0) return;

                var now = DateTimeOffset.UtcNow;
                foreach (var job in orphans)
                {
                    // PR-B4 — a still-running row that captured a UBAG gateway job id may have a
                    // gateway job still running: LEAVE it untouched and re-attach + re-poll instead
                    // of sweeping. Every other non-terminal row (queued; running with no gateway
                    // job) is swept to server_restart exactly as before — never re-enqueued.
                    if (job.Status == "running" && !string.IsNullOrEmpty(job.ProviderJobId))
                    {
                        reattachCandidates.Add(job.Id);
                        continue;
                    }
                    job.Status = "error";
                    job.ErrorKind = "server_restart";
                    job.Error = "Interrupted by a server restart. Retry to run it again.";
                    job.CompletedAt = now;
                    job.UpdatedAt = now;
                }
                await db.SaveChangesAsync(cancellationToken);

                _log.LogInformation(
                    "AI job recovery: swept {Swept} interrupted job(s) as server_restart; re-attaching {Reattach} UBAG job(s)",
                    orphans.Count - reattachCandidates.Count, reattachCandidates.Count);
            }

            // Launch one DETACHED re-attach per candidate AFTER the sweep commits — startup is
            // never blocked (StartAsync still completes before AiJobRunner consumes the queue),
            // and the candidate rows stay "running" so FindActiveAsync single-flight keeps
            // duplicate submits off the same (report, kind, mode) while re-attach proceeds.
            foreach (var jobId in reattachCandidates)
            {
                var id = jobId;
                _ = Task.Run(() => ReattachViaScopeAsync(id, appStopping));
            }
        }
        catch (Exception ex)
        {
            // Recovery must never block startup — a failure here just means the
            // widget shows a stale running job until the safety window elapses.
            _log.LogError(ex, "AI job boot recovery sweep failed");
        }
    }

    private async Task ReattachViaScopeAsync(Guid jobId, CancellationToken appStopping)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var coordinator = scope.ServiceProvider.GetRequiredService<AiJobCoordinator>();
            await coordinator.ReattachUbagAsync(jobId, appStopping);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "AI job re-attach for {JobId} failed to start", jobId);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
