using System.Net.Http;
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
/// PR-N2 — outbound webhook delivery (default queue, enqueue-only — NOT recurring). One job
/// is enqueued per (endpoint, event) by the <c>WebhookEnqueueingAuditLog</c> decorator after
/// an audit append. The payload is PHI-minimized (NOTIF-004): audit deliveries carry only
/// <c>{ id, action, tenantId, createdAt, integrityChain }</c> — never <c>DetailsJson</c> or
/// any clinical text — and are signed with <c>X-RadioPad-Signature: sha256=&lt;hex&gt;</c>
/// HMAC over the raw body using the endpoint's secret (mirroring the
/// <c>TenantSettings.FhirWebhookSecret</c> convention).
///
/// Retry/DLQ: a non-2xx response throws → the global <c>JitteredRetryAttribute</c> retries →
/// Hangfire's Failed set is the DLQ. Delivery is at-least-once (receivers dedupe on <c>id</c>).
/// Each failed attempt increments <see cref="TenantWebhookEndpoint.FailureCount"/>; a 2xx
/// resets it to 0. At 20 consecutive failures the endpoint auto-disables
/// (<see cref="TenantWebhookEndpoint.DisabledAt"/> set, <c>Active=false</c>, audited
/// <see cref="AuditAction.WebhookEndpointDisabled"/>) and delivery is abandoned.
///
/// NOTE (PR-N2 scope): <c>DeliverNotificationAsync</c> is intentionally NOT implemented here —
/// the Notification entity does not exist yet. It lands with the notification producers (PR-N4).
///
/// Registered as a singleton (holds only <see cref="IServiceScopeFactory"/> + logger and opens
/// its own scope per delivery). Skipped under Testing where Hangfire is not started — tests
/// drive <see cref="DeliverAuditEventAsync"/> directly.
/// </summary>
[Queue(HangfireSetup.QueueDefault)]
public sealed class WebhookDispatchJob
{
    /// <summary>Consecutive-failure threshold at which an endpoint auto-disables.</summary>
    public const int DisableThreshold = 20;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<WebhookDispatchJob> _log;

    public WebhookDispatchJob(IServiceScopeFactory scopeFactory, ILogger<WebhookDispatchJob> log)
    {
        _scopeFactory = scopeFactory;
        _log = log;
    }

    /// <summary>
    /// Delivers one audit event to one endpoint. Throws on a non-2xx response so the global
    /// jittered-retry filter re-runs it; a disabled/deleted endpoint or a vanished event is a
    /// silent no-op.
    /// </summary>
    public async Task DeliverAuditEventAsync(Guid endpointId, Guid auditEventId, CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RadioPadDbContext>();

        var endpoint = await db.TenantWebhookEndpoints.FirstOrDefaultAsync(e => e.Id == endpointId, ct);
        if (endpoint is null || !endpoint.Active) return;

        // Tenant isolation: the event MUST belong to the endpoint's tenant.
        var evt = await db.AuditEvents
            .AsNoTracking()
            .FirstOrDefaultAsync(e => e.Id == auditEventId && e.TenantId == endpoint.TenantId, ct);
        if (evt is null) return;

        // PHI-minimized payload — ids, action, timestamp, integrity hash only. Never DetailsJson.
        var body = JsonSerializer.Serialize(new
        {
            id = evt.Id,
            action = evt.Action.ToString(),
            tenantId = evt.TenantId,
            createdAt = evt.CreatedAt.ToString("o"),
            integrityChain = evt.IntegrityChain,
        });

        var signature = "sha256=" + HmacHex(endpoint.Secret, body);

        try
        {
            var http = scope.ServiceProvider.GetRequiredService<IHttpClientFactory>().CreateClient();
            using var request = new HttpRequestMessage(HttpMethod.Post, endpoint.Url)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            };
            request.Headers.TryAddWithoutValidation("X-RadioPad-Signature", signature);

            using var resp = await http.SendAsync(request, ct);
            if (!resp.IsSuccessStatusCode)
                throw new HttpRequestException(
                    $"Webhook delivery to endpoint {endpointId} failed with status {(int)resp.StatusCode}.");

            // Success — clear any accumulated failures.
            if (endpoint.FailureCount != 0)
            {
                endpoint.FailureCount = 0;
                endpoint.UpdatedAt = DateTimeOffset.UtcNow;
                await db.SaveChangesAsync(ct);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await RecordFailureAndMaybeDisableAsync(scope, db, endpoint, ex, ct);
            throw; // surface to Hangfire so the jittered-retry filter re-runs the delivery
        }
    }

    /// <summary>
    /// Increments the endpoint's failure count and, at <see cref="DisableThreshold"/>
    /// consecutive failures, auto-disables it and appends a
    /// <see cref="AuditAction.WebhookEndpointDisabled"/> audit row (PHI-free — endpoint id and
    /// count only). Kept internal so the accounting is directly unit-testable.
    /// </summary>
    private async Task RecordFailureAndMaybeDisableAsync(
        IServiceScope scope, RadioPadDbContext db, TenantWebhookEndpoint endpoint, Exception ex, CancellationToken ct)
    {
        endpoint.FailureCount += 1;
        endpoint.UpdatedAt = DateTimeOffset.UtcNow;

        var disabling = endpoint.FailureCount >= DisableThreshold && endpoint.DisabledAt is null;
        if (disabling)
        {
            endpoint.DisabledAt = DateTimeOffset.UtcNow;
            endpoint.Active = false;
        }
        await db.SaveChangesAsync(ct);

        _log.LogWarning(ex,
            "Webhook delivery to endpoint {EndpointId} failed (failureCount={FailureCount}{Disabled}).",
            endpoint.Id, endpoint.FailureCount, disabling ? ", auto-disabled" : "");

        if (disabling)
        {
            var audit = scope.ServiceProvider.GetRequiredService<IAuditLog>();
            await audit.AppendAsync(new AuditEvent
            {
                TenantId = endpoint.TenantId,
                Action = AuditAction.WebhookEndpointDisabled,
                DetailsJson = JsonSerializer.Serialize(new
                {
                    endpointId = endpoint.Id,
                    failureCount = endpoint.FailureCount,
                }),
            }, ct);
        }
    }

    private static string HmacHex(string secret, string body)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret ?? ""));
        return Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(body))).ToLowerInvariant();
    }
}
