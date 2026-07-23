using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using RadioPad.Api.Jobs;
using RadioPad.Application.Abstractions;
using RadioPad.Domain.Entities;
using RadioPad.Infrastructure.Persistence;
using RadioPad.Infrastructure.Repositories;

namespace RadioPad.Api.Services;

/// <summary>
/// PR-N2 — an <see cref="IAuditLog"/> decorator that fans an audit append out to any active,
/// <c>audit</c>-subscribed <see cref="TenantWebhookEndpoint"/> for the tenant. It forwards
/// <see cref="AppendAsync"/> to the real <see cref="EfAuditLog"/> first (which stamps the id +
/// integrity chain), then — best effort — enqueues one <see cref="WebhookDispatchJob"/> per
/// subscribed endpoint via Hangfire's <see cref="IBackgroundJobClient"/>.
///
/// The subscribed-endpoint lookup is <see cref="IMemoryCache"/>d for 60s per tenant so the
/// append hot path stays cheap; a newly created endpoint therefore starts receiving events
/// within ~60s. When Hangfire is not registered (Testing) the enqueue is a no-op, and any
/// lookup/enqueue failure is swallowed — a webhook must never break the append-only audit
/// write.
/// </summary>
public sealed class WebhookEnqueueingAuditLog : IAuditLog
{
    private readonly EfAuditLog _inner;
    private readonly RadioPadDbContext _db;
    private readonly IMemoryCache _cache;
    private readonly IBackgroundJobClient? _jobs;
    private readonly ILogger<WebhookEnqueueingAuditLog> _log;

    public WebhookEnqueueingAuditLog(
        EfAuditLog inner,
        RadioPadDbContext db,
        IMemoryCache cache,
        IBackgroundJobClient? jobs,
        ILogger<WebhookEnqueueingAuditLog> log)
    {
        _inner = inner;
        _db = db;
        _cache = cache;
        _jobs = jobs;
        _log = log;
    }

    public async Task AppendAsync(AuditEvent evt, CancellationToken cancellationToken)
    {
        await _inner.AppendAsync(evt, cancellationToken);

        // Nothing to fan out to when Hangfire isn't running (Testing) — the audit row is written.
        if (_jobs is null) return;

        try
        {
            var endpointIds = await GetAuditEndpointIdsAsync(evt.TenantId, cancellationToken);
            foreach (var endpointId in endpointIds)
            {
                var eventId = evt.Id;
                _jobs.Enqueue<WebhookDispatchJob>(j => j.DeliverAuditEventAsync(endpointId, eventId, CancellationToken.None));
            }
        }
        catch (Exception ex)
        {
            // A webhook enqueue must never fail the audit append.
            _log.LogWarning(ex, "Failed to enqueue webhook delivery for tenant {TenantId} audit event.", evt.TenantId);
        }
    }

    public Task<IReadOnlyList<AuditEvent>> QueryAsync(
        Guid tenantId, DateTimeOffset? from, DateTimeOffset? to, int take = 200,
        CancellationToken cancellationToken = default)
        => _inner.QueryAsync(tenantId, from, to, take, cancellationToken);

    public Task<AuditChainVerification> VerifyChainAsync(Guid tenantId, CancellationToken cancellationToken = default)
        => _inner.VerifyChainAsync(tenantId, cancellationToken);

    private async Task<IReadOnlyList<Guid>> GetAuditEndpointIdsAsync(Guid tenantId, CancellationToken ct)
    {
        var cached = _cache.Get<IReadOnlyList<Guid>>(CacheKey(tenantId));
        if (cached is not null) return cached;

        var candidates = await _db.TenantWebhookEndpoints
            .AsNoTracking()
            .Where(e => e.TenantId == tenantId && e.Active)
            .Select(e => new { e.Id, e.EventsCsv })
            .ToListAsync(ct);

        var ids = candidates
            .Where(e => SubscribesTo(e.EventsCsv, "audit"))
            .Select(e => e.Id)
            .ToList();

        _cache.Set(CacheKey(tenantId), (IReadOnlyList<Guid>)ids, TimeSpan.FromSeconds(60));
        return ids;
    }

    private static string CacheKey(Guid tenantId) => $"webhook-endpoints:audit:{tenantId}";

    private static bool SubscribesTo(string? eventsCsv, string kind)
    {
        if (string.IsNullOrWhiteSpace(eventsCsv)) return false;
        foreach (var part in eventsCsv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (string.Equals(part, kind, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }
}
