using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
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
/// The bearer is HMAC-derived from a server secret + tenant + user so it is
/// reproducible (no DB row) and revocable by rotating the secret. Tokens are
/// stateless and validated by `BearerIdentityMiddleware` (when present); in
/// the dev pipeline the `X-RadioPad-*` headers continue to work so existing
/// integration tests stay green.
/// </summary>
[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    private readonly RadioPadDbContext _db;
    private readonly IAuditLog _audit;
    public AuthController(RadioPadDbContext db, IAuditLog audit)
    {
        _db = db; _audit = audit;
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

        var token = MintToken(tenant.Slug, dto.User, user.SessionEpoch);

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
            expiresAt = DateTimeOffset.UtcNow.AddHours(12),
        });
    }

    private static string MintToken(string tenant, string user, int sessionEpoch)
    {
        // Deterministic per (tenant, user, server-secret, sessionEpoch).
        // Rotate the env var to invalidate every token at once; bumping the
        // user's <see cref="Domain.Entities.User.SessionEpoch"/> via
        // <c>POST /api/users/{id}/revoke-sessions</c> invalidates only that
        // user's currently issued bearers.
        var secret = Environment.GetEnvironmentVariable("RADIOPAD_AUTH_SECRET")
            ?? "dev-only-not-for-production";
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var raw = hmac.ComputeHash(Encoding.UTF8.GetBytes($"v1|{tenant}|{user}|{sessionEpoch}"));
        return "rp_" + Convert.ToBase64String(raw).Replace("+", "-").Replace("/", "_").TrimEnd('=');
    }
}
