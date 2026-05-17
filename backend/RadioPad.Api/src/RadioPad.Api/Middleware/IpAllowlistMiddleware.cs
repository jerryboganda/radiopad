using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RadioPad.Application.Abstractions;
using RadioPad.Api.Auth;
using RadioPad.Domain.Entities;
using RadioPad.Domain.Enums;
using RadioPad.Infrastructure.Persistence;

namespace RadioPad.Api.Middleware;

/// <summary>
/// PRD SEC-007 / Iter-32 SEC-008 — IP allowlist for the API. The global
/// envvar <c>RADIOPAD_IP_ALLOWLIST</c> (CSV or newline-separated CIDR list)
/// is the outer gate. When the request resolves a tenant context (via verified
/// identity, or explicit dev/test headers), the per-tenant allowlist
/// (<see cref="TenantSettings.IpAllowlistJson"/> JSON array of CIDRs, falling
/// back to the legacy CSV in <see cref="TenantSettings.IpAllowlistCidr"/>) is
/// AND-ed with the global gate — both must match.
///
/// Loopback (<c>127.0.0.1</c>, <c>::1</c>) is ALWAYS allowed because the API
/// binds <c>127.0.0.1</c> by default. <c>X-Forwarded-For</c> is honoured ONLY
/// when <c>RADIOPAD_TRUST_FORWARDED_FOR=1</c> is set (default off) — without
/// that toggle, an attacker behind a misconfigured proxy could spoof the
/// remote IP and bypass the allowlist.
///
/// On block, the middleware writes a <c>PolicyViolation</c> audit row with
/// <c>reason: "ip_not_allowed"</c> and a SHA-256-hashed client IP (we never
/// log raw IPs in the audit chain) and returns RFC-7807 problem+json with
/// <c>kind = "ip_not_allowed"</c>.
/// </summary>
public sealed class IpAllowlistMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<IpAllowlistMiddleware> _log;
    private static (IPAddress Network, int Prefix)[]? _ranges;
    private static string? _cachedRaw;

    public IpAllowlistMiddleware(RequestDelegate next, ILogger<IpAllowlistMiddleware> log)
    { _next = next; _log = log; }

    public async Task InvokeAsync(HttpContext ctx, RadioPadDbContext db, IAuditLog audit)
    {
        var raw = Environment.GetEnvironmentVariable("RADIOPAD_IP_ALLOWLIST");
        var globalRanges = ResolveRanges(raw);

        var remote = ResolveRemoteIp(ctx);
        if (remote is null) { await _next(ctx); return; }
        if (IPAddress.IsLoopback(remote)) { await _next(ctx); return; }

        if (globalRanges is { Length: > 0 } && !MatchAny(remote, globalRanges))
        {
            await BlockAsync(ctx, audit, remote, tenantId: null, scope: "global");
            return;
        }

        var tenantSlug = RadioPadRequestIdentity.TenantSlugOrDevHeader(ctx);
        if (!string.IsNullOrWhiteSpace(tenantSlug))
        {
            try
            {
                var tenant = await db.Tenants.AsNoTracking().FirstOrDefaultAsync(t => t.Slug == tenantSlug);
                if (tenant is not null)
                {
                    var settings = await db.TenantSettings.AsNoTracking().FirstOrDefaultAsync(s => s.TenantId == tenant.Id);
                    var perTenantRanges = ParseTenantRanges(settings);
                    if (perTenantRanges.Length > 0 && !MatchAny(remote, perTenantRanges))
                    {
                        await BlockAsync(ctx, audit, remote, tenantId: tenant.Id, scope: "tenant");
                        return;
                    }
                }
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "Per-tenant IP allowlist lookup failed; falling through to global gate.");
            }
        }

        await _next(ctx);
    }

    /// <summary>
    /// Resolve the client IP. By default this is the TCP peer
    /// (<see cref="ConnectionInfo.RemoteIpAddress"/>). When the operator
    /// explicitly opts in via <c>RADIOPAD_TRUST_FORWARDED_FOR=1</c>, the
    /// left-most address in <c>X-Forwarded-For</c> wins instead. We never
    /// trust XFF unless the operator has configured it; otherwise an
    /// attacker could spoof their way past the allowlist with a bogus header.
    /// </summary>
    public static IPAddress? ResolveRemoteIp(HttpContext ctx)
    {
        var trust = Environment.GetEnvironmentVariable("RADIOPAD_TRUST_FORWARDED_FOR");
        if (string.Equals(trust, "1", StringComparison.Ordinal))
        {
            var xff = ctx.Request.Headers["X-Forwarded-For"].FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(xff))
            {
                var first = xff.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
                if (first is not null && IPAddress.TryParse(first, out var parsed)) return parsed;
            }
        }
        return ctx.Connection.RemoteIpAddress;
    }

    private static (IPAddress, int)[] ParseTenantRanges(TenantSettings? settings)
    {
        if (settings is null) return Array.Empty<(IPAddress, int)>();
        // Prefer the JSON column when populated; fall back to the legacy CSV.
        var fromJson = ParseJsonRanges(settings.IpAllowlistJson);
        return fromJson.Length > 0 ? fromJson : ParseRanges(settings.IpAllowlistCidr);
    }

    private async Task BlockAsync(HttpContext ctx, IAuditLog audit, IPAddress remote, Guid? tenantId, string scope)
    {
        var hashed = HashIp(remote);
        _log.LogWarning("Blocked client (hashed={Hashed}) — not in {Scope} IP allowlist.", hashed, scope);
        if (tenantId is Guid tid)
        {
            try
            {
                await audit.AppendAsync(new AuditEvent
                {
                    TenantId = tid,
                    Action = AuditAction.PolicyViolation,
                    DetailsJson = $"{{\"reason\":\"ip_not_allowed\",\"scope\":\"{scope}\",\"clientIpHash\":\"{hashed}\"}}",
                }, ctx.RequestAborted);
            }
            catch { /* audit failure must not leak as a 500 here */ }
        }
        ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
        ctx.Response.ContentType = "application/problem+json";
        await ctx.Response.WriteAsJsonAsync(new
        {
            type = "https://radiopad.dev/errors/ip-not-allowed",
            title = "IP address not allowed",
            status = 403,
            kind = "ip_not_allowed",
            scope,
        });
    }

    internal static string HashIp(IPAddress addr)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(addr.ToString()));
        return Convert.ToHexString(bytes, 0, 8).ToLowerInvariant();
    }

    internal static (IPAddress, int)[]? ResolveRanges(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        if (!ReferenceEquals(raw, _cachedRaw)) { _ranges = ParseRanges(raw); _cachedRaw = raw; }
        return _ranges;
    }

    private static (IPAddress, int)[] ParseRanges(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return Array.Empty<(IPAddress, int)>();
        var list = new List<(IPAddress, int)>();
        foreach (var token in raw.Split(new[] { ',', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (TryParseCidr(token, out var entry)) list.Add(entry);
        }
        return list.ToArray();
    }

    private static (IPAddress, int)[] ParseJsonRanges(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return Array.Empty<(IPAddress, int)>();
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return Array.Empty<(IPAddress, int)>();
            var list = new List<(IPAddress, int)>();
            foreach (var el in doc.RootElement.EnumerateArray())
            {
                if (el.ValueKind != JsonValueKind.String) continue;
                var s = el.GetString();
                if (TryParseCidr(s, out var entry)) list.Add(entry);
            }
            return list.ToArray();
        }
        catch
        {
            return Array.Empty<(IPAddress, int)>();
        }
    }

    private static bool TryParseCidr(string? token, out (IPAddress Net, int Prefix) entry)
    {
        entry = default;
        if (string.IsNullOrWhiteSpace(token)) return false;
        var trimmed = token.Trim();
        var parts = trimmed.Split('/');
        if (!IPAddress.TryParse(parts[0], out var net)) return false;
        var defaultPrefix = net.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork ? 32 : 128;
        int prefix = parts.Length == 2 && int.TryParse(parts[1], out var p) ? p : defaultPrefix;
        if (prefix < 0 || prefix > defaultPrefix) return false;
        entry = (net, prefix);
        return true;
    }

    internal static bool MatchAny(IPAddress addr, (IPAddress, int)[] ranges)
    {
        foreach (var (net, prefix) in ranges)
        {
            if (Match(addr, net, prefix)) return true;
        }
        return false;
    }

    private static bool Match(IPAddress addr, IPAddress net, int prefix)
    {
        if (addr.AddressFamily != net.AddressFamily) return false;
        var a = addr.GetAddressBytes();
        var n = net.GetAddressBytes();
        int full = prefix / 8;
        int rem = prefix % 8;
        for (int i = 0; i < full; i++) if (a[i] != n[i]) return false;
        if (rem == 0) return true;
        int mask = 0xFF << (8 - rem);
        return (a[full] & mask) == (n[full] & mask);
    }
}
