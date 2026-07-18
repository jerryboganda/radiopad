using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RadioPad.Application.Abstractions;
using RadioPad.Application.Services;
using RadioPad.Domain.Entities;
using RadioPad.Domain.Enums;
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
    private readonly UbagOperatorAlertService? _alerts;
    private readonly IAuditLog? _audit;

    public UbagProviderDiscoveryService(
        IUbagClient client,
        RadioPadDbContext db,
        ILogger<UbagProviderDiscoveryService> logger,
        UbagOperatorAlertService? alerts = null,
        IAuditLog? audit = null)
    {
        _client = client;
        _db = db;
        _logger = logger;
        _alerts = alerts;
        _audit = audit;
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
            if (!health.Ok)
            {
                _alerts?.RecordGatewayState(reachable: false);
                return 0;
            }
            targets = await _client.ListTargetsAsync(ct);
            contexts = await _client.ListBrowserContextsAsync(ct);
        }
        catch (Exception ex) when (
            ex is ProviderTransportException or HttpRequestException or TaskCanceledException)
        {
            // Per-sweep visibility lives in the alert service (throttled WARNING +
            // "unreachable since" in the Hub status); Debug here would hide a
            // multi-day outage at production log levels.
            _alerts?.RecordGatewayState(reachable: false);
            _logger.LogDebug(ex, "UBAG discovery skipped for tenant {TenantId}: gateway unreachable", tenantId);
            return 0;
        }
        _alerts?.RecordGatewayState(reachable: true);

        var merged = UbagProviderAdapter.MergeTargetReadiness(targets, contexts);
        var existing = await _db.Providers
            .Where(p => p.TenantId == tenantId && p.Adapter == UbagProviderAdapter.AdapterId)
            .ToListAsync(ct);

        // Heal-on-empty (audit fix 2026-07-18): an org whose creation-time seeding
        // failed has ZERO ubag rows and used to be invisible to this sweep forever
        // (and POST /api/ubag/refresh-targets couldn't create the pinned primaries).
        // Ensure the curated primaries for such tenants so both paths fully heal.
        // Tenants with ANY ubag row are left alone — deliberate single-row deletions
        // still stick for the process lifetime.
        if (existing.Count == 0)
        {
            var healed = await UbagPrimarySeed.EnsureCuratedPrimariesAsync(_db, tenantId, ct);
            if (healed > 0)
            {
                _logger.LogInformation(
                    "UBAG discovery healed {Count} curated primary row(s) for tenant {TenantId}",
                    healed, tenantId);
                existing = await _db.Providers
                    .Where(p => p.TenantId == tenantId && p.Adapter == UbagProviderAdapter.AdapterId)
                    .ToListAsync(ct);
            }
        }

        var changes = 0;
        var loginLost = new List<string>();
        foreach (var t in merged)
        {
            if (Excluded.Contains(t.Id)) continue;
            // Never materialise a picker row for a target the operator cap
            // (RADIOPAD_UBAG_ALLOWED_TARGETS) would reject at request time —
            // that row would fail 100% of requests (audit finding, 2026-07-11).
            if (!UbagProviderAdapter.IsTargetAllowed(t.Id)) continue;

            var row = existing.FirstOrDefault(
                p => string.Equals(p.Model, t.Id, StringComparison.OrdinalIgnoreCase));

            if (Pinned.Contains(t.Id))
            {
                // Curated primaries stay DevSeed-owned (never created or renamed
                // here), but their Enabled flag now mirrors live login state too
                // (2026-07-11): a logged-out Gemini must stop receiving traffic
                // instead of failing every routed request until a human notices.
                // Ready is tri-state (2026-07-18): only an EXPLICIT gateway signal
                // may flip Enabled — null means the gateway never reported login
                // state (e.g. vps-local executors register no browser contexts even
                // while jobs succeed), so the operator's Enabled choice stands and
                // real outages surface via the failure-based alert path instead.
                if (t.Ready is bool pinnedReady)
                {
                    if (row is not null && row.Enabled != pinnedReady)
                    {
                        row.Enabled = pinnedReady;
                        changes++;
                        if (!pinnedReady) loginLost.Add(t.Id);
                    }
                    if (pinnedReady) _alerts?.RecordTargetAuthenticated(t.Id);
                }
                continue;
            }

            if (row is null)
            {
                // Only materialise a row once the operator has actually logged in, so
                // the picker isn't cluttered with every theoretical web AI the gateway
                // could drive. (An explicit TRUE is required — "no signal" stays hidden.)
                if (t.Ready != true) continue;
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
            else if (t.Ready is bool discoveredReady && row.Enabled != discoveredReady)
            {
                // Mirror live login state: logging out hides it, logging back in restores
                // it. Null (no signal) leaves the row exactly as the operator set it.
                row.Enabled = discoveredReady;
                changes++;
                if (!discoveredReady) loginLost.Add(t.Id);
            }
            if (t.Ready == true) _alerts?.RecordTargetAuthenticated(t.Id);
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

            // Alert AFTER the disable is durably saved: audit the transition in
            // this tenant's trail, light the Hub banner, and email the operator
            // (throttled per target per day). Manual noVNC re-login is the only
            // remedy — UBAG policy forbids automated login — so the first signal
            // must not be a radiologist's failed request.
            foreach (var target in loginLost)
            {
                _logger.LogWarning(
                    "UBAG target {Target} logged out — provider disabled for tenant {TenantId} until re-login",
                    target, tenantId);
                if (_audit is not null)
                {
                    try
                    {
                        await _audit.AppendAsync(new AuditEvent
                        {
                            TenantId = tenantId,
                            Action = AuditAction.SystemAlert,
                            DetailsJson = System.Text.Json.JsonSerializer.Serialize(new
                            {
                                kind = "ubag_login_lost",
                                target,
                                adapter = UbagProviderAdapter.AdapterId,
                                remedy = "manual re-login via UBAG browser viewer (noVNC)",
                            }),
                        }, ct);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to audit UBAG login-lost for tenant {TenantId}", tenantId);
                    }
                }
                if (_alerts is not null)
                    await _alerts.NotifyTargetLoggedOutAsync(target, ct);
            }
        }

        // Real-traffic failure alerting (2026-07-11): the gateway's topology
        // login_state can be SYNTHETIC (a cron upserts "authenticated"), so a
        // dead session may never show as logged out. Failing traffic is the
        // trustworthy signal — mirror the router's circuit-breaker window and
        // alert on any enabled UBAG provider whose recent requests all failed.
        if (_alerts is not null)
            await AlertOnFailureStreaksAsync(tenantId, existing, ct);

        return changes;
    }

    private async Task AlertOnFailureStreaksAsync(
        Guid tenantId, IReadOnlyList<ProviderConfig> ubagRows, CancellationToken ct)
    {
        try
        {
            var enabled = ubagRows.Where(p => p.Enabled).ToList();
            if (enabled.Count == 0) return;
            var names = enabled.Select(p => p.Name).ToHashSet(StringComparer.Ordinal);
            var since = DateTimeOffset.UtcNow - Repositories.EfProviderRouter.RecentFailureWindow;
            var recent = await _db.AiRequests.AsNoTracking()
                .Where(r => r.TenantId == tenantId
                         && r.CreatedAt >= since
                         && (r.Status == "ok" || r.Status == "error")
                         && names.Contains(r.Provider))
                .Select(r => new { r.Provider, r.Status })
                .ToListAsync(ct);
            var byName = recent.GroupBy(r => r.Provider, StringComparer.Ordinal)
                .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);
            foreach (var row in enabled)
            {
                var target = string.IsNullOrWhiteSpace(row.Model) ? row.Name : row.Model;
                if (byName.TryGetValue(row.Name, out var rows)
                    && rows.Count(r => r.Status == "error") >= Repositories.EfProviderRouter.FailureStreakThreshold
                    && !rows.Any(r => r.Status == "ok"))
                {
                    await _alerts!.NotifyTargetFailingAsync(target, ct);
                }
                else
                {
                    _alerts!.RecordTargetRecovered(target);
                }
            }
        }
        catch (Exception ex)
        {
            // Alerting must never break the sweep.
            _logger.LogDebug(ex, "UBAG failure-streak alert pass failed for tenant {TenantId}", tenantId);
        }
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

        // One-time startup backfill for pre-existing tenants (curated UBAG primaries,
        // admin catalogs, Gemini CLI provider + its compliance/name migrations). All
        // stages are idempotent + ensure-on-absence. Each stage — and each tenant
        // within a stage — runs in its OWN try/catch (audit fix 2026-07-18): a single
        // transient failure (e.g. a SQLite lock on tenant #1) used to abort every
        // remaining tenant and stage until the next restart. Zero-row UBAG tenants are
        // additionally healed by every discovery sweep (see SyncAsync), so this pass is
        // belt-and-suspenders for the non-UBAG stages.
        try
        {
            using var scope = _scopes.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<RadioPadDbContext>();
            var allTenantIds = await db.Tenants.Select(t => t.Id).ToListAsync(stoppingToken);

            // Per-tenant stage runner: one tenant's failure skips only that tenant.
            async Task<int> PerTenant(string stage, Func<Guid, Task<int>> run)
            {
                var total = 0;
                foreach (var tid in allTenantIds)
                {
                    stoppingToken.ThrowIfCancellationRequested();
                    try { total += await run(tid); }
                    catch (OperationCanceledException) { throw; }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "{Stage} backfill failed for tenant {TenantId}", stage, tid);
                    }
                }
                return total;
            }

            // Whole-DB stage runner: one stage's failure skips only that stage.
            async Task<int> Stage(string stage, Func<Task<int>> run)
            {
                try { return await run(); }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "{Stage} backfill stage failed", stage);
                    return 0;
                }
            }

            var seeded = await PerTenant("UBAG primaries",
                tid => UbagPrimarySeed.EnsureCuratedPrimariesAsync(db, tid, stoppingToken));
            if (seeded > 0)
                _logger.LogInformation(
                    "UBAG primary backfill: seeded {Count} curated row(s) across {Tenants} tenant(s)",
                    seeded, allTenantIds.Count);

            // Iter-36 — admin Modality + BodyPart catalog defaults for pre-existing orgs.
            var catalogRows = await PerTenant("Catalog",
                tid => Seeding.CatalogSeed.EnsureCatalogAsync(db, tid, stoppingToken));
            if (catalogRows > 0)
                _logger.LogInformation(
                    "Catalog backfill: seeded {Count} modality/body-part row(s) across {Tenants} tenant(s)",
                    catalogRows, allTenantIds.Count);

            // Gemini CLI provider row for the report intake's provider dropdown.
            var cliSeeded = await PerTenant("Gemini CLI",
                tid => Seeding.CliProviderSeed.EnsureGeminiCliAsync(db, tid, stoppingToken));
            if (cliSeeded > 0)
                _logger.LogInformation(
                    "Gemini CLI backfill: seeded {Count} provider row(s) across {Tenants} tenant(s)",
                    cliSeeded, allTenantIds.Count);

            // Operator promotion (2026-07-12): Gemini CLI is PhiApproved.
            var cliPromoted = await Stage("Gemini CLI compliance",
                () => Seeding.CliProviderSeed.EnsureGeminiCliComplianceAsync(db, stoppingToken));
            if (cliPromoted > 0)
                _logger.LogInformation(
                    "Gemini CLI compliance backfill: promoted {Count} row(s) to PhiApproved", cliPromoted);

            // Rename backfill (2026-07-13): API-key auth, not OAuth.
            var cliRenamed = await Stage("Gemini CLI name",
                () => Seeding.CliProviderSeed.EnsureGeminiCliNameAsync(db, stoppingToken));
            if (cliRenamed > 0)
                _logger.LogInformation(
                    "Gemini CLI name backfill: renamed {Count} row(s) to '{Name}'",
                    cliRenamed, Seeding.CliProviderSeed.ProviderName);

            // Policy change (2026-06-27): UBAG is PHI-approved.
            var promoted = await Stage("UBAG compliance",
                () => UbagPrimarySeed.EnsureCuratedComplianceAsync(db, stoppingToken));
            if (promoted > 0)
                _logger.LogInformation(
                    "UBAG compliance backfill: promoted {Count} row(s) to PhiApproved", promoted);

            // Rehydrate operator-alert state from the audit trail (audit fix
            // 2026-07-18): banner "since" timestamps and the 1/day email throttle
            // were process-memory only, so every restart re-armed the throttle and
            // blanked the Hub banners mid-outage. Seed from the last 7 days of
            // ubag_login_lost SystemAlert rows; live sweeps overwrite as needed.
            _ = await Stage("UBAG alert rehydrate", async () =>
            {
                var alerts = scope.ServiceProvider.GetService<UbagOperatorAlertService>();
                if (alerts is null) return 0;
                var cutoff = DateTimeOffset.UtcNow.AddDays(-7);
                var rows = await db.AuditEvents
                    .Where(a => a.Action == Domain.Enums.AuditAction.SystemAlert
                        && a.CreatedAt >= cutoff
                        && a.DetailsJson.Contains("ubag_login_lost"))
                    .Select(a => new { a.DetailsJson, a.CreatedAt })
                    .ToListAsync(stoppingToken);
                var rehydrated = 0;
                foreach (var group in rows
                    .Select(r => new
                    {
                        Target = System.Text.Json.JsonDocument.Parse(r.DetailsJson)
                            .RootElement.TryGetProperty("target", out var t) ? t.GetString() : null,
                        r.CreatedAt,
                    })
                    .Where(r => !string.IsNullOrWhiteSpace(r.Target))
                    .GroupBy(r => r.Target!, StringComparer.OrdinalIgnoreCase))
                {
                    alerts.RehydrateLoginLost(
                        group.Key,
                        since: group.Min(r => r.CreatedAt),
                        lastAlertAt: group.Max(r => r.CreatedAt));
                    rehydrated++;
                }
                if (rehydrated > 0)
                    _logger.LogInformation(
                        "UBAG alert rehydrate: restored login-lost state for {Count} target(s) from the audit trail",
                        rehydrated);
                return rehydrated;
            });
        }
        catch (OperationCanceledException) { return; }
        catch (Exception ex)
        {
            // Only scope/tenant-list construction can land here now; stages self-isolate.
            _logger.LogWarning(ex, "UBAG primary backfill failed");
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopes.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<RadioPadDbContext>();
                var disco = scope.ServiceProvider.GetRequiredService<UbagProviderDiscoveryService>();
                // Sweep EVERY tenant (audit fix 2026-07-18): selecting only tenants that
                // already had a ubag row made an org whose creation-time seeding failed
                // invisible to discovery forever. SyncAsync heals zero-row tenants by
                // ensuring the curated primaries, so all tenants must be visited.
                var tenantIds = await db.Tenants
                    .Select(t => t.Id)
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
