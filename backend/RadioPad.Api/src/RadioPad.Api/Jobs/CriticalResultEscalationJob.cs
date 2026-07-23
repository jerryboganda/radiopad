using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using RadioPad.Application.Abstractions;
using RadioPad.Domain.Entities;
using RadioPad.Domain.Enums;
using RadioPad.Infrastructure.Persistence;

namespace RadioPad.Api.Jobs;

/// <summary>
/// PRD §14.15 (CR-007) — periodic sweep that escalates critical results whose
/// communication deadline lapsed while the loop was still <see cref="CriticalResultStatus.Open"/>.
/// Deliberately narrow: it only flips Open → Escalated and writes the
/// append-only audit row. It never communicates, acknowledges, or closes on a
/// clinician's behalf — escalation is a flag for a human, not a substitute for one.
///
/// Migrated from the former <c>CriticalResultEscalationService</c> BackgroundService
/// (PR-N1) to a Hangfire recurring job (cron <c>* * * * *</c>, maintenance queue).
/// The Open→Escalated flip + audit are byte-identical; the only addition is a
/// <see cref="BatchCap"/> per pass as a storm guard. <see cref="ScanOnceAsync"/>
/// stays public so tests can drive a pass deterministically.
/// </summary>
public sealed class CriticalResultEscalationJob
{
    /// <summary>
    /// Storm guard: at most this many results escalate per pass. A backlog spike
    /// (e.g. the loop was down for an hour) drains over subsequent minute passes
    /// rather than in one giant transaction. Idempotent — nothing is lost.
    /// </summary>
    private const int BatchCap = 200;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<CriticalResultEscalationJob> _log;

    public CriticalResultEscalationJob(
        IServiceScopeFactory scopeFactory, ILogger<CriticalResultEscalationJob> log)
    {
        _scopeFactory = scopeFactory;
        _log = log;
    }

    /// <summary>
    /// Hangfire recurring entry point. Returns plain <see cref="Task"/> so the
    /// AddOrUpdate expression body stays a direct method call — Hangfire rejects a
    /// Convert-wrapped <c>Task&lt;T&gt;</c> body.
    /// </summary>
    public Task RunRecurringAsync(CancellationToken ct) => ScanOnceAsync(ct);

    /// <summary>
    /// Single sweep. Returns the number of results escalated. Public so tests can
    /// run a pass without waiting for the timer.
    /// </summary>
    public async Task<int> ScanOnceAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RadioPadDbContext>();
        var audit = scope.ServiceProvider.GetRequiredService<IAuditLog>();

        var now = DateTimeOffset.UtcNow;

        // Open past its deadline == the ordering clinician was never told in time.
        // Oldest-overdue first + a per-pass cap so a backlog spike can't build one
        // enormous transaction; the remainder escalates on the next minute's pass.
        var overdue = await db.CriticalResults
            .Where(c => c.Status == CriticalResultStatus.Open && c.DueAt < now)
            .OrderBy(c => c.DueAt)
            .Take(BatchCap)
            .ToListAsync(ct);

        if (overdue.Count == 0) return 0;

        foreach (var c in overdue)
        {
            c.Status = CriticalResultStatus.Escalated;
            c.EscalatedAt = now;
            c.UpdatedAt = now;
        }

        await db.SaveChangesAsync(ct);

        foreach (var c in overdue)
        {
            // Audit details carry workflow metadata only — never the finding narrative.
            await audit.AppendAsync(new AuditEvent
            {
                TenantId = c.TenantId,
                ReportId = c.ReportId,
                Action = AuditAction.CriticalResultEscalated,
                DetailsJson = JsonSerializer.Serialize(new
                {
                    criticalResultId = c.Id,
                    reportId = c.ReportId,
                    criticality = c.Criticality.ToString(),
                    reason = "overdue_sweep",
                    dueAt = c.DueAt,
                }),
            }, ct);

            _log.LogWarning(
                "Critical result escalated (overdue): tenant={TenantId} criticalResult={CriticalResultId} criticality={Criticality} dueAt={DueAt}",
                c.TenantId, c.Id, c.Criticality, c.DueAt);
        }

        return overdue.Count;
    }
}
