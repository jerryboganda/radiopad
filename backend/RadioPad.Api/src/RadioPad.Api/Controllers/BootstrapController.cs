using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RadioPad.Api.Auth;
using RadioPad.Application.Abstractions;
using RadioPad.Domain.Entities;
using RadioPad.Domain.Enums;
using RadioPad.Infrastructure.Identity;
using RadioPad.Infrastructure.Persistence;
using RadioPad.Infrastructure.Seeding;

namespace RadioPad.Api.Controllers;

/// <summary>
/// Operator org bootstrap — <c>POST /api/admin/bootstrap-org</c>.
///
/// RadioPad org creation is super-admin / seed only: there is no public
/// self-serve sign-up and no email is ever sent. This endpoint creates a brand
/// new tenant plus its first master-admin (<see cref="UserRole.MedicalDirector"/>)
/// user with a temporary password, returned exactly once for hand-off. The admin
/// signs in with it and is forced through TOTP enrolment on first login.
///
/// It runs before any tenant/admin exists, so it cannot carry a bearer; it is
/// instead gated by a constant-time check of the <c>X-RadioPad-Bootstrap</c>
/// header against the server-side <c>RADIOPAD_BOOTSTRAP_SECRET</c> (fail-closed
/// when unset). The companion CLI command is <c>radiopad org create</c>.
/// </summary>
[ApiController]
[Route("api/admin/bootstrap-org")]
public class BootstrapController : ControllerBase
{
    private readonly RadioPadDbContext _db;
    private readonly IAuditLog _audit;
    private readonly ILogger<BootstrapController> _log;

    public BootstrapController(RadioPadDbContext db, IAuditLog audit, ILogger<BootstrapController> log)
    { _db = db; _audit = audit; _log = log; }

    public record BootstrapDto(string Slug, string Name, string AdminEmail, string? AdminName, string? TempPassword);
    public record ResetAdminDto(string Slug, string Email, string? TempPassword);

    private static readonly Regex SlugFormat = new("^[a-z0-9][a-z0-9-]{1,38}[a-z0-9]$", RegexOptions.Compiled);
    private static readonly Regex EmailFormat = new(@"^[^@\s]+@[^@\s]+\.[^@\s]+$", RegexOptions.Compiled);

    /// <summary>
    /// Constant-time check of the bootstrap secret header. Returns a non-null
    /// IActionResult to short-circuit when the secret is unconfigured or wrong.
    /// </summary>
    private IActionResult? CheckSecret()
    {
        var secret = Environment.GetEnvironmentVariable("RADIOPAD_BOOTSTRAP_SECRET");
        if (string.IsNullOrWhiteSpace(secret) || secret.Length < 16)
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new
            {
                error = "Org bootstrap is not configured. Set RADIOPAD_BOOTSTRAP_SECRET (>= 16 chars) on the server.",
                kind = "bootstrap_unconfigured",
            });

        var provided = Request.Headers["X-RadioPad-Bootstrap"].FirstOrDefault() ?? string.Empty;
        var a = Encoding.UTF8.GetBytes(provided);
        var b = Encoding.UTF8.GetBytes(secret);
        if (a.Length != b.Length || !CryptographicOperations.FixedTimeEquals(a, b))
            return Unauthorized(new { error = "Invalid bootstrap secret.", kind = "unauthenticated" });
        return null;
    }

    [HttpPost]
    public async Task<IActionResult> Bootstrap([FromBody] BootstrapDto dto, CancellationToken ct)
    {
        var gate = CheckSecret();
        if (gate is not null) return gate;

        var slug = (dto.Slug ?? string.Empty).Trim().ToLowerInvariant();
        var name = string.IsNullOrWhiteSpace(dto.Name) ? slug : dto.Name.Trim();
        var email = (dto.AdminEmail ?? string.Empty).Trim();
        var adminName = string.IsNullOrWhiteSpace(dto.AdminName) ? name + " Admin" : dto.AdminName!.Trim();

        if (!SlugFormat.IsMatch(slug))
            return BadRequest(new { error = "Slug must be 3–40 chars: lowercase letters, numbers, hyphens.", kind = "validation", field = "slug" });
        if (!EmailFormat.IsMatch(email) || email.Length > 254)
            return BadRequest(new { error = "A valid admin email is required.", kind = "validation", field = "adminEmail" });
        if (await _db.Tenants.AnyAsync(t => t.Slug == slug, ct))
            return Conflict(new { error = "That organization slug is already taken.", kind = "conflict", field = "slug" });

        var tempPassword = string.IsNullOrWhiteSpace(dto.TempPassword)
            ? PasswordHasher.GenerateTemporaryPassword()
            : dto.TempPassword!.Trim();
        if (tempPassword.Length < PasswordHasher.MinLength)
            return BadRequest(new { error = $"Temporary password must be at least {PasswordHasher.MinLength} characters.", kind = "validation" });

        var tenant = new Tenant { Slug = slug, DisplayName = name };
        _db.Tenants.Add(tenant);
        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            return Conflict(new { error = "That organization slug is already taken.", kind = "conflict", field = "slug" });
        }

        var admin = new User
        {
            TenantId = tenant.Id,
            Email = email,
            DisplayName = adminName,
            Role = UserRole.MedicalDirector,
            PasswordHash = PasswordHasher.Hash(tempPassword),
            IsActive = true,
            MfaEnabled = false,
        };
        _db.Users.Add(admin);
        _db.TenantSettings.Add(new TenantSettings
        {
            TenantId = tenant.Id,
            Plan = TenantPlan.Trial,
            TrialEndsAt = DateTimeOffset.UtcNow.AddDays(30),
        });
        await _db.SaveChangesAsync(ct);
        await EnterpriseIdentityBridge.EnsureMembershipForUserAsync(_db, admin, ct);

        // Surface the curated UBAG models (Gemini Web + DeepSeek Web) on the new org's
        // AI-models page immediately. Idempotent + isolated: never fail org bootstrap on
        // a seed/DB hiccup — the startup backfill would catch it on the next restart.
        try { await UbagPrimarySeed.EnsureCuratedPrimariesAsync(_db, tenant.Id, ct); }
        catch (Exception ex) { _log.LogWarning(ex, "UBAG primary seeding failed for bootstrapped org {Slug}", slug); }

        // Iter-36 — seed the admin Modality + BodyPart catalogs for the bootstrapped org.
        try { await CatalogSeed.EnsureCatalogAsync(_db, tenant.Id, ct); }
        catch (Exception ex) { _log.LogWarning(ex, "Catalog seeding failed for bootstrapped org {Slug}", slug); }

        await _audit.AppendAsync(new AuditEvent
        {
            TenantId = tenant.Id,
            UserId = admin.Id,
            Action = AuditAction.OrganizationCreated,
            DetailsJson = JsonSerializer.Serialize(new { slug, adminEmail = email, method = "bootstrap", plan = TenantPlan.Trial.ToString() }),
        }, ct);

        return Ok(new { slug, adminEmail = email, tempPassword });
    }

    /// <summary>
    /// Emergency master-admin reset for an existing org — sets a fresh temporary
    /// password, restores the MedicalDirector role + active state, clears any
    /// enrolled TOTP (so the admin re-enrolls), and revokes existing sessions.
    /// Creates the admin if the email is not yet a member.
    /// </summary>
    [HttpPost("reset-admin")]
    public async Task<IActionResult> ResetAdmin([FromBody] ResetAdminDto dto, CancellationToken ct)
    {
        var gate = CheckSecret();
        if (gate is not null) return gate;

        var slug = (dto.Slug ?? string.Empty).Trim().ToLowerInvariant();
        var email = (dto.Email ?? string.Empty).Trim();
        if (!EmailFormat.IsMatch(email))
            return BadRequest(new { error = "A valid admin email is required.", kind = "validation", field = "email" });

        var tenant = await _db.Tenants.FirstOrDefaultAsync(t => t.Slug == slug, ct);
        if (tenant is null) return NotFound(new { error = "Unknown organization slug.", kind = "not_found", field = "slug" });

        var tempPassword = string.IsNullOrWhiteSpace(dto.TempPassword)
            ? PasswordHasher.GenerateTemporaryPassword()
            : dto.TempPassword!.Trim();
        if (tempPassword.Length < PasswordHasher.MinLength)
            return BadRequest(new { error = $"Temporary password must be at least {PasswordHasher.MinLength} characters.", kind = "validation" });

        var user = await _db.Users.FirstOrDefaultAsync(u => u.TenantId == tenant.Id && u.Email == email, ct);
        var created = user is null;
        if (user is null)
        {
            user = new User { TenantId = tenant.Id, Email = email, DisplayName = email };
            _db.Users.Add(user);
        }
        user.Role = UserRole.MedicalDirector;
        user.IsActive = true;
        user.LockedUntil = null;
        user.FailedLoginCount = 0;
        user.FailedLoginWindowStart = null;
        user.PasswordHash = PasswordHasher.Hash(tempPassword);
        user.MfaSecret = string.Empty;
        user.MfaEnabled = false;
        user.SessionEpoch += 1;
        user.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(ct);
        await EnterpriseIdentityBridge.EnsureMembershipForUserAsync(_db, user, ct);

        await _audit.AppendAsync(new AuditEvent
        {
            TenantId = tenant.Id,
            UserId = user.Id,
            Action = AuditAction.PasswordChanged,
            DetailsJson = JsonSerializer.Serialize(new { scope = "bootstrap-reset-admin", slug = tenant.Slug, adminEmail = email, created }),
        }, ct);

        return Ok(new { slug = tenant.Slug, email, tempPassword, created });
    }
}
