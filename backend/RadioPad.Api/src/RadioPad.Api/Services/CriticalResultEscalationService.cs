using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RadioPad.Application.Abstractions;
using RadioPad.Domain.Entities;
using RadioPad.Domain.Enums;
using RadioPad.Infrastructure.Persistence;

namespace RadioPad.Api.Services;

/// <summary>
/// PRD §14.15 (CR-007) — periodic sweep that escalates critical results whose
/// communication deadline lapsed while the loop was still <see cref="CriticalResultStatus.Open"/>.
/// Deliberately narrow: it only flips Open → Escalated and writes the
/// append-only audit row. It never communicates, acknowledges, or closes on a
/// clinician's behalf — escalation is a flag for a human, not a substitute for one.
///
/// Follows the <see cref="AnomalyDetector"/> pattern (scoped DbContext per pass,
/// failures logged and retried on the next tick, <see cref="ScanOnceAsync"/>
/// public so tests can drive a pass deterministically).
/// </summary>
public sealed class CriticalResultEscalationService : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(1);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<CriticalResultEscalationService> _log;

    public CriticalResultEscalationService(
        IServiceScopeFactory scopeFactory, ILogger<CriticalResultEscalationService> log)
    {
        _scopeFactory = scopeFactory;
        _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Small initial delay so we don't race the migrator on cold start.
        try { await Task.Delay(TimeSpan.FromSeconds(20), stoppingToken); } catch { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            try { await ScanOnceAsync(stoppingToken); }
            catch (OperationCanceledException) { return; }
            catch (Exception ex) { _log.LogWarning(ex, "Critical-result escalation sweep failed; will retry."); }

            try { await Task.Delay(Interval, stoppingToken); }
            catch (OperationCanceledException) { return; }
        }
    }

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
        var overdue = await db.CriticalResults
            .Where(c => c.Status == CriticalResultStatus.Open && c.DueAt < now)
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
