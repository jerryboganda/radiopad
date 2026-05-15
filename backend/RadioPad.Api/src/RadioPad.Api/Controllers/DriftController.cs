using Microsoft.AspNetCore.Mvc;
using RadioPad.Api.Services;
using RadioPad.Domain.Enums;
using RadioPad.Infrastructure.Persistence;

namespace RadioPad.Api.Controllers;

/// <summary>
/// PRD §18.2 — admin endpoints for model drift detection. Exposes the
/// latest drift baselines and allows manual trigger of a drift check
/// (MedicalDirector / ItAdmin only).
/// </summary>
[ApiController]
[Route("api/admin/drift")]
public class DriftController : TenantedController
{
    private readonly RadioPadDbContext _db;
    private readonly ModelDriftDetectionService _driftService;

    public DriftController(RadioPadDbContext db, ModelDriftDetectionService driftService)
    {
        _db = db;
        _driftService = driftService;
    }

    /// <summary>
    /// Returns the latest drift baselines for the current tenant
    /// (one row per provider + rulebook pair).
    /// </summary>
    [HttpGet("status")]
    public async Task<IActionResult> Status(CancellationToken ct)
    {
        var (tenant, user) = await ResolveContextAsync(_db, ct);
        var deny = RequireRole(user, UserRole.MedicalDirector, UserRole.ItAdmin);
        if (deny is not null) return deny;

        var results = await _driftService.GetStatusAsync(tenant.Id, ct);
        return Ok(results);
    }

    /// <summary>
    /// Manually triggers a drift check across all tenants and returns
    /// the aggregated results. MedicalDirector / ItAdmin only.
    /// </summary>
    [HttpPost("run")]
    public async Task<IActionResult> Run(CancellationToken ct)
    {
        var (_, user) = await ResolveContextAsync(_db, ct);
        var deny = RequireRole(user, UserRole.MedicalDirector, UserRole.ItAdmin);
        if (deny is not null) return deny;

        var results = await _driftService.RunAllTenantsAsync(ct);
        return Ok(results);
    }
}
