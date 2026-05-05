using Microsoft.AspNetCore.Mvc;
using RadioPad.Application.Abstractions;
using RadioPad.Domain.Enums;
using RadioPad.Infrastructure.Persistence;

namespace RadioPad.Api.Controllers;

/// <summary>
/// Iter-32 AI-010 — debug surface that explains the AI gateway's routing
/// decision for a hypothetical <c>(modality, phi, tokens)</c> tuple. No real
/// AI call is performed. Restricted to <see cref="UserRole.ItAdmin"/> so
/// support engineers can diagnose "why was provider X selected?" without
/// exposing the routing internals to clinicians.
/// </summary>
[ApiController]
[Route("api/ai/routing")]
public class RoutingPreviewController : TenantedController
{
    private readonly RadioPadDbContext _db;
    private readonly IRoutingPreviewService _preview;

    public RoutingPreviewController(RadioPadDbContext db, IRoutingPreviewService preview)
    {
        _db = db;
        _preview = preview;
    }

    [HttpGet("preview")]
    public async Task<IActionResult> Preview(
        [FromQuery] bool phi = false,
        [FromQuery] string? modality = null,
        [FromQuery] int? input = null,
        [FromQuery] int? output = null,
        CancellationToken ct = default)
    {
        var (tenant, user) = await ResolveContextAsync(_db, ct);
        var deny = RequireRole(user, UserRole.ItAdmin, UserRole.MedicalDirector);
        if (deny is not null) return deny;
        var preview = await _preview.PreviewAsync(tenant, phi, modality, input, output, ct);
        return Ok(preview);
    }
}
