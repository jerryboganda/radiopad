using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RadioPad.Api.Services;
using RadioPad.Domain.Entities;
using RadioPad.Domain.Enums;
using RadioPad.Infrastructure.Persistence;

namespace RadioPad.Api.Controllers;

/// <summary>
/// Iter-35 — versioned clinical validation packs (rulebook golden suites).
/// Lifecycle: Draft → Approved (Medical Director / ItAdmin) → Deprecated.
/// Run is also available to Radiologists for read-only certification of a
/// rulebook before signing.
/// </summary>
[ApiController]
[Route("api/validation-packs")]
public class ValidationPacksController : TenantedController
{
    private readonly RadioPadDbContext _db;
    private readonly ValidationPackService _service;

    public ValidationPacksController(RadioPadDbContext db, ValidationPackService service)
    {
        _db = db;
        _service = service;
    }

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] string? rulebookId, CancellationToken ct)
    {
        var (tenant, _) = await ResolveContextAsync(_db, ct);
        var query = _db.ValidationPacks.AsNoTracking().Where(p => p.TenantId == tenant.Id);
        if (!string.IsNullOrWhiteSpace(rulebookId))
            query = query.Where(p => p.RulebookId == rulebookId);
        var items = await query
            .OrderBy(p => p.RulebookId).ThenByDescending(p => p.CreatedAt)
            .ToListAsync(ct);
        return Ok(items.Select(p => new
        {
            p.Id,
            p.RulebookId,
            p.Version,
            p.Name,
            status = p.Status.ToString(),
            p.ApprovedAt,
            p.ApprovedBy,
            p.CreatedAt,
            p.CreatedBy,
            caseCount = CountCases(p.GoldenCasesJson),
        }));
    }

    public record CreatePackDto(string RulebookId, string Version, string? Name, object GoldenCases);

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreatePackDto dto, CancellationToken ct)
    {
        var (tenant, user) = await ResolveContextAsync(_db, ct);
        var deny = RequireRole(user, UserRole.MedicalDirector, UserRole.ItAdmin);
        if (deny is not null) return deny;
        if (dto is null || string.IsNullOrWhiteSpace(dto.RulebookId) || string.IsNullOrWhiteSpace(dto.Version))
            return BadRequest(new { error = "rulebookId and version are required.", kind = "validation_packs" });

        try
        {
            var json = System.Text.Json.JsonSerializer.Serialize(dto.GoldenCases ?? Array.Empty<object>());
            var pack = await _service.CreateAsync(tenant, user, dto.RulebookId, dto.Version, dto.Name ?? "", json, ct);
            return Ok(new
            {
                pack.Id,
                pack.RulebookId,
                pack.Version,
                pack.Name,
                status = pack.Status.ToString(),
            });
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { error = ex.Message, kind = "validation_packs" });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message, kind = "validation_packs" });
        }
    }

    [HttpPost("{id:guid}/approve")]
    public async Task<IActionResult> Approve(Guid id, CancellationToken ct)
    {
        var (tenant, user) = await ResolveContextAsync(_db, ct);
        var deny = RequireRole(user, UserRole.MedicalDirector, UserRole.ItAdmin);
        if (deny is not null) return deny;
        try
        {
            var pack = await _service.ApproveAsync(tenant, user, id, ct);
            return Ok(new
            {
                pack.Id,
                status = pack.Status.ToString(),
                pack.ApprovedAt,
            });
        }
        catch (KeyNotFoundException) { return NotFound(); }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { error = ex.Message, kind = "validation_packs" });
        }
    }

    [HttpPost("{id:guid}/deprecate")]
    public async Task<IActionResult> Deprecate(Guid id, CancellationToken ct)
    {
        var (tenant, user) = await ResolveContextAsync(_db, ct);
        var deny = RequireRole(user, UserRole.MedicalDirector, UserRole.ItAdmin);
        if (deny is not null) return deny;
        try
        {
            var pack = await _service.DeprecateAsync(tenant, user, id, ct);
            return Ok(new { pack.Id, status = pack.Status.ToString() });
        }
        catch (KeyNotFoundException) { return NotFound(); }
    }

    [HttpPost("{id:guid}/run")]
    public async Task<IActionResult> Run(Guid id, CancellationToken ct)
    {
        var (tenant, user) = await ResolveContextAsync(_db, ct);
        // Radiologist may run packs in read-only mode (no state change).
        var deny = RequireRole(user,
            UserRole.MedicalDirector, UserRole.ItAdmin, UserRole.Radiologist, UserRole.ReportingAdmin);
        if (deny is not null) return deny;
        try
        {
            var summary = await _service.RunAsync(tenant, user, id, ct);
            return Ok(new
            {
                summary.Passed,
                summary.Failed,
                summary.TotalCases,
                Failures = summary.Failures.Select(f => new
                {
                    caseId = f.CaseName,
                    missing = f.Missing,
                    unexpected = f.Unexpected,
                }),
            });
        }
        catch (KeyNotFoundException) { return NotFound(); }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { error = ex.Message, kind = "validation_packs" });
        }
    }

    [HttpGet("{id:guid}/export")]
    public async Task<IActionResult> Export(Guid id, CancellationToken ct)
    {
        var (tenant, _) = await ResolveContextAsync(_db, ct);
        try
        {
            var body = await _service.ExportAsync(tenant, id, ct);
            return new JsonResult(body);
        }
        catch (KeyNotFoundException) { return NotFound(); }
    }

    private static int CountCases(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return 0;
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != System.Text.Json.JsonValueKind.Array) return 0;
            return doc.RootElement.GetArrayLength();
        }
        catch { return 0; }
    }
}
