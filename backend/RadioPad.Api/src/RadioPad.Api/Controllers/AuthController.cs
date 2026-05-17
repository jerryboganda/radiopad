using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
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
/// The bearer is HMAC-derived from a server secret + tenant + user so it is
/// reproducible (no DB row) and revocable by rotating the secret. Tokens are
/// stateless and validated by `RadioPadBearerIdentityMiddleware`; explicit
/// dev/test mode keeps `X-RadioPad-*` headers available for local flows.
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

        var token = RadioPadBearerToken.Mint(tenant.Slug, dto.User, user.SessionEpoch);
        var expiresAt = DateTimeOffset.UtcNow.Add(RadioPadBearerToken.Lifetime);
        await EnterpriseIdentityBridge.RecordAuthSessionAsync(_db, user, token, "dev-header", expiresAt, ct,
            ip: HttpContext.Connection.RemoteIpAddress?.ToString(),
            userAgent: HttpContext.Request.Headers.UserAgent.FirstOrDefault());

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

    private async Task AuditBlockedDevSignInAsync(SignInDto dto, CancellationToken ct)
    {
        var tenant = await _db.Tenants.FirstOrDefaultAsync(t => t.Slug == dto.Tenant, ct);
        if (tenant is null)
            return;

        var user = await _db.Users.FirstOrDefaultAsync(u => u.TenantId == tenant.Id && u.Email == dto.User, ct);
        await _audit.AppendAsync(new AuditEvent
        {
            TenantId = tenant.Id,
            UserId = user?.Id,
            Action = AuditAction.PolicyViolation,
            DetailsJson = JsonSerializer.Serialize(new { reason = "dev_signin_disabled" }),
        }, ct);
    }
}
