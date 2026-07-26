using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RadioPad.Api.Services;
using RadioPad.Api.Services.ReportLayouts;
using RadioPad.Application.Abstractions;
using RadioPad.Domain.Entities;
using RadioPad.Domain.Enums;
using RadioPad.Infrastructure.Persistence;

namespace RadioPad.Api.Controllers;

/// <summary>
/// Report Templates (RPT-030) — CRUD for radiologist-authored output-document
/// designs, the caller's chosen default, and sample-data previews. Mirrors
/// <see cref="FindingsLibraryController"/>: reading and authoring are open to
/// every tenant member (a personal design is how a radiologist shapes their own
/// exported document), but editing or deleting someone else's row is restricted
/// to its author plus the reporting-governance roles — <see cref="CanMutate"/>.
///
/// The tenant admin's "recommended" layout is NOT set here — it rides the
/// existing <c>POST /api/tenant/settings</c> (<see cref="RbacPermission.TenantSettingsManage"/>),
/// alongside every other tenant-wide policy toggle.
/// </summary>
[ApiController]
[Route("api/report-layouts")]
public class ReportLayoutsController : TenantedController
{
    private readonly RadioPadDbContext _db;
    private readonly IAuditLog _audit;

    public ReportLayoutsController(RadioPadDbContext db, IAuditLog audit)
    {
        _db = db;
        _audit = audit;
    }

    // ------------------------------------------------------------------- DTOs

    public record LayoutDto(
        Guid Id,
        string Name,
        string? Description,
        string LayoutJson,
        int SchemaVersion,
        Guid CreatedByUserId,
        string CreatedByName,
        DateTimeOffset CreatedAt,
        DateTimeOffset UpdatedAt);

    public record ListResponseDto(IReadOnlyList<LayoutDto> Items, Guid? RecommendedId, Guid? MyDefaultId);

    public record SaveLayoutDto(string Name, string? Description, string LayoutJson);

    public record SetDefaultDto(Guid? LayoutId);

    public record PreviewDto(string LayoutJson);

    // -------------------------------------------------------------------- list/get

    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        var (tenant, user) = await ResolveContextAsync(_db, ct);

        var rows = await _db.ReportLayouts
            .Where(r => r.TenantId == tenant.Id)
            .OrderBy(r => r.Name)
            .ToListAsync(ct);

        var authorIds = rows.Select(r => r.CreatedByUserId).Distinct().ToList();
        var names = await _db.Users
            .Where(u => authorIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.DisplayName, ct);

        var settings = await _db.TenantSettings.FirstOrDefaultAsync(s => s.TenantId == tenant.Id, ct);
        var myDefault = await _db.ReportLayoutUserDefaults
            .FirstOrDefaultAsync(d => d.TenantId == tenant.Id && d.UserId == user.Id, ct);

        return Ok(new ListResponseDto(
            rows.Select(r => ToDto(r, names)).ToList(),
            settings?.RecommendedReportLayoutId,
            myDefault?.ReportLayoutId));
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
    {
        var (tenant, _) = await ResolveContextAsync(_db, ct);
        var row = await _db.ReportLayouts.FirstOrDefaultAsync(r => r.Id == id && r.TenantId == tenant.Id, ct);
        if (row is null) return NotFound();

        var name = await _db.Users.Where(u => u.Id == row.CreatedByUserId).Select(u => u.DisplayName).FirstOrDefaultAsync(ct);
        return Ok(ToDto(row, new Dictionary<Guid, string> { [row.CreatedByUserId] = name ?? "" }));
    }

    // -------------------------------------------------------------------- create/update/delete

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] SaveLayoutDto dto, CancellationToken ct)
    {
        var (tenant, user) = await ResolveContextAsync(_db, ct);

        var validation = ValidateSave(dto);
        if (validation is not null) return validation;

        var row = new ReportLayout
        {
            TenantId = tenant.Id,
            CreatedByUserId = user.Id,
            Name = dto.Name.Trim(),
            Description = string.IsNullOrWhiteSpace(dto.Description) ? null : dto.Description.Trim(),
            LayoutJson = dto.LayoutJson,
            SchemaVersion = 1,
        };
        _db.ReportLayouts.Add(row);
        await _db.SaveChangesAsync(ct);
        await AuditAsync(tenant.Id, user.Id, row, "created", ct);

        return Ok(ToDto(row, new Dictionary<Guid, string> { [user.Id] = user.DisplayName }));
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] SaveLayoutDto dto, CancellationToken ct)
    {
        var (tenant, user) = await ResolveContextAsync(_db, ct);
        var row = await _db.ReportLayouts.FirstOrDefaultAsync(r => r.Id == id && r.TenantId == tenant.Id, ct);
        if (row is null) return NotFound();
        if (!CanMutate(row, user)) return NotAuthor();

        var validation = ValidateSave(dto);
        if (validation is not null) return validation;

        row.Name = dto.Name.Trim();
        row.Description = string.IsNullOrWhiteSpace(dto.Description) ? null : dto.Description.Trim();
        row.LayoutJson = dto.LayoutJson;
        row.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(ct);
        await AuditAsync(tenant.Id, user.Id, row, "updated", ct);

        var name = await _db.Users.Where(u => u.Id == row.CreatedByUserId).Select(u => u.DisplayName).FirstOrDefaultAsync(ct);
        return Ok(ToDto(row, new Dictionary<Guid, string> { [row.CreatedByUserId] = name ?? "" }));
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var (tenant, user) = await ResolveContextAsync(_db, ct);
        var row = await _db.ReportLayouts.FirstOrDefaultAsync(r => r.Id == id && r.TenantId == tenant.Id, ct);
        if (row is null) return NotFound();
        if (!CanMutate(row, user)) return NotAuthor();

        _db.ReportLayouts.Remove(row);

        // A deleted layout can't stay anyone's default or the tenant's recommendation —
        // both are dangling pointers otherwise (this row is tenant-visible, so any
        // colleague, not just the caller, may have set it as their own default).
        var danglingDefaults = await _db.ReportLayoutUserDefaults
            .Where(d => d.TenantId == tenant.Id && d.ReportLayoutId == id)
            .ToListAsync(ct);
        _db.ReportLayoutUserDefaults.RemoveRange(danglingDefaults);

        var settings = await _db.TenantSettings.FirstOrDefaultAsync(s => s.TenantId == tenant.Id, ct);
        if (settings is not null && settings.RecommendedReportLayoutId == id)
        {
            settings.RecommendedReportLayoutId = null;
            settings.UpdatedAt = DateTimeOffset.UtcNow;
        }

        await _db.SaveChangesAsync(ct);
        await AuditAsync(tenant.Id, user.Id, row, "deleted", ct);
        return NoContent();
    }

    // -------------------------------------------------------------------- default

    [HttpPut("default")]
    public async Task<IActionResult> SetDefault([FromBody] SetDefaultDto dto, CancellationToken ct)
    {
        var (tenant, user) = await ResolveContextAsync(_db, ct);
        var existing = await _db.ReportLayoutUserDefaults
            .FirstOrDefaultAsync(d => d.TenantId == tenant.Id && d.UserId == user.Id, ct);

        if (dto?.LayoutId is null)
        {
            if (existing is not null)
            {
                _db.ReportLayoutUserDefaults.Remove(existing);
                await _db.SaveChangesAsync(ct);
                await AuditPointerAsync(tenant.Id, user.Id, null, "default_cleared", ct);
            }
            return Ok(new { layoutId = (Guid?)null });
        }

        var target = await _db.ReportLayouts.FirstOrDefaultAsync(r => r.Id == dto.LayoutId && r.TenantId == tenant.Id, ct);
        if (target is null)
            return BadRequest(new { error = "layoutId does not refer to a layout in this tenant.", kind = "validation" });

        if (existing is null)
        {
            _db.ReportLayoutUserDefaults.Add(new ReportLayoutUserDefault
            {
                TenantId = tenant.Id,
                UserId = user.Id,
                ReportLayoutId = target.Id,
            });
        }
        else
        {
            existing.ReportLayoutId = target.Id;
            existing.UpdatedAt = DateTimeOffset.UtcNow;
        }
        await _db.SaveChangesAsync(ct);
        await AuditPointerAsync(tenant.Id, user.Id, target, "default_set", ct);

        return Ok(new { layoutId = target.Id });
    }

    // -------------------------------------------------------------------- preview

    /// <summary>
    /// Renders a not-yet-saved (or already-saved) layout against a fictional
    /// in-memory sample report — no database report is read, no status/ack gate
    /// applies, and nothing is audited. This is what lets the designer show true
    /// PDF/DOCX fidelity before the radiologist commits to a design.
    /// </summary>
    [HttpPost("preview/pdf")]
    public async Task<IActionResult> PreviewPdf([FromBody] PreviewDto dto, CancellationToken ct)
    {
        var (tenant, _) = await ResolveContextAsync(_db, ct);
        if (!ReportLayoutParser.TryParse(dto?.LayoutJson, out var layout, out var errors))
            return BadRequest(new { error = "layoutJson is invalid.", kind = "validation", errors });

        var sample = ReportLayoutSampleData.CreateSampleReport();
        var bytes = ReportDocumentRenderer.RenderPdf(sample, tenant, layout!);
        return File(bytes, "application/pdf", "report-layout-preview.pdf");
    }

    [HttpPost("preview/docx")]
    public async Task<IActionResult> PreviewDocx([FromBody] PreviewDto dto, CancellationToken ct)
    {
        var (tenant, _) = await ResolveContextAsync(_db, ct);
        if (!ReportLayoutParser.TryParse(dto?.LayoutJson, out var layout, out var errors))
            return BadRequest(new { error = "layoutJson is invalid.", kind = "validation", errors });

        var sample = ReportLayoutSampleData.CreateSampleReport();
        var bytes = ReportDocumentRenderer.RenderDocx(sample, tenant, layout!);
        return File(bytes, "application/vnd.openxmlformats-officedocument.wordprocessingml.document", "report-layout-preview.docx");
    }

    // -------------------------------------------------------------------- helpers

    private static LayoutDto ToDto(ReportLayout r, IReadOnlyDictionary<Guid, string> names) => new(
        r.Id, r.Name, r.Description, r.LayoutJson, r.SchemaVersion,
        r.CreatedByUserId, names.TryGetValue(r.CreatedByUserId, out var n) ? n : "",
        r.CreatedAt, r.UpdatedAt);

    private IActionResult? ValidateSave(SaveLayoutDto? dto)
    {
        if (dto is null || string.IsNullOrWhiteSpace(dto.Name))
            return BadRequest(new { error = "name is required.", kind = "validation" });
        if (dto.Name.Trim().Length > 200)
            return BadRequest(new { error = "name exceeds the 200 character limit.", kind = "validation" });
        if ((dto.Description?.Length ?? 0) > 500)
            return BadRequest(new { error = "description exceeds the 500 character limit.", kind = "validation" });
        if (!ReportLayoutParser.TryParse(dto.LayoutJson, out _, out var errors))
            return BadRequest(new { error = "layoutJson is invalid.", kind = "validation", errors });
        return null;
    }

    /// <summary>403 in the shape RequirePermission produces, so the client's error handling is uniform.</summary>
    private static IActionResult NotAuthor() => new ObjectResult(new
    {
        error = "Only the layout's author or a reporting administrator may change it.",
        kind = "forbidden",
    })
    { StatusCode = StatusCodes.Status403Forbidden };

    /// <summary>The author always may; governance roles may curate anyone's.</summary>
    private static bool CanMutate(ReportLayout row, User user) =>
        row.CreatedByUserId == user.Id
        || user.Role is UserRole.MedicalDirector or UserRole.ReportingAdmin or UserRole.ItAdmin;

    private Task AuditAsync(Guid tenantId, Guid userId, ReportLayout row, string action, CancellationToken ct) =>
        _audit.AppendAsync(new AuditEvent
        {
            TenantId = tenantId,
            UserId = userId,
            Action = AuditAction.ReportLayoutChanged,
            DetailsJson = System.Text.Json.JsonSerializer.Serialize(new
            {
                layoutId = row.Id,
                name = row.Name,
                action,
            }),
        }, ct);

    private Task AuditPointerAsync(Guid tenantId, Guid userId, ReportLayout? target, string action, CancellationToken ct) =>
        _audit.AppendAsync(new AuditEvent
        {
            TenantId = tenantId,
            UserId = userId,
            Action = AuditAction.ReportLayoutChanged,
            DetailsJson = System.Text.Json.JsonSerializer.Serialize(new
            {
                layoutId = target?.Id,
                name = target?.Name,
                action,
            }),
        }, ct);
}
