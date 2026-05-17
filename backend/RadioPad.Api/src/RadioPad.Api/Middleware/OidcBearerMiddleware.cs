using System.IdentityModel.Tokens.Jwt;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using RadioPad.Api.Auth;

namespace RadioPad.Api.Middleware;

/// <summary>
/// PRD AUTH-002 — translate an external IdP OIDC bearer (Keycloak / Auth0 /
/// Okta / etc.) into the server-verified tenant/user request context consumed
/// by <c>TenantedController.ResolveContextAsync</c>.
///
/// Configuration (env-only — never committed):
///   <list type="bullet">
///   <item><c>RADIOPAD_OIDC_AUTHORITY</c> — issuer URL, e.g. <c>https://auth.example.com/realms/radiopad</c>. Required to enable the path.</item>
///   <item><c>RADIOPAD_OIDC_AUDIENCE</c> — expected <c>aud</c> claim. Optional.</item>
///   <item><c>RADIOPAD_OIDC_TENANT_CLAIM</c> — claim that carries the tenant slug (default <c>tenant_slug</c>).</item>
///   <item><c>RADIOPAD_OIDC_EMAIL_CLAIM</c> — claim that carries the user email (default <c>email</c>).</item>
///   <item><c>RADIOPAD_OIDC_REQUIRE_MFA</c> — when <c>1</c>, the JWT must carry <c>amr</c> containing <c>mfa</c> or <c>otp</c>.</item>
///   <item><c>RADIOPAD_DEV_HEADERS</c> — when <c>1</c>, dev headers remain available for local/test compatibility.</item>
///   </list>
///
/// JWKS metadata is cached for an hour by
/// <c>ConfigurationManager&lt;OpenIdConnectConfiguration&gt;</c>; signature
/// validation is delegated to <c>JwtSecurityTokenHandler</c>. Failures fall
/// through to the next middleware so controllers reject unless explicit
/// dev/test headers are configured.
/// </summary>
public sealed class OidcBearerMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<OidcBearerMiddleware> _log;
    private static ConfigurationManager<OpenIdConnectConfiguration>? _configMgr;
    private static readonly object _lock = new();

    public OidcBearerMiddleware(RequestDelegate next, ILogger<OidcBearerMiddleware> log)
    {
        _next = next; _log = log;
    }

    public async Task InvokeAsync(HttpContext ctx)
    {
        var authority = Environment.GetEnvironmentVariable("RADIOPAD_OIDC_AUTHORITY");

        if (string.IsNullOrWhiteSpace(authority))
        {
            await _next(ctx);
            return;
        }

        var auth = ctx.Request.Headers["Authorization"].FirstOrDefault();
        if (auth is null || !auth.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            // No JWT to validate — let dev headers (if any) handle it.
            await _next(ctx);
            return;
        }

        var jwt = auth["Bearer ".Length..].Trim();
        // RadioPad-minted opaque tokens (`rp_…`) are stamped by AuthController.SignIn
        // and never claim to be JWTs. Skip OIDC validation for those.
        if (jwt.StartsWith("rp_", StringComparison.Ordinal))
        {
            await _next(ctx);
            return;
        }

        try
        {
            var mgr = GetConfigManager(authority);
            var config = await mgr.GetConfigurationAsync(ctx.RequestAborted);
            var audience = Environment.GetEnvironmentVariable("RADIOPAD_OIDC_AUDIENCE");
            var validationParameters = new TokenValidationParameters
            {
                ValidIssuer = config.Issuer,
                ValidateIssuer = true,
                ValidateAudience = !string.IsNullOrWhiteSpace(audience),
                ValidAudience = audience,
                ValidateLifetime = true,
                IssuerSigningKeys = config.SigningKeys,
                ValidateIssuerSigningKey = true,
                ClockSkew = TimeSpan.FromMinutes(2),
            };

            var handler = new JwtSecurityTokenHandler();
            var principal = handler.ValidateToken(jwt, validationParameters, out _);

            var tenantClaim = Environment.GetEnvironmentVariable("RADIOPAD_OIDC_TENANT_CLAIM") ?? "tenant_slug";
            var emailClaim = Environment.GetEnvironmentVariable("RADIOPAD_OIDC_EMAIL_CLAIM") ?? "email";

            var slug = principal.FindFirst(tenantClaim)?.Value;
            var email = principal.FindFirst(emailClaim)?.Value;
            if (string.IsNullOrWhiteSpace(slug) || string.IsNullOrWhiteSpace(email))
            {
                _log.LogWarning("OIDC token missing tenant/email claim ({TenantClaim}/{EmailClaim}).",
                    tenantClaim, emailClaim);
                await _next(ctx);
                return;
            }

            if (Environment.GetEnvironmentVariable("RADIOPAD_OIDC_REQUIRE_MFA") == "1")
            {
                var amr = principal.FindAll("amr").Select(c => c.Value).ToArray();
                if (!amr.Any(a => a.Equals("mfa", StringComparison.OrdinalIgnoreCase)
                              || a.Equals("otp", StringComparison.OrdinalIgnoreCase)))
                {
                    ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    await ctx.Response.WriteAsJsonAsync(new { error = "MFA required.", kind = "mfa_required" });
                    return;
                }
            }

            // Promote OIDC into the same verified context used by RadioPad bearers.
            // Dev/test headers remain only a fallback when no verified identity exists.
            RadioPadRequestIdentity.Set(ctx, slug, email, "oidc");
        }
        catch (SecurityTokenException ex)
        {
            _log.LogWarning(ex, "OIDC token validation failed.");
            // Fall through; controllers will see no header injection and reject.
        }

        await _next(ctx);
    }

    private static ConfigurationManager<OpenIdConnectConfiguration> GetConfigManager(string authority)
    {
        if (_configMgr is not null) return _configMgr;
        lock (_lock)
        {
            if (_configMgr is not null) return _configMgr;
            var metadata = authority.TrimEnd('/') + "/.well-known/openid-configuration";
            _configMgr = new ConfigurationManager<OpenIdConnectConfiguration>(
                metadata,
                new OpenIdConnectConfigurationRetriever(),
                new HttpDocumentRetriever { RequireHttps = !authority.Contains("://localhost", StringComparison.OrdinalIgnoreCase) });
            return _configMgr;
        }
    }
}
