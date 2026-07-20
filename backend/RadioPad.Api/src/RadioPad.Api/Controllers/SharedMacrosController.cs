using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RadioPad.Application.Abstractions;
using RadioPad.Domain.Entities;
using RadioPad.Domain.Enums;
using RadioPad.Infrastructure.Persistence;

namespace RadioPad.Api.Controllers;

/// <summary>
/// PRD RPT-021 — tenant- and subspecialty-scoped autotext macros.
///
/// RadioPad already had per-user, device-local snippets; the PRD also requires
/// shared macros so a department publishes an agreed phrase once instead of
/// every radiologist retyping it. Reading is open to every authenticated
/// member of the tenant (macros are clinical boilerplate, and a reader who
/// could not fetch them could not dictate with them); authoring is restricted
/// to the reporting-governance roles, because a shared macro changes what
/// everyone else's expansion produces.
/// </summary>
[ApiController]
[Route("api/macros")]
public class SharedMacrosController : TenantedController
{
    private readonly RadioPadDbContext _db;
    private readonly IAuditLog _audit;

    public SharedMacrosController(RadioPadDbContext db, IAuditLog audit)
    {
        _db = db;
        _audit = audit;
    }

    public record MacroDto(
        Guid Id,
        string Trigger,
        string Body,
        string Description,
        string Scope,
        string Subspecialty,
        DateTimeOffset UpdatedAt);

    private static MacroDto ToDto(SharedMacro m) => new(
        m.Id, m.Trigger, m.Body, m.Description, m.Scope.ToString(), m.Subspecialty, m.UpdatedAt);

    /// <summary>
    /// Macros visible to the caller. <paramref name="subspecialty"/> narrows the
    /// result to tenant-wide macros plus that subspecialty's; omitting it returns
    /// every macro in the tenant (the management view).
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] string? subspecialty, CancellationToken ct)
    {
        var (tenant, _) = await ResolveContextAsync(_db, ct);
        var q = _db.SharedMacros.Where(m => m.TenantId == tenant.Id);
        if (!string.IsNullOrWhiteSpace(subspecialty))
        {
            var s = subspecialty.Trim();
            q = q.Where(m => m.Scope == MacroScope.Tenant
                || EF.Functions.Like(m.Subspecialty, s));
        }
        var rows = await q
            .OrderBy(m => m.Scope)
            .ThenBy(m => m.Trigger)
            .ToListAsync(ct);
        return Ok(rows.Select(ToDto));
    }

    public record SaveMacroDto(
        Guid? Id,
        string Trigger,
        string Body,
        string? Description,
        string? Scope,
        string? Subspecialty);

    [HttpPost]
    public async Task<IActionResult> Save([FromBody] SaveMacroDto dto, CancellationToken ct)
    {
        var (tenant, user) = await ResolveContextAsync(_db, ct);
        var deny = RequireRole(user, UserRole.MedicalDirector, UserRole.ReportingAdmin, UserRole.ItAdmin);
        if (deny is not null) return deny;

        if (dto is null || string.IsNullOrWhiteSpace(dto.Trigger))
            return BadRequest(new { error = "trigger is required.", kind = "validation" });
        if (string.IsNullOrWhiteSpace(dto.Body))
            return BadRequest(new { error = "body is required.", kind = "validation" });

        var scope = ParseScope(dto.Scope);
        var subspecialty = (dto.Subspecialty ?? "").Trim();
        if (scope == MacroScope.Subspecialty && subspecialty.Length == 0)
        {
            return BadRequest(new
            {
                error = "subspecialty is required when scope is Subspecialty.",
                kind = "validation",
            });
        }
        if (scope == MacroScope.Tenant) subspecialty = "";

        var trigger = dto.Trigger.Trim();
        SharedMacro row;
        if (dto.Id is { } id)
        {
            row = await _db.SharedMacros.FirstOrDefaultAsync(m => m.Id == id && m.TenantId == tenant.Id, ct)
                is { } found
                ? found
                : throw new KeyNotFoundException("macro_not_found");
        }
        else
        {
            // A duplicate (scope, subspecialty, trigger) would make expansion
            // non-deterministic, so an existing row with the same key is updated
            // rather than shadowed.
            row = await _db.SharedMacros.FirstOrDefaultAsync(
                m => m.TenantId == tenant.Id
                     && m.Scope == scope
                     && m.Subspecialty == subspecialty
                     && m.Trigger == trigger, ct)
                ?? NewRow(tenant.Id, user.Id);
        }

        row.Trigger = trigger;
        row.Body = dto.Body;
        row.Description = (dto.Description ?? "").Trim();
        row.Scope = scope;
        row.Subspecialty = subspecialty;
        row.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(ct);

        await _audit.AppendAsync(new AuditEvent
        {
            TenantId = tenant.Id,
            UserId = user.Id,
            Action = AuditAction.MacroChanged,
            DetailsJson = System.Text.Json.JsonSerializer.Serialize(new
            {
                macroId = row.Id,
                trigger = row.Trigger,
                scope = row.Scope.ToString(),
                subspecialty = row.Subspecialty,
                action = dto.Id is null ? "created" : "updated",
            }),
        }, ct);

        return Ok(ToDto(row));
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var (tenant, user) = await ResolveContextAsync(_db, ct);
        var deny = RequireRole(user, UserRole.MedicalDirector, UserRole.ReportingAdmin, UserRole.ItAdmin);
        if (deny is not null) return deny;

        var row = await _db.SharedMacros.FirstOrDefaultAsync(m => m.Id == id && m.TenantId == tenant.Id, ct);
        if (row is null) return NotFound();

        _db.SharedMacros.Remove(row);
        await _db.SaveChangesAsync(ct);
        await _audit.AppendAsync(new AuditEvent
        {
            TenantId = tenant.Id,
            UserId = user.Id,
            Action = AuditAction.MacroChanged,
            DetailsJson = System.Text.Json.JsonSerializer.Serialize(new
            {
                macroId = id,
                trigger = row.Trigger,
                action = "deleted",
            }),
        }, ct);
        return NoContent();
    }

    private SharedMacro NewRow(Guid tenantId, Guid userId)
    {
        var row = new SharedMacro { TenantId = tenantId, CreatedByUserId = userId };
        _db.SharedMacros.Add(row);
        return row;
    }

    private static MacroScope ParseScope(string? raw) =>
        string.Equals(raw?.Trim(), "Subspecialty", StringComparison.OrdinalIgnoreCase)
            ? MacroScope.Subspecialty
            : MacroScope.Tenant;
}
