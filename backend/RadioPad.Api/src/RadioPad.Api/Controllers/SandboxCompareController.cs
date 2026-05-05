using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RadioPad.Application.Abstractions;
using RadioPad.Application.Services;
using RadioPad.Domain.Entities;
using RadioPad.Domain.Enums;
using RadioPad.Infrastructure.Persistence;

namespace RadioPad.Api.Controllers;

/// <summary>
/// PRD PROV-005 (iter-34) — sandbox model comparison. Runs the same prompt
/// across up to four sandbox-class providers in series so the radiologist
/// can diff outputs before promoting a model to <c>PhiApproved</c>. PHI
/// policy is NOT bypassed: every dispatch goes through
/// <see cref="ReportingService.RunAsync"/> → <see cref="IAiGateway.RouteAsync"/>,
/// which still calls <c>EnforcePhiPolicy</c>. The endpoint additionally
/// gates the call on <see cref="Tenant.AllowSandboxRulebooks"/> and refuses
/// any provider whose <see cref="ProviderComplianceClass"/> is not
/// <see cref="ProviderComplianceClass.Sandbox"/>.
/// </summary>
[ApiController]
[Route("api/ai/sandbox")]
public class SandboxCompareController : TenantedController
{
    private readonly RadioPadDbContext _db;
    private readonly ReportingService _reporting;
    private readonly IAuditLog _audit;

    public SandboxCompareController(RadioPadDbContext db, ReportingService reporting, IAuditLog audit)
    {
        _db = db;
        _reporting = reporting;
        _audit = audit;
    }

    public record CompareDto(Guid ReportId, string Mode, Guid[] ProviderIds);

    public record CompareRun(
        Guid ProviderId,
        string Provider,
        string Model,
        string? Output,
        int LatencyMs,
        int InputTokens,
        int OutputTokens,
        string? Error);

    public record CompareResponse(IReadOnlyList<CompareRun> Runs);

    [HttpPost("compare")]
    [Microsoft.AspNetCore.RateLimiting.EnableRateLimiting("ai")]
    public async Task<IActionResult> Compare([FromBody] CompareDto dto, CancellationToken ct)
    {
        var (tenant, user) = await ResolveContextAsync(_db, ct);
        var deny = RequireRole(user,
            UserRole.Radiologist, UserRole.MedicalDirector,
            UserRole.ReportingAdmin, UserRole.ItAdmin);
        if (deny is not null) return deny;

        if (dto is null || dto.ReportId == Guid.Empty)
            return BadRequest(new { error = "reportId is required.", kind = "validation" });

        var providerIds = (dto.ProviderIds ?? Array.Empty<Guid>())
            .Where(id => id != Guid.Empty)
            .Distinct()
            .ToArray();
        if (providerIds.Length < 1 || providerIds.Length > 4)
            return BadRequest(new
            {
                error = "providerIds must contain between 1 and 4 unique provider ids.",
                kind = "validation",
            });

        var mode = string.IsNullOrWhiteSpace(dto.Mode) ? "impression" : dto.Mode.Trim().ToLowerInvariant();
        if (!ReportingService.SupportedModes.Contains(mode, StringComparer.OrdinalIgnoreCase))
        {
            return BadRequest(new
            {
                error = $"Unsupported AI mode '{mode}'.",
                kind = "validation",
                supportedModes = ReportingService.SupportedModes,
            });
        }

        if (!tenant.AllowSandboxRulebooks)
        {
            return Conflict(new
            {
                error = "Sandbox compare requires Tenant.AllowSandboxRulebooks = true.",
                kind = "sandbox_required",
            });
        }

        var report = await _db.Reports.FirstOrDefaultAsync(
            r => r.Id == dto.ReportId && r.TenantId == tenant.Id, ct);
        if (report is null) return NotFound(new { error = "Report not found.", kind = "not_found" });

        var tenantProviders = await _db.Providers
            .Where(p => p.TenantId == tenant.Id)
            .ToListAsync(ct);
        var providerIdSet = providerIds.ToHashSet();
        var providers = tenantProviders
            .Where(p => providerIdSet.Contains(p.Id))
            .ToList();
        if (providers.Count != providerIds.Length
            || providers.Any(p => !p.Enabled)
            || providers.Any(p => p.Compliance != ProviderComplianceClass.Sandbox))
        {
            return BadRequest(new
            {
                error = "Every providerId must be an enabled, tenant-owned provider with Compliance = Sandbox.",
                kind = "providers_not_sandbox",
            });
        }

        var runs = new List<CompareRun>(providers.Count);
        // Serialised dispatch — comparing latency apples-to-apples and
        // keeping the audit chain deterministic.
        foreach (var pid in providerIds)
        {
            var provider = providers.First(p => p.Id == pid);
            try
            {
                var result = await _reporting.RunAsync(tenant, user, report, provider, mode, ct);
                runs.Add(new CompareRun(
                    ProviderId: provider.Id,
                    Provider: result.Provider,
                    Model: result.Model,
                    Output: result.Text,
                    LatencyMs: result.LatencyMs,
                    InputTokens: result.InputTokens,
                    OutputTokens: result.OutputTokens,
                    Error: null));
            }
            catch (ProviderPolicyException pex)
            {
                runs.Add(new CompareRun(provider.Id, provider.Name, provider.Model, null, 0, 0, 0,
                    Error: $"provider_policy: {pex.Message}"));
            }
            catch (Exception)
            {
                // Generic message — never leak provider stack traces or PHI.
                runs.Add(new CompareRun(provider.Id, provider.Name, provider.Model, null, 0, 0, 0,
                    Error: "provider_error"));
            }
        }

        await _audit.AppendAsync(new AuditEvent
        {
            TenantId = tenant.Id,
            UserId = user.Id,
            ReportId = report.Id,
            Action = AuditAction.AiResponse,
            DetailsJson = JsonSerializer.Serialize(new
            {
                kind = "sandbox_compare",
                mode,
                providerCount = providerIds.Length,
            }),
        }, ct);

        return Ok(new CompareResponse(runs));
    }
}
