using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RadioPad.Application.Security;
using RadioPad.Domain.Entities;
using RadioPad.Domain.Enums;
using RadioPad.Infrastructure.Persistence;

namespace RadioPad.Api.Controllers;

/// <summary>
/// F7 — the per-user correction dictionary (dictation brief §6). Personal find→replace entries
/// applied deterministically BEFORE the LLM, layered over the org <c>TenantLexicon</c> (the user's
/// entry wins for the same term). Scoped to the signed-in user; consumed by the dictation-draft
/// pipeline via <c>CorrectionDictionary.Resolve</c>.
/// </summary>
[ApiController]
[Route("api/user-corrections")]
public sealed class UserCorrectionsController : TenantedController
{
    private readonly RadioPadDbContext _db;

    public UserCorrectionsController(RadioPadDbContext db)
    {
        _db = db;
    }

    public record CorrectionDto(string From, string To);

    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        var (tenant, user) = await ResolveContextAsync(_db, ct);
        var rows = await _db.UserCorrections
            .Where(c => c.TenantId == tenant.Id && c.UserId == user.Id)
            .OrderBy(c => c.From)
            .Select(c => new { c.Id, c.From, c.To })
            .ToListAsync(ct);
        return Ok(rows);
    }

    /// <summary>Add or update a personal correction (idempotent on the source term).</summary>
    [HttpPost]
    public async Task<IActionResult> Upsert([FromBody] CorrectionDto dto, CancellationToken ct)
    {
        var (tenant, user) = await ResolveContextAsync(_db, ct);
        var deny = RequirePermission(user, RbacPermission.ReportsEdit);
        if (deny is not null) return deny;
        if (dto is null || string.IsNullOrWhiteSpace(dto.From) || string.IsNullOrWhiteSpace(dto.To))
            return BadRequest(new { error = "from and to are required.", kind = "validation" });

        var from = dto.From.Trim();
        var existing = await _db.UserCorrections.FirstOrDefaultAsync(
            c => c.TenantId == tenant.Id && c.UserId == user.Id && c.From == from, ct);
        if (existing is null)
        {
            existing = new UserCorrection { TenantId = tenant.Id, UserId = user.Id, From = from, To = dto.To.Trim() };
            _db.UserCorrections.Add(existing);
        }
        else
        {
            existing.To = dto.To.Trim();
        }
        await _db.SaveChangesAsync(ct);
        return Ok(new { existing.Id, existing.From, existing.To });
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var (tenant, user) = await ResolveContextAsync(_db, ct);
        var deny = RequirePermission(user, RbacPermission.ReportsEdit);
        if (deny is not null) return deny;
        var row = await _db.UserCorrections.FirstOrDefaultAsync(
            c => c.Id == id && c.TenantId == tenant.Id && c.UserId == user.Id, ct);
        if (row is null) return NotFound();
        _db.UserCorrections.Remove(row);
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }
}
