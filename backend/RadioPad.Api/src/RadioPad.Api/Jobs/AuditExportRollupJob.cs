using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using RadioPad.Application.Abstractions;
using RadioPad.Domain.Entities;
using RadioPad.Domain.Enums;
using RadioPad.Infrastructure.Persistence;

namespace RadioPad.Api.Jobs;

/// <summary>
/// PR-N2 — daily audit-export rollup (cron <c>0 2 * * *</c>, maintenance queue). The
/// recurring entry point fans out one enqueued job PER TENANT
/// (<see cref="RunForTenantOnDayAsync"/> via <see cref="IBackgroundJobClient"/>) so one
/// tenant's failure never fails the whole sweep. Each per-tenant job serializes the prior
/// UTC day's <see cref="AuditEvent"/>s as the SAME PHI-minimized JSONL shape as
/// <c>AuditController.Siem</c> (ids, action, timestamps, integrity hash — <c>DetailsJson</c>
/// is deliberately excluded), appends a manifest line, optionally HMAC-SHA256-signs the
/// body with the <c>AuditExport:SigningKey</c> config key, and upserts an
/// <see cref="AuditExportBundle"/> keyed by (TenantId, Date). Idempotent: a re-run replaces
/// the bundle. Bundles are retained 90 days (pruned by <c>RetentionSweepJob</c>).
///
/// Registered as a singleton (holds only <see cref="IServiceScopeFactory"/> + logger and
/// opens its own scope per unit of work), and skipped entirely under the Testing
/// environment where Hangfire is not started — tests drive
/// <see cref="RunForTenantAsync"/> directly.
/// </summary>
[Queue(HangfireSetup.QueueMaintenance)]
public sealed class AuditExportRollupJob
{
    private static readonly JsonSerializerOptions Json = new();

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<AuditExportRollupJob> _log;

    public AuditExportRollupJob(IServiceScopeFactory scopeFactory, ILogger<AuditExportRollupJob> log)
    {
        _scopeFactory = scopeFactory;
        _log = log;
    }

    /// <summary>
    /// Hangfire recurring entry point. Returns plain <see cref="Task"/> so the AddOrUpdate
    /// expression body stays a direct method call (Hangfire rejects a Convert-wrapped
    /// <c>Task&lt;T&gt;</c> body).
    /// </summary>
    public Task RunRecurringAsync(CancellationToken ct) => FanOutAsync(ct);

    /// <summary>
    /// Enqueues one <see cref="RunForTenantOnDayAsync"/> job per tenant for the previous UTC
    /// day. When Hangfire's <see cref="IBackgroundJobClient"/> is not registered (Testing),
    /// falls back to running each tenant inline so behaviour is still exercisable.
    /// </summary>
    internal async Task FanOutAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RadioPadDbContext>();
        var jobs = scope.ServiceProvider.GetService<IBackgroundJobClient>();

        var dayUtc = DateOnly.FromDateTime(DateTime.UtcNow.Date.AddDays(-1));
        var tenantIds = await db.Tenants.Select(t => t.Id).ToListAsync(ct);

        foreach (var tenantId in tenantIds)
        {
            if (jobs is not null)
            {
                // Pass a DateTime (not DateOnly) through the Hangfire argument boundary so the
                // job-argument serializer never has to round-trip a DateOnly.
                var dayDt = dayUtc.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
                jobs.Enqueue<AuditExportRollupJob>(j => j.RunForTenantOnDayAsync(tenantId, dayDt, CancellationToken.None));
            }
            else
            {
                try { await RunForTenantAsync(tenantId, dayUtc, ct); }
                catch (Exception ex) { _log.LogWarning(ex, "Inline audit-export rollup failed for tenant {TenantId}", tenantId); }
            }
        }
    }

    /// <summary>
    /// Hangfire fan-out entry point. Thin serialization-safe wrapper around
    /// <see cref="RunForTenantAsync"/> — takes a <see cref="DateTime"/> so the DateOnly never
    /// crosses the job-argument boundary.
    /// </summary>
    public Task RunForTenantOnDayAsync(Guid tenantId, DateTime dayUtc, CancellationToken ct)
        => RunForTenantAsync(tenantId, DateOnly.FromDateTime(dayUtc), ct);

    /// <summary>
    /// Builds and upserts the signed export bundle for one tenant + UTC day. Directly
    /// testable — the integration tests call this rather than going through Hangfire.
    /// </summary>
    public async Task RunForTenantAsync(Guid tenantId, DateOnly dayUtc, CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RadioPadDbContext>();
        var audit = scope.ServiceProvider.GetRequiredService<IAuditLog>();
        var config = scope.ServiceProvider.GetRequiredService<IConfiguration>();

        var dayStart = new DateTimeOffset(dayUtc.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var dayEnd = dayStart.AddDays(1);

        var events = await db.AuditEvents
            .AsNoTracking()
            .Where(e => e.TenantId == tenantId && e.CreatedAt >= dayStart && e.CreatedAt < dayEnd)
            .OrderBy(e => e.CreatedAt)
            .ThenBy(e => e.Id)
            .ToListAsync(ct);

        // Body: one PHI-minimized line per event — the SAME shape AuditController.Siem emits
        // (ids, action, timestamps, integrity hash). DetailsJson is intentionally excluded.
        var body = new StringBuilder();
        foreach (var e in events)
        {
            body.Append(JsonSerializer.Serialize(new
            {
                id = e.Id,
                tenantId = e.TenantId,
                userId = e.UserId,
                reportId = e.ReportId,
                action = e.Action.ToString(),
                actionCode = (int)e.Action,
                createdAt = e.CreatedAt.ToString("o"),
                integrityHash = e.IntegrityChain,
            }, Json)).Append('\n');
        }

        var bodyBytes = Encoding.UTF8.GetBytes(body.ToString());
        var bodySha256 = Convert.ToHexString(SHA256.HashData(bodyBytes)).ToLowerInvariant();

        var manifest = JsonSerializer.Serialize(new
        {
            tenantId,
            date = dayUtc.ToString("yyyy-MM-dd"),
            eventCount = events.Count,
            firstEventId = events.Count > 0 ? events[0].Id : (Guid?)null,
            lastEventId = events.Count > 0 ? events[^1].Id : (Guid?)null,
            bodySha256,
        }, Json);

        var contentJsonl = body.ToString() + manifest + "\n";

        // Sign the full content with HMAC-SHA256 when a key is configured; otherwise bundle
        // unsigned (the export is still produced — signing is an optional integrity add-on).
        string? signature = null;
        var signingKey = config["AuditExport:SigningKey"];
        if (!string.IsNullOrEmpty(signingKey))
        {
            using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(signingKey));
            signature = Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(contentJsonl))).ToLowerInvariant();
        }
        else
        {
            _log.LogInformation(
                "Audit-export rollup for tenant {TenantId} on {Date}: no AuditExport:SigningKey configured — bundle stored unsigned.",
                tenantId, dayUtc);
        }

        // Idempotent upsert by (TenantId, Date) — a re-run replaces the bundle in place.
        var bundle = await db.AuditExportBundles
            .FirstOrDefaultAsync(x => x.TenantId == tenantId && x.Date == dayUtc, ct);
        var isNew = bundle is null;
        if (bundle is null)
        {
            bundle = new AuditExportBundle { TenantId = tenantId, Date = dayUtc };
            db.AuditExportBundles.Add(bundle);
        }
        bundle.ContentJsonl = contentJsonl;
        bundle.Signature = signature;
        bundle.EventCount = events.Count;
        bundle.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);

        await audit.AppendAsync(new AuditEvent
        {
            TenantId = tenantId,
            Action = AuditAction.AuditExportBundleCreated,
            DetailsJson = JsonSerializer.Serialize(new
            {
                date = dayUtc.ToString("yyyy-MM-dd"),
                eventCount = events.Count,
                bodySha256,
                signed = signature is not null,
                replaced = !isNew,
            }),
        }, ct);
    }
}
