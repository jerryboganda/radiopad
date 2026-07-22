using Microsoft.EntityFrameworkCore;
using RadioPad.Application.Abstractions;
using RadioPad.Domain.Entities;
using RadioPad.Domain.Enums;
using RadioPad.Infrastructure.Persistence;

namespace RadioPad.Api.Services;

/// <summary>
/// PRD §13.3 — retention enforcement worker. Runs every 6 hours; for each
/// tenant with <see cref="TenantSettings.RetentionDays"/> &gt; 0 AND
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
/// </summary>
public sealed class RetentionWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<RetentionWorker> _log;

    /// <summary>Sweep cadence. Six hours is short enough that admin policy
    /// changes take effect quickly and long enough that a misconfiguration
    /// only burns CPU four times a day.</summary>
    private static readonly TimeSpan Interval = TimeSpan.FromHours(6);

    public RetentionWorker(IServiceScopeFactory scopeFactory, ILogger<RetentionWorker> log)
    {
        _scopeFactory = scopeFactory;
        _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        // First pass after a 30s delay so the API is fully warm before we
        // start scanning rows.
        try { await Task.Delay(TimeSpan.FromSeconds(30), ct); }
        catch (OperationCanceledException) { return; }

        while (!ct.IsCancellationRequested)
        {
            try
            {
                await SweepAsync(ct);
            }
            catch (OperationCanceledException) { return; }
            catch (Exception ex)
            {
                _log.LogError(ex, "Retention sweep failed");
            }

            try { await Task.Delay(Interval, ct); }
            catch (OperationCanceledException) { return; }
        }
    }

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

        var aiResultsCleared = await db.AiJobs
            .Where(j => j.Status != "queued" && j.Status != "running"
                        && j.ResultJson != null
                        && j.CompletedAt != null && j.CompletedAt < aiResultCutoff)
            .ExecuteUpdateAsync(s => s.SetProperty(j => j.ResultJson, (string?)null), ct);

        var aiJobsDeleted = await db.AiJobs
            .Where(j => j.CompletedAt != null && j.CompletedAt < aiRowCutoff)
            .ExecuteDeleteAsync(ct);

        if (aiResultsCleared > 0 || aiJobsDeleted > 0)
            _log.LogInformation(
                "Retention sweep: AI jobs — cleared {Cleared} result payload(s) (>24h), deleted {Deleted} row(s) (>30d)",
                aiResultsCleared, aiJobsDeleted);
    }
}
