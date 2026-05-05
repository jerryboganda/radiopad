using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RadioPad.Infrastructure.Persistence;

namespace RadioPad.Api.Middleware;

/// <summary>
/// PRD BILL-001..006 — short-circuits mutating requests for tenants whose
/// <see cref="RadioPad.Domain.Entities.TenantSettings.SuspendedAt"/> is set
/// (suspended for billing reasons). Read-only traffic and the billing/auth/
/// health surfaces stay reachable so an operator can recover the tenant.
///
/// Runs after <see cref="IpAllowlistMiddleware"/> and before controllers.
/// Tenant resolution mirrors the dev-tenant header pattern used by
/// <c>TenantedController.ResolveContextAsync</c>.
/// </summary>
public sealed class SuspensionGuardMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<SuspensionGuardMiddleware> _log;

    public SuspensionGuardMiddleware(RequestDelegate next, ILogger<SuspensionGuardMiddleware> log)
    {
        _next = next;
        _log = log;
    }

    public async Task InvokeAsync(HttpContext ctx, RadioPadDbContext db)
    {
        var method = ctx.Request.Method;
        if (HttpMethods.IsGet(method) || HttpMethods.IsHead(method) || HttpMethods.IsOptions(method))
        {
            await _next(ctx);
            return;
        }

        var path = ctx.Request.Path.Value ?? string.Empty;
        if (!path.StartsWith("/api/", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("/api/billing/", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("/api/auth/", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("/api/health/", StringComparison.OrdinalIgnoreCase)
            || path.Equals("/api/health", StringComparison.OrdinalIgnoreCase))
        {
            await _next(ctx);
            return;
        }

        var slug = ctx.Request.Headers["X-RadioPad-Tenant"].FirstOrDefault() ?? "dev";

        var tenant = await db.Tenants.AsNoTracking()
            .FirstOrDefaultAsync(t => t.Slug == slug, ctx.RequestAborted);
        if (tenant is null)
        {
            await _next(ctx);
            return;
        }

        var settings = await db.TenantSettings
            .FirstOrDefaultAsync(s => s.TenantId == tenant.Id, ctx.RequestAborted);
        if (settings is null)
        {
            await _next(ctx);
            return;
        }

        var now = DateTimeOffset.UtcNow;
        if (settings.SuspendedAt is null && settings.GracePeriodUntil is { } graceUntil && now > graceUntil)
        {
            settings.SuspendedAt = now;
            settings.UpdatedAt = now;
            await db.SaveChangesAsync(ctx.RequestAborted);
        }

        if (settings.SuspendedAt is null)
        {
            await _next(ctx);
            return;
        }

        _log.LogWarning("Blocking {Method} {Path}: tenant {Slug} suspended at {SuspendedAt}",
            method, path, tenant.Slug, settings.SuspendedAt);

        if (ctx.Response.HasStarted) return;
        ctx.Response.StatusCode = StatusCodes.Status402PaymentRequired;
        ctx.Response.ContentType = "application/problem+json";
        await ctx.Response.WriteAsync(JsonSerializer.Serialize(new
        {
            type = "https://radiopad.app/problems/tenant-suspended",
            title = "Tenant suspended",
            status = StatusCodes.Status402PaymentRequired,
            kind = "tenant_suspended",
            reason = "billing",
            suspendedAt = settings.SuspendedAt,
            requestId = ctx.Response.Headers[RequestCorrelationMiddleware.Header].ToString(),
        }));
    }
}
