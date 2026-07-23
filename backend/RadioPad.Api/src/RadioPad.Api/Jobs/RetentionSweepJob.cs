using Microsoft.EntityFrameworkCore;
using RadioPad.Application.Abstractions;
using RadioPad.Domain.Entities;
using RadioPad.Domain.Enums;
using RadioPad.Infrastructure.Persistence;

namespace RadioPad.Api.Jobs;

/// <summary>
/// PRD §13.3 — retention enforcement. Runs every 6 hours as a Hangfire recurring
/// job (cron <c>0 */6 * * *</c>, maintenance queue); for each tenant with
/// <see cref="TenantSettings.RetentionDays"/> &gt; 0 AND
/// <see cref="TenantSettings.LegalHold"/> = false, purges:
///   • <see cref="AiRequest"/> rows older than the cutoff (already
///     PHI-minimised — only input/output hashes are stored, never the bodies);
///   • <see cref="ReportVersion"/> rows older than the cutoff (the current
///     <see cref="Report"/> row is never touched).
///
/// AuditEvents are NEVER deleted regardless of retention — the SHA-256 chain
/// is the platform's tamper-evidence and PRD §13.2 mandates immutability. The
/// worker logs a single <see cref="AuditAction.RetentionPurge"/> entry per
/// tenant per pass with the affected counts, so the action itself is auditable.
///
/// LegalHold = true short-circuits the entire pass for that tenant — no
/// deletions are issued, even if RetentionDays is configured.
///
/// Migrated from the former <c>RetentionWorker</c> BackgroundService (PR-N1); the
/// <see cref="SweepAsync"/> body is byte-identical, and stays <c>internal</c> so
/// the existing reflection-driven tests re-point with only a type-name change.
/// </summary>
public sealed class RetentionSweepJob
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<RetentionSweepJob> _log;

    public RetentionSweepJob(IServiceScopeFactory scopeFactory, ILogger<RetentionSweepJob> log)
    {
        _scopeFactory = scopeFactory;
        _log = log;
    }

    /// <summary>
    /// Hangfire recurring entry point. Returns plain <see cref="Task"/> so the
    /// AddOrUpdate expression body stays a direct method call — Hangfire rejects a
    /// Convert-wrapped <c>Task&lt;T&gt;</c> body.
    /// </summary>
    public Task RunRecurringAsync(CancellationToken ct) => SweepAsync(ct);

    internal async Task SweepAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RadioPadDbContext>();
        var audit = scope.ServiceProvider.GetRequiredService<IAuditLog>();

        var policies = await db.TenantSettings
            .Where(s => s.RetentionDays > 0 && !s.LegalHold)
            .Select(s => new { s.TenantId, s.RetentionDays })
            .ToListAsync(ct);

        foreach (var p in policies)
        {
            var cutoff = DateTimeOffset.UtcNow.AddDays(-p.RetentionDays);

            var aiPurged = await db.AiRequests
                .Where(a => a.TenantId == p.TenantId && a.CreatedAt < cutoff)
                .ExecuteDeleteAsync(ct);

            // ReportVersion rows are tenant-scoped via their parent Report.
            // We join through Reports to enforce tenant isolation rather than
            // trusting any direct TenantId column on the version row.
            var versionPurged = await db.ReportVersions
                .Where(v => db.Reports.Any(r => r.Id == v.ReportId && r.TenantId == p.TenantId)
                            && v.CreatedAt < cutoff)
                .ExecuteDeleteAsync(ct);

            if (aiPurged == 0 && versionPurged == 0) continue;

            await audit.AppendAsync(new AuditEvent
            {
                TenantId = p.TenantId,
                Action = AuditAction.RetentionPurge,
                DetailsJson = System.Text.Json.JsonSerializer.Serialize(new
                {
                    cutoff = cutoff.ToString("o"),
                    retentionDays = p.RetentionDays,
                    aiRequestsPurged = aiPurged,
                    reportVersionsPurged = versionPurged,
                }),
            }, ct);

            _log.LogInformation(
                "Retention sweep: tenant {TenantId} purged {AiRequests} AI requests, {Versions} report versions (cutoff {Cutoff:o})",
                p.TenantId, aiPurged, versionPurged, cutoff);
        }

        // Durable AI jobs — a global (not per-tenant-policy) housekeeping pass: shed
        // the heavy ResultJson payload 24h after completion (the widget re-fetches
        // on demand, so it need not linger), then delete terminal rows after 30 days.
        // Non-terminal rows are never touched here — the boot recovery sweep owns those.
        var aiResultCutoff = DateTimeOffset.UtcNow.AddHours(-24);
        var aiRowCutoff = DateTimeOffset.UtcNow.AddDays(-30);

        // PR-B5 — InputJson (raw dictation / cross-check text on the input-carrying kinds) is the
        // same clinical-text-at-rest class as ResultJson, so it is shed on the same 24h cadence in
        // the same SetProperty chain. Consequence (deliberate): a cleanup/cross-check job older than
        // 24h can no longer be retried, because the raw input needed to re-run it is gone. The Where
        // also matches rows that carry InputJson but never produced a ResultJson (e.g. a failed
        // cleanup job) so their input is not left behind.
        var aiResultsCleared = await db.AiJobs
            .Where(j => j.Status != "queued" && j.Status != "running"
                        && (j.ResultJson != null || j.InputJson != null)
                        && j.CompletedAt != null && j.CompletedAt < aiResultCutoff)
            .ExecuteUpdateAsync(s => s
                .SetProperty(j => j.ResultJson, (string?)null)
                .SetProperty(j => j.InputJson, (string?)null), ct);

        var aiJobsDeleted = await db.AiJobs
            .Where(j => j.CompletedAt != null && j.CompletedAt < aiRowCutoff)
            .ExecuteDeleteAsync(ct);

        if (aiResultsCleared > 0 || aiJobsDeleted > 0)
            _log.LogInformation(
                "Retention sweep: AI jobs — cleared {Cleared} result/input payload(s) (>24h), deleted {Deleted} row(s) (>30d)",
                aiResultsCleared, aiJobsDeleted);

        // PR-N2 — signed audit-export bundles are retained 90 days (a global housekeeping pass,
        // not subject to per-tenant retention policy). Pruned by CreatedAt so the cutoff is
        // robust regardless of the exported day.
        var exportCutoff = DateTimeOffset.UtcNow.AddDays(-90);
        var exportsDeleted = await db.AuditExportBundles
            .Where(b => b.CreatedAt < exportCutoff)
            .ExecuteDeleteAsync(ct);
        if (exportsDeleted > 0)
            _log.LogInformation("Retention sweep: deleted {Deleted} audit-export bundle(s) (>90d)", exportsDeleted);
    }
}
