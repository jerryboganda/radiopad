using System.Security.Cryptography;
using System.Text.Json;
using System.IdentityModel.Tokens.Jwt;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RadioPad.Api.Auth;
using RadioPad.Application.Abstractions;
using RadioPad.Domain.Entities;
using RadioPad.Domain.Enums;
using RadioPad.Infrastructure.Identity;
using RadioPad.Infrastructure.Persistence;

namespace RadioPad.Api.Controllers;

/// <summary>
/// PRD AUTH-001 (dev tier) — issue an opaque session token after the caller
/// proves they own a tenant slug + user email. v0.1 keeps the existing
/// tenant-local dev identity model: there is no password, the endpoint
/// accepts the same `(tenant, user)` tuple used by `X-RadioPad-Tenant` /
/// `X-RadioPad-User` and exchanges it for an opaque HMAC bearer the
/// frontend can stash in the OS-level secure store. SSO replacement is
/// tracked under ADR-0004.
///
/// The bearer is a stateless HMAC-signed payload containing tenant, user,
/// session epoch, issue time, expiry, and nonce. Rotating the secret invalidates
/// every bearer; bumping <see cref="User.SessionEpoch"/> invalidates one user.
/// </summary>
[ApiController]
[Route("api/auth")]
public class AuthController : TenantedController
{
    private const string OidcStateCookie = "rp_oidc_state";
    private const string OidcVerifierCookie = "rp_oidc_verifier";
    private const string OidcReturnCookie = "rp_oidc_return";
    private const string OidcNonceCookie = "rp_oidc_nonce";
    private static ConfigurationManager<OpenIdConnectConfiguration>? _oidcConfig;
    private static string? _oidcAuthority;
    private static readonly object OidcLock = new();

    private readonly RadioPadDbContext _db;
    private readonly IAuditLog _audit;
    private readonly IWebHostEnvironment _env;
    public AuthController(RadioPadDbContext db, IAuditLog audit, IWebHostEnvironment env)
    {
        _db = db; _audit = audit; _env = env;
    }

    public record SignInDto(string Tenant, string User);

    [HttpPost("signin")]
    public async Task<IActionResult> SignIn([FromBody] SignInDto dto, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(dto.Tenant) || string.IsNullOrWhiteSpace(dto.User))
            return BadRequest(new { error = "Tenant and user are required.", kind = "validation" });
        if (!RadioPadRequestIdentity.DevHeadersEnabled(HttpContext))
        {
            await AuditBlockedDevSignInAsync(dto, ct);
            return Unauthorized(new
            {
                error = "Dev sign-in is disabled outside explicit dev/test mode.",
                kind = "unauthenticated",
            });
        }

        var tenant = await _db.Tenants.FirstOrDefaultAsync(t => t.Slug == dto.Tenant, ct);
        if (tenant is null)
            return Unauthorized(new { error = "Unknown tenant.", kind = "unauthenticated" });

        var user = await _db.Users.FirstOrDefaultAsync(
            u => u.TenantId == tenant.Id && u.Email == dto.User, ct);
        if (user is null)
            return Unauthorized(new { error = "User is not a member of this tenant.", kind = "unauthenticated" });
        if (!user.IsActive)
            return Unauthorized(new { error = "User has been deprovisioned.", kind = "unauthenticated" });
        if (user.LockedUntil is not null && user.LockedUntil > DateTimeOffset.UtcNow)
            return Unauthorized(new { error = "Account locked.", kind = "unauthenticated", until = user.LockedUntil });

        var issuedAt = DateTimeOffset.UtcNow;
        var token = RadioPadBearerTokens.Mint(tenant.Slug, dto.User, user.SessionEpoch, _env, issuedAt);
        var expiresAt = RadioPadBearerTokens.ExpiresAt(issuedAt);
        RadioPadSessionCookies.Append(Response, Request, token, expiresAt, _env);

        await _audit.AppendAsync(new AuditEvent
        {
            TenantId = tenant.Id,
            UserId = user.Id,
            Action = AuditAction.UserLogin,
            DetailsJson = JsonSerializer.Serialize(new { method = "dev-header" }),
        }, ct);

        return Ok(new
        {
            token,
            tenant = tenant.Slug,
            user = dto.User,
            expiresAt,
        });
    }

    [HttpPost("logout")]
    public IActionResult Logout()
    {
        RadioPadSessionCookies.Delete(Response, Request, _env);
        return NoContent();
    }

    [HttpGet("oidc/authorize-url")]
    public IActionResult OidcAuthorizeUrl([FromQuery] string? returnUrl = null)
    {
        var authorizeUrl = Environment.GetEnvironmentVariable("RADIOPAD_OIDC_AUTHORIZE_URL");
        var clientId = Environment.GetEnvironmentVariable("RADIOPAD_OIDC_CLIENT_ID");
        var publicWebUrl = Environment.GetEnvironmentVariable("RADIOPAD_PUBLIC_WEB_URL")?.TrimEnd('/');
        if (string.IsNullOrWhiteSpace(authorizeUrl) || string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(publicWebUrl))
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new
            {
                error = "OIDC browser sign-in is not configured.",
                kind = "oidc_unconfigured",
            });
        }

        var redirectUri = Environment.GetEnvironmentVariable("RADIOPAD_OIDC_REDIRECT_URI")
            ?? $"{publicWebUrl}/login";
        var audience = Environment.GetEnvironmentVariable("RADIOPAD_OIDC_AUDIENCE");
        var state = Convert.ToBase64String(RandomNumberGenerator.GetBytes(24))
            .Replace('+', '-').Replace('/', '_').TrimEnd('=');
        if (!string.IsNullOrWhiteSpace(returnUrl) && Uri.TryCreate(returnUrl, UriKind.Absolute, out var parsed))
        {
            state += "." + Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(parsed.PathAndQuery))
                .Replace('+', '-').Replace('/', '_').TrimEnd('=');
        }

        var separator = authorizeUrl.Contains('?', StringComparison.Ordinal) ? '&' : '?';
        var parameters = new List<string>
        {
            "response_type=code",
            $"client_id={Uri.EscapeDataString(clientId)}",
            $"redirect_uri={Uri.EscapeDataString(redirectUri)}",
            "scope=openid%20email%20profile",
            $"state={Uri.EscapeDataString(state)}",
        };
        if (!string.IsNullOrWhiteSpace(audience))
            parameters.Add($"audience={Uri.EscapeDataString(audience)}");

        var url = authorizeUrl + separator + string.Join('&', parameters);
        return Ok(new { url });
    }

    [HttpGet("oidc/authorize")]
    public async Task<IActionResult> BeginOidc([FromQuery] string? returnUrl, CancellationToken ct)
    {
        var authority = Environment.GetEnvironmentVariable("RADIOPAD_OIDC_AUTHORITY");
        var clientId = Environment.GetEnvironmentVariable("RADIOPAD_OIDC_CLIENT_ID")
            ?? Environment.GetEnvironmentVariable("RADIOPAD_OIDC_AUDIENCE");
        if (string.IsNullOrWhiteSpace(authority) || string.IsNullOrWhiteSpace(clientId))
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new
            {
                error = "OIDC is not configured.",
                kind = "configuration",
            });
        }

        var config = await GetOidcConfigurationAsync(authority, ct);
        if (string.IsNullOrWhiteSpace(config.AuthorizationEndpoint))
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new { error = "OIDC authorization endpoint is unavailable.", kind = "configuration" });

        var state = Base64Url(RandomNumberGenerator.GetBytes(32));
        var nonce = Base64Url(RandomNumberGenerator.GetBytes(32));
        var verifier = Base64Url(RandomNumberGenerator.GetBytes(32));
        var challenge = Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
        var redirectUri = OidcRedirectUri();
        var safeReturn = NormalizeReturnUrl(returnUrl);
        var expires = DateTimeOffset.UtcNow.AddMinutes(10);
        AppendOidcCookie(OidcStateCookie, state, expires);
        AppendOidcCookie(OidcVerifierCookie, verifier, expires);
        AppendOidcCookie(OidcReturnCookie, safeReturn, expires);
        AppendOidcCookie(OidcNonceCookie, nonce, expires);

        var scope = Environment.GetEnvironmentVariable("RADIOPAD_OIDC_SCOPE") ?? "openid profile email";
        var url = AddQuery(config.AuthorizationEndpoint, new Dictionary<string, string>
        {
            ["response_type"] = "code",
            ["client_id"] = clientId,
            ["redirect_uri"] = redirectUri,
            ["scope"] = scope,
            ["state"] = state,
            ["nonce"] = nonce,
            ["code_challenge"] = challenge,
            ["code_challenge_method"] = "S256",
        });

        return Redirect(url);
    }

    [HttpGet("oidc/callback")]
    public async Task<IActionResult> OidcCallback([FromQuery] string? code, [FromQuery] string? state, [FromQuery] string? error, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(error))
            return Unauthorized(new { error = "OIDC sign-in was rejected.", kind = "unauthenticated" });

        var expectedState = Request.Cookies[OidcStateCookie];
        var verifier = Request.Cookies[OidcVerifierCookie];
        var returnUrl = NormalizeReturnUrl(Request.Cookies[OidcReturnCookie]);
        var expectedNonce = Request.Cookies[OidcNonceCookie];
        if (string.IsNullOrWhiteSpace(code)
            || string.IsNullOrWhiteSpace(state)
            || string.IsNullOrWhiteSpace(expectedState)
            || !CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(state), Encoding.UTF8.GetBytes(expectedState))
            || string.IsNullOrWhiteSpace(verifier)
            || string.IsNullOrWhiteSpace(expectedNonce))
        {
            ClearOidcCookies();
            return Unauthorized(new { error = "OIDC state is invalid or expired.", kind = "unauthenticated" });
        }

        var authority = Environment.GetEnvironmentVariable("RADIOPAD_OIDC_AUTHORITY");
        var clientId = Environment.GetEnvironmentVariable("RADIOPAD_OIDC_CLIENT_ID")
            ?? Environment.GetEnvironmentVariable("RADIOPAD_OIDC_AUDIENCE");
        if (string.IsNullOrWhiteSpace(authority) || string.IsNullOrWhiteSpace(clientId))
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new { error = "OIDC is not configured.", kind = "configuration" });

        var config = await GetOidcConfigurationAsync(authority, ct);
        if (string.IsNullOrWhiteSpace(config.TokenEndpoint))
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new { error = "OIDC token endpoint is unavailable.", kind = "configuration" });

        var tokenResponse = await ExchangeOidcCodeAsync(config.TokenEndpoint, clientId, code, verifier, ct);
        if (string.IsNullOrWhiteSpace(tokenResponse.Jwt))
        {
            ClearOidcCookies();
            return Unauthorized(new { error = "OIDC token response did not contain a usable token.", kind = "unauthenticated" });
        }

        System.Security.Claims.ClaimsPrincipal principal;
        try
        {
            principal = ValidateOidcToken(tokenResponse.Jwt, config, clientId);
        }
        catch (SecurityTokenException)
        {
            ClearOidcCookies();
            return Unauthorized(new { error = "OIDC token validation failed.", kind = "unauthenticated" });
        }
        catch (ArgumentException)
        {
            ClearOidcCookies();
            return Unauthorized(new { error = "OIDC token validation failed.", kind = "unauthenticated" });
        }

        var nonce = principal.FindFirst("nonce")?.Value;
        if (string.IsNullOrWhiteSpace(nonce)
            || !CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(nonce), Encoding.UTF8.GetBytes(expectedNonce)))
        {
            ClearOidcCookies();
            return Unauthorized(new { error = "OIDC nonce is invalid or expired.", kind = "unauthenticated" });
        }

        var tenantClaim = Environment.GetEnvironmentVariable("RADIOPAD_OIDC_TENANT_CLAIM") ?? "tenant_slug";
        var emailClaim = Environment.GetEnvironmentVariable("RADIOPAD_OIDC_EMAIL_CLAIM") ?? "email";
        var slug = principal.FindFirst(tenantClaim)?.Value;
        var email = principal.FindFirst(emailClaim)?.Value;
        if (string.IsNullOrWhiteSpace(slug) || string.IsNullOrWhiteSpace(email))
        {
            ClearOidcCookies();
            return Unauthorized(new { error = "OIDC token is missing tenant or email claims.", kind = "unauthenticated" });
        }

        if (Environment.GetEnvironmentVariable("RADIOPAD_OIDC_REQUIRE_MFA") == "1")
        {
            var amr = principal.FindAll("amr").Select(c => c.Value).ToArray();
            if (!amr.Any(a => a.Equals("mfa", StringComparison.OrdinalIgnoreCase)
                || a.Equals("otp", StringComparison.OrdinalIgnoreCase)))
            {
                ClearOidcCookies();
                return Unauthorized(new { error = "MFA required.", kind = "mfa_required" });
            }
        }

        var tenant = await _db.Tenants.FirstOrDefaultAsync(t => t.Slug == slug, ct);
        if (tenant is null)
        {
            ClearOidcCookies();
            return Unauthorized(new { error = "Unknown tenant.", kind = "unauthenticated" });
        }
        var user = await _db.Users.FirstOrDefaultAsync(u => u.TenantId == tenant.Id && u.Email == email, ct);
        if (user is null)
        {
            ClearOidcCookies();
            return Unauthorized(new { error = "User is not a member of this tenant.", kind = "unauthenticated" });
        }
        if (!user.IsActive)
        {
            ClearOidcCookies();
            return Unauthorized(new { error = "User has been deprovisioned.", kind = "unauthenticated" });
        }
        if (user.LockedUntil is not null && user.LockedUntil > DateTimeOffset.UtcNow)
        {
            ClearOidcCookies();
            return Unauthorized(new { error = "Account locked.", kind = "unauthenticated", until = user.LockedUntil });
        }

        var token = RadioPadBearerToken.Mint(tenant.Slug, user.Email, user.SessionEpoch);
        var expiresAt = DateTimeOffset.UtcNow.Add(RadioPadBearerToken.Lifetime);
        await EnterpriseIdentityBridge.RecordAuthSessionAsync(_db, user, token, "oidc-code", expiresAt, ct,
            ip: HttpContext.Connection.RemoteIpAddress?.ToString(),
            userAgent: HttpContext.Request.Headers.UserAgent.FirstOrDefault());
        RadioPadSessionCookies.Append(HttpContext, token, expiresAt);
        await _audit.AppendAsync(new AuditEvent
        {
            TenantId = tenant.Id,
            UserId = user.Id,
            Action = AuditAction.UserLogin,
            DetailsJson = JsonSerializer.Serialize(new { method = "oidc-code" }),
        }, ct);

        ClearOidcCookies();
        return Redirect(returnUrl);
    }

    [HttpGet("session")]
    public async Task<IActionResult> CurrentSession(CancellationToken ct)
    {
        var (tenant, user) = await ResolveContextAsync(_db, ct);
        var token = RadioPadSessionCookies.ExtractBearer(Request);
        if (string.IsNullOrWhiteSpace(token))
            return Unauthorized(new { error = "Bearer session is required.", kind = "unauthenticated" });

        var tokenHash = EnterpriseIdentityBridge.Sha256Hex(token);
        var session = await _db.AuthSessions.AsNoTracking()
            .Where(s => s.TokenHash == tokenHash && s.TenantId == tenant.Id && s.UserId == user.Id)
            .Select(s => new AuthSessionDto(
                s.Id,
                s.Method,
                s.IssuedAt,
                s.ExpiresAt,
                s.RevokedAt,
                s.RevocationReason,
                true))
            .FirstOrDefaultAsync(ct);

        return Ok(new
        {
            tenant = new { tenant.Slug, tenant.DisplayName },
            user = new { user.Email, user.DisplayName, user.Role },
            session,
        });
    }

    [HttpGet("sessions")]
    public async Task<IActionResult> ListSessions(CancellationToken ct)
    {
        var (tenant, user) = await ResolveContextAsync(_db, ct);
        var currentHash = CurrentBearerHash();
        var now = DateTimeOffset.UtcNow;
        var sessions = await _db.AuthSessions.AsNoTracking()
            .Where(s => s.TenantId == tenant.Id && s.UserId == user.Id && s.ExpiresAt > now)
            .OrderByDescending(s => s.IssuedAt)
            .Select(s => new AuthSessionDto(
                s.Id,
                s.Method,
                s.IssuedAt,
                s.ExpiresAt,
                s.RevokedAt,
                s.RevocationReason,
                currentHash != null && s.TokenHash == currentHash))
            .ToListAsync(ct);

        return Ok(new { sessions });
    }

    [HttpPost("sessions/{sessionId:guid}/revoke")]
    public async Task<IActionResult> RevokeSession(Guid sessionId, CancellationToken ct)
    {
        var (tenant, user) = await ResolveContextAsync(_db, ct);
        var now = DateTimeOffset.UtcNow;
        var updated = await _db.AuthSessions
            .Where(s => s.Id == sessionId
                && s.TenantId == tenant.Id
                && s.UserId == user.Id
                && s.RevokedAt == null)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(s => s.RevokedAt, now)
                .SetProperty(s => s.RevocationReason, "user-revoked")
                .SetProperty(s => s.UpdatedAt, now), ct);

        if (updated == 0)
        {
            var exists = await _db.AuthSessions.AsNoTracking()
                .AnyAsync(s => s.Id == sessionId && s.TenantId == tenant.Id && s.UserId == user.Id, ct);
            if (!exists)
                return NotFound(new { error = "Session not found.", kind = "not_found" });
        }

        await _audit.AppendAsync(new AuditEvent
        {
            TenantId = tenant.Id,
            UserId = user.Id,
            Action = AuditAction.SessionsRevoked,
            DetailsJson = JsonSerializer.Serialize(new { scope = "session", sessionId }),
        }, ct);

        return Ok(new { ok = true, revoked = updated > 0 });
    }

    [HttpPost("logout")]
    public async Task<IActionResult> Logout(CancellationToken ct)
    {
        var token = RadioPadSessionCookies.ExtractBearer(Request);
        var revoked = false;

        if (!string.IsNullOrWhiteSpace(token) && RadioPadRequestIdentity.TryGet(HttpContext, out var identity))
        {
            var tenant = await _db.Tenants.AsNoTracking()
                .FirstOrDefaultAsync(t => t.Slug == identity.TenantSlug, ct);
            if (tenant is not null)
            {
                var user = await _db.Users.AsNoTracking()
                    .FirstOrDefaultAsync(u => u.TenantId == tenant.Id && u.Email == identity.UserEmail, ct);
                if (user is not null)
                {
                    var now = DateTimeOffset.UtcNow;
                    var tokenHash = EnterpriseIdentityBridge.Sha256Hex(token);
                    var updated = await _db.AuthSessions
                        .Where(s => s.TokenHash == tokenHash
                            && s.TenantId == tenant.Id
                            && s.UserId == user.Id
                            && s.RevokedAt == null)
                        .ExecuteUpdateAsync(setters => setters
                            .SetProperty(s => s.RevokedAt, now)
                            .SetProperty(s => s.RevocationReason, "logout")
                            .SetProperty(s => s.UpdatedAt, now), ct);
                    revoked = updated > 0;

                    if (revoked)
                    {
                        await _audit.AppendAsync(new AuditEvent
                        {
                            TenantId = tenant.Id,
                            UserId = user.Id,
                            Action = AuditAction.SessionsRevoked,
                            DetailsJson = JsonSerializer.Serialize(new { scope = "current-session", reason = "logout" }),
                        }, ct);
                    }
                }
            }
        }

        RadioPadSessionCookies.Clear(HttpContext);
        return Ok(new { ok = true, revoked });
    }

    private string? CurrentBearerHash()
    {
        var token = RadioPadSessionCookies.ExtractBearer(Request);
        return string.IsNullOrWhiteSpace(token) ? null : EnterpriseIdentityBridge.Sha256Hex(token);
    }

    public sealed record AuthSessionDto(
        Guid Id,
        string Method,
        DateTimeOffset IssuedAt,
        DateTimeOffset ExpiresAt,
        DateTimeOffset? RevokedAt,
        string RevocationReason,
        bool IsCurrent);

    private async Task<OpenIdConnectConfiguration> GetOidcConfigurationAsync(string authority, CancellationToken ct)
    {
        ConfigurationManager<OpenIdConnectConfiguration> manager;
        lock (OidcLock)
        {
            if (_oidcConfig is null || !string.Equals(_oidcAuthority, authority, StringComparison.Ordinal))
            {
                _oidcAuthority = authority;
                _oidcConfig = new ConfigurationManager<OpenIdConnectConfiguration>(
                    authority.TrimEnd('/') + "/.well-known/openid-configuration",
                    new OpenIdConnectConfigurationRetriever(),
                    new HttpDocumentRetriever { RequireHttps = !authority.Contains("://localhost", StringComparison.OrdinalIgnoreCase) });
            }
            manager = _oidcConfig;
        }

        return await manager.GetConfigurationAsync(ct);
    }

    private async Task<OidcTokenResponse> ExchangeOidcCodeAsync(
        string tokenEndpoint,
        string clientId,
        string code,
        string verifier,
        CancellationToken ct)
    {
        var fields = new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["client_id"] = clientId,
            ["code"] = code,
            ["redirect_uri"] = OidcRedirectUri(),
            ["code_verifier"] = verifier,
        };
        var secret = Environment.GetEnvironmentVariable("RADIOPAD_OIDC_CLIENT_SECRET");
        if (!string.IsNullOrWhiteSpace(secret))
            fields["client_secret"] = secret;

        using var response = await _http.CreateClient().PostAsync(tokenEndpoint, new FormUrlEncodedContent(fields), ct);
        if (!response.IsSuccessStatusCode)
            return new OidcTokenResponse(null);

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
        var root = doc.RootElement;
        var jwt = root.TryGetProperty("id_token", out var idToken) ? idToken.GetString() : null;
        return new OidcTokenResponse(jwt);
    }

    private System.Security.Claims.ClaimsPrincipal ValidateOidcToken(
        string jwt,
        OpenIdConnectConfiguration config,
        string clientId)
    {
        var audience = Environment.GetEnvironmentVariable("RADIOPAD_OIDC_AUDIENCE");
        var audiences = new[] { clientId, audience }
            .Where(a => !string.IsNullOrWhiteSpace(a))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var handler = new JwtSecurityTokenHandler();
        return handler.ValidateToken(jwt, new TokenValidationParameters
        {
            ValidIssuer = config.Issuer,
            ValidateIssuer = true,
            ValidateAudience = audiences.Length > 0,
            ValidAudiences = audiences,
            ValidateLifetime = true,
            IssuerSigningKeys = config.SigningKeys,
            ValidateIssuerSigningKey = true,
            ClockSkew = TimeSpan.FromMinutes(2),
        }, out _);
    }

    private string OidcRedirectUri() =>
        Environment.GetEnvironmentVariable("RADIOPAD_OIDC_REDIRECT_URI")
        ?? BuildAbsoluteUrl("/api/auth/oidc/callback");

    private string BuildAbsoluteUrl(string path)
    {
        var basePath = Request.PathBase.HasValue ? Request.PathBase.Value : "";
        return $"{Request.Scheme}://{Request.Host}{basePath}{path}";
    }

    private string NormalizeReturnUrl(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "/";
        if (Uri.TryCreate(value, UriKind.Relative, out var relative)
            && !value.StartsWith("//", StringComparison.Ordinal)
            && relative.OriginalString.StartsWith("/", StringComparison.Ordinal))
        {
            return relative.OriginalString;
        }
        if (Uri.TryCreate(value, UriKind.Absolute, out var absolute)
            && string.Equals(absolute.Host, Request.Host.Host, StringComparison.OrdinalIgnoreCase))
        {
            return absolute.PathAndQuery == "" ? "/" : absolute.PathAndQuery;
        }
        return "/";
    }

    private void AppendOidcCookie(string name, string value, DateTimeOffset expiresAt) =>
        Response.Cookies.Append(name, value, new CookieOptions
        {
            HttpOnly = true,
            SameSite = SameSiteMode.Lax,
            Secure = ShouldUseSecureCookies(),
            Path = "/api/auth/oidc",
            Expires = expiresAt,
            IsEssential = true,
        });

    private void ClearOidcCookies()
    {
        DeleteOidcCookie(OidcStateCookie);
        DeleteOidcCookie(OidcVerifierCookie);
        DeleteOidcCookie(OidcReturnCookie);
        DeleteOidcCookie(OidcNonceCookie);
    }

    private void DeleteOidcCookie(string name) =>
        Response.Cookies.Delete(name, new CookieOptions
        {
            HttpOnly = true,
            SameSite = SameSiteMode.Lax,
            Secure = ShouldUseSecureCookies(),
            Path = "/api/auth/oidc",
        });

    private bool ShouldUseSecureCookies() =>
        Request.IsHttps || (!_env.IsDevelopment() && !_env.IsEnvironment("Testing"));

    private static string AddQuery(string endpoint, IReadOnlyDictionary<string, string> values)
    {
        var separator = endpoint.Contains('?') ? "&" : "?";
        return endpoint + separator + string.Join("&", values.Select(kv =>
            $"{Uri.EscapeDataString(kv.Key)}={Uri.EscapeDataString(kv.Value)}"));
    }

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private sealed record OidcTokenResponse(string? Jwt);
}
