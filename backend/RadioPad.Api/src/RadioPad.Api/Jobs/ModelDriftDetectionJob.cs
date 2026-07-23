using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RadioPad.Application.Abstractions;
using RadioPad.Domain.Entities;
using RadioPad.Domain.Enums;
using RadioPad.Domain.ValueObjects;
using RadioPad.Infrastructure.Persistence;
using RadioPad.Validation.Engine;
using RadioPad.Validation.Rulebook;

namespace RadioPad.Api.Jobs;

/// <summary>
/// PRD §18.2 — periodically runs golden-case regression against active AI
/// providers and raises <see cref="AuditAction.SystemAlert"/> events when quality
/// degrades beyond a configurable threshold.
///
/// Only sandbox-class providers are tested to avoid production costs.
/// The job is a no-op if no tenants have approved validation packs.
///
/// Migrated from the former <c>ModelDriftDetectionService</c> BackgroundService
/// (PR-N1) to a Hangfire recurring job (cron derived from <see cref="ResolveInterval"/>
/// hours, maintenance queue). All method bodies are byte-identical;
/// <see cref="RunAllTenantsAsync"/> and <see cref="GetStatusAsync"/> stay public
/// because <c>DriftController</c> invokes them for the admin drift endpoints.
/// </summary>
public sealed class ModelDriftDetectionJob
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ModelDriftDetectionJob> _log;

    /// <summary>
    /// Default interval between drift checks (hours). Overridden by
    /// <c>RADIOPAD_DRIFT_CHECK_INTERVAL_HOURS</c> env var.
    /// </summary>
    private static readonly TimeSpan DefaultInterval = TimeSpan.FromHours(6);

    /// <summary>
    /// Default quality-score delta threshold (points). Overridden by
    /// <c>RADIOPAD_DRIFT_THRESHOLD_SCORE_DELTA</c> env var.
    /// </summary>
    internal const int DefaultThreshold = 15;

    public ModelDriftDetectionJob(
        IServiceScopeFactory scopeFactory,
        ILogger<ModelDriftDetectionJob> log)
    {
        _scopeFactory = scopeFactory;
        _log = log;
    }

    /// <summary>
    /// Hangfire recurring entry point. Returns plain <see cref="Task"/> so the
    /// AddOrUpdate expression body stays a direct method call — Hangfire rejects a
    /// Convert-wrapped <c>Task&lt;T&gt;</c> body. Discards the aggregated results
    /// (only the audit side effects matter on the schedule).
    /// </summary>
    public Task RunRecurringAsync(CancellationToken ct) => RunAllTenantsAsync(ct);

    internal static TimeSpan ResolveInterval()
    {
        var raw = Environment.GetEnvironmentVariable("RADIOPAD_DRIFT_CHECK_INTERVAL_HOURS");
        if (double.TryParse(raw, out var h) && h > 0)
            return TimeSpan.FromHours(h);
        return DefaultInterval;
    }

    internal static int ResolveThreshold()
    {
        var raw = Environment.GetEnvironmentVariable("RADIOPAD_DRIFT_THRESHOLD_SCORE_DELTA");
        if (int.TryParse(raw, out var t) && t > 0)
            return t;
        return DefaultThreshold;
    }

    /// <summary>
    /// Public entry point for manual trigger via API. Runs drift checks
    /// across all tenants immediately and returns aggregated results.
    /// </summary>
    public async Task<IReadOnlyList<DriftCheckResult>> RunAllTenantsAsync(CancellationToken ct)
    {
        var allResults = new List<DriftCheckResult>();
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RadioPadDbContext>();
        var audit = scope.ServiceProvider.GetRequiredService<IAuditLog>();
        var validator = scope.ServiceProvider.GetRequiredService<ReportValidator>();

        var threshold = ResolveThreshold();

        // Find tenants that have at least one approved validation pack.
        var tenantIds = await db.ValidationPacks
            .Where(p => p.Status == ValidationPackStatus.Approved)
            .Select(p => p.TenantId)
            .Distinct()
            .ToListAsync(ct);

        foreach (var tenantId in tenantIds)
        {
            var results = await RunTenantAsync(db, audit, validator, tenantId, threshold, ct);
            allResults.AddRange(results);
        }

        return allResults;
    }

    private async Task<List<DriftCheckResult>> RunTenantAsync(
        RadioPadDbContext db,
        IAuditLog audit,
        ReportValidator validator,
        Guid tenantId,
        int threshold,
        CancellationToken ct)
    {
        var results = new List<DriftCheckResult>();

        // Load approved validation packs for this tenant.
        var packs = await db.ValidationPacks
            .AsNoTracking()
            .Where(p => p.TenantId == tenantId && p.Status == ValidationPackStatus.Approved)
            .ToListAsync(ct);

        if (packs.Count == 0) return results;

        // Load sandbox providers for this tenant (avoid production costs).
        var providers = await db.Providers
            .AsNoTracking()
            .Where(p => p.TenantId == tenantId
                        && p.Enabled
                        && p.Compliance == ProviderComplianceClass.Sandbox)
            .ToListAsync(ct);

        if (providers.Count == 0) return results;

        // Load rulebooks for this tenant.
        var rulebooks = await db.Rulebooks
            .AsNoTracking()
            .Where(r => r.TenantId == tenantId && r.Status == RulebookStatus.Approved)
            .ToListAsync(ct);

        var rulebookMap = rulebooks
            .GroupBy(r => r.RulebookId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(r => r.CreatedAt).First());

        foreach (var pack in packs)
        {
            if (!rulebookMap.TryGetValue(pack.RulebookId, out var rulebookEntity))
                continue;

            RulebookSpec spec;
            try { spec = RulebookSpec.FromYaml(rulebookEntity.SourceYaml); }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "Failed to parse rulebook '{RulebookId}' YAML for drift check", pack.RulebookId);
                continue;
            }

            // Run golden cases through validator and aggregate quality score.
            var (currentScore, currentRuleIds) = RunGoldenCases(pack, spec, validator);

            foreach (var provider in providers)
            {
                var result = await EvaluateDriftAsync(
                    db, audit, tenantId, provider, pack.RulebookId,
                    currentScore, currentRuleIds, threshold, ct);
                results.Add(result);
            }
        }

        return results;
    }

    internal static (int QualityScore, List<string> FiredRuleIds) RunGoldenCases(
        ValidationPack pack, RulebookSpec spec, ReportValidator validator)
    {
        var allRuleIds = new HashSet<string>();
        int totalScore = 0;
        int caseCount = 0;

        using var doc = JsonDocument.Parse(pack.GoldenCasesJson);
        foreach (var caseEl in doc.RootElement.EnumerateArray())
        {
            var report = ParseReport(caseEl);
            var result = validator.Validate(report, spec);
            totalScore += result.QualityScore;
            caseCount++;
            foreach (var f in result.Findings)
                allRuleIds.Add(f.RuleId);
        }

        var avgScore = caseCount > 0 ? totalScore / caseCount : 100;
        return (avgScore, allRuleIds.OrderBy(r => r).ToList());
    }

    private async Task<DriftCheckResult> EvaluateDriftAsync(
        RadioPadDbContext db,
        IAuditLog audit,
        Guid tenantId,
        ProviderConfig provider,
        string rulebookId,
        int currentScore,
        List<string> currentRuleIds,
        int threshold,
        CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var providerIdStr = provider.Id.ToString();

        // Load or create baseline.
        var baseline = await db.DriftBaselines.FirstOrDefaultAsync(
            b => b.TenantId == tenantId
                 && b.ProviderId == providerIdStr
                 && b.RulebookId == rulebookId, ct);

        if (baseline is null)
        {
            // First run — establish baseline, no drift.
            baseline = new DriftBaseline
            {
                TenantId = tenantId,
                ProviderId = providerIdStr,
                RulebookId = rulebookId,
                QualityScore = currentScore,
                FindingRuleIdsJson = JsonSerializer.Serialize(currentRuleIds),
                CheckedAt = now,
            };
            db.DriftBaselines.Add(baseline);
            await db.SaveChangesAsync(ct);

            return new DriftCheckResult(
                providerIdStr, rulebookId,
                currentScore, currentScore, 0,
                new List<string>(), new List<string>(),
                now, DriftDetected: false);
        }

        var baselineScore = baseline.QualityScore;
        var baselineRuleIds = DeserializeRuleIds(baseline.FindingRuleIdsJson);

        int scoreDelta = baselineScore - currentScore; // positive = degradation
        var newBlockers = currentRuleIds.Except(baselineRuleIds).ToList();
        var resolved = baselineRuleIds.Except(currentRuleIds).ToList();
        bool driftDetected = scoreDelta >= threshold || newBlockers.Count > 0;

        if (driftDetected)
        {
            // Raise SystemAlert audit event.
            await audit.AppendAsync(new AuditEvent
            {
                TenantId = tenantId,
                Action = AuditAction.SystemAlert,
                DetailsJson = JsonSerializer.Serialize(new
                {
                    kind = "model_drift",
                    provider = provider.Name,
                    providerId = providerIdStr,
                    model = provider.Model,
                    rulebookId,
                    baselineQualityScore = baselineScore,
                    currentQualityScore = currentScore,
                    scoreDelta,
                    newBlockerRules = newBlockers,
                    resolvedRules = resolved,
                }),
            }, ct);

            _log.LogWarning(
                "Model drift detected for provider {Provider} on rulebook {Rulebook}: " +
                "score {Baseline} → {Current} (Δ{Delta}), {NewBlockers} new blocker rules",
                provider.Name, rulebookId, baselineScore, currentScore, scoreDelta, newBlockers.Count);
        }
        else
        {
            // Update baseline to current (last known-good).
            baseline.QualityScore = currentScore;
            baseline.FindingRuleIdsJson = JsonSerializer.Serialize(currentRuleIds);
            baseline.CheckedAt = now;
            baseline.UpdatedAt = now;
            await db.SaveChangesAsync(ct);
        }

        return new DriftCheckResult(
            providerIdStr, rulebookId,
            baselineScore, currentScore, scoreDelta,
            newBlockers, resolved,
            now, driftDetected);
    }

    /// <summary>
    /// Returns the latest drift check results for all provider/rulebook
    /// combinations within a tenant. Used by the admin status endpoint.
    /// </summary>
    public async Task<IReadOnlyList<object>> GetStatusAsync(Guid tenantId, CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RadioPadDbContext>();

        var baselines = await db.DriftBaselines
            .AsNoTracking()
            .Where(b => b.TenantId == tenantId)
            .OrderBy(b => b.RulebookId).ThenBy(b => b.ProviderId)
            .ToListAsync(ct);

        return baselines.Select(b => (object)new
        {
            b.Id,
            b.ProviderId,
            b.RulebookId,
            b.QualityScore,
            findingRuleIds = DeserializeRuleIds(b.FindingRuleIdsJson),
            b.CheckedAt,
            b.CreatedAt,
        }).ToList();
    }

    private static List<string> DeserializeRuleIds(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<List<string>>(json) ?? new List<string>();
        }
        catch
        {
            return new List<string>();
        }
    }

    private static Report ParseReport(JsonElement caseEl)
    {
        var el = caseEl.TryGetProperty("report", out var rEl) ? rEl : caseEl;
        var r = new Report();
        if (el.TryGetProperty("study", out var st))
        {
            r.Study.Modality = st.TryGetProperty("modality", out var m) ? (m.GetString() ?? "") : "";
            r.Study.BodyPart = st.TryGetProperty("bodyPart", out var b) ? (b.GetString() ?? "") : "";
            // Iter-36 — study-context Indication removed; map the sample's indication onto the report-body section.
            r.Indication = st.TryGetProperty("indication", out var i) ? (i.GetString() ?? "") : "";
            r.Study.AccessionNumber = st.TryGetProperty("accessionNumber", out var a) ? (a.GetString() ?? "") : "";
        }
        r.Indication = el.TryGetProperty("indication", out var ind) ? (ind.GetString() ?? "") : "";
        r.Technique = el.TryGetProperty("technique", out var tq) ? (tq.GetString() ?? "") : "";
        r.Comparison = el.TryGetProperty("comparison", out var cm) ? (cm.GetString() ?? "") : "";
        r.Findings = el.TryGetProperty("findings", out var fn) ? (fn.GetString() ?? "") : "";
        r.Impression = el.TryGetProperty("impression", out var ip) ? (ip.GetString() ?? "") : "";
        r.Recommendations = el.TryGetProperty("recommendations", out var rc) ? (rc.GetString() ?? "") : "";
        return r;
    }
}
