using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RadioPad.Application.Abstractions;
using RadioPad.Application.Services;
using RadioPad.Domain.Entities;
using RadioPad.Infrastructure.Persistence;
using RadioPad.Infrastructure.Seeding;

namespace RadioPad.Infrastructure.Providers.Ubag;

/// <summary>
/// Keeps each tenant's UBAG provider rows in sync with the gateway's live target
/// catalog + browser login state, so any web AI the operator logs into through the
/// UBAG Chromium session appears in the provider picker automatically — no code
/// change, no manual provider config, no developer involvement.
///
/// Curated primaries (<see cref="Pinned"/> = gemini_web, deepseek_web) are owned by
/// <c>DevSeed</c> and never touched here. Every OTHER catalog target is auto-managed:
/// a row is materialised the first time the target is authenticated, and its
/// <see cref="ProviderConfig.Enabled"/> flag then mirrors the live login state
/// (logged in → selectable; logged out → hidden), so the picker always reflects what
/// the operator can actually use right now.
/// </summary>
public sealed class UbagProviderDiscoveryService
{
    // Curated, DevSeed-owned primaries — discovery never changes these.
    private static readonly HashSet<string> Pinned =
        new(StringComparer.OrdinalIgnoreCase) { "gemini_web", "deepseek_web" };

    // Non-provider / placeholder catalog entries that must never become pickers.
    private static readonly HashSet<string> Excluded =
        new(StringComparer.OrdinalIgnoreCase) { "mock", "generic_chat", "generic_form" };

    private readonly IUbagClient _client;
    private readonly RadioPadDbContext _db;
    private readonly ILogger<UbagProviderDiscoveryService> _logger;

    public UbagProviderDiscoveryService(
        IUbagClient client,
        RadioPadDbContext db,
        ILogger<UbagProviderDiscoveryService> logger)
    {
        _client = client;
        _db = db;
        _logger = logger;
    }

    /// <summary>
    /// Reconciles tenant <paramref name="tenantId"/>'s UBAG provider rows against the
    /// live gateway catalog. Returns the number of rows inserted/updated. Never throws
    /// for transport problems — the gateway being unreachable is a no-op (returns 0).
    /// </summary>
    public async Task<int> SyncAsync(Guid tenantId, CancellationToken ct)
    {
        IReadOnlyList<UbagTarget> targets;
        IReadOnlyList<UbagBrowserContext> contexts;
        try
        {
            var health = await _client.GetHealthAsync(ct);
            if (!health.Ok) return 0;
            targets = await _client.ListTargetsAsync(ct);
            contexts = await _client.ListBrowserContextsAsync(ct);
        }
        catch (Exception ex) when (
            ex is ProviderTransportException or HttpRequestException or TaskCanceledException)
        {
            _logger.LogDebug(ex, "UBAG discovery skipped for tenant {TenantId}: gateway unreachable", tenantId);
            return 0;
        }

        var merged = UbagProviderAdapter.MergeTargetReadiness(targets, contexts);
        var existing = await _db.Providers
            .Where(p => p.TenantId == tenantId && p.Adapter == UbagProviderAdapter.AdapterId)
            .ToListAsync(ct);

        var changes = 0;
        foreach (var t in merged)
        {
            if (Excluded.Contains(t.Id) || Pinned.Contains(t.Id)) continue;

            var row = existing.FirstOrDefault(
                p => string.Equals(p.Model, t.Id, StringComparison.OrdinalIgnoreCase));
            if (row is null)
            {
                // Only materialise a row once the operator has actually logged in, so
                // the picker isn't cluttered with every theoretical web AI the gateway
                // could drive.
                if (!t.Ready) continue;
                _db.Providers.Add(new ProviderConfig
                {
                    TenantId = tenantId,
                    Name = $"UBAG ({t.Name})",
                    Adapter = UbagProviderAdapter.AdapterId,
                    Model = t.Id,
                    Compliance = UbagProviderAdapter.DefaultComplianceClass,
                    Enabled = true,
                    // Conservative default quality so a freshly discovered provider does
                    // not outrank the curated primaries until a human grades it.
                    Quality = 0.5m,
                    Priority = 60,
                });
                changes++;
            }
            else if (row.Enabled != t.Ready)
            {
                // Mirror live login state: logging out hides it, logging back in restores it.
                row.Enabled = t.Ready;
                changes++;
            }
        }

        if (changes > 0)
        {
            try
            {
                await _db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException ex)
            {
                _logger.LogWarning(ex, "UBAG discovery save failed for tenant {TenantId}", tenantId);
                return 0;
            }
        }
        return changes;
    }
}

/// <summary>
/// Background sweeper that runs <see cref="UbagProviderDiscoveryService"/> for every
/// UBAG-using tenant shortly after start-up and every few minutes thereafter, so newly
/// logged-in providers surface on their own without anyone hitting the manual refresh
/// endpoint.
/// </summary>
public sealed class UbagProviderDiscoveryHostedService : BackgroundService
{
    private static readonly TimeSpan StartupDelay = TimeSpan.FromSeconds(8);
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(5);

    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<UbagProviderDiscoveryHostedService> _logger;

    public UbagProviderDiscoveryHostedService(
        IServiceScopeFactory scopes,
        ILogger<UbagProviderDiscoveryHostedService> logger)
    {
        _scopes = scopes;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Let the sidecar finish migrating/seeding and the gateway come up first.
        try { await Task.Delay(StartupDelay, stoppingToken); }
        catch (OperationCanceledException) { return; }

        // One-time backfill: ensure EVERY existing tenant has the curated UBAG primaries
        // (Gemini Web + DeepSeek Web). Orgs created before per-controller seeding shipped —
        // e.g. production orgs bootstrapped earlier — would otherwise never get them, because
        // the periodic discovery below only runs for tenants that ALREADY have a UBAG row, and
        // those primaries were historically seeded only for the DevSeed "dev" tenant. This is
        // idempotent + ensure-on-absence and runs ONCE at startup (not every sweep), so an
        // operator who deletes a primary keeps it deleted until the next restart.
        try
        {
            using var scope = _scopes.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<RadioPadDbContext>();
            var allTenantIds = await db.Tenants.Select(t => t.Id).ToListAsync(stoppingToken);
            var seeded = 0;
            foreach (var tid in allTenantIds)
                seeded += await UbagPrimarySeed.EnsureCuratedPrimariesAsync(db, tid, stoppingToken);
            if (seeded > 0)
                _logger.LogInformation(
                    "UBAG primary backfill: seeded {Count} curated row(s) across {Tenants} tenant(s)",
                    seeded, allTenantIds.Count);

            // Iter-36 — same one-time, all-tenant backfill for the admin Modality +
            // BodyPart catalogs, so pre-existing (production) orgs get the defaults
            // that were formerly hardcoded in the frontend. Idempotent on absence.
            var catalogRows = 0;
            foreach (var tid in allTenantIds)
                catalogRows += await Seeding.CatalogSeed.EnsureCatalogAsync(db, tid, stoppingToken);
            if (catalogRows > 0)
                _logger.LogInformation(
                    "Catalog backfill: seeded {Count} modality/body-part row(s) across {Tenants} tenant(s)",
                    catalogRows, allTenantIds.Count);

            // Policy change (2026-06-27): UBAG is PHI-approved. Promote any rows
            // that were seeded as Sandbox before this change so existing orgs'
            // AI features (cleanup/impression/rewrite/cross-check) stop being
            // blocked by the PHI gates. Idempotent — only non-PhiApproved rows.
            var promoted = await UbagPrimarySeed.EnsureCuratedComplianceAsync(db, stoppingToken);
            if (promoted > 0)
                _logger.LogInformation(
                    "UBAG compliance backfill: promoted {Count} row(s) to PhiApproved", promoted);
        }
        catch (OperationCanceledException) { return; }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "UBAG primary backfill failed");
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopes.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<RadioPadDbContext>();
                var disco = scope.ServiceProvider.GetRequiredService<UbagProviderDiscoveryService>();
                // Only tenants that already use UBAG (DevSeed / prod-seed gave them the
                // curated primaries) opt in to discovery of additional web targets.
                var tenantIds = await db.Providers
                    .Where(p => p.Adapter == UbagProviderAdapter.AdapterId)
                    .Select(p => p.TenantId)
                    .Distinct()
                    .ToListAsync(stoppingToken);
                foreach (var tid in tenantIds)
                {
                    var n = await disco.SyncAsync(tid, stoppingToken);
                    if (n > 0)
                        _logger.LogInformation(
                            "UBAG discovery: {Count} provider row(s) reconciled for tenant {TenantId}", n, tid);
                }
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "UBAG discovery sweep failed");
            }

            try { await Task.Delay(Interval, stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }
}
