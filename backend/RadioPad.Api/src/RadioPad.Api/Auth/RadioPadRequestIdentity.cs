using System.Diagnostics.CodeAnalysis;

namespace RadioPad.Api.Auth;

internal sealed record RadioPadRequestIdentity(string TenantSlug, string UserEmail, string Source)
{
    private const string ItemKey = "__radiopad_verified_identity";

    public static void Set(HttpContext ctx, string tenantSlug, string userEmail, string source) =>
        ctx.Items[ItemKey] = new RadioPadRequestIdentity(tenantSlug, userEmail, source);

    public static bool TryGet(HttpContext ctx, [NotNullWhen(true)] out RadioPadRequestIdentity? identity)
    {
        if (ctx.Items.TryGetValue(ItemKey, out var raw) && raw is RadioPadRequestIdentity value)
        {
            identity = value;
            return true;
        }

        identity = null;
        return false;
    }

    public static bool DevHeadersEnabled(HttpContext ctx)
    {
        if (ctx.RequestServices is null)
            return false;

        var cfg = ctx.RequestServices.GetService<IConfiguration>();
        var configured = cfg?["RadioPad:DevHeaders"];
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return string.Equals(configured, "true", StringComparison.OrdinalIgnoreCase)
                || string.Equals(configured, "1", StringComparison.Ordinal);
        }

        var env = ctx.RequestServices.GetService<IWebHostEnvironment>();
        return env is not null && env.IsEnvironment("Testing")
            || Environment.GetEnvironmentVariable("RADIOPAD_DEV_HEADERS") == "1";
    }

    public static string? TenantSlugOrDevHeader(HttpContext ctx)
    {
        if (TryGet(ctx, out var identity)) return identity.TenantSlug;
        return DevHeadersEnabled(ctx)
            ? ctx.Request.Headers["X-RadioPad-Tenant"].FirstOrDefault()
            : null;
    }
}
