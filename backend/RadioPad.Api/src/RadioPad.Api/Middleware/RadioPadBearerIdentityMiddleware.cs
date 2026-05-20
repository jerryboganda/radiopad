using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RadioPad.Api.Auth;
using RadioPad.Application.Abstractions;
using RadioPad.Domain.Entities;
using RadioPad.Domain.Enums;
using RadioPad.Infrastructure.Identity;
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

        var token = RadioPadSessionCookies.ExtractBearer(ctx.Request);
        if (string.IsNullOrWhiteSpace(token))
        {
            await _next(ctx);
            return;
        }

        if (!RadioPadBearerToken.IsRadioPadBearer(token))
        {
            await _next(ctx);
            return;
        }

        var tokenHash = EnterpriseIdentityBridge.Sha256Hex(token);
        var tenantSlug = ctx.Request.Headers["X-RadioPad-Tenant"].FirstOrDefault();
        var email = ctx.Request.Headers["X-RadioPad-User"].FirstOrDefault();
        Tenant? tenant = null;
        User? user = null;
        if (string.IsNullOrWhiteSpace(tenantSlug) || string.IsNullOrWhiteSpace(email))
        {
            var session = await db.AuthSessions.AsNoTracking()
                .FirstOrDefaultAsync(s => s.TokenHash == tokenHash, ctx.RequestAborted);
            if (session?.TenantId is Guid sessionTenantId && session.UserId is Guid sessionUserId)
            {
                tenant = await db.Tenants.AsNoTracking()
                    .FirstOrDefaultAsync(t => t.Id == sessionTenantId, ctx.RequestAborted);
                user = await db.Users.AsNoTracking()
                    .FirstOrDefaultAsync(u => u.TenantId == sessionTenantId && u.Id == sessionUserId, ctx.RequestAborted);
                tenantSlug = tenant?.Slug;
                email = user?.Email;
            }
        }

        if (string.IsNullOrWhiteSpace(tenantSlug) || string.IsNullOrWhiteSpace(email))
        {
            await RejectAsync(ctx, "missing_rp_bearer_context", audit, null, null);
            return;
        }

        tenant ??= await db.Tenants.AsNoTracking()
            .FirstOrDefaultAsync(t => t.Slug == tenantSlug, ctx.RequestAborted);
        var lookupTenantId = tenant?.Id ?? Guid.Empty;
        user ??= await db.Users.AsNoTracking()
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

        var sessionState = await db.AuthSessions.AsNoTracking()
            .Where(s => s.TokenHash == tokenHash
                && s.TenantId == tenant.Id
                && s.UserId == user.Id)
            .Select(s => new { s.ExpiresAt, s.RevokedAt })
            .FirstOrDefaultAsync(ctx.RequestAborted);
        if (sessionState is null)
        {
            await RejectAsync(ctx, "missing_auth_session", audit, tenant.Id, user.Id);
            return;
        }

        if (sessionState.RevokedAt is not null)
        {
            await RejectAsync(ctx, "revoked_rp_bearer", audit, tenant.Id, user.Id);
            return;
        }

        if (sessionState.ExpiresAt <= DateTimeOffset.UtcNow)
        {
            await RejectAsync(ctx, "expired_auth_session", audit, tenant.Id, user.Id);
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
