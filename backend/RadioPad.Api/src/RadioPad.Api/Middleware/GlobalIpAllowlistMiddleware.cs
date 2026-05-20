using System.Net;

namespace RadioPad.Api.Middleware;

/// <summary>
/// Enforces the operator-wide IP allowlist before any authentication middleware
/// does OIDC crypto or tenant/user database lookups. Tenant-specific allowlists
/// still run later in <see cref="IpAllowlistMiddleware"/> after identity projection.
/// </summary>
public sealed class GlobalIpAllowlistMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<GlobalIpAllowlistMiddleware> _log;

    public GlobalIpAllowlistMiddleware(RequestDelegate next, ILogger<GlobalIpAllowlistMiddleware> log)
    {
        _next = next;
        _log = log;
    }

    public async Task InvokeAsync(HttpContext ctx)
    {
        var ranges = IpAllowlistMiddleware.ResolveRanges(Environment.GetEnvironmentVariable("RADIOPAD_IP_ALLOWLIST"));
        if (ranges.Configured && !ranges.Valid)
        {
            _log.LogError("Global IP allowlist is configured but invalid; failing closed before auth.");
            ctx.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            ctx.Response.ContentType = "application/problem+json";
            await ctx.Response.WriteAsJsonAsync(new
            {
                type = "https://radiopad.dev/errors/ip-allowlist-invalid",
                title = "IP allowlist is invalid",
                status = 503,
                kind = "ip_allowlist_invalid",
                scope = "global",
            });
            return;
        }

        if (ranges.Ranges.Length == 0)
        {
            await _next(ctx);
            return;
        }

        var remote = IpAllowlistMiddleware.ResolveRemoteIp(ctx);
        if (remote is null || IPAddress.IsLoopback(remote) || IpAllowlistMiddleware.MatchAny(remote, ranges.Ranges))
        {
            await _next(ctx);
            return;
        }

        var hashed = IpAllowlistMiddleware.HashIp(remote);
        _log.LogWarning("Blocked client (hashed={Hashed}) before auth: not in global IP allowlist.", hashed);
        ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
        ctx.Response.ContentType = "application/problem+json";
        await ctx.Response.WriteAsJsonAsync(new
        {
            type = "https://radiopad.dev/errors/ip-not-allowed",
            title = "IP address not allowed",
            status = 403,
            kind = "ip_not_allowed",
            scope = "global",
        });
    }
}
