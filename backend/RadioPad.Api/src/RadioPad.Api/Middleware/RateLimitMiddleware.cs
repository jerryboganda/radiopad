using System.Threading.RateLimiting;

namespace RadioPad.Api.Middleware;

/// <summary>
/// Iter-32 SEC-008 — global request rate limiter using
/// <see cref="System.Threading.RateLimiting"/>. Two partitioned fixed-window
/// limiters run in series:
///
/// <list type="bullet">
///   <item><description>Per-IP: 100 req / minute (default; override with
///     <c>RADIOPAD_RATE_LIMIT_IP_PER_MIN</c>).</description></item>
///   <item><description>Per-tenant: 5000 req / minute (default; override with
///     <c>RADIOPAD_RATE_LIMIT_TENANT_PER_MIN</c>). Tenant key is the
///     <c>X-RadioPad-Tenant</c> header; absent header falls into a
///     <c>__no_tenant</c> partition.</description></item>
/// </list>
///
/// Health endpoints (<c>/api/health</c>, <c>/api/health/ready</c>) and the
/// global <c>/health</c> path bypass the limiter so liveness probes aren't
/// rate-limited. Loopback also bypasses (dev productivity + the API binds
/// loopback by default; rate-limiting localhost just frustrates engineers).
///
/// On limit, the middleware returns RFC-7807 problem+json with
/// <c>kind = "rate_limited"</c> and a <c>retryAfterSeconds</c> hint, plus a
/// <c>Retry-After</c> header for HTTP-spec-compliant clients.
/// </summary>
public sealed class RateLimitMiddleware : IDisposable
{
    private readonly RequestDelegate _next;
    private readonly ILogger<RateLimitMiddleware> _log;
    private readonly PartitionedRateLimiter<HttpContext> _ipLimiter;
    private readonly PartitionedRateLimiter<HttpContext> _tenantLimiter;
    private readonly int _windowSeconds;

    public RateLimitMiddleware(RequestDelegate next, ILogger<RateLimitMiddleware> log)
    {
        _next = next;
        _log = log;

        var ipPerMin = ReadInt("RADIOPAD_RATE_LIMIT_IP_PER_MIN", 100);
        var tenantPerMin = ReadInt("RADIOPAD_RATE_LIMIT_TENANT_PER_MIN", 5000);
        _windowSeconds = 60;

        _ipLimiter = PartitionedRateLimiter.Create<HttpContext, string>(ctx =>
        {
            var ip = IpAllowlistMiddleware.ResolveRemoteIp(ctx)?.ToString() ?? "__no_ip";
            return RateLimitPartition.GetFixedWindowLimiter(ip, _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = ipPerMin,
                Window = TimeSpan.FromSeconds(_windowSeconds),
                QueueLimit = 0,
                AutoReplenishment = true,
            });
        });

        _tenantLimiter = PartitionedRateLimiter.Create<HttpContext, string>(ctx =>
        {
            var tenant = ctx.Request.Headers["X-RadioPad-Tenant"].FirstOrDefault();
            if (string.IsNullOrWhiteSpace(tenant)) tenant = "__no_tenant";
            return RateLimitPartition.GetFixedWindowLimiter(tenant, _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = tenantPerMin,
                Window = TimeSpan.FromSeconds(_windowSeconds),
                QueueLimit = 0,
                AutoReplenishment = true,
            });
        });
    }

    public async Task InvokeAsync(HttpContext ctx)
    {
        if (IsExempt(ctx)) { await _next(ctx); return; }

        using var ipLease = await _ipLimiter.AcquireAsync(ctx, 1, ctx.RequestAborted);
        if (!ipLease.IsAcquired)
        {
            await Reject(ctx, "ip");
            return;
        }

        using var tenantLease = await _tenantLimiter.AcquireAsync(ctx, 1, ctx.RequestAborted);
        if (!tenantLease.IsAcquired)
        {
            await Reject(ctx, "tenant");
            return;
        }

        await _next(ctx);
    }

    private async Task Reject(HttpContext ctx, string scope)
    {
        var retry = _windowSeconds;
        ctx.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        ctx.Response.ContentType = "application/problem+json";
        ctx.Response.Headers["Retry-After"] = retry.ToString();
        _log.LogWarning("Rate limit hit ({Scope}) on {Path}", scope, ctx.Request.Path);
        await ctx.Response.WriteAsJsonAsync(new
        {
            type = "https://radiopad.dev/errors/rate-limited",
            title = "Too many requests",
            status = 429,
            kind = "rate_limited",
            scope,
            retryAfterSeconds = retry,
        });
    }

    private static bool IsExempt(HttpContext ctx)
    {
        var path = ctx.Request.Path.Value ?? "";
        if (path.StartsWith("/api/health", StringComparison.OrdinalIgnoreCase)) return true;
        if (path.Equals("/health", StringComparison.OrdinalIgnoreCase)) return true;
        var remote = IpAllowlistMiddleware.ResolveRemoteIp(ctx);
        if (remote is not null && System.Net.IPAddress.IsLoopback(remote)) return true;
        return false;
    }

    private static int ReadInt(string envName, int fallback)
    {
        var raw = Environment.GetEnvironmentVariable(envName);
        return int.TryParse(raw, out var v) && v > 0 ? v : fallback;
    }

    public void Dispose()
    {
        _ipLimiter.Dispose();
        _tenantLimiter.Dispose();
    }
}
