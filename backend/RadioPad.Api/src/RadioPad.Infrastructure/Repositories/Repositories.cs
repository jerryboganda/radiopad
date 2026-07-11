using System.Collections.Concurrent;
using System.Data;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using RadioPad.Application.Abstractions;
using RadioPad.Domain.Entities;
using RadioPad.Domain.Enums;
using RadioPad.Infrastructure.Persistence;

namespace RadioPad.Infrastructure.Repositories;

public class EfAuditLog : IAuditLog
{
    private static readonly ConcurrentDictionary<Guid, SemaphoreSlim> TenantLocks = new();
    private readonly RadioPadDbContext _db;
    public EfAuditLog(RadioPadDbContext db) => _db = db;

    public async Task AppendAsync(AuditEvent evt, CancellationToken cancellationToken)
    {
        var gate = TenantLocks.GetOrAdd(evt.TenantId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            IDbContextTransaction? tx = null;
            try
            {
                if (_db.Database.CurrentTransaction is null)
                    tx = await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);

                var prev = await _db.AuditEvents
                    .Where(e => e.TenantId == evt.TenantId)
                    .OrderByDescending(e => e.CreatedAt)
                    .ThenByDescending(e => e.Id)
                    .Select(e => e.IntegrityChain)
                    .FirstOrDefaultAsync(cancellationToken) ?? "";
                var payload = $"{evt.Id}|{evt.TenantId}|{(int)evt.Action}|{evt.DetailsJson}|{prev}";
                evt.IntegrityChain = Convert.ToHexString(
                    SHA256.HashData(Encoding.UTF8.GetBytes(payload))).ToLowerInvariant();
                evt.CreatedAt = DateTimeOffset.UtcNow;
                evt.UpdatedAt = evt.CreatedAt;
                _db.AuditEvents.Add(evt);
                await _db.SaveChangesAsync(cancellationToken);
                if (tx is not null)
                    await tx.CommitAsync(cancellationToken);
            }
            finally
            {
                if (tx is not null)
                    await tx.DisposeAsync();
            }
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<IReadOnlyList<AuditEvent>> QueryAsync(
        Guid tenantId, DateTimeOffset? from, DateTimeOffset? to, int take = 200,
        CancellationToken cancellationToken = default)
    {
        var q = _db.AuditEvents.AsNoTracking().Where(e => e.TenantId == tenantId);
        if (from.HasValue) q = q.Where(e => e.CreatedAt >= from.Value);
        if (to.HasValue) q = q.Where(e => e.CreatedAt <= to.Value);
        return await q.OrderByDescending(e => e.CreatedAt).Take(take).ToListAsync(cancellationToken);
    }

    public async Task<AuditChainVerification> VerifyChainAsync(
        Guid tenantId, CancellationToken cancellationToken = default)
    {
        var events = await _db.AuditEvents.AsNoTracking()
            .Where(e => e.TenantId == tenantId)
            .OrderBy(e => e.CreatedAt)
            .ThenBy(e => e.Id)
            .ToListAsync(cancellationToken);

        var prev = "";
        DateTimeOffset? lastOk = null;
        foreach (var evt in events)
        {
            var payload = $"{evt.Id}|{evt.TenantId}|{(int)evt.Action}|{evt.DetailsJson}|{prev}";
            var expected = Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes(payload))).ToLowerInvariant();
            if (!string.Equals(expected, evt.IntegrityChain, StringComparison.Ordinal))
            {
                return new AuditChainVerification(
                    EventCount: events.Count,
                    Intact: false,
                    FirstBrokenEventId: evt.Id,
                    LastVerifiedAt: lastOk);
            }
            prev = evt.IntegrityChain;
            lastOk = evt.CreatedAt;
        }
        return new AuditChainVerification(events.Count, Intact: true, FirstBrokenEventId: null, LastVerifiedAt: lastOk);
    }
}

public class EfRulebookStore : IRulebookStore
{
    private readonly RadioPadDbContext _db;
    public EfRulebookStore(RadioPadDbContext db) => _db = db;

    public Task<Rulebook?> GetAsync(Guid tenantId, string rulebookId, CancellationToken ct) =>
        _db.Rulebooks.AsNoTracking()
            .Where(r => r.TenantId == tenantId && r.RulebookId == rulebookId)
            .OrderByDescending(r => r.Version)
            .FirstOrDefaultAsync(ct);

    public async Task<IReadOnlyList<Rulebook>> ListAsync(Guid tenantId, CancellationToken ct) =>
        await _db.Rulebooks.AsNoTracking()
            .Where(r => r.TenantId == tenantId)
            .OrderBy(r => r.Name).ToListAsync(ct);

    public async Task SaveAsync(Rulebook rulebook, CancellationToken ct)
    {
        rulebook.UpdatedAt = DateTimeOffset.UtcNow;
        var existing = await _db.Rulebooks.FindAsync(new object[] { rulebook.Id }, ct);
        if (existing is null) _db.Rulebooks.Add(rulebook);
        else _db.Entry(existing).CurrentValues.SetValues(rulebook);
        await _db.SaveChangesAsync(ct);
    }
}

/// <summary>
/// EF-backed AI usage ledger. Persists every <see cref="AiRequest"/> row and
/// computes per-tenant rollups used by <c>GET /api/usage/summary</c>.
/// </summary>
public class EfAiUsageStore : IAiUsageStore
{
    private readonly RadioPadDbContext _db;
    public EfAiUsageStore(RadioPadDbContext db) => _db = db;

    public async Task RecordAsync(AiRequest request, CancellationToken ct)
    {
        request.CreatedAt = DateTimeOffset.UtcNow;
        request.UpdatedAt = request.CreatedAt;
        _db.AiRequests.Add(request);
        await _db.SaveChangesAsync(ct);
    }

    public async Task<UsageSummary> SummariseAsync(
        Guid tenantId, DateTimeOffset? from, DateTimeOffset? to, CancellationToken ct)
    {
        var q = _db.AiRequests.AsNoTracking().Where(r => r.TenantId == tenantId);
        if (from.HasValue) q = q.Where(r => r.CreatedAt >= from.Value);
        if (to.HasValue) q = q.Where(r => r.CreatedAt <= to.Value);
        var rows = await q.ToListAsync(ct);

        // Iter-34 BILL-005 — price the rollup against the tenant's current
        // ProviderConfig rows. Retired providers (no matching name) leave the
        // cost columns at zero and surface `unpriced=true` for the UI.
        var providerCosts = await _db.Providers.AsNoTracking()
            .Where(p => p.TenantId == tenantId)
            .Select(p => new { p.Name, p.CostPerInputKToken, p.CostPerOutputKToken })
            .ToListAsync(ct);
        var costByName = providerCosts
            .GroupBy(p => p.Name, StringComparer.Ordinal)
            .ToDictionary(
                g => g.Key,
                g => (Input: g.First().CostPerInputKToken, Output: g.First().CostPerOutputKToken),
                StringComparer.Ordinal);

        var byProvider = rows
            .GroupBy(r => new { r.Provider, r.Model })
            .Select(g =>
            {
                var inputTokens = g.Sum(r => (long)r.InputTokens);
                var outputTokens = g.Sum(r => (long)r.OutputTokens);
                var hasPrice = costByName.TryGetValue(g.Key.Provider, out var price);
                var costIn = hasPrice ? (inputTokens / 1000m) * price.Input : 0m;
                var costOut = hasPrice ? (outputTokens / 1000m) * price.Output : 0m;
                return new UsageByProvider(
                    Provider: g.Key.Provider,
                    Adapter: g.Key.Model,
                    Requests: g.Count(),
                    InputTokens: inputTokens,
                    OutputTokens: outputTokens,
                    CostInputUsd: costIn,
                    CostOutputUsd: costOut,
                    CostTotalUsd: costIn + costOut,
                    Unpriced: !hasPrice);
            })
            .OrderByDescending(p => p.Requests)
            .ToList();

        return new UsageSummary(
            TotalRequests: rows.Count,
            OkCount: rows.Count(r => r.Status == "ok"),
            BlockedCount: rows.Count(r => r.Status == "blocked"),
            ErrorCount: rows.Count(r => r.Status == "error"),
            InputTokens: rows.Sum(r => (long)r.InputTokens),
            OutputTokens: rows.Sum(r => (long)r.OutputTokens),
            AvgLatencyMs: rows.Count == 0 ? 0 : (int)rows.Where(r => r.Status == "ok").DefaultIfEmpty().Average(r => r?.LatencyMs ?? 0),
            ByProvider: byProvider,
            CostTotalUsd: byProvider.Sum(p => p.CostTotalUsd));
    }
}


/// <summary>
/// PRD AI-010 — composite cost / quality / latency scoring with tenant-tunable
/// weights. The cheapest, highest-quality, lowest-latency provider wins; ties
/// are broken by the per-provider rolling P95 latency over the last 24 h
/// computed from the <see cref="AiRequest"/> usage ledger. Tenant-wide
/// weights live on <see cref="TenantSettings.RoutingWeightsJson"/> and are
/// normalised to sum to 1.0; missing / invalid JSON falls back to the
/// documented defaults (cost 0.5 / quality 0.4 / latency 0.1).
/// </summary>
public class EfProviderRouter : IProviderRouter
{
    /// <summary>Trailing window the routing-level circuit breaker inspects.</summary>
    public static readonly TimeSpan RecentFailureWindow = TimeSpan.FromMinutes(10);

    /// <summary>Error rows (with zero ok) in the window before a provider is skipped.</summary>
    public const int FailureStreakThreshold = 3;

    private readonly RadioPadDbContext _db;
    public EfProviderRouter(RadioPadDbContext db) => _db = db;

    public async Task<ProviderConfig?> SelectAsync(
        Tenant tenant, bool containsPhi, CancellationToken ct)
    {
        var ranked = await ScoreAsync(tenant, containsPhi, estimatedInputTokens: 1000, estimatedOutputTokens: 500, ct);
        return ranked.Where(r => r.Eligible).OrderByDescending(r => r.Composite).FirstOrDefault()?.Provider;
    }

    /// <summary>
    /// The full eligible candidate list ordered by composite score (best first) —
    /// the failover chain consumed by <see cref="RadioPad.Application.Services.ProviderFailover"/>.
    /// Same scoring pass as <see cref="SelectAsync"/>; the winner is element 0.
    /// </summary>
    public async Task<IReadOnlyList<ProviderConfig>> SelectRankedAsync(
        Tenant tenant, bool containsPhi, CancellationToken ct)
    {
        var ranked = await ScoreAsync(tenant, containsPhi, estimatedInputTokens: 1000, estimatedOutputTokens: 500, ct);
        return ranked
            .Where(r => r.Eligible)
            .OrderByDescending(r => r.Composite)
            .Select(r => r.Provider)
            .ToList();
    }

    /// <summary>
    /// Iter-32 AI-010 — produce a ranked, fully-explained candidate list.
    /// Used by the routing-preview endpoint and by <see cref="SelectAsync"/>.
    /// </summary>
    internal async Task<List<ProviderRanking>> ScoreAsync(
        Tenant tenant, bool containsPhi,
        int estimatedInputTokens, int estimatedOutputTokens,
        CancellationToken ct)
    {
        var providers = await _db.Providers.AsNoTracking()
            .Where(p => p.TenantId == tenant.Id && p.Enabled)
            .ToListAsync(ct);

        var settings = await _db.TenantSettings.AsNoTracking()
            .FirstOrDefaultAsync(s => s.TenantId == tenant.Id, ct);
        var weights = ParseWeights(settings?.RoutingWeightsJson);

        // P95 latency per provider over the trailing 24 h (status=ok only).
        var since = DateTimeOffset.UtcNow.AddHours(-24);
        var providerNames = providers.Select(p => p.Name).ToHashSet(StringComparer.Ordinal);
        var latencyRows = await _db.AiRequests.AsNoTracking()
            .Where(r => r.TenantId == tenant.Id
                     && r.Status == "ok"
                     && r.CreatedAt >= since
                     && providerNames.Contains(r.Provider))
            .Select(r => new { r.Provider, r.LatencyMs })
            .ToListAsync(ct);
        var p95 = latencyRows
            .GroupBy(r => r.Provider, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => Percentile(g.Select(r => r.LatencyMs).ToList(), 0.95), StringComparer.Ordinal);

        // 2026-07-11 UBAG hardening — routing-level circuit breaker. A provider
        // whose trailing window shows only failures (>= FailureStreakThreshold
        // errors, zero ok) is excluded from ranking, so the failover chain and
        // the winner both skip it. Self-healing: the window slides past the
        // failures, and any success immediately clears the streak. "blocked"
        // rows (policy denials) do NOT count — a healthy provider a policy
        // rejected must not look broken.
        var failureSince = DateTimeOffset.UtcNow - RecentFailureWindow;
        var recentRows = await _db.AiRequests.AsNoTracking()
            .Where(r => r.TenantId == tenant.Id
                     && r.CreatedAt >= failureSince
                     && (r.Status == "ok" || r.Status == "error")
                     && providerNames.Contains(r.Provider))
            .Select(r => new { r.Provider, r.Status })
            .ToListAsync(ct);
        var failingProviders = recentRows
            .GroupBy(r => r.Provider, StringComparer.Ordinal)
            .Where(g => g.Count(r => r.Status == "error") >= FailureStreakThreshold
                     && !g.Any(r => r.Status == "ok"))
            .Select(g => g.Key)
            .ToHashSet(StringComparer.Ordinal);

        // Eligibility filters mirror the legacy router's rules.
        var rankings = new List<ProviderRanking>(providers.Count);
        foreach (var p in providers)
        {
            string? ineligible = null;
            if (p.Compliance == ProviderComplianceClass.Blocked)
                ineligible = "compliance_blocked";
            else if (containsPhi && p.Compliance is not (ProviderComplianceClass.PhiApproved or ProviderComplianceClass.LocalOnly))
                ineligible = "phi_not_allowed";
            else if (!containsPhi && tenant.RequirePhiApprovedProvider
                     && p.Compliance is not (ProviderComplianceClass.PhiApproved or ProviderComplianceClass.LocalOnly or ProviderComplianceClass.DeIdentifiedOnly))
                ineligible = "tenant_requires_phi_grade_provider";
            else if (failingProviders.Contains(p.Name))
                ineligible = "recent_failures";

            var costPerCall = (p.CostPerInputKToken * estimatedInputTokens / 1000m)
                            + (p.CostPerOutputKToken * estimatedOutputTokens / 1000m);
            int? p95Ms = p95.TryGetValue(p.Name, out var v) ? v : (int?)null;
            rankings.Add(new ProviderRanking(
                provider: p,
                costUsd: costPerCall,
                p95LatencyMs: p95Ms,
                ineligibleReason: ineligible));
        }

        // Compute normalised sub-scores in [0,1]. Higher = better.
        var costs = rankings.Where(r => r.IneligibleReason is null).Select(r => r.CostUsd).Where(c => c > 0m).ToList();
        var maxCost = costs.Count == 0 ? 1m : costs.Max();
        var latencies = rankings.Where(r => r.IneligibleReason is null && r.P95LatencyMs is > 0).Select(r => r.P95LatencyMs!.Value).ToList();
        var maxLatency = latencies.Count == 0 ? 1 : latencies.Max();

        foreach (var r in rankings)
        {
            // Cost: lower is better. 0 (unpriced) gets the worst score so an
            // operator who fills in costs always wins over an unpriced row.
            r.CostScore = r.IneligibleReason is not null
                ? 0
                : (r.CostUsd <= 0m ? 0 : Math.Max(0, 1.0 - (double)(r.CostUsd / maxCost)));
            r.QualityScore = (double)Math.Clamp(r.Provider.Quality, 0m, 1m);
            r.LatencyScore = r.P95LatencyMs is null
                ? 0.5  // unknown latency is neutral
                : Math.Max(0, 1.0 - (double)r.P95LatencyMs / Math.Max(1, maxLatency));
            r.Composite = (r.CostScore * weights.Cost)
                        + (r.QualityScore * weights.Quality)
                        + (r.LatencyScore * weights.Latency);
        }

        // Tie-break by lower P95 latency, then by Priority asc, then by Name.
        rankings.Sort((a, b) =>
        {
            var ce = b.Composite.CompareTo(a.Composite);
            if (ce != 0) return ce;
            var la = a.P95LatencyMs ?? int.MaxValue;
            var lb = b.P95LatencyMs ?? int.MaxValue;
            var le = la.CompareTo(lb);
            if (le != 0) return le;
            var pe = a.Provider.Priority.CompareTo(b.Provider.Priority);
            if (pe != 0) return pe;
            return string.CompareOrdinal(a.Provider.Name, b.Provider.Name);
        });
        return rankings;
    }

    internal static RoutingWeights ParseWeights(string? json)
    {
        var defaults = new RoutingWeights(0.5, 0.4, 0.1);
        if (string.IsNullOrWhiteSpace(json)) return defaults;
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            double Read(string key, double fallback) =>
                doc.RootElement.TryGetProperty(key, out var v) && v.ValueKind == System.Text.Json.JsonValueKind.Number
                    ? v.GetDouble() : fallback;
            var c = Math.Max(0, Read("cost", defaults.Cost));
            var q = Math.Max(0, Read("quality", defaults.Quality));
            var l = Math.Max(0, Read("latency", defaults.Latency));
            var sum = c + q + l;
            if (sum <= 0) return defaults;
            return new RoutingWeights(c / sum, q / sum, l / sum);
        }
        catch (System.Text.Json.JsonException)
        {
            return defaults;
        }
    }

    internal static int Percentile(List<int> values, double pct)
    {
        if (values.Count == 0) return 0;
        values.Sort();
        var idx = (int)Math.Ceiling(pct * values.Count) - 1;
        idx = Math.Clamp(idx, 0, values.Count - 1);
        return values[idx];
    }

    internal sealed class ProviderRanking
    {
        public ProviderConfig Provider { get; }
        public decimal CostUsd { get; }
        public int? P95LatencyMs { get; }
        public string? IneligibleReason { get; }
        public bool Eligible => IneligibleReason is null;
        public double CostScore { get; set; }
        public double QualityScore { get; set; }
        public double LatencyScore { get; set; }
        public double Composite { get; set; }

        public ProviderRanking(ProviderConfig provider, decimal costUsd, int? p95LatencyMs, string? ineligibleReason)
        {
            Provider = provider;
            CostUsd = costUsd;
            P95LatencyMs = p95LatencyMs;
            IneligibleReason = ineligibleReason;
        }
    }
}

/// <summary>
/// Iter-32 AI-010 — debug surface (ItAdmin only) that explains the router's
/// decision without performing a real AI call.
/// </summary>
public class EfRoutingPreviewService : IRoutingPreviewService
{
    private readonly EfProviderRouter _router;
    private readonly RadioPadDbContext _db;

    public EfRoutingPreviewService(IProviderRouter router, RadioPadDbContext db)
    {
        if (router is not EfProviderRouter ef)
            throw new InvalidOperationException("EfRoutingPreviewService requires the EfProviderRouter implementation.");
        _router = ef;
        _db = db;
    }

    public async Task<RoutingPreview> PreviewAsync(
        Tenant tenant, bool containsPhi, string? modality,
        int? estimatedInputTokens, int? estimatedOutputTokens, CancellationToken ct)
    {
        var input = estimatedInputTokens is > 0 ? estimatedInputTokens.Value : 1000;
        var output = estimatedOutputTokens is > 0 ? estimatedOutputTokens.Value : 500;
        var ranked = await _router.ScoreAsync(tenant, containsPhi, input, output, ct);
        var settings = await _db.TenantSettings.AsNoTracking()
            .FirstOrDefaultAsync(s => s.TenantId == tenant.Id, ct);
        var w = EfProviderRouter.ParseWeights(settings?.RoutingWeightsJson);

        var winner = ranked.FirstOrDefault(r => r.Eligible);
        var candidates = ranked.Select(r => new RoutingCandidate(
            ProviderId: r.Provider.Id,
            Name: r.Provider.Name,
            Adapter: r.Provider.Adapter,
            Compliance: r.Provider.Compliance.ToString(),
            Eligible: r.Eligible,
            IneligibleReason: r.IneligibleReason,
            CostUsdEstimate: Math.Round(r.CostUsd, 6),
            CostScore: Math.Round(r.CostScore, 4),
            QualityScore: Math.Round(r.QualityScore, 4),
            LatencyScore: Math.Round(r.LatencyScore, 4),
            P95LatencyMs24h: r.P95LatencyMs,
            CompositeScore: Math.Round(r.Composite, 4))).ToList();

        return new RoutingPreview(
            SelectedProviderId: winner?.Provider.Id,
            SelectedProviderName: winner?.Provider.Name,
            Reason: winner is null
                ? (containsPhi ? "no_phi_eligible_provider" : "no_eligible_provider")
                : "composite_score",
            Candidates: candidates,
            Weights: w);
    }
}

/// <summary>
/// PRD BILL-001..006 — EF implementation of <see cref="IPlanQuotaStore"/>.
/// Counts month-to-date OK AI calls per tenant and persists settings updates
/// (e.g. when the dunning grace period elapses).
/// </summary>
public class EfPlanQuotaStore : IPlanQuotaStore
{
    private readonly RadioPadDbContext _db;
    public EfPlanQuotaStore(RadioPadDbContext db) => _db = db;

    public async Task<PlanQuotaUsage> GetOkAiUsageAsync(Guid tenantId, DateTimeOffset since, CancellationToken ct)
    {
        var rows = await _db.AiRequests.AsNoTracking()
            .Where(r => r.TenantId == tenantId && r.Status == "ok")
            .Select(r => new { r.CreatedAt, r.InputTokens, r.OutputTokens })
            .ToListAsync(ct);
        var scoped = rows.Where(r => r.CreatedAt >= since).ToArray();
        return new PlanQuotaUsage(
            scoped.Length,
            scoped.Sum(r => r.InputTokens),
            scoped.Sum(r => r.OutputTokens));
    }

    public async Task<int> CountOkAiCallsAsync(Guid tenantId, DateTimeOffset since, CancellationToken ct) =>
        (await GetOkAiUsageAsync(tenantId, since, ct)).AiCalls;

    public async Task SaveSettingsAsync(TenantSettings settings, CancellationToken ct)
    {
        settings.UpdatedAt = DateTimeOffset.UtcNow;
        var tracked = await _db.TenantSettings.FindAsync(new object[] { settings.Id }, ct);
        if (tracked is null) _db.TenantSettings.Add(settings);
        else _db.Entry(tracked).CurrentValues.SetValues(settings);
        await _db.SaveChangesAsync(ct);
    }

    public Task<TenantSettings?> LoadSettingsAsync(Guid tenantId, CancellationToken ct) =>
        _db.TenantSettings.AsNoTracking()
            .FirstOrDefaultAsync(s => s.TenantId == tenantId, ct);
}

/// <summary>
/// Iter-31 AI-009 — EF-backed loader for per-tenant prompt overrides.
/// Tenant isolation enforced via the <c>TenantId</c> predicate; the lookup
/// never crosses tenants.
/// </summary>
public class EfPromptOverrideStore : IPromptOverrideStore
{
    private readonly RadioPadDbContext _db;
    public EfPromptOverrideStore(RadioPadDbContext db) => _db = db;

    public async Task<IReadOnlyDictionary<string, string>> LoadAsync(
        Guid tenantId, string rulebookId, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(rulebookId)) return new Dictionary<string, string>();
        // Iter-32 AI-009 — only Approved overrides take effect at AI runtime.
        var rows = await _db.PromptOverrides.AsNoTracking()
            .Where(p => p.TenantId == tenantId
                     && p.RulebookId == rulebookId
                     && p.Status == RadioPad.Domain.Enums.PromptOverrideStatus.Approved)
            .Select(p => new { p.BlockKey, p.Body })
            .ToListAsync(ct);
        var map = new Dictionary<string, string>(rows.Count, StringComparer.Ordinal);
        foreach (var r in rows) map[r.BlockKey] = r.Body ?? string.Empty;
        return map;
    }
}
