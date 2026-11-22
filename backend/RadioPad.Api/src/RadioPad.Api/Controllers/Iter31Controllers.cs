using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RadioPad.Api.Auth;
using RadioPad.Application.Abstractions;
using RadioPad.Application.Security;
using RadioPad.Application.Services;
using RadioPad.Domain.Entities;
using RadioPad.Domain.Enums;
using RadioPad.Infrastructure.Identity;
using RadioPad.Infrastructure.Persistence;

namespace RadioPad.Api.Controllers;

/// <summary>
/// Iter-31 AI-009 — per-tenant overrides for rulebook prompt blocks.
/// Allows a Medical Director or Reporting Admin to override the
/// <c>system</c>, <c>impression</c>, <c>dictation_cleanup</c>,
/// <c>follow_up</c>, etc. prompt blocks for a specific rulebook id without
/// re-importing the rulebook YAML.
/// </summary>
[ApiController]
[Route("api/prompts/overrides")]
public class PromptOverridesController : TenantedController
{
    private readonly RadioPadDbContext _db;
    private readonly IAuditLog _audit;
    public PromptOverridesController(RadioPadDbContext db, IAuditLog audit)
    {
        _db = db;
        _audit = audit;
    }

    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        var (tenant, user) = await ResolveContextAsync(_db, ct);
        var deny = RequirePermission(user, RbacPermission.PromptOverridesManage);
        if (deny is not null) return deny;
        var rows = await _db.PromptOverrides
            .Where(p => p.TenantId == tenant.Id)
            .OrderBy(p => p.RulebookId).ThenBy(p => p.BlockKey)
            .ToListAsync(ct);
        return Ok(rows.Select(p => new
        {
            p.Id, p.RulebookId, p.BlockKey, p.Body,
            status = p.Status.ToString(),
            p.ApprovedByUserId, p.ApprovedAt,
            p.UpdatedAt,
        }));
    }

    public record SaveOverrideDto(Guid? Id, string RulebookId, string BlockKey, string Body);

    [HttpPost]
    public async Task<IActionResult> Save([FromBody] SaveOverrideDto dto, CancellationToken ct)
    {
        var (tenant, user) = await ResolveContextAsync(_db, ct);
        var deny = RequirePermission(user, RbacPermission.PromptOverridesManage);
        if (deny is not null) return deny;
        if (string.IsNullOrWhiteSpace(dto.RulebookId) || string.IsNullOrWhiteSpace(dto.BlockKey))
            return BadRequest(new { error = "rulebookId and blockKey are required.", kind = "validation" });

        // Upsert by (tenant, rulebookId, blockKey). Iter-32 AI-009: any save
        // (insert OR edit) drops the row back to Draft so the body must be
        // re-approved before it takes effect at AI-runtime.
        var row = await _db.PromptOverrides.FirstOrDefaultAsync(
            p => p.TenantId == tenant.Id
              && p.RulebookId == dto.RulebookId
              && p.BlockKey == dto.BlockKey, ct);
        if (row is null)
        {
            row = new PromptOverride
            {
                TenantId = tenant.Id,
                RulebookId = dto.RulebookId.Trim(),
                BlockKey = dto.BlockKey.Trim(),
            };
            _db.PromptOverrides.Add(row);
        }
        row.Body = dto.Body ?? string.Empty;
        row.Status = PromptOverrideStatus.Draft;
        row.ApprovedByUserId = null;
        row.ApprovedAt = null;
        row.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(ct);
        return Ok(new { row.Id, status = row.Status.ToString() });
    }

    /// <summary>
    /// Iter-32 AI-009 — promote a Draft override to Approved. Restricted to
    /// MedicalDirector to enforce clinical-governance separation of duties
    /// (the ReportingAdmin who edits the body cannot also approve it).
    /// </summary>
    [HttpPost("{id:guid}/approve")]
    public async Task<IActionResult> Approve(Guid id, CancellationToken ct)
    {
        var (tenant, user) = await ResolveContextAsync(_db, ct);
        var deny = RequirePermission(user, RbacPermission.PromptOverridesApprove);
        if (deny is not null) return deny;
        var row = await _db.PromptOverrides.FirstOrDefaultAsync(
            p => p.Id == id && p.TenantId == tenant.Id, ct);
        if (row is null) return NotFound();
        row.Status = PromptOverrideStatus.Approved;
        row.ApprovedByUserId = user.Id;
        row.ApprovedAt = DateTimeOffset.UtcNow;
        row.UpdatedAt = row.ApprovedAt.Value;
        await _db.SaveChangesAsync(ct);
        await _audit.AppendAsync(new AuditEvent
        {
            TenantId = tenant.Id,
            UserId = user.Id,
            Action = AuditAction.PromptOverrideApproved,
            DetailsJson = System.Text.Json.JsonSerializer.Serialize(new
            {
                overrideId = row.Id,
                rulebookId = row.RulebookId,
                blockKey = row.BlockKey,
                bodyHash = Sha256(row.Body),
            }),
        }, ct);
        return Ok(new { row.Id, status = row.Status.ToString(), row.ApprovedAt });
    }

    private static string Sha256(string s)
    {
        var bytes = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(s ?? ""));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var (tenant, user) = await ResolveContextAsync(_db, ct);
        var deny = RequirePermission(user, RbacPermission.PromptOverridesManage);
        if (deny is not null) return deny;
        var row = await _db.PromptOverrides.FirstOrDefaultAsync(p => p.Id == id && p.TenantId == tenant.Id, ct);
        if (row is null) return NotFound();
        _db.PromptOverrides.Remove(row);
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }
}

/// <summary>
/// PRD §16.4 — Prompt Studio golden-case test endpoint. Runs the
/// validation packs associated with a rulebook against the current prompt
/// overrides to produce pass/fail results per golden case.
/// </summary>
[ApiController]
[Route("api/prompts")]
public class PromptStudioController : TenantedController
{
    private readonly RadioPadDbContext _db;
    private readonly ReportingService _reporting;
    private readonly RadioPad.Api.Services.ValidationPackService _packs;
    public PromptStudioController(RadioPadDbContext db, ReportingService reporting, RadioPad.Api.Services.ValidationPackService packs)
    {
        _db = db;
        _reporting = reporting;
        _packs = packs;
    }

    public record TestGoldenDto(string RulebookId, Guid? PromptOverrideId);

    public record TestValidateDto(Guid RulebookId, string? Findings, string? Impression, Guid? PromptOverrideId);

    /// <summary>
    /// POST /api/prompts/validate — dry-run validation for the Prompt Studio
    /// Test Runner. Validates the supplied sample findings against the selected
    /// rulebook using a TRANSIENT, in-memory report that is never added to the
    /// DbContext and never saved — no report row is created or modified.
    /// Returns the same ValidationResult shape as
    /// <c>POST /api/reports/{id}/validate</c>.
    /// </summary>
    [HttpPost("validate")]
    public async Task<IActionResult> Validate([FromBody] TestValidateDto dto, CancellationToken ct)
    {
        var (tenant, user) = await ResolveContextAsync(_db, ct);
        var deny = RequirePermission(user, RbacPermission.PromptOverridesManage);
        if (deny is not null) return deny;

        // Transient report — bound to the selected rulebook (resolved by GUID),
        // carrying the sample findings. Built in memory; never persisted.
        var report = new Report
        {
            TenantId = tenant.Id,
            CreatedByUserId = user.Id,
            RulebookId = dto.RulebookId,
            Status = ReportStatus.Draft,
            Findings = dto.Findings ?? string.Empty,
            Impression = dto.Impression ?? string.Empty,
            Study = new StudyContext(),
        };

        var lexicon = await _db.Lexicons.Where(l => l.TenantId == tenant.Id).ToListAsync(ct);
        var settings = await _db.TenantSettings.FirstOrDefaultAsync(s => s.TenantId == tenant.Id, ct);
        var result = await _reporting.ValidateAsync(tenant, report, lexicon, settings, ct);
        return Ok(result);
    }

    /// <summary>
    /// POST /api/prompts/test-golden — run golden cases from the validation
    /// pack(s) for the given rulebook. Returns an array of per-case results
    /// with expected vs actual rules and pass/fail status.
    /// </summary>
    [HttpPost("test-golden")]
    public async Task<IActionResult> TestGolden([FromBody] TestGoldenDto dto, CancellationToken ct)
    {
        var (tenant, user) = await ResolveContextAsync(_db, ct);
        var deny = RequirePermission(user, RbacPermission.PromptOverridesManage);
        if (deny is not null) return deny;

        // Find validation packs for this rulebook
        var packs = await _db.ValidationPacks
            .Where(vp => vp.TenantId == tenant.Id && vp.RulebookId == dto.RulebookId)
            .OrderByDescending(vp => vp.CreatedAt)
            .ToListAsync(ct);

        if (packs.Count == 0)
            return Ok(Array.Empty<object>());

        // Every case runs through the real ReportValidator against the rulebook. The validator does
        // not read prompt overrides (they shape generation, which is not re-run here), so
        // PromptOverrideId does not change these results.
        var results = new List<object>();
        foreach (var pack in packs)
        {
            IReadOnlyList<RadioPad.Api.Services.ValidationPackService.GoldenCaseResultDto> evaluated;
            try
            {
                evaluated = await _packs.EvaluateAsync(tenant, pack, ct);
            }
            catch (Exception ex) when (ex is System.Text.Json.JsonException or KeyNotFoundException or InvalidOperationException)
            {
                // A malformed pack (bad JSON / a case without a "report") is skipped, as before.
                continue;
            }

            foreach (var c in evaluated)
            {
                results.Add(new
                {
                    caseName = c.CaseName,
                    passed = c.Passed,
                    expectedRules = c.ExpectedRules,
                    actualRules = c.ActualRules,
                    qualityScore = c.QualityScore / 100.0,
                });
            }
        }

        return Ok(results);
    }
}

/// <summary>
/// Iter-31 AUTH-006 — admin-managed user lockout. Flips
/// <see cref="User.IsActive"/>; sign-in and tenant-scoped queries already
/// honour the flag. Audit <see cref="AuditAction.UserLockedOut"/> /
/// <see cref="AuditAction.UserUnlocked"/>.
/// </summary>
[ApiController]
[Route("api/users")]
public class UsersController : TenantedController
{
    private readonly RadioPadDbContext _db;
    private readonly IAuditLog _audit;
    private readonly IEmailSender _email;
    public UsersController(RadioPadDbContext db, IAuditLog audit, IEmailSender email) { _db = db; _audit = audit; _email = email; }

    public record CreateUserDto(string Email, string? DisplayName, string Role, string? TempPassword);
    public record UpdateUserDto(string? DisplayName, string? Role, bool? IsActive);
    public record ResetPasswordDto(string? TempPassword);

    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        var (tenant, user) = await ResolveContextAsync(_db, ct);
        var deny = RequirePermission(user, RbacPermission.UsersRead);
        if (deny is not null) return deny;
        var now = DateTimeOffset.UtcNow;
        var rows = await _db.Users
            .Where(u => u.TenantId == tenant.Id)
            .OrderBy(u => u.Email)
            .Select(u => new
            {
                u.Id, u.Email, u.DisplayName, role = u.Role.ToString(), u.IsActive,
                u.MfaEnabled, u.LockedUntil,
                locked = u.LockedUntil != null && u.LockedUntil > now,
            })
            .ToListAsync(ct);
        return Ok(rows);
    }

    /// <summary>Available roles for the admin role picker, with permission counts.</summary>
    [HttpGet("roles")]
    public async Task<IActionResult> Roles(CancellationToken ct)
    {
        var (_, user) = await ResolveContextAsync(_db, ct);
        var deny = RequirePermission(user, RbacPermission.UsersRead);
        if (deny is not null) return deny;
        var roles = Enum.GetValues<UserRole>()
            .Select(r => new { value = r.ToString(), permissions = RolePermissionMap.ForRole(r).Count })
            .ToArray();
        return Ok(new { roles });
    }

    /// <summary>
    /// Master-admin user creation. Issues a temporary password (generated when
    /// not supplied) and leaves MFA un-enrolled so the new user is forced through
    /// TOTP setup on first sign-in. The temp password is returned exactly once.
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateUserDto dto, CancellationToken ct)
    {
        var (tenant, actor) = await ResolveContextAsync(_db, ct);
        var deny = RequirePermission(actor, RbacPermission.UsersManage);
        if (deny is not null) return deny;

        var email = (dto.Email ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(email) || !email.Contains('@') || email.Length > 254)
            return BadRequest(new { error = "A valid email is required.", kind = "validation" });
        if (!Enum.TryParse<UserRole>(dto.Role, ignoreCase: true, out var role))
            return BadRequest(new { error = "Unknown role.", kind = "validation" });
        if (await _db.Users.AnyAsync(u => u.TenantId == tenant.Id && u.Email == email, ct))
            return Conflict(new { error = "A user with that email already exists.", kind = "conflict" });

        var tempPassword = string.IsNullOrWhiteSpace(dto.TempPassword)
            ? PasswordHasher.GenerateTemporaryPassword()
            : dto.TempPassword!.Trim();
        if (tempPassword.Length < PasswordHasher.MinLength)
            return BadRequest(new { error = $"Temporary password must be at least {PasswordHasher.MinLength} characters.", kind = "validation" });

        var created = new User
        {
            TenantId = tenant.Id,
            Email = email,
            DisplayName = string.IsNullOrWhiteSpace(dto.DisplayName) ? email : dto.DisplayName!.Trim(),
            Role = role,
            PasswordHash = PasswordHasher.Hash(tempPassword),
            IsActive = true,
            MfaEnabled = false,
        };
        _db.Users.Add(created);
        await _db.SaveChangesAsync(ct);
        await EnterpriseIdentityBridge.EnsureMembershipForUserAsync(_db, created, ct);
        await _audit.AppendAsync(new AuditEvent
        {
            TenantId = tenant.Id,
            UserId = actor.Id,
            Action = AuditAction.UserCreated,
            DetailsJson = System.Text.Json.JsonSerializer.Serialize(new
            {
                targetUserId = created.Id,
                targetEmail = created.Email,
                role = role.ToString(),
            }),
        }, ct);
        // Best-effort invite: tells the new user they have an account and where to sign
        // in. It never carries the temporary password — the admin hands that over
        // separately — and a mail failure must not fail the creation.
        var inviteSent = await TrySendInviteAsync(created.Email, tenant.DisplayName, ct);
        return Ok(new { id = created.Id, email = created.Email, displayName = created.DisplayName, role = role.ToString(), tempPassword, inviteSent });
    }

    private async Task<bool> TrySendInviteAsync(string to, string orgName, CancellationToken ct)
    {
        orgName = orgName.ReplaceLineEndings(" "); // it goes into the Subject header
        const string download = "https://radiopadstudio.com/download";
        var plain = $"You have been added to \"{orgName}\" on RadioPad.\r\n\r\n"
            + $"1. Download the desktop app: {download}\r\n"
            + $"2. Sign in with this email address ({to}) and the temporary password your administrator gives you.\r\n"
            + "3. Set up your authenticator app when prompted.\r\n\r\n"
            + "This email does not contain your password. If you were not expecting it, you can ignore it.\r\n\r\n-- RadioPad\r\n";
        var org = System.Net.WebUtility.HtmlEncode(orgName);
        var html = $"<p>You have been added to <strong>{org}</strong> on RadioPad.</p>"
            + $"<ol><li><a href=\"{download}\">Download the desktop app</a></li>"
            + $"<li>Sign in with this email address ({System.Net.WebUtility.HtmlEncode(to)}) and the temporary password your administrator gives you.</li>"
            + "<li>Set up your authenticator app when prompted.</li></ol>"
            + "<p>This email does not contain your password. If you were not expecting it, you can ignore it.</p>";
        try
        {
            return await _email.SendAsync(new EmailMessage(To: to, Subject: $"You have been added to {orgName} on RadioPad", HtmlBody: html, PlainBody: plain), ct);
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>Master-admin profile/role/active-state update.</summary>
    [HttpPatch("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateUserDto dto, CancellationToken ct)
    {
        var (tenant, actor) = await ResolveContextAsync(_db, ct);
        var deny = RequirePermission(actor, RbacPermission.UsersManage);
        if (deny is not null) return deny;
        var target = await _db.Users.FirstOrDefaultAsync(u => u.Id == id && u.TenantId == tenant.Id, ct);
        if (target is null) return NotFound();

        var changes = new List<string>();
        if (!string.IsNullOrWhiteSpace(dto.DisplayName) && dto.DisplayName.Trim() != target.DisplayName)
        {
            target.DisplayName = dto.DisplayName.Trim();
            changes.Add("displayName");
        }
        if (dto.Role is not null)
        {
            if (!Enum.TryParse<UserRole>(dto.Role, ignoreCase: true, out var role))
                return BadRequest(new { error = "Unknown role.", kind = "validation" });
            if (target.Id == actor.Id && role != actor.Role)
                return BadRequest(new { error = "You cannot change your own role.", kind = "validation" });
            if (target.Role != role) { target.Role = role; changes.Add("role"); }
        }
        if (dto.IsActive is bool active)
        {
            if (target.Id == actor.Id && !active)
                return BadRequest(new { error = "You cannot deactivate yourself.", kind = "validation" });
            if (target.IsActive != active)
            {
                target.IsActive = active;
                if (active) { target.LockedUntil = null; target.FailedLoginCount = 0; target.FailedLoginWindowStart = null; }
                changes.Add("isActive");
            }
        }

        if (changes.Count == 0) return Ok(new { id = target.Id, changed = false });
        target.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(ct);
        await _audit.AppendAsync(new AuditEvent
        {
            TenantId = tenant.Id,
            UserId = actor.Id,
            Action = AuditAction.UserUpdated,
            DetailsJson = System.Text.Json.JsonSerializer.Serialize(new
            {
                targetUserId = target.Id,
                targetEmail = target.Email,
                fields = changes,
            }),
        }, ct);
        return Ok(new { id = target.Id, changed = true, fields = changes });
    }

    /// <summary>
    /// Master-admin user removal — soft-delete (deprovision). The row is kept so
    /// audit-log and report references stay intact; the account is deactivated
    /// and every session is revoked.
    /// </summary>
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var (tenant, actor) = await ResolveContextAsync(_db, ct);
        var deny = RequirePermission(actor, RbacPermission.UsersManage);
        if (deny is not null) return deny;
        if (id == actor.Id)
            return BadRequest(new { error = "You cannot delete yourself.", kind = "validation" });
        var target = await _db.Users.FirstOrDefaultAsync(u => u.Id == id && u.TenantId == tenant.Id, ct);
        if (target is null) return NotFound();

        target.IsActive = false;
        target.SessionEpoch += 1;
        target.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(ct);
        await _audit.AppendAsync(new AuditEvent
        {
            TenantId = tenant.Id,
            UserId = actor.Id,
            Action = AuditAction.UserDeleted,
            DetailsJson = System.Text.Json.JsonSerializer.Serialize(new
            {
                targetUserId = target.Id,
                targetEmail = target.Email,
                mode = "soft",
            }),
        }, ct);
        return Ok(new { id = target.Id, deactivated = true });
    }

    /// <summary>
    /// Master-admin password reset. Sets a temporary password (generated when not
    /// supplied), revokes all sessions, and returns the temp password once for
    /// hand-off. No email is sent.
    /// </summary>
    [HttpPost("{id:guid}/reset-password")]
    public async Task<IActionResult> ResetPassword(Guid id, [FromBody] ResetPasswordDto? dto, CancellationToken ct)
    {
        var (tenant, actor) = await ResolveContextAsync(_db, ct);
        var deny = RequirePermission(actor, RbacPermission.UsersManage);
        if (deny is not null) return deny;
        var target = await _db.Users.FirstOrDefaultAsync(u => u.Id == id && u.TenantId == tenant.Id, ct);
        if (target is null) return NotFound();

        var temp = string.IsNullOrWhiteSpace(dto?.TempPassword)
            ? PasswordHasher.GenerateTemporaryPassword()
            : dto!.TempPassword!.Trim();
        if (temp.Length < PasswordHasher.MinLength)
            return BadRequest(new { error = $"Temporary password must be at least {PasswordHasher.MinLength} characters.", kind = "validation" });

        target.PasswordHash = PasswordHasher.Hash(temp);
        target.SessionEpoch += 1;
        target.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(ct);
        await _audit.AppendAsync(new AuditEvent
        {
            TenantId = tenant.Id,
            UserId = actor.Id,
            Action = AuditAction.PasswordChanged,
            DetailsJson = System.Text.Json.JsonSerializer.Serialize(new
            {
                scope = "admin-reset",
                targetUserId = target.Id,
                targetEmail = target.Email,
                newEpoch = target.SessionEpoch,
            }),
        }, ct);
        return Ok(new { id = target.Id, tempPassword = temp });
    }

    /// <summary>
    /// Master-admin MFA reset — clears the enrolled TOTP secret so the user is
    /// forced to re-enroll an authenticator on their next sign-in. Sessions are
    /// revoked.
    /// </summary>
    [HttpPost("{id:guid}/reset-mfa")]
    public async Task<IActionResult> ResetMfa(Guid id, CancellationToken ct)
    {
        var (tenant, actor) = await ResolveContextAsync(_db, ct);
        var deny = RequirePermission(actor, RbacPermission.UsersManage);
        if (deny is not null) return deny;
        var target = await _db.Users.FirstOrDefaultAsync(u => u.Id == id && u.TenantId == tenant.Id, ct);
        if (target is null) return NotFound();

        target.MfaSecret = string.Empty;
        target.MfaEnabled = false;
        target.SessionEpoch += 1;
        target.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(ct);
        await _audit.AppendAsync(new AuditEvent
        {
            TenantId = tenant.Id,
            UserId = actor.Id,
            Action = AuditAction.UserMfaReset,
            DetailsJson = System.Text.Json.JsonSerializer.Serialize(new
            {
                targetUserId = target.Id,
                targetEmail = target.Email,
            }),
        }, ct);
        return Ok(new { id = target.Id, mfaEnabled = false });
    }

    [HttpPost("{id:guid}/lockout")]
    public async Task<IActionResult> Lockout(Guid id, CancellationToken ct) =>
        await SetActiveAsync(id, active: false, ct);

    [HttpPost("{id:guid}/unlock")]
    public async Task<IActionResult> Unlock(Guid id, CancellationToken ct) =>
        await SetActiveAsync(id, active: true, ct);

    /// <summary>
    /// Iter-32 AUTH-006 — invalidate every currently issued bearer for the
    /// target user by bumping <see cref="User.SessionEpoch"/>. The new epoch
    /// is folded into the HMAC seed of every minted token, so existing
    /// bearers no longer match. Audited as
    /// <see cref="AuditAction.SessionsRevoked"/>.
    /// </summary>
    [HttpPost("{id:guid}/revoke-sessions")]
    public async Task<IActionResult> RevokeSessions(Guid id, CancellationToken ct)
    {
        var (tenant, user) = await ResolveContextAsync(_db, ct);
        var deny = RequirePermission(user, RbacPermission.UsersRevokeSessions);
        if (deny is not null) return deny;
        var target = await _db.Users.FirstOrDefaultAsync(u => u.Id == id && u.TenantId == tenant.Id, ct);
        if (target is null) return NotFound();
        target.SessionEpoch += 1;
        target.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(ct);
        await _audit.AppendAsync(new AuditEvent
        {
            TenantId = tenant.Id,
            UserId = user.Id,
            Action = AuditAction.SessionsRevoked,
            DetailsJson = System.Text.Json.JsonSerializer.Serialize(new
            {
                targetUserId = target.Id,
                targetEmail = target.Email,
                newEpoch = target.SessionEpoch,
            }),
        }, ct);
        return Ok(new { id = target.Id, sessionEpoch = target.SessionEpoch });
    }

    private async Task<IActionResult> SetActiveAsync(Guid id, bool active, CancellationToken ct)
    {
        var (tenant, user) = await ResolveContextAsync(_db, ct);
        var deny = RequirePermission(user, RbacPermission.UsersManage);
        if (deny is not null) return deny;
        var target = await _db.Users.FirstOrDefaultAsync(u => u.Id == id && u.TenantId == tenant.Id, ct);
        if (target is null) return NotFound();
        if (target.Id == user.Id && !active)
            return BadRequest(new { error = "Cannot lock yourself out.", kind = "validation" });
        if (target.IsActive == active)
        {
            return Ok(new { id = target.Id, isActive = target.IsActive, changed = false });
        }
        target.IsActive = active;
        target.UpdatedAt = DateTimeOffset.UtcNow;
        if (active)
        {
            // Iter-32 AUTH-006 — explicit unlock also clears the
            // sliding-window failure counter and the time-based lock so
            // the user can sign in immediately.
            target.LockedUntil = null;
            target.FailedLoginCount = 0;
            target.FailedLoginWindowStart = null;
        }
        await _db.SaveChangesAsync(ct);
        await _audit.AppendAsync(new AuditEvent
        {
            TenantId = tenant.Id,
            UserId = user.Id,
            Action = active ? AuditAction.UserUnlocked : AuditAction.UserLockedOut,
            DetailsJson = System.Text.Json.JsonSerializer.Serialize(new
            {
                targetUserId = target.Id,
                targetEmail = target.Email,
            }),
        }, ct);
        return Ok(new { id = target.Id, isActive = target.IsActive, changed = true });
    }
}

/// <summary>
/// Iter-31 AUTH-007 — paired devices (CLI + desktop shells) registered via
/// the OAuth 2.0 device authorization grant. Lists currently approved or
/// consumed pairings and lets an admin revoke one.
/// </summary>
[ApiController]
[Route("api/devices")]
public class DevicesController : TenantedController
{
    private readonly RadioPadDbContext _db;
    public DevicesController(RadioPadDbContext db) => _db = db;

    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        var (tenant, user) = await ResolveContextAsync(_db, ct);
        var deny = RequireRole(user, UserRole.ItAdmin, UserRole.MedicalDirector);
        if (deny is not null) return deny;
        var rows = await _db.DeviceAuth
            .Where(d => d.TenantId == tenant.Id)
            .OrderByDescending(d => d.UpdatedAt)
            .Select(d => new
            {
                d.Id,
                d.UserCode,
                d.Status,
                d.UserId,
                d.ExpiresAt,
                d.LastPolledAt,
                d.DeviceFingerprint,
                d.UpdatedAt,
            })
            .ToListAsync(ct);
        return Ok(rows);
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Revoke(Guid id, CancellationToken ct)
    {
        var (tenant, user) = await ResolveContextAsync(_db, ct);
        var deny = RequireRole(user, UserRole.ItAdmin, UserRole.MedicalDirector);
        if (deny is not null) return deny;
        var row = await _db.DeviceAuth.FirstOrDefaultAsync(d => d.Id == id && d.TenantId == tenant.Id, ct);
        if (row is null) return NotFound();
        // Mark as denied so any token-poll path returns access_denied; we keep
        // the row so it remains visible in the audit / device list.
        row.Status = "denied";
        row.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }
}
