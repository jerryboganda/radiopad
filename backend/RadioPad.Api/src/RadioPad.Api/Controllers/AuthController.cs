using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RadioPad.Api.Auth;
using RadioPad.Application.Abstractions;
using RadioPad.Domain.Entities;
using RadioPad.Domain.Enums;
using RadioPad.Infrastructure.Persistence;

namespace RadioPad.Api.Controllers;

/// <summary>
/// PRD AUTH-001 (dev tier) — issue an opaque session token after the caller
/// proves they own a tenant slug + user email. v0.1 keeps the existing
/// header-based dev identity model: there is no password, the endpoint
/// accepts the same `(tenant, user)` tuple used by `X-RadioPad-Tenant` /
/// `X-RadioPad-User` and exchanges it for a 32-byte random bearer the
/// frontend can stash in the OS-level secure store. SSO replacement is
/// tracked under ADR-0004.
///
/// The bearer is a stateless HMAC-signed payload containing tenant, user,
/// session epoch, issue time, expiry, and nonce. Rotating the secret invalidates
/// every bearer; bumping <see cref="User.SessionEpoch"/> invalidates one user.
/// </summary>
[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
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
}
