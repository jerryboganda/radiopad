using Hangfire;
using Microsoft.EntityFrameworkCore;
using RadioPad.Domain.Entities;
using RadioPad.Infrastructure.Persistence;

namespace RadioPad.Api.Jobs;

/// <summary>
/// PR-N2 — daily AI cost rollup (cron <c>30 1 * * *</c>, maintenance queue). Aggregates the
/// previous UTC day's <see cref="AiRequest"/> rows per (TenantId, Provider, Model) into
/// request counts and token sums, upserting an <see cref="AiUsageRollup"/> keyed by the unique
/// (TenantId, Date, Provider, Model) 4-tuple. Runs at T+1.5h, before the 6-hourly retention
/// sweep can purge the day's raw rows — so the rollup is what preserves billing counts past
/// retention. Idempotent: a re-run upserts in place (no duplicate rows).
///
/// Registered as a singleton (holds only <see cref="IServiceScopeFactory"/> + logger); skipped
/// under Testing where Hangfire is not started — tests drive <see cref="RunForDayAsync"/>
/// directly.
/// </summary>
[Queue(HangfireSetup.QueueMaintenance)]
public sealed class AiCostRollupJob
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<AiCostRollupJob> _log;

    public AiCostRollupJob(IServiceScopeFactory scopeFactory, ILogger<AiCostRollupJob> log)
    {
        _scopeFactory = scopeFactory;
        _log = log;
    }

    /// <summary>
    /// Hangfire recurring entry point. Returns plain <see cref="Task"/> so the AddOrUpdate
    /// expression body stays a direct method call.
    /// </summary>
    public Task RunRecurringAsync(CancellationToken ct) =>
        RunForDayAsync(DateOnly.FromDateTime(DateTime.UtcNow.Date.AddDays(-1)), ct);

    /// <summary>
    /// Aggregates and upserts the rollups for one UTC day across all tenants. Directly
    /// testable — the integration tests call this rather than going through Hangfire.
    /// </summary>
    public async Task RunForDayAsync(DateOnly dayUtc, CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RadioPadDbContext>();

        var dayStart = new DateTimeOffset(dayUtc.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var dayEnd = dayStart.AddDays(1);

        var groups = await db.AiRequests
            .Where(a => a.CreatedAt >= dayStart && a.CreatedAt < dayEnd)
            .GroupBy(a => new { a.TenantId, a.Provider, a.Model })
            .Select(g => new
            {
                g.Key.TenantId,
                g.Key.Provider,
                g.Key.Model,
                RequestCount = g.Count(),
                InputTokens = g.Sum(x => (long)x.InputTokens),
                OutputTokens = g.Sum(x => (long)x.OutputTokens),
            })
            .ToListAsync(ct);

        foreach (var group in groups)
        {
            var rollup = await db.AiUsageRollups.FirstOrDefaultAsync(
                r => r.TenantId == group.TenantId && r.Date == dayUtc
                    && r.Provider == group.Provider && r.Model == group.Model,
                ct);
            if (rollup is null)
            {
                rollup = new AiUsageRollup
                {
                    TenantId = group.TenantId,
                    Date = dayUtc,
                    Provider = group.Provider,
                    Model = group.Model,
                };
                db.AiUsageRollups.Add(rollup);
            }
            rollup.RequestCount = group.RequestCount;
            rollup.InputTokens = group.InputTokens;
            rollup.OutputTokens = group.OutputTokens;
            rollup.UpdatedAt = DateTimeOffset.UtcNow;
        }

        if (groups.Count > 0)
        {
            await db.SaveChangesAsync(ct);
            _log.LogInformation(
                "AI cost rollup for {Date}: upserted {Count} (tenant, provider, model) rollup(s).",
                dayUtc, groups.Count);
        }
    }
}
