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
/// PRD WL-009 — multi-tenant worklist switching for teleradiologists.
///
/// A teleradiologist reads for several practices under one enterprise identity
/// (<see cref="GlobalUser"/> with a <see cref="TenantMembership"/> per practice).
/// Signing out and back in to change practice is the workflow this removes.
///
/// Switching is a real re-authentication, not a client-side header swap: the
/// endpoint mints a NEW bearer bound to the target tenant + that tenant's user
/// row, so every downstream tenant-isolation check (which reads the session,
/// never a client-supplied header) keeps working unchanged. A membership that
/// is not active, or whose user row is inactive/locked, is refused.
/// </summary>
[ApiController]
[Route("api/tenant")]
public class TenantSwitchController : TenantedController
{
    private readonly RadioPadDbContext _db;
    private readonly IAuditLog _audit;
    private readonly IWebHostEnvironment _env;

    public TenantSwitchController(RadioPadDbContext db, IAuditLog audit, IWebHostEnvironment env)
    {
        _db = db;
        _audit = audit;
        _env = env;
    }

    public record MembershipDto(
        string Slug,
        string DisplayName,
        string Role,
        bool IsCurrent,
        bool IsDefault);

    /// <summary>
    /// Practices this identity can read for. Always includes the current tenant,
    /// so a single-practice user simply sees one row (and the UI hides the
    /// switcher).
    /// </summary>
    [HttpGet("memberships")]
    public async Task<IActionResult> Memberships(CancellationToken ct)
    {
        var (tenant, user) = await ResolveContextAsync(_db, ct);

        var membership = await _db.TenantMemberships
            .FirstOrDefaultAsync(m => m.TenantId == tenant.Id && m.UserId == user.Id, ct);
        if (membership is null)
        {
            // No enterprise identity linked yet (legacy single-tenant account).
            return Ok(new[]
            {
                new MembershipDto(tenant.Slug, tenant.DisplayName, user.Role.ToString(), true, true),
            });
        }

        var rows = await _db.TenantMemberships
            .Where(m => m.GlobalUserId == membership.GlobalUserId && m.Status == "active")
            .ToListAsync(ct);

        var tenantIds = rows.Select(r => r.TenantId).ToList();
        var userIds = rows.Select(r => r.UserId).ToList();
        var tenants = await _db.Tenants
            .Where(t => tenantIds.Contains(t.Id))
            .ToDictionaryAsync(t => t.Id, ct);
        var users = await _db.Users
            .Where(u => userIds.Contains(u.Id) && u.IsActive)
            .ToDictionaryAsync(u => u.Id, ct);

        var result = rows
            .Where(r => tenants.ContainsKey(r.TenantId) && users.ContainsKey(r.UserId))
            .Select(r => new MembershipDto(
                tenants[r.TenantId].Slug,
                tenants[r.TenantId].DisplayName,
                users[r.UserId].Role.ToString(),
                tenants[r.TenantId].Id == tenant.Id,
                r.IsDefault))
            .OrderByDescending(m => m.IsCurrent)
            .ThenBy(m => m.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return Ok(result);
    }

    public record SwitchDto(string Slug);

    /// <summary>
    /// Mint a session for another practice this identity belongs to. Returns the
    /// new bearer (and sets the session cookie) exactly like a fresh sign-in.
    /// </summary>
    [HttpPost("switch")]
    public async Task<IActionResult> Switch([FromBody] SwitchDto dto, CancellationToken ct)
    {
        var (tenant, user) = await ResolveContextAsync(_db, ct);
        if (dto is null || string.IsNullOrWhiteSpace(dto.Slug))
            return BadRequest(new { error = "slug is required.", kind = "validation" });

        var slug = dto.Slug.Trim();
        if (string.Equals(slug, tenant.Slug, StringComparison.OrdinalIgnoreCase))
            return BadRequest(new { error = "Already signed in to that workspace.", kind = "validation" });

        var membership = await _db.TenantMemberships
            .FirstOrDefaultAsync(m => m.TenantId == tenant.Id && m.UserId == user.Id, ct);
        if (membership is null)
            return Forbid();

        var target = await _db.Tenants.FirstOrDefaultAsync(t => t.Slug == slug, ct);
        if (target is null)
            return NotFound(new { error = "Unknown workspace.", kind = "not_found" });

        // The membership row is the authorization: an identity may only switch
        // into a practice it has an ACTIVE membership in. Never trust the slug.
        var targetMembership = await _db.TenantMemberships.FirstOrDefaultAsync(
            m => m.GlobalUserId == membership.GlobalUserId
                 && m.TenantId == target.Id
                 && m.Status == "active", ct);
        if (targetMembership is null)
            return Forbid();

        var targetUser = await _db.Users.FirstOrDefaultAsync(
            u => u.Id == targetMembership.UserId && u.TenantId == target.Id, ct);
        if (targetUser is null || !targetUser.IsActive)
            return Forbid();
        if (targetUser.LockedUntil is not null && targetUser.LockedUntil > DateTimeOffset.UtcNow)
        {
            return Unauthorized(new
            {
                error = "Account locked in that workspace.",
                kind = "unauthenticated",
                until = targetUser.LockedUntil,
            });
        }

        var issuedAt = DateTimeOffset.UtcNow;
        var token = RadioPadBearerTokens.Mint(target.Slug, targetUser.Email, targetUser.SessionEpoch, _env, issuedAt);
        var expiresAt = RadioPadBearerTokens.ExpiresAt(issuedAt);
        await EnterpriseIdentityBridge.RecordAuthSessionAsync(
            _db,
            targetUser,
            token,
            "tenant-switch",
            expiresAt,
            ct,
            ip: HttpContext.Connection.RemoteIpAddress?.ToString(),
            userAgent: HttpContext.Request.Headers.UserAgent.FirstOrDefault());
        RadioPadSessionCookies.Append(Response, Request, token, expiresAt, _env);

        // Audited in BOTH tenants: the practice being left needs the record of
        // the departure, and the practice being entered needs the sign-in.
        await _audit.AppendAsync(new AuditEvent
        {
            TenantId = tenant.Id,
            UserId = user.Id,
            Action = AuditAction.UserLogin,
            DetailsJson = JsonSerializer.Serialize(new { method = "tenant-switch", direction = "left", to = target.Slug }),
        }, ct);
        await _audit.AppendAsync(new AuditEvent
        {
            TenantId = target.Id,
            UserId = targetUser.Id,
            Action = AuditAction.UserLogin,
            DetailsJson = JsonSerializer.Serialize(new { method = "tenant-switch", direction = "entered", from = tenant.Slug }),
        }, ct);

        return Ok(new
        {
            token,
            tenant = target.Slug,
            user = targetUser.Email,
            displayName = target.DisplayName,
            role = targetUser.Role.ToString(),
        });
    }
}
