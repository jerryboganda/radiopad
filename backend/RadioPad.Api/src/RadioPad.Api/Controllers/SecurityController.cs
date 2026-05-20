using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using RadioPad.Application.Abstractions;
using RadioPad.Application.Security;
using RadioPad.Domain.Entities;
using RadioPad.Domain.Enums;
using RadioPad.Infrastructure.Persistence;

namespace RadioPad.Api.Controllers;

/// <summary>
/// Admin security utility endpoints. These are tenant-scoped and RBAC-gated;
/// they never return secrets or PHI.
/// </summary>
[ApiController]
[Route("api/admin/security")]
public class SecurityController : TenantedController
{
    private readonly RadioPadDbContext _db;
    private readonly IAuditLog _audit;
    private readonly IHttpClientFactory _http;

    public SecurityController(RadioPadDbContext db, IAuditLog audit, IHttpClientFactory http)
    {
        _db = db;
        _audit = audit;
        _http = http;
    }

    [HttpPost("test-webhook")]
    public async Task<IActionResult> TestSecurityWebhook(CancellationToken ct)
    {
        var (tenant, user) = await ResolveContextAsync(_db, ct);
        var deny = RequirePermission(user, RbacPermission.SecurityManage);
        if (deny is not null) return deny;

        var url = Environment.GetEnvironmentVariable("RADIOPAD_SECURITY_WEBHOOK_URL")
            ?? Environment.GetEnvironmentVariable("RADIOPAD_ANOMALY_WEBHOOK_URL");
        if (string.IsNullOrWhiteSpace(url))
        {
            return Ok(new { sent = false, configured = false, statusCode = (int?)null });
        }

        var details = new
        {
            reason = "manual_security_webhook_test",
            source = "admin_security_page",
            requestedByUserId = user.Id,
        };
        var body = JsonSerializer.Serialize(new
        {
            tenantId = tenant.Id,
            kind = "manual_security_webhook_test",
            level = "Warning",
            action = AuditAction.SecurityAlert.ToString(),
            details,
        });
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(body))).ToLowerInvariant();

        using var client = _http.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(5);
        using var content = new StringContent(body, Encoding.UTF8, "application/json");
        var secret = Environment.GetEnvironmentVariable("RADIOPAD_SECURITY_WEBHOOK_SECRET");
        if (!string.IsNullOrWhiteSpace(secret))
        {
            using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
            var signature = Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(body))).ToLowerInvariant();
            content.Headers.Add("X-RadioPad-Signature", $"sha256={signature}");
        }

        int? statusCode = null;
        var sent = false;
        string? errorType = null;
        try
        {
            using var response = await client.PostAsync(url, content, ct);
            statusCode = (int)response.StatusCode;
            sent = response.IsSuccessStatusCode;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            errorType = "Timeout";
        }
        catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException or UriFormatException)
        {
            errorType = ex.GetType().Name;
        }

        await _audit.AppendAsync(new AuditEvent
        {
            TenantId = tenant.Id,
            UserId = user.Id,
            Action = AuditAction.SecurityAlert,
            DetailsJson = JsonSerializer.Serialize(new
            {
                reason = "manual_security_webhook_test",
                webhookStatus = statusCode,
                errorType,
                payloadSha256 = hash,
            }),
        }, ct);

        return Ok(new { sent, configured = true, statusCode });
    }
}
