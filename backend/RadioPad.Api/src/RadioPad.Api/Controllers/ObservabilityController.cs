using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using RadioPad.Api.Services;
using RadioPad.Application.Abstractions;
using RadioPad.Application.Security;
using RadioPad.Domain.Entities;
using RadioPad.Domain.Enums;
using RadioPad.Infrastructure.Persistence;

namespace RadioPad.Api.Controllers;

/// <summary>
/// Iter-33 PERF-004 — admin-only Alertmanager webhook ingester. Posts
/// from the Alertmanager (or any compatible source — Grafana OnCall,
/// Datadog, etc.) are appended to the audit log as
/// <see cref="AuditAction.SystemAlert"/> events so the SOC has a single
/// pane of glass. The endpoint is RBAC-gated (ItAdmin / MedicalDirector
/// / ComplianceReviewer) since alerts may carry operational PII.
/// </summary>
[ApiController]
[Route("api/admin/observability")]
public class ObservabilityController : TenantedController
{
    private readonly RadioPadDbContext _db;
    private readonly IAuditLog _audit;
    private readonly IAvailabilitySnapshotProvider _availability;

    public ObservabilityController(
        RadioPadDbContext db,
        IAuditLog audit,
        IAvailabilitySnapshotProvider availability)
    {
        _db = db;
        _audit = audit;
        _availability = availability;
    }

    [HttpPost("slo-alerts")]
    public async Task<IActionResult> IngestSloAlert([FromBody] JsonElement payload, CancellationToken ct)
    {
        var (tenant, user) = await ResolveContextAsync(_db, ct);
        var deny = RequirePermission(user, RbacPermission.SecurityManage);
        if (deny is not null) return deny;

        // Extract a small, non-PII summary for the audit row. The full payload is hashed.
        string status = TryGetString(payload, "status") ?? "(unknown)";
        string receiver = TryGetString(payload, "receiver") ?? "(unknown)";
        int alertCount = 0;
        var alertNames = new List<string>();
        if (payload.ValueKind == JsonValueKind.Object && payload.TryGetProperty("alerts", out var alerts) && alerts.ValueKind == JsonValueKind.Array)
        {
            alertCount = alerts.GetArrayLength();
            foreach (var a in alerts.EnumerateArray())
            {
                if (a.TryGetProperty("labels", out var labels) && labels.TryGetProperty("alertname", out var name) && name.ValueKind == JsonValueKind.String)
                {
                    var s = name.GetString();
                    if (!string.IsNullOrEmpty(s) && !alertNames.Contains(s)) alertNames.Add(s);
                }
            }
        }
        var raw = payload.ValueKind == JsonValueKind.Undefined ? "{}" : payload.GetRawText();
        var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(raw))).ToLowerInvariant();

        await _audit.AppendAsync(new AuditEvent
        {
            TenantId = tenant.Id,
            UserId = user.Id,
            Action = AuditAction.SystemAlert,
            DetailsJson = JsonSerializer.Serialize(new
            {
                source = "alertmanager_webhook",
                status,
                receiver,
                alertCount,
                alertNames,
                payloadSha256 = hash,
                payloadBytes = raw.Length,
            }),
        }, ct);

        return Ok(new { accepted = true, alertCount, hash });
    }

    private static string? TryGetString(JsonElement el, string prop)
    {
        if (el.ValueKind != JsonValueKind.Object) return null;
        if (!el.TryGetProperty(prop, out var v)) return null;
        return v.ValueKind == JsonValueKind.String ? v.GetString() : null;
    }

    /// <summary>
    /// Iter-35 PERF-004 — admin-only read of the in-process synthetic
    /// availability monitor. Returns the rolling 5-minute snapshot computed
    /// by <see cref="AvailabilityMonitorService"/>.
    /// </summary>
    [HttpGet("availability")]
    public async Task<IActionResult> GetAvailability(CancellationToken ct)
    {
        var (_, user) = await ResolveContextAsync(_db, ct);
        var deny = RequirePermission(user, RbacPermission.SecurityManage);
        if (deny is not null) return deny;

        var snap = _availability.Current;
        return Ok(new
        {
            windowSec = snap.WindowSec,
            totalProbes = snap.TotalProbes,
            errorCount = snap.ErrorCount,
            errorRate = snap.ErrorRate,
            lastCheckedAt = snap.LastCheckedAt,
            targets = snap.Targets,
        });
    }
}
