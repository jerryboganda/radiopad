using System.Globalization;
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
/// is the outer gate. When the request resolves a tenant context after
/// bearer/cookie/OIDC identity projection (or from the public magic-link
/// request body), the per-tenant allowlist (<see cref="TenantSettings.IpAllowlistJson"/>
/// JSON array of CIDRs, falling back to the legacy CSV in
/// <see cref="TenantSettings.IpAllowlistCidr"/>) is AND-ed with the global
/// gate — both must match.
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
    private static AllowlistRanges _ranges;
    private static string? _cachedRaw;

    public IpAllowlistMiddleware(RequestDelegate next, ILogger<IpAllowlistMiddleware> log)
    { _next = next; _log = log; }

    public async Task InvokeAsync(HttpContext ctx, RadioPadDbContext db, IAuditLog audit)
    {
        var raw = Environment.GetEnvironmentVariable("RADIOPAD_IP_ALLOWLIST");
        var globalRanges = ResolveRanges(raw);
        if (globalRanges.Configured && !globalRanges.Valid)
        {
            _log.LogError("Global IP allowlist is configured but invalid; failing closed.");
            await DenyInvalidAllowlistAsync(ctx, "global");
            return;
        }

        var remote = ResolveRemoteIp(ctx);
        if (remote is null) { await _next(ctx); return; }
        if (IPAddress.IsLoopback(remote)) { await _next(ctx); return; }

        if (globalRanges.Ranges.Length > 0 && !MatchAny(remote, globalRanges.Ranges))
        {
            await BlockAsync(ctx, audit, remote, tenantId: null, scope: "global");
            return;
        }

        var tenantSlug = await ResolveTenantSlugAsync(ctx);
        if (!string.IsNullOrWhiteSpace(tenantSlug))
        {
            try
            {
                var tenant = await db.Tenants.AsNoTracking().FirstOrDefaultAsync(t => t.Slug == tenantSlug);
                if (tenant is not null)
                {
                    var settings = await db.TenantSettings.AsNoTracking().FirstOrDefaultAsync(s => s.TenantId == tenant.Id);
                    var perTenantRanges = ParseTenantRanges(settings);
                    if (perTenantRanges.Configured && !perTenantRanges.Valid)
                    {
                        _log.LogError("Per-tenant IP allowlist for tenant {TenantId} is configured but invalid; failing closed.", tenant.Id);
                        await DenyInvalidAllowlistAsync(ctx, "tenant");
                        return;
                    }
                    if (perTenantRanges.Ranges.Length > 0 && !MatchAny(remote, perTenantRanges.Ranges))
                    {
                        await BlockAsync(ctx, audit, remote, tenantId: tenant.Id, scope: "tenant");
                        return;
                    }
                }
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "Per-tenant IP allowlist lookup failed; failing closed.");
                await DenyTenantLookupFailureAsync(ctx);
                return;
            }
        }

        await _next(ctx);
    }

    private static async Task DenyTenantLookupFailureAsync(HttpContext ctx)
    {
        ctx.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
        ctx.Response.ContentType = "application/problem+json";
        await ctx.Response.WriteAsJsonAsync(new
        {
            type = "https://radiopad.dev/errors/ip-allowlist-unavailable",
            title = "Tenant allowlist unavailable",
            status = 503,
            kind = "ip_allowlist_unavailable",
        });
    }

    private static async Task DenyInvalidAllowlistAsync(HttpContext ctx, string scope)
    {
        ctx.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
        ctx.Response.ContentType = "application/problem+json";
        await ctx.Response.WriteAsJsonAsync(new
        {
            type = "https://radiopad.dev/errors/ip-allowlist-invalid",
            title = "IP allowlist is invalid",
            status = 503,
            kind = "ip_allowlist_invalid",
            scope,
        });
    }

    /// <summary>
    /// Resolve the client IP. By default this is the TCP peer
    /// (<see cref="ConnectionInfo.RemoteIpAddress"/>). When the operator
    /// explicitly opts in via <c>RADIOPAD_TRUST_FORWARDED_FOR=1</c>, the
    /// left-most address in <c>X-Forwarded-For</c> wins instead. In
    /// Production, the immediate peer must also match
    /// <c>RADIOPAD_TRUSTED_PROXY_CIDRS</c>; otherwise the TCP peer is used.
    /// </summary>
    public static IPAddress? ResolveRemoteIp(HttpContext ctx)
    {
        var peer = ctx.Connection.RemoteIpAddress;
        var trust = Environment.GetEnvironmentVariable("RADIOPAD_TRUST_FORWARDED_FOR");
        if (string.Equals(trust, "1", StringComparison.Ordinal))
        {
            var trustedProxies = ParseCsvRanges(Environment.GetEnvironmentVariable("RADIOPAD_TRUSTED_PROXY_CIDRS")).Ranges;
            if (trustedProxies.Length > 0)
            {
                if (peer is null || (!IPAddress.IsLoopback(peer) && !MatchAny(peer, trustedProxies)))
                    return peer;
            }
            else if (string.Equals(Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT"), "Production", StringComparison.OrdinalIgnoreCase))
            {
                return peer;
            }

            var xff = ctx.Request.Headers["X-Forwarded-For"].FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(xff))
            {
                var first = xff.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
                if (first is not null && IPAddress.TryParse(first, out var parsed)) return parsed;
            }
        }
        return peer;
    }

    private static async Task<string?> ResolveTenantSlugAsync(HttpContext ctx)
    {
        if (!ctx.Request.Path.StartsWithSegments("/api/auth/magic-link/request") ||
            !HttpMethods.IsPost(ctx.Request.Method))
        {
            return ctx.Request.Headers["X-RadioPad-Tenant"].FirstOrDefault();
        }

        if (ctx.Request.ContentLength is > 8192) return null;

        ctx.Request.EnableBuffering(bufferThreshold: 4096, bufferLimit: 8192);
        using var reader = new StreamReader(
            ctx.Request.Body,
            Encoding.UTF8,
            detectEncodingFromByteOrderMarks: false,
            bufferSize: 1024,
            leaveOpen: true);
        var body = await reader.ReadToEndAsync(ctx.RequestAborted);
        ctx.Request.Body.Position = 0;
        if (string.IsNullOrWhiteSpace(body)) return null;

        try
        {
            using var doc = JsonDocument.Parse(body);
            return doc.RootElement.TryGetProperty("tenant", out var tenant) && tenant.ValueKind == JsonValueKind.String
                ? tenant.GetString()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static AllowlistRanges ParseTenantRanges(TenantSettings? settings)
    {
        if (settings is null) return AllowlistRanges.Unconfigured;
        // Prefer the JSON column when populated; fall back to the legacy CSV.
        var fromJson = ParseJsonRanges(settings.IpAllowlistJson);
        return fromJson.Configured ? fromJson : ParseCsvRanges(settings.IpAllowlistCidr);
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

    internal static AllowlistRanges ResolveRanges(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return AllowlistRanges.Unconfigured;
        if (!string.Equals(raw, _cachedRaw, StringComparison.Ordinal)) { _ranges = ParseCsvRanges(raw); _cachedRaw = raw; }
        return _ranges;
    }

    private static AllowlistRanges ParseCsvRanges(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return AllowlistRanges.Unconfigured;
        var list = new List<(IPAddress, int)>();
        foreach (var token in raw.Split(new[] { ',', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!TryParseCidr(token, out var entry)) return AllowlistRanges.Invalid;
            list.Add(entry);
        }
        return AllowlistRanges.ValidRanges(list.ToArray());
    }

    private static AllowlistRanges ParseJsonRanges(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return AllowlistRanges.Unconfigured;
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return AllowlistRanges.Invalid;
            var list = new List<(IPAddress, int)>();
            foreach (var el in doc.RootElement.EnumerateArray())
            {
                if (el.ValueKind != JsonValueKind.String) return AllowlistRanges.Invalid;
                var s = el.GetString();
                if (!TryParseCidr(s, out var entry)) return AllowlistRanges.Invalid;
                list.Add(entry);
            }
            return AllowlistRanges.ValidRanges(list.ToArray());
        }
        catch
        {
            return AllowlistRanges.Invalid;
        }
    }

    internal readonly record struct AllowlistRanges(
        bool Configured,
        bool Valid,
        (IPAddress, int)[] Ranges)
    {
        public static AllowlistRanges Unconfigured { get; } = new(false, true, Array.Empty<(IPAddress, int)>());
        public static AllowlistRanges Invalid { get; } = new(true, false, Array.Empty<(IPAddress, int)>());
        public static AllowlistRanges ValidRanges((IPAddress, int)[] ranges) => new(true, true, ranges);
    }

    private static bool TryParseCidr(string? token, out (IPAddress Net, int Prefix) entry)
    {
        entry = default;
        if (string.IsNullOrWhiteSpace(token)) return false;
        var trimmed = token.Trim();
        var slashIndex = trimmed.IndexOf('/');
        if (slashIndex >= 0 && trimmed.IndexOf('/', slashIndex + 1) >= 0) return false;
        var addressPart = slashIndex >= 0 ? trimmed[..slashIndex] : trimmed;
        var prefixPart = slashIndex >= 0 ? trimmed[(slashIndex + 1)..] : null;
        if (string.IsNullOrWhiteSpace(addressPart)) return false;
        if (!IPAddress.TryParse(addressPart, out var net)) return false;
        var defaultPrefix = net.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork ? 32 : 128;
        var prefix = defaultPrefix;
        if (prefixPart is not null && !int.TryParse(prefixPart, NumberStyles.None, CultureInfo.InvariantCulture, out prefix)) return false;
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
