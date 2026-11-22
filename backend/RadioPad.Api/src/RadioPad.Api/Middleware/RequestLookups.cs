using Microsoft.EntityFrameworkCore;
using RadioPad.Domain.Entities;
using RadioPad.Infrastructure.Persistence;

namespace RadioPad.Api.Middleware;

/// <summary>
/// Per-request memo for the tenant / user / settings rows that the auth middlewares and
/// <c>TenantedController.ResolveContextAsync</c> each looked up again (3x tenant, 2x user per call).
/// EF's identity map does not short-circuit <c>FirstOrDefault</c>, so every repeat was a database
/// round trip — ~180 ms each against the remote MariaDB. The cache lives in <c>HttpContext.Items</c>, so it
/// never outlives the request and a session revocation or lock-out is still seen on the very next request.
/// </summary>
internal static class RequestLookups
{
    public static async Task<Tenant?> TenantAsync(HttpContext ctx, RadioPadDbContext db, string slug, CancellationToken ct)
    {
        var key = "RadioPad.Lookup.Tenant:" + slug;
        if (ctx.Items.TryGetValue(key, out var hit) && hit is Tenant cached) return cached;
        var tenant = await db.Tenants.FirstOrDefaultAsync(t => t.Slug == slug, ct);
        if (tenant is not null) ctx.Items[key] = tenant;
        return tenant;
    }

    public static async Task<User?> UserAsync(HttpContext ctx, RadioPadDbContext db, Guid tenantId, string email, CancellationToken ct)
    {
        var key = $"RadioPad.Lookup.User:{tenantId}:{email}";
        if (ctx.Items.TryGetValue(key, out var hit) && hit is User cached) return cached;
        var user = await db.Users.FirstOrDefaultAsync(u => u.TenantId == tenantId && u.Email == email, ct);
        if (user is not null) ctx.Items[key] = user;
        return user;
    }

    public static async Task<TenantSettings?> SettingsAsync(HttpContext ctx, RadioPadDbContext db, Guid tenantId, CancellationToken ct)
    {
        var key = "RadioPad.Lookup.Settings:" + tenantId;
        if (ctx.Items.TryGetValue(key, out var hit) && hit is TenantSettings cached) return cached;
        var settings = await db.TenantSettings.FirstOrDefaultAsync(s => s.TenantId == tenantId, ct);
        if (settings is not null) ctx.Items[key] = settings;
        return settings;
    }
}
