using Microsoft.EntityFrameworkCore;
using RadioPad.Api.Auth;
using RadioPad.Infrastructure.Identity;
using RadioPad.Infrastructure.Persistence;

namespace RadioPad.Api.Middleware;

/// <summary>
/// Validates RadioPad-issued opaque bearers (<c>rp_...</c>) and prevents the
/// dev header identity fallback from being accepted in production unless the
/// operator explicitly enables <c>RADIOPAD_DEV_HEADERS=1</c>.
/// </summary>
public sealed class RadioPadBearerMiddleware
{
    private readonly RequestDelegate _next;
    private readonly IWebHostEnvironment _env;

    public RadioPadBearerMiddleware(RequestDelegate next, IWebHostEnvironment env)
    {
        _next = next;
        _env = env;
    }

    public async Task InvokeAsync(HttpContext ctx, RadioPadDbContext db)
    {
        if (!ctx.Request.Path.StartsWithSegments("/api") || IsPublicApi(ctx.Request.Path))
        {
            await _next(ctx);
            return;
        }

        var devHeadersAllowed = Environment.GetEnvironmentVariable("RADIOPAD_DEV_HEADERS") == "1";
        var auth = ctx.Request.Headers.Authorization.FirstOrDefault();
        var bearer = auth is not null && auth.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
            ? auth["Bearer ".Length..].Trim()
            : null;
        var cookieBearer = ctx.Request.Cookies[RadioPadSessionCookies.CookieName];
        var token = !string.IsNullOrWhiteSpace(bearer) ? bearer : cookieBearer;
        var tokenFromCookie = string.IsNullOrWhiteSpace(bearer) && !string.IsNullOrWhiteSpace(cookieBearer);

        // PR-B1 SSE bearer-in-URL fallback. A browser EventSource cannot send an
        // Authorization header, so the desktop webview opens the stream with
        // ?access_token=. This is deliberately RESTRICTED to exactly GET
        // /api/events/stream (bearer-in-URL must never generalize) and only when no
        // header/cookie bearer is present; the token still flows through the identical
        // HMAC + AuthSession validation below. Mirrors the /ws/companion precedent
        // (CompanionRelayEndpoint reads the same query param for the same reason).
        // First-party clients use fetch()+ReadableStream with real headers and never
        // hit this branch.
        if (string.IsNullOrWhiteSpace(token)
            && HttpMethods.IsGet(ctx.Request.Method)
            && ctx.Request.Path.Equals("/api/events/stream", StringComparison.OrdinalIgnoreCase))
        {
            token = ctx.Request.Query["access_token"].FirstOrDefault();
        }

        if (!string.IsNullOrWhiteSpace(token) && token.StartsWith("rp_", StringComparison.Ordinal))
        {
            var slug = ctx.Request.Headers["X-RadioPad-Tenant"].FirstOrDefault();
            var email = ctx.Request.Headers["X-RadioPad-User"].FirstOrDefault();

            if (tokenFromCookie || string.IsNullOrWhiteSpace(slug) || string.IsNullOrWhiteSpace(email))
            {
                if (!RadioPadBearerTokens.TryReadUnvalidatedContext(token, out var tokenTenant, out var tokenUser))
                {
                    await RejectAsync(ctx, "invalid_token", "Bearer token is invalid or expired.");
                    return;
                }

                slug = tokenTenant;
                email = tokenUser;
            }

            if (string.IsNullOrWhiteSpace(slug) || string.IsNullOrWhiteSpace(email))
            {
                await RejectAsync(ctx, "tenant_user_required", "Bearer requests must include tenant and user context.");
                return;
            }

            var tenant = await db.Tenants.FirstOrDefaultAsync(t => t.Slug == slug, ctx.RequestAborted);
            if (tenant is null)
            {
                await RejectAsync(ctx, "unauthenticated", "Bearer tenant is not active.");
                return;
            }

            var user = await db.Users
                .FirstOrDefaultAsync(u => u.Email == email && u.TenantId == tenant.Id, ctx.RequestAborted);
            if (user is null || !user.IsActive || user.LockedUntil > DateTimeOffset.UtcNow)
            {
                await RejectAsync(ctx, "unauthenticated", "Bearer identity is not active.");
                return;
            }

            if (!RadioPadBearerTokens.TryValidate(token, slug, email, user.SessionEpoch, _env, out var reason))
            {
                await RejectAsync(ctx, reason, "Bearer token is invalid or expired.");
                return;
            }

            var tokenHash = EnterpriseIdentityBridge.Sha256Hex(token);
            var session = await db.AuthSessions.AsNoTracking()
                .Where(s => s.TokenHash == tokenHash && s.TenantId == tenant.Id && s.UserId == user.Id)
                .Select(s => new { s.ExpiresAt, s.RevokedAt, s.SessionEpochAtIssue })
                .FirstOrDefaultAsync(ctx.RequestAborted);
            if (session is not null)
            {
                if (session.RevokedAt is not null)
                {
                    await RejectAsync(ctx, "revoked_auth_session", "Bearer session has been revoked.");
                    return;
                }

                if (session.ExpiresAt <= DateTimeOffset.UtcNow || session.SessionEpochAtIssue != user.SessionEpoch)
                {
                    await RejectAsync(ctx, "expired_auth_session", "Bearer session is expired.");
                    return;
                }
            }

            ctx.Request.Headers["X-RadioPad-Tenant"] = slug;
            ctx.Request.Headers["X-RadioPad-User"] = email;
            RadioPadRequestIdentity.Set(ctx, slug, email, "rp-bearer");
            ctx.Items["RadioPad.Identity.Validated"] = true;
            await _next(ctx);
            return;
        }

        if (ctx.Items.ContainsKey("RadioPad.Identity.Validated"))
        {
            await _next(ctx);
            return;
        }

        if (_env.IsProduction() && !devHeadersAllowed)
        {
            await RejectAsync(ctx, "authentication_required", "Production API requests require a validated bearer or OIDC identity.");
            return;
        }

        await _next(ctx);
    }

    private static bool IsPublicApi(PathString path) =>
        path.StartsWithSegments("/api/health") ||
        // Mobile companion "Check for updates" — a public version check returning
        // only public GitHub release metadata; the phone may be unpaired/signed-out
        // when it checks.
        path.StartsWithSegments("/api/mobile") ||
        // On-device model manager (download/test/diagnostics). Loopback-only on the
        // desktop sidecar; on a hosted build LocalModelsController gates every action
        // on RADIOPAD_LOCAL_STT_ENABLED and returns inert results, so anonymous
        // reachability exposes no tenant data, secrets, or PHI. Whitelisted here so
        // it returns those inert results instead of a 401.
        path.StartsWithSegments("/api/local-models") ||
        // On-device whole-report generation (LocalGenerationController) — same safety
        // model as /api/local-models: loopback-only on the desktop sidecar, gated on
        // RADIOPAD_LOCAL_STT_ENABLED and inert (503) on a hosted build, so anonymous
        // reachability exposes no tenant data, secrets, or PHI.
        path.StartsWithSegments("/api/local-generation") ||
        path.StartsWithSegments("/api/auth/logout") ||
        path.StartsWithSegments("/api/auth/oidc/authorize-url") ||
        path.StartsWithSegments("/api/auth/magic-link/request") ||
        path.StartsWithSegments("/api/auth/magic-link/consume") ||
        path.StartsWithSegments("/api/auth/device/authorize") ||
        path.StartsWithSegments("/api/auth/device/token") ||
        // AUTH-001 password sign-in is the pre-session primary factor (no bearer
        // yet). Match the sign-in route EXACTLY (allowing the client's trailing
        // slash) so the authenticated `/api/auth/password/change` endpoint stays
        // behind the bearer/OIDC gate.
        (path.StartsWithSegments("/api/auth/password", out var pwRest) && (!pwRest.HasValue || pwRest == "/")) ||
        // Email-free password reset proves possession of the enrolled TOTP in the
        // controller; the caller has no session.
        path.StartsWithSegments("/api/auth/password/reset-with-totp") ||
        // TOTP step-up + mandatory first-login enrolment run before a session
        // exists. The MFA controller still requires a verified identity or a
        // signed mfa-setup ticket, so anonymous reachability is safe here.
        path.StartsWithSegments("/api/auth/mfa") ||
        // Operator org bootstrap — runs before any tenant/admin exists and is
        // gated by the RADIOPAD_BOOTSTRAP_SECRET header inside the controller.
        path.StartsWithSegments("/api/admin/bootstrap-org") ||
        // Self-serve SaaS onboarding — creating a brand-new organization happens
        // before any tenant/user exists, so it cannot carry a bearer. Gated
        // separately by RADIOPAD_ALLOW_SELF_SIGNUP inside the controller.
        path.StartsWithSegments("/api/registration/create-organization") ||
        path.StartsWithSegments("/api/billing/webhook");

    private static async Task RejectAsync(HttpContext ctx, string kind, string message)
    {
        ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
        ctx.Response.ContentType = "application/problem+json";
        await ctx.Response.WriteAsJsonAsync(new
        {
            type = "https://radiopad.app/problems/authentication-required",
            title = "Authentication required",
            status = StatusCodes.Status401Unauthorized,
            kind,
            detail = message,
        });
    }

}