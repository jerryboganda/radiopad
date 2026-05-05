using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RadioPad.Application.Services;
using RadioPad.Infrastructure.Persistence;

namespace RadioPad.Api.Controllers;

/// <summary>
/// Iter-30 — terminology lookup endpoints. Tenant-scoped via the standard
/// dev-headers / OIDC pipeline. The data is read-only and contains no PHI;
/// each tenant nonetheless authenticates so usage is bounded by suspension /
/// rate-limit middleware.
/// </summary>
[ApiController]
[Route("api/terminology")]
public class TerminologyController : TenantedController
{
    private readonly RadioPadDbContext _db;
    private readonly IRadLexService _radlex;
    private readonly IRadsService _rads;

    public TerminologyController(RadioPadDbContext db, IRadLexService radlex, IRadsService rads)
    {
        _db = db;
        _radlex = radlex;
        _rads = rads;
    }

    /// <summary>STD-001 — RadLex® prefix search.</summary>
    [HttpGet("radlex/search")]
    public async Task<IActionResult> RadLexSearch([FromQuery] string q, [FromQuery] int take = 20, CancellationToken ct = default)
    {
        await ResolveContextAsync(_db, ct);
        if (string.IsNullOrWhiteSpace(q))
            return BadRequest(new { error = "Query parameter 'q' is required.", kind = "validation" });
        var hits = _radlex.Search(q, take);
        return Ok(hits.Select(c => new
        {
            rid = c.Rid,
            preferredLabel = c.PreferredLabel,
            synonyms = c.Synonyms,
            category = c.Category,
        }));
    }

    /// <summary>STD-001 — minimal FHIR R4 CodeSystem resource for RadLex®.</summary>
    [HttpGet("radlex/CodeSystem")]
    public async Task<IActionResult> RadLexCodeSystem(CancellationToken ct)
    {
        await ResolveContextAsync(_db, ct);
        var resource = new
        {
            resourceType = "CodeSystem",
            id = "radlex-radiopad-subset",
            url = "http://radlex.org",
            version = "subset-iter30",
            name = "RadLexRadioPadSubset",
            status = "active",
            content = "fragment",
            count = _radlex.Count,
            description = "Curated RadLex® subset bundled with RadioPad. RadLex® is a registered trademark of RSNA.",
            concept = _radlex.All.Select(c => new
            {
                code = c.Rid,
                display = c.PreferredLabel,
                designation = c.Synonyms.Select(s => new
                {
                    use = new { code = "synonym" },
                    value = s,
                }).ToArray(),
            }).ToArray(),
        };
        return new ContentResult
        {
            Content = System.Text.Json.JsonSerializer.Serialize(resource, new System.Text.Json.JsonSerializerOptions
            {
                DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
            }),
            ContentType = "application/fhir+json",
            StatusCode = 200,
        };
    }

    /// <summary>STD-002 — ACR RADS lookup by system; returns category list.</summary>
    [HttpGet("rads")]
    public async Task<IActionResult> Rads([FromQuery] string? system, CancellationToken ct)
    {
        await ResolveContextAsync(_db, ct);
        if (string.IsNullOrWhiteSpace(system))
        {
            return Ok(new { systems = _rads.ListSystems() });
        }
        var sys = _rads.GetSystem(system);
        if (sys is null)
            return NotFound(new { error = $"Unknown RADS system '{system}'.", kind = "not_found" });
        return Ok(new
        {
            system = sys.System,
            description = sys.Description,
            publicGuidanceUrl = sys.PublicGuidanceUrl,
            categories = sys.Categories.Select(c => new
            {
                code = c.Code,
                shortLabel = c.ShortLabel,
                publicGuidanceUrl = c.PublicGuidanceUrl,
            }),
        });
    }
}
