using System.Text.Json;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using RadioPad.Application.Abstractions;
using RadioPad.Domain.Entities;
using RadioPad.Domain.Enums;
using RadioPad.Infrastructure.Persistence;

namespace RadioPad.Api.Jobs;

/// <summary>
/// PR-N2 — weekly orphaned-draft cleanup (cron <c>0 3 * * 0</c>, maintenance queue). For each
/// tenant that has opted in via <see cref="Tenant.DraftAutoArchiveDays"/> &gt; 0, soft-archives
/// <see cref="ReportStatus.Draft"/> reports that have been untouched
/// (<see cref="Report.UpdatedAt"/>) for longer than the window: it sets
/// <see cref="Report.ArchivedAt"/> (the status enum is deliberately NOT changed — no new
/// state, no wire break) and appends a <see cref="AuditAction.ReportDraftArchived"/> audit row
/// per report. Batch cap <see cref="MaxPerTenantPerRun"/> per tenant per run. Naturally
/// idempotent (the <c>ArchivedAt == null</c> guard means an already-archived draft is skipped).
///
/// Registered as a singleton (holds only <see cref="IServiceScopeFactory"/> + logger); skipped
/// under Testing where Hangfire is not started — tests drive <see cref="SweepAsync"/> directly.
/// </summary>
[Queue(HangfireSetup.QueueMaintenance)]
public sealed class OrphanedDraftCleanupJob
{
    /// <summary>Storm guard — most reports archived for a single tenant in one run.</summary>
    public const int MaxPerTenantPerRun = 500;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<OrphanedDraftCleanupJob> _log;

    public OrphanedDraftCleanupJob(IServiceScopeFactory scopeFactory, ILogger<OrphanedDraftCleanupJob> log)
    {
        _scopeFactory = scopeFactory;
        _log = log;
    }

    /// <summary>
    /// Hangfire recurring entry point. Returns plain <see cref="Task"/> so the AddOrUpdate
    /// expression body stays a direct method call.
    /// </summary>
    public Task RunRecurringAsync(CancellationToken ct) => SweepAsync(DateTimeOffset.UtcNow, ct);

    /// <summary>
    /// Archives stale drafts for every opted-in tenant as of <paramref name="nowUtc"/>.
    /// Directly testable — the integration tests call this rather than going through Hangfire.
    /// </summary>
    public async Task SweepAsync(DateTimeOffset nowUtc, CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RadioPadDbContext>();
        var audit = scope.ServiceProvider.GetRequiredService<IAuditLog>();

        var tenants = await db.Tenants
            .Where(t => t.DraftAutoArchiveDays > 0)
            .Select(t => new { t.Id, t.DraftAutoArchiveDays })
            .ToListAsync(ct);

        foreach (var tenant in tenants)
        {
            var cutoff = nowUtc.AddDays(-tenant.DraftAutoArchiveDays);
            var stale = await db.Reports
                .Where(r => r.TenantId == tenant.Id
                    && r.Status == ReportStatus.Draft
                    && r.ArchivedAt == null
                    && r.UpdatedAt < cutoff)
                .OrderBy(r => r.UpdatedAt)
                .Take(MaxPerTenantPerRun)
                .ToListAsync(ct);

            if (stale.Count == 0) continue;

            foreach (var report in stale)
            {
                var lastTouched = report.UpdatedAt;
                // Set only ArchivedAt — Status is untouched, and UpdatedAt is preserved so the
                // audit's lastTouched reflects the genuine staleness.
                report.ArchivedAt = nowUtc;
                await audit.AppendAsync(new AuditEvent
                {
                    TenantId = tenant.Id,
                    ReportId = report.Id,
                    Action = AuditAction.ReportDraftArchived,
                    DetailsJson = JsonSerializer.Serialize(new
                    {
                        reportId = report.Id,
                        lastTouched = lastTouched.ToString("o"),
                        thresholdDays = tenant.DraftAutoArchiveDays,
                    }),
                }, ct);
            }

            await db.SaveChangesAsync(ct);
            _log.LogInformation(
                "Orphaned-draft cleanup: tenant {TenantId} archived {Count} stale draft(s) (>{Days}d).",
                tenant.Id, stale.Count, tenant.DraftAutoArchiveDays);
        }
    }
}
