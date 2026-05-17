using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RadioPad.Api.Auth;
using RadioPad.Application.Abstractions;
using RadioPad.Domain.Entities;
using RadioPad.Domain.Enums;
using RadioPad.Infrastructure.Persistence;

namespace RadioPad.Api.Middleware;

/// <summary>
/// Validates RadioPad-minted opaque bearers and promotes them into a verified
/// tenant/user context. Tenant/user headers are only lookup hints here; they do
/// not become authoritative unless the bearer matches that exact tuple.
/// </summary>
public sealed class RadioPadBearerIdentityMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<RadioPadBearerIdentityMiddleware> _log;

    public RadioPadBearerIdentityMiddleware(RequestDelegate next, ILogger<RadioPadBearerIdentityMiddleware> log)
    {
        _next = next;
        _log = log;
    }

    public async Task InvokeAsync(HttpContext ctx, RadioPadDbContext db, IAuditLog audit)
    {
        if (RadioPadRequestIdentity.TryGet(ctx, out _))
        {
            await _next(ctx);
            return;
        }

        var auth = ctx.Request.Headers.Authorization.FirstOrDefault();
        if (auth is null || !auth.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            await _next(ctx);
            return;
        }

        var token = auth["Bearer ".Length..].Trim();
        if (!RadioPadBearerToken.IsRadioPadBearer(token))
        {
            await _next(ctx);
            return;
        }

        var tenantSlug = ctx.Request.Headers["X-RadioPad-Tenant"].FirstOrDefault();
        var email = ctx.Request.Headers["X-RadioPad-User"].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(tenantSlug) || string.IsNullOrWhiteSpace(email))
        {
            await RejectAsync(ctx, "missing_rp_bearer_context", audit, null, null);
            return;
        }

        var tenant = await db.Tenants.AsNoTracking()
            .FirstOrDefaultAsync(t => t.Slug == tenantSlug, ctx.RequestAborted);
        var lookupTenantId = tenant?.Id ?? Guid.Empty;
        var user = await db.Users.AsNoTracking()
            .FirstOrDefaultAsync(u => u.TenantId == lookupTenantId && u.Email == email, ctx.RequestAborted);
        var tokenMatches = false;
        if (tenant is not null && user is not null)
        {
            tokenMatches = RadioPadBearerToken.Matches(token, tenant.Slug, user.Email, user.SessionEpoch);
        }
        else
        {
            // Keep invalid bearer paths doing an HMAC without letting phantom identities validate.
            _ = RadioPadBearerToken.Matches(token, "__missing_tenant", "__missing_user", 0);
        }

        if (tenant is null)
        {
            await RejectAsync(ctx, "unknown_tenant", audit, null, null);
            return;
        }

        if (user is null)
        {
            await RejectAsync(ctx, "unknown_user", audit, tenant.Id, null);
            return;
        }

        if (!tokenMatches)
        {
            await RejectAsync(ctx, "invalid_rp_bearer", audit, tenant.Id, user.Id);
            return;
        }

        if (!user.IsActive)
        {
            await RejectAsync(ctx, "inactive_user", audit, tenant.Id, user.Id);
            return;
        }

        if (user.LockedUntil is not null && user.LockedUntil > DateTimeOffset.UtcNow)
        {
            await RejectAsync(ctx, "locked_user", audit, tenant.Id, user.Id);
            return;
        }

        RadioPadRequestIdentity.Set(ctx, tenant.Slug, user.Email, "rp-bearer");
        await _next(ctx);
    }

    private async Task RejectAsync(
        HttpContext ctx,
        string reason,
        IAuditLog audit,
        Guid? tenantId,
        Guid? userId)
    {
        _log.LogWarning("Rejected RadioPad bearer for {Path}: {Reason}", ctx.Request.Path, reason);
        if (tenantId is Guid tid)
        {
            await audit.AppendAsync(new AuditEvent
            {
                TenantId = tid,
                UserId = userId,
                Action = AuditAction.PolicyViolation,
                DetailsJson = JsonSerializer.Serialize(new { reason, source = "rp-bearer" }),
            }, ctx.RequestAborted);
        }

        ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
        ctx.Response.ContentType = "application/problem+json";
        await ctx.Response.WriteAsJsonAsync(new
        {
            type = "auth/unauthorized",
            status = StatusCodes.Status401Unauthorized,
            title = "Unauthorized",
            kind = "unauthenticated",
        });
    }
}
