using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using RadioPad.Application.Abstractions;
using RadioPad.Domain.Entities;
using RadioPad.Domain.Enums;

namespace RadioPad.Application.Services;

/// <summary>
/// PRD BILL-001..006 — writes <see cref="AuditAction.BillingChanged"/> rows
/// with PII (emails, Stripe customer ids, payment intent ids, subscription
/// ids) replaced by `sha16:&lt;hex&gt;` so the audit log never carries raw
/// billing identifiers.
/// </summary>
public interface IBillingAudit
{
    Task AppendAsync(Guid tenantId, Guid? userId, string action, object detail, CancellationToken ct);
}

public sealed class BillingAudit : IBillingAudit
{
    private static readonly HashSet<string> SensitiveKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "stripeCustomerId",
        "customerId",
        "email",
        "invoiceId",
        "paymentIntentId",
        "subscriptionId",
        "stripeConnectAccountId",
        "connectAccountId",
    };

    private readonly IAuditLog _audit;

    public BillingAudit(IAuditLog audit) => _audit = audit;

    public async Task AppendAsync(Guid tenantId, Guid? userId, string action, object detail, CancellationToken ct)
    {
        var node = JsonSerializer.SerializeToNode(detail) as JsonObject ?? new JsonObject();
        Sanitize(node);
        node["action"] = action;
        var json = node.ToJsonString();

        await _audit.AppendAsync(new AuditEvent
        {
            TenantId = tenantId,
            UserId = userId,
            Action = AuditAction.BillingChanged,
            DetailsJson = json,
        }, ct);
    }

    private static void Sanitize(JsonNode? node)
    {
        switch (node)
        {
            case JsonObject obj:
                foreach (var kvp in obj.ToList())
                {
                    if (SensitiveKeys.Contains(kvp.Key) && kvp.Value is not null)
                    {
                        var raw = kvp.Value.ToString();
                        obj[kvp.Key] = string.IsNullOrEmpty(raw) ? "" : $"sha16:{Hash16(raw)}";
                    }
                    else
                    {
                        Sanitize(kvp.Value);
                    }
                }
                break;
            case JsonArray arr:
                foreach (var item in arr) Sanitize(item);
                break;
        }
    }

    private static string Hash16(string input)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(bytes, 0, 8).ToLowerInvariant();
    }
}
