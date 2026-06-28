using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RadioPad.Application.Abstractions;
using RadioPad.Application.Security;
using RadioPad.Domain.Entities;
using RadioPad.Domain.Enums;
using RadioPad.Infrastructure.Persistence;

namespace RadioPad.Api.Controllers;

/// <summary>
/// Iter-36 — admin CRUD for the tenant-scoped imaging-modality catalog. Reads are
/// open to any authenticated tenant user (the reporting UI populates dropdowns from
/// them, mirroring <see cref="TemplatesController"/>); mutations require
/// <see cref="RbacPermission.ModalitiesManage"/>.
/// </summary>
[ApiController]
[Route("api/modalities")]
public class ModalitiesController : TenantedController
{
    private readonly RadioPadDbContext _db;
    public ModalitiesController(RadioPadDbContext db) { _db = db; }

    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        var (tenant, _) = await ResolveContextAsync(_db, ct);
        var rows = await _db.Modalities
            .Where(m => m.TenantId == tenant.Id)
            .OrderBy(m => m.SortOrder).ThenBy(m => m.Code)
            .ToListAsync(ct);
        return Ok(rows);
    }

    // Nullable fields so an empty body is model-valid: the permission gate must run
    // before any field validation (RBAC matrix enforcement relies on this).
    public record SaveModalityDto(string? Code, string? Name, bool? Active, int? SortOrder);

    [HttpPost]
    public async Task<IActionResult> Save([FromBody] SaveModalityDto dto, CancellationToken ct)
    {
        var (tenant, user) = await ResolveContextAsync(_db, ct);
        var deny = RequirePermission(user, RbacPermission.ModalitiesManage);
        if (deny is not null) return deny;

        var code = (dto.Code ?? "").Trim();
        if (code.Length == 0) return BadRequest(new { error = "Code is required.", kind = "validation" });

        var existing = await _db.Modalities.FirstOrDefaultAsync(
            m => m.TenantId == tenant.Id && m.Code == code, ct);
        if (existing is null)
        {
            existing = new Modality { TenantId = tenant.Id, Code = code };
            _db.Modalities.Add(existing);
        }
        existing.Name = (dto.Name ?? "").Trim();
        if (dto.Active is not null) existing.Active = dto.Active.Value;
        if (dto.SortOrder is not null) existing.SortOrder = dto.SortOrder.Value;
        existing.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(ct);
        return Ok(existing);
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var (tenant, user) = await ResolveContextAsync(_db, ct);
        var deny = RequirePermission(user, RbacPermission.ModalitiesManage);
        if (deny is not null) return deny;
        var row = await _db.Modalities.FirstOrDefaultAsync(m => m.Id == id && m.TenantId == tenant.Id, ct);
        if (row is null) return NotFound();
        _db.Modalities.Remove(row);
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }
}

/// <summary>
/// Iter-36 — admin CRUD for the tenant-scoped body-part catalog. Mirrors
/// <see cref="ModalitiesController"/>; mutations require
/// <see cref="RbacPermission.BodyPartsManage"/>.
/// </summary>
[ApiController]
[Route("api/body-parts")]
public class BodyPartsController : TenantedController
{
    private readonly RadioPadDbContext _db;
    public BodyPartsController(RadioPadDbContext db) { _db = db; }

    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        var (tenant, _) = await ResolveContextAsync(_db, ct);
        var rows = await _db.BodyParts
            .Where(b => b.TenantId == tenant.Id)
            .OrderBy(b => b.SortOrder).ThenBy(b => b.Code)
            .ToListAsync(ct);
        return Ok(rows);
    }

    public record SaveBodyPartDto(string? Code, string? Name, bool? Active, int? SortOrder);

    [HttpPost]
    public async Task<IActionResult> Save([FromBody] SaveBodyPartDto dto, CancellationToken ct)
    {
        var (tenant, user) = await ResolveContextAsync(_db, ct);
        var deny = RequirePermission(user, RbacPermission.BodyPartsManage);
        if (deny is not null) return deny;

        var code = (dto.Code ?? "").Trim();
        if (code.Length == 0) return BadRequest(new { error = "Code is required.", kind = "validation" });

        var existing = await _db.BodyParts.FirstOrDefaultAsync(
            b => b.TenantId == tenant.Id && b.Code == code, ct);
        if (existing is null)
        {
            existing = new BodyPart { TenantId = tenant.Id, Code = code };
            _db.BodyParts.Add(existing);
        }
        existing.Name = (dto.Name ?? "").Trim();
        if (dto.Active is not null) existing.Active = dto.Active.Value;
        if (dto.SortOrder is not null) existing.SortOrder = dto.SortOrder.Value;
        existing.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(ct);
        return Ok(existing);
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var (tenant, user) = await ResolveContextAsync(_db, ct);
        var deny = RequirePermission(user, RbacPermission.BodyPartsManage);
        if (deny is not null) return deny;
        var row = await _db.BodyParts.FirstOrDefaultAsync(b => b.Id == id && b.TenantId == tenant.Id, ct);
        if (row is null) return NotFound();
        _db.BodyParts.Remove(row);
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }
}
