using System.Security.Cryptography;
using System.Text;
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
/// Iter-31/Iter-32 SEC-011 — periodic scan of the last 5 minutes of audit events
/// that raises alerts for known burst patterns. Each alert is recorded as an
/// append-only audit row (<see cref="AuditAction.SecurityAlert"/> for iter-32
/// patterns, <see cref="AuditAction.AnomalyDetected"/> for the legacy patterns
/// kept for back-compat), emitted to the structured log at
/// <see cref="LogLevel.Warning"/>, and (optionally) POSTed to
/// <c>RADIOPAD_SECURITY_WEBHOOK_URL</c> with an
/// <c>X-RadioPad-Signature: sha256=&lt;hex&gt;</c> HMAC header derived from
/// <c>RADIOPAD_SECURITY_WEBHOOK_SECRET</c>. The legacy
/// <c>RADIOPAD_ANOMALY_WEBHOOK_URL</c> is still honoured for back-compat.
/// Bodies are JSON-only and never include PHI; the webhook is fire-and-forget
/// so a down receiver cannot stall the loop.
///
/// Migrated from the former <c>AnomalyDetector</c> BackgroundService (PR-N1) to a
/// Hangfire recurring job (cron <c>* * * * *</c>, maintenance queue). Stateless
/// per pass — the 5-minute / 24-hour windows are recomputed from the DB each run,
/// so moving to Hangfire changes nothing. <see cref="ScanOnceAsync"/> stays public
/// so tests can drive a pass deterministically.
/// </summary>
public sealed class AnomalyScanJob
{
    private static readonly TimeSpan Window = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan BaselineWindow = TimeSpan.FromHours(24);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<AnomalyScanJob> _log;
    private readonly IHttpClientFactory _http;
    private readonly INotificationProducer _producer;

    public AnomalyScanJob(
        IServiceScopeFactory scopeFactory, ILogger<AnomalyScanJob> log, IHttpClientFactory http,
        INotificationProducer producer)
    { _scopeFactory = scopeFactory; _log = log; _http = http; _producer = producer; }

    /// <summary>
    /// Hangfire recurring entry point. Delegates to <see cref="ScanOnceAsync"/>;
    /// a dedicated entry keeps the AddOrUpdate registrations uniform across jobs.
    /// </summary>
    public Task RunRecurringAsync(CancellationToken ct) => ScanOnceAsync(ct);

    /// <summary>
    /// Single scan pass. Public so integration tests can drive it
    /// deterministically without waiting for the timer.
    /// </summary>
    public async Task ScanOnceAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RadioPadDbContext>();
        var audit = scope.ServiceProvider.GetRequiredService<IAuditLog>();

        var now = DateTimeOffset.UtcNow;
        var since = now - Window;

        // ---- Legacy iter-31 patterns (kept for back-compat with existing tests). ----

        // Provider-block burst per tenant (>100 in window).
        var providerBursts = await db.AuditEvents.AsNoTracking()
            .Where(a => a.CreatedAt >= since && a.Action == AuditAction.ProviderBlocked)
            .GroupBy(a => a.TenantId)
            .Select(g => new { TenantId = g.Key, Count = g.Count() })
            .Where(x => x.Count > 100)
            .ToListAsync(ct);

        foreach (var b in providerBursts)
        {
            await RaiseAsync(audit,
                tenantId: b.TenantId,
                level: LogLevel.Warning,
                action: AuditAction.AnomalyDetected,
                kind: "provider_blocked_burst",
                detailsJson: $"{{\"reason\":\"provider_blocked_burst\",\"count\":{b.Count},\"windowMinutes\":5}}",
                ct: ct);
        }

        // Policy-violation burst per user (>50 in window).
        var userBursts = await db.AuditEvents.AsNoTracking()
            .Where(a => a.CreatedAt >= since && a.Action == AuditAction.PolicyViolation && a.UserId != null)
            .GroupBy(a => new { a.TenantId, a.UserId })
            .Select(g => new { g.Key.TenantId, g.Key.UserId, Count = g.Count() })
            .Where(x => x.Count > 50)
            .ToListAsync(ct);

        foreach (var b in userBursts)
        {
            await RaiseAsync(audit,
                tenantId: b.TenantId,
                level: LogLevel.Warning,
                action: AuditAction.AnomalyDetected,
                kind: "policy_violation_burst",
                detailsJson: $"{{\"reason\":\"policy_violation_burst\",\"userId\":\"{b.UserId}\",\"count\":{b.Count},\"windowMinutes\":5}}",
                ct: ct);
        }

        // Audit-chain breakage (>5 ever for this tenant). Critical alert.
        var brokenChainBursts = await db.AuditEvents.AsNoTracking()
            .Where(a => a.Action == AuditAction.AnomalyDetected && a.DetailsJson.Contains("audit_chain_broken"))
            .GroupBy(a => a.TenantId)
            .Select(g => new { TenantId = g.Key, Count = g.Count() })
            .Where(x => x.Count > 5)
            .ToListAsync(ct);

        foreach (var b in brokenChainBursts)
        {
            await RaiseAsync(audit,
                tenantId: b.TenantId,
                level: LogLevel.Critical,
                action: AuditAction.AnomalyDetected,
                kind: "audit_chain_broken_burst",
                detailsJson: $"{{\"reason\":\"audit_chain_broken_burst\",\"count\":{b.Count}}}",
                ct: ct);
        }

        // ---- Iter-32 SEC-011 patterns (emit AuditAction.SecurityAlert). ----

        // (a) >50 ProviderBlocked from one user in the window.
        var providerBlockedByUser = await db.AuditEvents.AsNoTracking()
            .Where(a => a.CreatedAt >= since && a.Action == AuditAction.ProviderBlocked && a.UserId != null)
            .GroupBy(a => new { a.TenantId, a.UserId })
            .Select(g => new { g.Key.TenantId, g.Key.UserId, Count = g.Count() })
            .Where(x => x.Count > 50)
            .ToListAsync(ct);

        foreach (var b in providerBlockedByUser)
        {
            await RaiseAlertAsync(audit, b.TenantId, "provider_blocked_burst_by_user",
                $"{{\"reason\":\"provider_blocked_burst_by_user\",\"userId\":\"{b.UserId}\",\"count\":{b.Count},\"windowMinutes\":5}}",
                ct);
        }

        // (b) >20 PolicyViolation from one client-IP-hash in the window.
        // The IpAllowlistMiddleware records `clientIpHash` in details JSON;
        // we group on that string.
        var policyByIp = await db.AuditEvents.AsNoTracking()
            .Where(a => a.CreatedAt >= since && a.Action == AuditAction.PolicyViolation
                        && a.DetailsJson.Contains("clientIpHash"))
            .Select(a => new { a.TenantId, a.DetailsJson })
            .ToListAsync(ct);

        foreach (var grp in policyByIp.GroupBy(x => new { x.TenantId, IpHash = ExtractIpHash(x.DetailsJson) }))
        {
            if (string.IsNullOrEmpty(grp.Key.IpHash)) continue;
            var count = grp.Count();
            if (count <= 20) continue;
            await RaiseAlertAsync(audit, grp.Key.TenantId, "policy_violation_burst_by_ip",
                $"{{\"reason\":\"policy_violation_burst_by_ip\",\"clientIpHash\":\"{grp.Key.IpHash}\",\"count\":{count},\"windowMinutes\":5}}",
                ct);
        }

        // (c) >100 UserLogin failures from one user in the window.
        var loginFailures = await db.AuditEvents.AsNoTracking()
            .Where(a => a.CreatedAt >= since && a.Action == AuditAction.UserLogin && a.UserId != null
                        && a.DetailsJson.Contains("failure"))
            .GroupBy(a => new { a.TenantId, a.UserId })
            .Select(g => new { g.Key.TenantId, g.Key.UserId, Count = g.Count() })
            .Where(x => x.Count > 100)
            .ToListAsync(ct);

        foreach (var b in loginFailures)
        {
            await RaiseAlertAsync(audit, b.TenantId, "user_login_failure_burst",
                $"{{\"reason\":\"user_login_failure_burst\",\"userId\":\"{b.UserId}\",\"count\":{b.Count},\"windowMinutes\":5}}",
                ct);
        }

        // (d) sudden 10× spike in AI requests vs prior 24h baseline (per tenant).
        // Baseline = average per-5-min count over the previous 24h excluding
        // the current window. Alert when recent count ≥ max(20, 10 × baseline).
        var baselineSince = now - BaselineWindow;
        var aiCounts = await db.AuditEvents.AsNoTracking()
            .Where(a => a.CreatedAt >= baselineSince && a.Action == AuditAction.AiRequest)
            .Select(a => new { a.TenantId, a.CreatedAt })
            .ToListAsync(ct);

        var byTenant = aiCounts
            .GroupBy(x => x.TenantId)
            .Select(g => new
            {
                TenantId = g.Key,
                Recent = g.Count(x => x.CreatedAt >= since),
                Prior = g.Count(x => x.CreatedAt < since),
            });
        // 24h has 288 five-minute buckets; subtract the current window (1 bucket).
        const int priorBuckets = (24 * 60 / 5) - 1;
        foreach (var t in byTenant)
        {
            var baseline = t.Prior / (double)priorBuckets;
            // Require both an absolute floor (>=20) and a 10× spike to avoid
            // false positives at the cold-start edge where baseline is ~0.
            if (t.Recent >= 20 && t.Recent >= Math.Max(20, baseline * 10))
            {
                await RaiseAlertAsync(audit, t.TenantId, "ai_request_spike",
                    $"{{\"reason\":\"ai_request_spike\",\"recent\":{t.Recent},\"baselinePerWindow\":{baseline.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)},\"windowMinutes\":5}}",
                    ct);
            }
        }
    }

    private static string ExtractIpHash(string details)
    {
        try
        {
            using var doc = JsonDocument.Parse(details);
            if (doc.RootElement.TryGetProperty("clientIpHash", out var v) && v.ValueKind == JsonValueKind.String)
                return v.GetString() ?? "";
        }
        catch { }
        return "";
    }

    private async Task RaiseAlertAsync(IAuditLog audit, Guid tenantId, string kind, string detailsJson, CancellationToken ct)
    {
        await RaiseAsync(audit, tenantId, LogLevel.Warning, AuditAction.SecurityAlert, kind, detailsJson, ct);
    }

    private async Task RaiseAsync(IAuditLog audit, Guid tenantId, LogLevel level, AuditAction action, string kind, string detailsJson, CancellationToken ct)
    {
        await audit.AppendAsync(new AuditEvent
        {
            TenantId = tenantId,
            Action = action,
            DetailsJson = detailsJson,
        }, ct);
        _log.Log(level, "Anomaly detected: tenant={TenantId} kind={Kind} action={Action}", tenantId, kind, action);

        // NOTIF-001 (PR-N4) — surface the anomaly in-app to the tenant's SecurityManage holders.
        // Deduped per (tenant, kind, hour) so a persistent burst across minute passes does not
        // spam the inbox. A producer failure must never fail the scan — wrapped + logged.
        try
        {
            await _producer.NotifyPermissionHoldersAsync(
                tenantId, RbacPermission.SecurityManage, excludeUserId: null,
                uid => new NotificationDraft(
                    tenantId, uid, NotificationCategory.System, NotificationUrgency.Warning,
                    "Security anomaly detected", "A security anomaly was detected and needs review.",
                    "/admin/security", "system", null, RequiresAck: false,
                    DedupeKey: $"anomaly:{tenantId:N}:{kind}:{DateTimeOffset.UtcNow:yyyyMMddHH}"), ct);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "anomaly notification fan-out failed for tenant {TenantId} kind {Kind}", tenantId, kind);
        }

        var url = Environment.GetEnvironmentVariable("RADIOPAD_SECURITY_WEBHOOK_URL")
            ?? Environment.GetEnvironmentVariable("RADIOPAD_ANOMALY_WEBHOOK_URL");
        if (string.IsNullOrWhiteSpace(url)) return;

        try
        {
            using var client = _http.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(5);
            var body = $"{{\"tenantId\":\"{tenantId}\",\"kind\":\"{kind}\",\"level\":\"{level}\",\"action\":\"{action}\",\"details\":{detailsJson}}}";
            using var content = new StringContent(body, Encoding.UTF8, "application/json");

            // Iter-32 SEC-011 — HMAC-SHA256 signature so the receiver can
            // verify the alert came from RadioPad. The secret is never
            // echoed back in the body or logs.
            var secret = Environment.GetEnvironmentVariable("RADIOPAD_SECURITY_WEBHOOK_SECRET");
            if (!string.IsNullOrWhiteSpace(secret))
            {
                using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
                var sig = Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(body))).ToLowerInvariant();
                content.Headers.Add("X-RadioPad-Signature", $"sha256={sig}");
            }
            using var resp = await client.PostAsync(url, content, ct);
            // Fire-and-forget; we do not retry. A down receiver must not stall the loop.
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Anomaly webhook POST failed (non-fatal): {Url}", url);
        }
    }
}
