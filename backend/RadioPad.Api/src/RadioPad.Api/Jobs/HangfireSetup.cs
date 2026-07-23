using Hangfire;
using Hangfire.InMemory;
using Hangfire.PostgreSql;

namespace RadioPad.Api.Jobs;

/// <summary>
/// PR-N1 — Hangfire bootstrap for RadioPad's cron platform. Owns storage
/// selection (mirroring the EF Core connection-string sniff), the processing
/// server, the global retry policy, and the recurring-job registrations for the
/// five sweeps migrated off <c>BackgroundService</c>s. Skipped entirely under the
/// Testing environment (see <c>Program.cs</c>): tests drive the job classes'
/// sweep/scan methods directly, so no storage or processing server is spun up.
///
/// No Hangfire dashboard is mapped. Its filter-based auth does not compose with
/// RadioPad's custom <c>HttpContext.Items</c> identity, its pages would expose
/// PHI-adjacent job arguments (tenant / notification ids), and the backend binds
/// 127.0.0.1 — so its operational value here is ~zero. A tenanted admin surface
/// over <c>Hangfire.MonitoringApi</c> is the future seam.
/// </summary>
public static class HangfireSetup
{
    /// <summary>Highest-priority queue: escalation + notification channel dispatch.</summary>
    public const string QueueCritical = "critical";

    /// <summary>Default queue: webhooks, push, email.</summary>
    public const string QueueDefault = "default";

    /// <summary>Lowest-priority queue: all cron-shaped maintenance sweeps.</summary>
    public const string QueueMaintenance = "maintenance";

    /// <summary>
    /// Registers Hangfire storage (Postgres or InMemory, per
    /// <see cref="HangfireStorageSelector"/>), the global jittered-retry filter,
    /// and the processing server. Call only outside the Testing environment.
    /// The job CLASSES themselves are registered separately and unconditionally in
    /// <c>Program.cs</c> so tests can resolve them even when this is skipped.
    /// </summary>
    public static void AddRadioPadHangfire(this WebApplicationBuilder builder, string conn)
    {
        builder.Services.AddHangfire(config =>
        {
            if (HangfireStorageSelector.Select(conn) == HangfireStorageSelector.Kind.Postgres)
            {
                // Hangfire provisions and owns its own `hangfire` schema
                // (PrepareSchemaIfNecessary defaults true) — independent of EF Core's
                // hand-written migrations, so there is no model-snapshot interaction.
                // The DB role needs CREATE on first boot.
                config
                    .UsePostgreSqlStorage(c => c.UseNpgsqlConnection(conn))
                    .WithJobExpirationTimeout(TimeSpan.FromDays(30));
            }
            else
            {
                // SQLite dev workstation + the desktop sidecar's bundled backend:
                // schedules + delayed retries live only for the process lifetime.
                // Accepted because every recurring job re-registers at boot via the
                // idempotent AddOrUpdate below and every job body is safe to run twice.
                config
                    .UseInMemoryStorage()
                    .WithJobExpirationTimeout(TimeSpan.FromDays(30));
            }
        });

        // One retry policy for every job: RadioPad's 5-attempt exponential-jitter
        // filter, then the Failed set (our DLQ). Remove Hangfire's built-in
        // 10-attempt AutomaticRetry so the two policies can never both fire.
        foreach (var builtInRetry in GlobalJobFilters.Filters
                     .Where(f => f.Instance is AutomaticRetryAttribute)
                     .Select(f => f.Instance)
                     .ToList())
        {
            GlobalJobFilters.Filters.Remove(builtInRetry);
        }
        GlobalJobFilters.Filters.Add(new JitteredRetryAttribute());

        builder.Services.AddHangfireServer(options =>
        {
            options.WorkerCount = Math.Clamp(Environment.ProcessorCount * 2, 4, 16);
            // Priority order (Hangfire drains queues left-to-right): critical > default > maintenance.
            options.Queues = new[] { QueueCritical, QueueDefault, QueueMaintenance };
            options.SchedulePollingInterval = TimeSpan.FromSeconds(15);
        });
    }

    /// <summary>
    /// (Re)registers the recurring cron jobs migrated off BackgroundServices in
    /// PR-N1. Idempotent — <c>RecurringJob.AddOrUpdate</c> upserts by id, so this
    /// simply refreshes each schedule on every boot (which is exactly how InMemory
    /// storage recovers its schedules after a restart). Call only outside Testing.
    /// </summary>
    public static void UseRadioPadRecurringJobs(this WebApplication app)
    {
        // Force JobStorage initialization so the static RecurringJob API has
        // JobStorage.Current set before the Hangfire server hosted-service starts.
        _ = app.Services.GetRequiredService<JobStorage>();

        // All maintenance-queue crons. Cadences preserve the intervals of the
        // deleted BackgroundServices (RetentionWorker 6h, CriticalResultEscalation
        // 1m, AnomalyDetector 1m, OAuthRefreshRotation 15m, ModelDrift Nh).
        // Each job exposes a plain Task-returning RunRecurringAsync so the
        // AddOrUpdate expression body is a direct method call (Hangfire rejects a
        // Convert-wrapped Task<T> body from the sweep methods that return values).
        RecurringJob.AddOrUpdate<RetentionSweepJob>(
            "retention-sweep", QueueMaintenance,
            j => j.RunRecurringAsync(CancellationToken.None), "0 */6 * * *");

        RecurringJob.AddOrUpdate<CriticalResultEscalationJob>(
            "critical-result-escalation", QueueMaintenance,
            j => j.RunRecurringAsync(CancellationToken.None), "* * * * *");

        RecurringJob.AddOrUpdate<AnomalyScanJob>(
            "anomaly-scan", QueueMaintenance,
            j => j.RunRecurringAsync(CancellationToken.None), "* * * * *");

        RecurringJob.AddOrUpdate<OAuthRefreshRotationJob>(
            "oauth-refresh-rotation", QueueMaintenance,
            j => j.RunRecurringAsync(CancellationToken.None), "*/15 * * * *");

        // Model drift: cron derived from ResolveInterval() hours so the
        // RADIOPAD_DRIFT_CHECK_INTERVAL_HOURS override is preserved. Clamped to a
        // valid 1..24 hour-of-day step.
        var driftHours = Math.Clamp((int)Math.Round(ModelDriftDetectionJob.ResolveInterval().TotalHours), 1, 24);
        RecurringJob.AddOrUpdate<ModelDriftDetectionJob>(
            "model-drift-detection", QueueMaintenance,
            j => j.RunRecurringAsync(CancellationToken.None), $"0 */{driftHours} * * *");

        // PR-N2 — the three recurring cron-platform jobs. (WebhookDispatchJob is enqueue-only,
        // so it is NOT registered here.) Audit-export rollup fans out per tenant at 02:00 UTC;
        // AI cost rollup aggregates the prior day at 01:30 UTC (before retention can purge it);
        // orphaned-draft cleanup runs weekly on Sunday at 03:00 UTC.
        RecurringJob.AddOrUpdate<AuditExportRollupJob>(
            "audit-export-rollup", QueueMaintenance,
            j => j.RunRecurringAsync(CancellationToken.None), "0 2 * * *");

        RecurringJob.AddOrUpdate<AiCostRollupJob>(
            "ai-cost-rollup", QueueMaintenance,
            j => j.RunRecurringAsync(CancellationToken.None), "30 1 * * *");

        RecurringJob.AddOrUpdate<OrphanedDraftCleanupJob>(
            "orphaned-draft-cleanup", QueueMaintenance,
            j => j.RunRecurringAsync(CancellationToken.None), "0 3 * * 0");
    }
}
