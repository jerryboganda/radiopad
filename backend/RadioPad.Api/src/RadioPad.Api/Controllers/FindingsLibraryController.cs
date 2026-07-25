using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RadioPad.Application.Abstractions;
using RadioPad.Domain.Entities;
using RadioPad.Domain.Enums;
using RadioPad.Infrastructure.Persistence;

namespace RadioPad.Api.Controllers;

/// <summary>
/// Findings Library — the reusable-phrase surface. It has two sources: report
/// templates (read through the existing templates endpoints, already surfaced by
/// the page) and the tenant's own hand-authored <see cref="Snippet"/> rows, which
/// this controller owns. It also stores the two per-user overlays the library
/// needs — stars (<see cref="LibraryFavorite"/>) and a "used in report" trail
/// (<see cref="LibraryRecentUse"/>) — both keyed loosely by (entityType, entityKey)
/// so they cover templates and snippets with one table each.
///
/// Reading is open to every authenticated member of the tenant: snippets are
/// clinical boilerplate, and a radiologist who could not fetch them could not
/// dictate with them. Authoring is open too — snippets are how an individual
/// radiologist captures their own phrasing — but editing and deleting are
/// restricted to the author plus the reporting-governance roles, so one user
/// cannot rewrite another's saved phrasing out from under them.
/// </summary>
[ApiController]
[Route("api/findings-library")]
public class FindingsLibraryController : TenantedController
{
    private readonly RadioPadDbContext _db;
    private readonly IAuditLog _audit;

    public FindingsLibraryController(RadioPadDbContext db, IAuditLog audit)
    {
        _db = db;
        _audit = audit;
    }

    // ---------------------------------------------------------------- snippets

    public record SnippetDto(
        Guid Id,
        string Name,
        string Modality,
        string BodyPart,
        string Category,
        string SectionsJson,
        Guid CreatedByUserId,
        DateTimeOffset CreatedAt,
        DateTimeOffset UpdatedAt);

    private static SnippetDto ToDto(Snippet s) => new(
        s.Id, s.Name, s.Modality, s.BodyPart, s.Category ?? "", s.SectionsJson,
        s.CreatedByUserId, s.CreatedAt, s.UpdatedAt);

    [HttpGet("snippets")]
    public async Task<IActionResult> ListSnippets(CancellationToken ct)
    {
        var (tenant, _) = await ResolveContextAsync(_db, ct);
        var rows = await _db.Snippets
            .Where(s => s.TenantId == tenant.Id)
            .OrderBy(s => s.Name)
            .ToListAsync(ct);
        return Ok(rows.Select(ToDto));
    }

    public record SaveSnippetDto(
        Guid? Id,
        string Name,
        string? Modality,
        string? BodyPart,
        string? Category,
        string? SectionsJson);

    [HttpPost("snippets")]
    public async Task<IActionResult> SaveSnippet([FromBody] SaveSnippetDto dto, CancellationToken ct)
    {
        var (tenant, user) = await ResolveContextAsync(_db, ct);

        if (dto is null || string.IsNullOrWhiteSpace(dto.Name))
            return BadRequest(new { error = "name is required.", kind = "validation" });

        var sections = NormaliseSections(dto.SectionsJson);
        if (sections is null)
            return BadRequest(new { error = "sectionsJson must be a JSON array of { id, label, text }.", kind = "validation" });

        Snippet row;
        var created = dto.Id is null;
        if (dto.Id is { } id)
        {
            var found = await _db.Snippets.FirstOrDefaultAsync(s => s.Id == id && s.TenantId == tenant.Id, ct);
            if (found is null) return NotFound();
            if (!CanMutate(found, user)) return NotAuthor();
            row = found;
        }
        else
        {
            row = new Snippet { TenantId = tenant.Id, CreatedByUserId = user.Id };
            _db.Snippets.Add(row);
        }

        row.Name = dto.Name.Trim();
        row.Modality = (dto.Modality ?? "").Trim();
        row.BodyPart = (dto.BodyPart ?? "").Trim();
        row.Category = string.IsNullOrWhiteSpace(dto.Category) ? null : dto.Category.Trim();
        row.SectionsJson = sections;
        row.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(ct);

        await AuditSnippetAsync(tenant.Id, user.Id, row, created ? "created" : "updated", ct);
        return Ok(ToDto(row));
    }

    [HttpDelete("snippets/{id:guid}")]
    public async Task<IActionResult> DeleteSnippet(Guid id, CancellationToken ct)
    {
        var (tenant, user) = await ResolveContextAsync(_db, ct);
        var row = await _db.Snippets.FirstOrDefaultAsync(s => s.Id == id && s.TenantId == tenant.Id, ct);
        if (row is null) return NotFound();
        if (!CanMutate(row, user)) return NotAuthor();

        _db.Snippets.Remove(row);

        // The stars and use-trail for a deleted snippet would otherwise linger as
        // rows pointing at nothing, inflating the sidebar counts forever.
        var key = id.ToString();
        var orphanedFavorites = await _db.LibraryFavorites
            .Where(f => f.TenantId == tenant.Id && f.EntityType == "snippet" && f.EntityKey == key)
            .ToListAsync(ct);
        _db.LibraryFavorites.RemoveRange(orphanedFavorites);
        var orphanedUses = await _db.LibraryRecentUses
            .Where(r => r.TenantId == tenant.Id && r.EntityType == "snippet" && r.EntityKey == key)
            .ToListAsync(ct);
        _db.LibraryRecentUses.RemoveRange(orphanedUses);

        await _db.SaveChangesAsync(ct);
        await AuditSnippetAsync(tenant.Id, user.Id, row, "deleted", ct);
        return NoContent();
    }

    public record ImportSnippetsDto(List<SaveSnippetDto> Snippets);

    /// <summary>
    /// Bulk create from an uploaded file. The client parses the file (JSON export
    /// from another tenant, or a converted CSV) and posts the rows; the server does
    /// not sniff file formats. Rows that fail validation are skipped and reported
    /// rather than failing the whole import, because a half-usable export is still
    /// worth importing.
    /// </summary>
    [HttpPost("snippets/import")]
    public async Task<IActionResult> ImportSnippets([FromBody] ImportSnippetsDto dto, CancellationToken ct)
    {
        var (tenant, user) = await ResolveContextAsync(_db, ct);
        if (dto?.Snippets is null || dto.Snippets.Count == 0)
            return BadRequest(new { error = "snippets is required and must not be empty.", kind = "validation" });

        var imported = new List<Snippet>();
        var skipped = new List<string>();
        foreach (var item in dto.Snippets)
        {
            if (item is null || string.IsNullOrWhiteSpace(item.Name))
            {
                skipped.Add("(unnamed)");
                continue;
            }
            var sections = NormaliseSections(item.SectionsJson);
            if (sections is null)
            {
                skipped.Add(item.Name);
                continue;
            }
            var row = new Snippet
            {
                TenantId = tenant.Id,
                CreatedByUserId = user.Id,
                Name = item.Name.Trim(),
                Modality = (item.Modality ?? "").Trim(),
                BodyPart = (item.BodyPart ?? "").Trim(),
                Category = string.IsNullOrWhiteSpace(item.Category) ? null : item.Category.Trim(),
                SectionsJson = sections,
            };
            _db.Snippets.Add(row);
            imported.Add(row);
        }

        if (imported.Count == 0)
            return BadRequest(new { error = "no valid snippets in the import.", kind = "validation", skipped });

        await _db.SaveChangesAsync(ct);
        await _audit.AppendAsync(new AuditEvent
        {
            TenantId = tenant.Id,
            UserId = user.Id,
            Action = AuditAction.SnippetChanged,
            DetailsJson = System.Text.Json.JsonSerializer.Serialize(new
            {
                action = "imported",
                count = imported.Count,
                skipped = skipped.Count,
            }),
        }, ct);

        return Ok(new { imported = imported.Count, skipped, items = imported.Select(ToDto) });
    }

    // --------------------------------------------------------------- favorites

    public record LibraryRefDto(string EntityType, string EntityKey);

    [HttpGet("favorites")]
    public async Task<IActionResult> ListFavorites(CancellationToken ct)
    {
        var (tenant, user) = await ResolveContextAsync(_db, ct);
        var rows = await _db.LibraryFavorites
            .Where(f => f.TenantId == tenant.Id && f.UserId == user.Id)
            .OrderByDescending(f => f.CreatedAt)
            .Select(f => new LibraryRefDto(f.EntityType, f.EntityKey))
            .ToListAsync(ct);
        return Ok(rows);
    }

    public record ToggleFavoriteDto(string EntityType, string EntityKey, bool Favorite);

    [HttpPost("favorites")]
    public async Task<IActionResult> ToggleFavorite([FromBody] ToggleFavoriteDto dto, CancellationToken ct)
    {
        var (tenant, user) = await ResolveContextAsync(_db, ct);
        if (!TryNormaliseRef(dto?.EntityType, dto?.EntityKey, out var type, out var key))
            return BadRequest(new { error = "entityType must be 'template' or 'snippet' and entityKey is required.", kind = "validation" });

        var existing = await _db.LibraryFavorites.FirstOrDefaultAsync(
            f => f.TenantId == tenant.Id && f.UserId == user.Id && f.EntityType == type && f.EntityKey == key, ct);

        if (dto!.Favorite && existing is null)
        {
            _db.LibraryFavorites.Add(new LibraryFavorite
            {
                TenantId = tenant.Id,
                UserId = user.Id,
                EntityType = type,
                EntityKey = key,
            });
            await _db.SaveChangesAsync(ct);
        }
        else if (!dto.Favorite && existing is not null)
        {
            _db.LibraryFavorites.Remove(existing);
            await _db.SaveChangesAsync(ct);
        }

        return Ok(new { entityType = type, entityKey = key, favorite = dto.Favorite });
    }

    // ------------------------------------------------------------------ recent

    public record RecentUseDto(string EntityType, string EntityKey, DateTimeOffset LastUsedAt, int UseCount);

    [HttpGet("recent")]
    public async Task<IActionResult> ListRecent([FromQuery] int limit, CancellationToken ct)
    {
        var (tenant, user) = await ResolveContextAsync(_db, ct);
        var take = limit <= 0 ? 25 : Math.Min(limit, 200);
        // Grouped server-side, then ordered in memory: ordering by a property of a
        // projected group is fragile to translate on SQLite, and the collapsed set
        // is one row per distinct library item the user has ever used.
        var grouped = await _db.LibraryRecentUses
            .Where(r => r.TenantId == tenant.Id && r.UserId == user.Id)
            .GroupBy(r => new { r.EntityType, r.EntityKey })
            .Select(g => new
            {
                g.Key.EntityType,
                g.Key.EntityKey,
                LastUsedAt = g.Max(r => r.CreatedAt),
                UseCount = g.Count(),
            })
            .ToListAsync(ct);

        var rows = grouped
            .OrderByDescending(r => r.LastUsedAt)
            .Take(take)
            .Select(r => new RecentUseDto(r.EntityType, r.EntityKey, r.LastUsedAt, r.UseCount))
            .ToList();
        return Ok(rows);
    }

    /// <summary>
    /// Records that the caller pulled a library item into a draft. Append-only —
    /// the read path collapses the rows per item, so repeated use is a use count
    /// rather than a lost history.
    /// </summary>
    [HttpPost("recent")]
    public async Task<IActionResult> RecordUse([FromBody] LibraryRefDto dto, CancellationToken ct)
    {
        var (tenant, user) = await ResolveContextAsync(_db, ct);
        if (!TryNormaliseRef(dto?.EntityType, dto?.EntityKey, out var type, out var key))
            return BadRequest(new { error = "entityType must be 'template' or 'snippet' and entityKey is required.", kind = "validation" });

        _db.LibraryRecentUses.Add(new LibraryRecentUse
        {
            TenantId = tenant.Id,
            UserId = user.Id,
            EntityType = type,
            EntityKey = key,
        });
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    // ------------------------------------------------------------------ helpers

    /// <summary>403 in the shape RequirePermission produces, so the client's error handling is uniform.</summary>
    private static IActionResult NotAuthor() => new ObjectResult(new
    {
        error = "Only the snippet's author or a reporting administrator may change it.",
        kind = "forbidden",
    })
    { StatusCode = StatusCodes.Status403Forbidden };

    /// <summary>The author always may; governance roles may curate anyone's.</summary>
    private static bool CanMutate(Snippet row, User user) =>
        row.CreatedByUserId == user.Id
        || user.Role is UserRole.MedicalDirector or UserRole.ReportingAdmin or UserRole.ItAdmin;

    private static bool TryNormaliseRef(string? rawType, string? rawKey, out string type, out string key)
    {
        type = (rawType ?? "").Trim().ToLowerInvariant();
        key = (rawKey ?? "").Trim();
        return (type is "template" or "snippet") && key.Length > 0;
    }

    /// <summary>
    /// Returns the canonical sections JSON, or null when the payload is not a JSON
    /// array (which would otherwise be stored verbatim and break every reader).
    /// </summary>
    private static string? NormaliseSections(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "[]";
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(raw);
            return doc.RootElement.ValueKind == System.Text.Json.JsonValueKind.Array ? raw : null;
        }
        catch (System.Text.Json.JsonException)
        {
            return null;
        }
    }

    private Task AuditSnippetAsync(Guid tenantId, Guid userId, Snippet row, string action, CancellationToken ct) =>
        _audit.AppendAsync(new AuditEvent
        {
            TenantId = tenantId,
            UserId = userId,
            Action = AuditAction.SnippetChanged,
            DetailsJson = System.Text.Json.JsonSerializer.Serialize(new
            {
                snippetId = row.Id,
                name = row.Name,
                modality = row.Modality,
                bodyPart = row.BodyPart,
                action,
            }),
        }, ct);
}
