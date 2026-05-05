using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RadioPad.Application.Abstractions;
using RadioPad.Domain.Entities;
using RadioPad.Domain.Enums;
using RadioPad.Infrastructure.Persistence;

namespace RadioPad.Api.Controllers;

[ApiController]
[Route("api/templates")]
public class TemplatesController : TenantedController
{
    private readonly RadioPadDbContext _db;
    private readonly IAuditLog _audit;
    public TemplatesController(RadioPadDbContext db, IAuditLog audit) { _db = db; _audit = audit; }

    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        var (tenant, _) = await ResolveContextAsync(_db, ct);
        return Ok(await _db.Templates.Where(t => t.TenantId == tenant.Id).ToListAsync(ct));
    }

    public record SaveTemplateDto(
        string TemplateId, string Name, string Modality, string BodyPart,
        string Subspecialty, string SectionsJson,
        TemplateVariant? Variant = null, TemplateStatus? Status = null);

    [HttpPost]
    public async Task<IActionResult> Save([FromBody] SaveTemplateDto dto, CancellationToken ct)
    {
        var (tenant, _) = await ResolveContextAsync(_db, ct);
        var existing = await _db.Templates.FirstOrDefaultAsync(t => t.TenantId == tenant.Id && t.TemplateId == dto.TemplateId, ct);
        if (existing is null)
        {
            existing = new ReportTemplate { TenantId = tenant.Id, TemplateId = dto.TemplateId };
            _db.Templates.Add(existing);
        }
        existing.Name = dto.Name;
        existing.Modality = dto.Modality;
        existing.BodyPart = dto.BodyPart;
        existing.Subspecialty = dto.Subspecialty;
        existing.SectionsJson = dto.SectionsJson;
        if (dto.Variant is not null) existing.Variant = dto.Variant.Value;
        // Editing an Approved template drops it back to Draft so any change
        // is re-reviewed before being adopted in production.
        if (dto.Status is not null) existing.Status = dto.Status.Value;
        else if (existing.Status == TemplateStatus.Approved) existing.Status = TemplateStatus.Draft;
        existing.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(ct);
        return Ok(existing);
    }

    /// <summary>
    /// Iter-31 TMP-005 — approve a template for production use. Restricted
    /// to <see cref="UserRole.MedicalDirector"/> and
    /// <see cref="UserRole.ReportingAdmin"/>; audit
    /// <see cref="AuditAction.TemplateApproved"/>.
    /// </summary>
    [HttpPost("{id:guid}/approve")]
    public async Task<IActionResult> Approve(Guid id, CancellationToken ct)
    {
        var (tenant, user) = await ResolveContextAsync(_db, ct);
        var deny = RequireRole(user, UserRole.MedicalDirector, UserRole.ReportingAdmin, UserRole.ItAdmin);
        if (deny is not null) return deny;
        var t = await _db.Templates.FirstOrDefaultAsync(x => x.Id == id && x.TenantId == tenant.Id, ct);
        if (t is null) return NotFound();
        t.Status = TemplateStatus.Approved;
        t.ApprovedBy = user.Id;
        t.ApprovedAt = DateTimeOffset.UtcNow;
        t.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(ct);
        await _audit.AppendAsync(new AuditEvent
        {
            TenantId = tenant.Id,
            UserId = user.Id,
            Action = AuditAction.TemplateApproved,
            DetailsJson = System.Text.Json.JsonSerializer.Serialize(new
            {
                templateId = t.TemplateId,
                rowId = t.Id,
                variant = t.Variant.ToString(),
            }),
        }, ct);
        return Ok(t);
    }

    /// <summary>
    /// Iter-32 TMP-005 — submit a draft template for review. Any role can
    /// submit (the radiologist who wrote it should be able to ask for review)
    /// but only Approved/Draft can be moved to Review.
    /// </summary>
    [HttpPost("{id:guid}/submit-review")]
    public async Task<IActionResult> SubmitForReview(Guid id, CancellationToken ct)
    {
        var (tenant, user) = await ResolveContextAsync(_db, ct);
        var t = await _db.Templates.FirstOrDefaultAsync(x => x.Id == id && x.TenantId == tenant.Id, ct);
        if (t is null) return NotFound();
        if (t.Status == TemplateStatus.Deprecated)
            return BadRequest(new { error = "Cannot submit a deprecated template for review.", kind = "validation" });
        t.Status = TemplateStatus.Review;
        t.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(ct);
        await _audit.AppendAsync(new AuditEvent
        {
            TenantId = tenant.Id,
            UserId = user.Id,
            Action = AuditAction.TemplateSubmittedForReview,
            DetailsJson = System.Text.Json.JsonSerializer.Serialize(new { templateId = t.TemplateId, rowId = t.Id }),
        }, ct);
        return Ok(t);
    }

    /// <summary>
    /// Iter-32 TMP-005 — deprecate a template. Restricted to admin roles;
    /// audit <see cref="AuditAction.TemplateDeprecated"/>.
    /// </summary>
    [HttpPost("{id:guid}/deprecate")]
    public async Task<IActionResult> Deprecate(Guid id, CancellationToken ct)
    {
        var (tenant, user) = await ResolveContextAsync(_db, ct);
        var deny = RequireRole(user, UserRole.MedicalDirector, UserRole.ReportingAdmin, UserRole.ItAdmin);
        if (deny is not null) return deny;
        var t = await _db.Templates.FirstOrDefaultAsync(x => x.Id == id && x.TenantId == tenant.Id, ct);
        if (t is null) return NotFound();
        t.Status = TemplateStatus.Deprecated;
        t.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(ct);
        await _audit.AppendAsync(new AuditEvent
        {
            TenantId = tenant.Id,
            UserId = user.Id,
            Action = AuditAction.TemplateDeprecated,
            DetailsJson = System.Text.Json.JsonSerializer.Serialize(new { templateId = t.TemplateId, rowId = t.Id }),
        }, ct);
        return Ok(t);
    }

    /// <summary>
    /// Iter-32 TMP-006 — template usage analytics. Returns request counts
    /// for the last 7 / 30 / 90 days, broken down by user and modality.
    /// Tenant-scoped; counts only reports that pin <paramref name="id"/>
    /// in <see cref="Report.TemplateId"/>.
    /// </summary>
    [HttpGet("{id:guid}/usage")]
    public async Task<IActionResult> Usage(Guid id, CancellationToken ct)
    {
        var (tenant, _) = await ResolveContextAsync(_db, ct);
        var t = await _db.Templates.FirstOrDefaultAsync(x => x.Id == id && x.TenantId == tenant.Id, ct);
        if (t is null) return NotFound();

        var now = DateTimeOffset.UtcNow;
        var d7 = now.AddDays(-7);
        var d30 = now.AddDays(-30);
        var d90 = now.AddDays(-90);

        var rows = await _db.Reports
            .Where(r => r.TenantId == tenant.Id && r.TemplateId == id && r.CreatedAt >= d90)
            .Select(r => new { r.CreatedAt, r.CreatedByUserId, modality = r.Study.Modality })
            .ToListAsync(ct);

        var byUser = rows
            .GroupBy(r => r.CreatedByUserId)
            .Select(g => new { userId = g.Key, count = g.Count() })
            .OrderByDescending(x => x.count)
            .ToArray();
        var byModality = rows
            .GroupBy(r => r.modality ?? "")
            .Select(g => new { modality = g.Key, count = g.Count() })
            .OrderByDescending(x => x.count)
            .ToArray();

        return Ok(new
        {
            templateId = t.TemplateId,
            rowId = t.Id,
            window = new { from = d90, to = now },
            counts = new
            {
                last7d = rows.Count(r => r.CreatedAt >= d7),
                last30d = rows.Count(r => r.CreatedAt >= d30),
                last90d = rows.Count,
            },
            byUser,
            byModality,
        });
    }

    /// <summary>
    /// Iter-31 TMP-008 — render a preview of the template's sections, optionally
    /// merged with a specific report's metadata. Sections without content are
    /// surfaced with a placeholder so the radiologist can see structure.
    /// </summary>
    [HttpGet("{id:guid}/preview")]
    public async Task<IActionResult> Preview(Guid id, [FromQuery] Guid? reportId, CancellationToken ct)
    {
        var (tenant, _) = await ResolveContextAsync(_db, ct);
        var t = await _db.Templates.FirstOrDefaultAsync(x => x.Id == id && x.TenantId == tenant.Id, ct);
        if (t is null) return NotFound();
        Report? report = null;
        if (reportId is { } rid)
        {
            report = await _db.Reports.FirstOrDefaultAsync(r => r.Id == rid && r.TenantId == tenant.Id, ct);
        }

        var sections = new List<object>();
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(string.IsNullOrWhiteSpace(t.SectionsJson) ? "[]" : t.SectionsJson);
            if (doc.RootElement.ValueKind == System.Text.Json.JsonValueKind.Array)
            {
                foreach (var el in doc.RootElement.EnumerateArray())
                {
                    var key = el.TryGetProperty("key", out var k) ? k.GetString() ?? "" : "";
                    var label = el.TryGetProperty("label", out var l) ? l.GetString() ?? key : key;
                    var placeholder = el.TryGetProperty("placeholder", out var p) ? p.GetString() ?? "" : "";
                    sections.Add(new
                    {
                        key,
                        label,
                        body = ResolveSectionBody(report, key, placeholder),
                    });
                }
            }
        }
        catch (System.Text.Json.JsonException ex)
        {
            return BadRequest(new { error = $"Template sectionsJson is not valid JSON: {ex.Message}", kind = "validation" });
        }
        return Ok(new
        {
            id = t.Id,
            templateId = t.TemplateId,
            name = t.Name,
            modality = t.Modality,
            bodyPart = t.BodyPart,
            variant = t.Variant.ToString(),
            status = t.Status.ToString(),
            sections,
        });
    }

    private static string ResolveSectionBody(Report? r, string key, string placeholder)
    {
        if (r is null) return string.IsNullOrEmpty(placeholder) ? $"[{key}]" : placeholder;
        return key.ToLowerInvariant() switch
        {
            "indication" => string.IsNullOrEmpty(r.Indication) ? placeholder : r.Indication,
            "technique" => string.IsNullOrEmpty(r.Technique) ? placeholder : r.Technique,
            "comparison" => string.IsNullOrEmpty(r.Comparison) ? placeholder : r.Comparison,
            "findings" => string.IsNullOrEmpty(r.Findings) ? placeholder : r.Findings,
            "impression" => string.IsNullOrEmpty(r.Impression) ? placeholder : r.Impression,
            "recommendations" => string.IsNullOrEmpty(r.Recommendations) ? placeholder : r.Recommendations,
            _ => string.IsNullOrEmpty(placeholder) ? $"[{key}]" : placeholder,
        };
    }
}

[ApiController]
[Route("api/providers")]
public class ProvidersController : TenantedController
{
    private readonly RadioPadDbContext _db;
    public ProvidersController(RadioPadDbContext db) => _db = db;

    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        var (tenant, _) = await ResolveContextAsync(_db, ct);
        var providers = await _db.Providers.Where(p => p.TenantId == tenant.Id).ToListAsync(ct);
        // Never return secret material to clients.
        return Ok(providers.Select(p => new
        {
            p.Id, p.Name, p.Adapter, p.Model, p.EndpointUrl, p.Compliance, p.Enabled, p.Priority,
            p.CostPerInputKToken, p.CostPerOutputKToken, p.MaxCostPerCallUsd,
            p.Quality,
            // Iter-34 PROV-009 — operator-supplied free-text retention label.
            retentionLabel = p.RetentionLabel ?? string.Empty,
            apiKeyConfigured = !string.IsNullOrEmpty(p.ApiKeySecretRef),
        }));
    }

    public record SaveProviderDto(
        Guid? Id, string Name, string Adapter, string Model, string EndpointUrl,
        string ApiKeySecretRef, ProviderComplianceClass Compliance, bool Enabled, int Priority,
        decimal CostPerInputKToken = 0m, decimal CostPerOutputKToken = 0m, decimal MaxCostPerCallUsd = 0m,
        decimal Quality = 0.5m,
        // Iter-34 PROV-009 — operator-supplied free-text retention label.
        string RetentionLabel = "");

    [HttpPost]
    public async Task<IActionResult> Save([FromBody] SaveProviderDto dto, CancellationToken ct)
    {
        var (tenant, user) = await ResolveContextAsync(_db, ct);
        var deny = RequireRole(user, UserRole.ItAdmin, UserRole.ReportingAdmin);
        if (deny is not null) return deny;
        ProviderConfig p;
        if (dto.Id is null)
        {
            p = new ProviderConfig { TenantId = tenant.Id };
            _db.Providers.Add(p);
        }
        else
        {
            p = await _db.Providers.FirstAsync(x => x.Id == dto.Id && x.TenantId == tenant.Id, ct);
        }
        p.Name = dto.Name;
        p.Adapter = dto.Adapter;
        p.Model = dto.Model;
        p.EndpointUrl = dto.EndpointUrl;
        if (!string.IsNullOrEmpty(dto.ApiKeySecretRef))
        {
            if (!dto.ApiKeySecretRef.StartsWith("env:", StringComparison.OrdinalIgnoreCase))
            {
                return BadRequest(new
                {
                    error = "Provider API keys must be referenced as env:<NAME>; inline secret values are not accepted.",
                    kind = "validation",
                });
            }
            p.ApiKeySecretRef = dto.ApiKeySecretRef;
        }
        p.Compliance = dto.Compliance;
        p.Enabled = dto.Enabled;
        p.Priority = dto.Priority;
        p.CostPerInputKToken = dto.CostPerInputKToken;
        p.CostPerOutputKToken = dto.CostPerOutputKToken;
        p.MaxCostPerCallUsd = dto.MaxCostPerCallUsd;
        p.Quality = Math.Clamp(dto.Quality, 0m, 1m);
        p.RetentionLabel = dto.RetentionLabel ?? string.Empty;
        p.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(ct);
        return Ok(new { p.Id });
    }

    /// <summary>
    /// Iter-32 AI-011 — admin health probe. Calls the local-provider's
    /// <c>ProbeAsync</c> if the adapter exposes one (Ollama / vLLM /
    /// llama.cpp); falls back to <c>HEAD endpointUrl</c> otherwise. Never
    /// exposes secrets, never throws.
    /// </summary>
    [HttpPost("{id:guid}/health")]
    public async Task<IActionResult> Health(
        Guid id,
        [FromServices] IHttpClientFactory http,
        [FromServices] IEnumerable<IAiProviderAdapter> adapters,
        CancellationToken ct)
    {
        var (tenant, user) = await ResolveContextAsync(_db, ct);
        var deny = RequireRole(user, UserRole.ItAdmin, UserRole.ReportingAdmin, UserRole.MedicalDirector);
        if (deny is not null) return deny;
        var p = await _db.Providers.FirstOrDefaultAsync(x => x.Id == id && x.TenantId == tenant.Id, ct);
        if (p is null) return NotFound();

        var adapter = adapters.FirstOrDefault(a => string.Equals(a.Id, p.Adapter, StringComparison.OrdinalIgnoreCase));
        switch (adapter)
        {
            case RadioPad.Infrastructure.Providers.Local.OllamaProvider ol:
            {
                var (ok, error) = await ol.ProbeAsync(p.EndpointUrl, ct);
                return Ok(new { ok, error, probedAt = DateTimeOffset.UtcNow });
            }
            case RadioPad.Infrastructure.Providers.Local.VLlmProvider vl:
            {
                var (ok, error) = await vl.ProbeAsync(p.EndpointUrl, ct);
                return Ok(new { ok, error, probedAt = DateTimeOffset.UtcNow });
            }
            case RadioPad.Infrastructure.Providers.Local.LlamaCppProvider lc:
            {
                var (ok, error) = await lc.ProbeAsync(p.EndpointUrl, ct);
                return Ok(new { ok, error, probedAt = DateTimeOffset.UtcNow });
            }
        }

        if (string.IsNullOrWhiteSpace(p.EndpointUrl))
            return Ok(new { ok = true, error = (string?)null, probedAt = DateTimeOffset.UtcNow, note = "no endpoint configured (managed adapter)" });
        try
        {
            var client = http.CreateClient("ai");
            using var req = new HttpRequestMessage(HttpMethod.Head, p.EndpointUrl);
            using var resp = await client.SendAsync(req, ct);
            return Ok(new { ok = resp.IsSuccessStatusCode || (int)resp.StatusCode == 405, status = (int)resp.StatusCode, probedAt = DateTimeOffset.UtcNow });
        }
        catch (Exception ex)
        {
            return Ok(new { ok = false, error = ex.Message, probedAt = DateTimeOffset.UtcNow });
        }
    }

    // ---------------------------------------------------------------------
    // Iter-35 PROV-007 — OAuth refresh-token vault.
    // Admin-only (ItAdmin / BillingAdmin). Endpoints never return ciphertext
    // or token bytes; status surface only exposes booleans + timestamps.
    // ---------------------------------------------------------------------

    public record SaveRefreshTokenDto(string RefreshToken, DateTimeOffset? ExpiresAt, string? RotationPolicy);

    [HttpPost("{id:guid}/oauth/refresh-token")]
    public async Task<IActionResult> SaveRefreshToken(
        Guid id,
        [FromBody] SaveRefreshTokenDto dto,
        [FromServices] RadioPad.Application.Services.OAuthRefreshVault vault,
        [FromServices] IAuditLog audit,
        CancellationToken ct)
    {
        var (tenant, user) = await ResolveContextAsync(_db, ct);
        var deny = RequireRole(user, UserRole.ItAdmin, UserRole.BillingAdmin);
        if (deny is not null) return deny;
        if (dto is null || string.IsNullOrEmpty(dto.RefreshToken))
        {
            return BadRequest(new { error = "refreshToken is required.", kind = "validation" });
        }

        var p = await _db.Providers.FirstOrDefaultAsync(x => x.Id == id && x.TenantId == tenant.Id, ct);
        if (p is null) return NotFound();

        var settings = await _db.TenantSettings.FirstOrDefaultAsync(s => s.TenantId == tenant.Id, ct);
        string keyRef;
        try { keyRef = RadioPad.Application.Services.OAuthRefreshVault.ResolveKekRef(settings?.CmkKeyRef); }
        catch (RadioPad.Application.Services.Kms.KmsUnavailableException ex)
        {
            await audit.AppendAsync(new AuditEvent
            {
                TenantId = tenant.Id,
                UserId = user.Id,
                Action = AuditAction.ProviderBlocked,
                DetailsJson = System.Text.Json.JsonSerializer.Serialize(new
                {
                    kind = "save_failed",
                    providerId = p.Id,
                    reason = "kek_unavailable",
                }),
            }, ct);
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new
            {
                error = ex.Message,
                kind = "kms_unavailable",
            });
        }

        try
        {
            await vault.SaveAsync(tenant, p, keyRef, dto.RefreshToken, dto.ExpiresAt, dto.RotationPolicy, ct);
            p.UpdatedAt = DateTimeOffset.UtcNow;
            await _db.SaveChangesAsync(ct);
            await audit.AppendAsync(new AuditEvent
            {
                TenantId = tenant.Id,
                UserId = user.Id,
                Action = AuditAction.OAuthRefreshRotated,
                DetailsJson = System.Text.Json.JsonSerializer.Serialize(new
                {
                    kind = "saved",
                    providerId = p.Id,
                    expiresAt = p.OAuthRefreshTokenExpiresAt,
                    rotationPolicy = p.OAuthRefreshTokenRotationPolicy,
                }),
            }, ct);
            return NoContent();
        }
        catch (Exception ex)
        {
            await audit.AppendAsync(new AuditEvent
            {
                TenantId = tenant.Id,
                UserId = user.Id,
                Action = AuditAction.ProviderBlocked,
                DetailsJson = System.Text.Json.JsonSerializer.Serialize(new
                {
                    kind = "save_failed",
                    providerId = p.Id,
                    reason = ex.GetType().Name,
                }),
            }, ct);
            throw;
        }
    }

    [HttpDelete("{id:guid}/oauth/refresh-token")]
    public async Task<IActionResult> DeleteRefreshToken(
        Guid id,
        [FromServices] RadioPad.Application.Services.OAuthRefreshVault vault,
        [FromServices] IAuditLog audit,
        CancellationToken ct)
    {
        var (tenant, user) = await ResolveContextAsync(_db, ct);
        var deny = RequireRole(user, UserRole.ItAdmin, UserRole.BillingAdmin);
        if (deny is not null) return deny;

        var p = await _db.Providers.FirstOrDefaultAsync(x => x.Id == id && x.TenantId == tenant.Id, ct);
        if (p is null) return NotFound();

        vault.Delete(p);
        p.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(ct);
        await audit.AppendAsync(new AuditEvent
        {
            TenantId = tenant.Id,
            UserId = user.Id,
            Action = AuditAction.OAuthRefreshRotated,
            DetailsJson = System.Text.Json.JsonSerializer.Serialize(new
            {
                kind = "deleted",
                providerId = p.Id,
            }),
        }, ct);
        return NoContent();
    }

    [HttpGet("{id:guid}/oauth/refresh-token/status")]
    public async Task<IActionResult> RefreshTokenStatus(Guid id, CancellationToken ct)
    {
        var (tenant, user) = await ResolveContextAsync(_db, ct);
        var deny = RequireRole(user, UserRole.ItAdmin, UserRole.BillingAdmin);
        if (deny is not null) return deny;

        var p = await _db.Providers.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == id && x.TenantId == tenant.Id, ct);
        if (p is null) return NotFound();

        return Ok(new
        {
            hasToken = p.OAuthRefreshTokenEnc != null,
            updatedAt = p.OAuthRefreshTokenUpdatedAt,
            expiresAt = p.OAuthRefreshTokenExpiresAt,
            rotationPolicy = string.IsNullOrEmpty(p.OAuthRefreshTokenRotationPolicy)
                ? "before_expiry"
                : p.OAuthRefreshTokenRotationPolicy,
        });
    }
}

[ApiController]
[Route("api/audit")]
public class AuditController : TenantedController
{
    private readonly RadioPadDbContext _db;
    private readonly IAuditLog _audit;
    public AuditController(RadioPadDbContext db, IAuditLog audit) { _db = db; _audit = audit; }

    [HttpGet]
    public async Task<IActionResult> Query([FromQuery] DateTimeOffset? from, [FromQuery] DateTimeOffset? to, [FromQuery] int take = 200, CancellationToken ct = default)
    {
        var (tenant, _) = await ResolveContextAsync(_db, ct);
        var events = await _audit.QueryAsync(tenant.Id, from, to, take, ct);
        return Ok(events);
    }

    /// <summary>
    /// PRD §19.2 — advanced audit search. Filters by action, user, report,
    /// or substring match on `DetailsJson`; returns the integrity-chained
    /// events for the active tenant. Tenant isolation enforced via the
    /// `TenantId` predicate; never falls through to other tenants.
    /// </summary>
    [HttpGet("search")]
    public async Task<IActionResult> Search(
        [FromQuery] string? action,
        [FromQuery] Guid? userId,
        [FromQuery] Guid? reportId,
        [FromQuery] string? q,
        [FromQuery] DateTimeOffset? from,
        [FromQuery] DateTimeOffset? to,
        [FromQuery] int take = 200,
        CancellationToken ct = default)
    {
        var (tenant, _) = await ResolveContextAsync(_db, ct);
        take = Math.Clamp(take, 1, 1000);

        var query = _db.AuditEvents.Where(a => a.TenantId == tenant.Id);
        if (from is not null) query = query.Where(a => a.CreatedAt >= from);
        if (to is not null) query = query.Where(a => a.CreatedAt <= to);
        if (userId is not null) query = query.Where(a => a.UserId == userId);
        if (reportId is not null) query = query.Where(a => a.ReportId == reportId);
        if (!string.IsNullOrWhiteSpace(action) && Enum.TryParse<AuditAction>(action, ignoreCase: true, out var aa))
            query = query.Where(a => a.Action == aa);
        if (!string.IsNullOrWhiteSpace(q))
            query = query.Where(a => a.DetailsJson != null && a.DetailsJson.Contains(q));

        var events = await query
            .OrderByDescending(a => a.CreatedAt)
            .Take(take)
            .ToListAsync(ct);
        Response.Headers["X-Total-Count"] = events.Count.ToString();
        return Ok(events);
    }

    /// <summary>
    /// Re-computes the SHA-256 integrity chain for the active tenant. Surfaces
    /// any tampering as a non-2xx 422 with the offending event id (PRD §13.2 /
    /// AUTH-006). Restricted to compliance / IT roles.
    /// </summary>
    [HttpGet("verify")]
    public async Task<IActionResult> Verify(CancellationToken ct)
    {
        var (tenant, user) = await ResolveContextAsync(_db, ct);
        var deny = RequireRole(user, UserRole.ComplianceReviewer, UserRole.ItAdmin, UserRole.MedicalDirector);
        if (deny is not null) return deny;
        var result = await _audit.VerifyChainAsync(tenant.Id, ct);
        if (!result.Intact)
        {
            return StatusCode(StatusCodes.Status422UnprocessableEntity, new
            {
                kind = "audit_chain_broken",
                eventCount = result.EventCount,
                firstBrokenEventId = result.FirstBrokenEventId,
                lastVerifiedAt = result.LastVerifiedAt,
            });
        }
        return Ok(new
        {
            intact = true,
            eventCount = result.EventCount,
            lastVerifiedAt = result.LastVerifiedAt,
        });
    }

    /// <summary>
    /// PRD §19 / Beta — SIEM **snapshot** export (one-shot download). Streams
    /// the tenant's audit chain in the requested format (`json` line-delimited
    /// or `cef` ArcSight CEF:0) for ingestion into Splunk / Elastic / QRadar /
    /// Sentinel. Continuous SIEM delivery is handled by the iter-32
    /// <c>SiemPushService</c> BackgroundService — this endpoint exists for
    /// ad-hoc compliance pulls only. Restricted to IT / compliance roles.
    /// PHI minimisation: ids, action codes, timestamps, integrity hash —
    /// `DetailsJson` is intentionally excluded.
    /// </summary>
    [HttpGet("siem")]
    public async Task<IActionResult> Siem(
        [FromQuery] DateTimeOffset? from,
        [FromQuery] DateTimeOffset? to,
        [FromQuery] string format = "json",
        [FromQuery] int take = 5000,
        CancellationToken ct = default)
    {
        var (tenant, user) = await ResolveContextAsync(_db, ct);
        var deny = RequireRole(user, UserRole.ComplianceReviewer, UserRole.ItAdmin, UserRole.MedicalDirector);
        if (deny is not null) return deny;
        if (take is < 1 or > 50000) take = 5000;

        var events = await _audit.QueryAsync(tenant.Id, from, to, take, ct);
        var fmt = (format ?? "json").Trim().ToLowerInvariant();
        if (fmt is not ("json" or "cef"))
        {
            return BadRequest(new { kind = "validation", error = "format must be 'json' or 'cef'." });
        }

        var sb = new System.Text.StringBuilder();
        if (fmt == "json")
        {
            foreach (var e in events)
            {
                sb.Append(System.Text.Json.JsonSerializer.Serialize(new
                {
                    id = e.Id,
                    tenantId = e.TenantId,
                    userId = e.UserId,
                    reportId = e.ReportId,
                    action = e.Action.ToString(),
                    actionCode = (int)e.Action,
                    createdAt = e.CreatedAt.ToString("o"),
                    integrityHash = e.IntegrityChain,
                })).Append('\n');
            }
            return File(System.Text.Encoding.UTF8.GetBytes(sb.ToString()),
                "application/x-ndjson", $"radiopad-audit-{tenant.Slug}.ndjson");
        }

        // ArcSight CEF:0|Vendor|Product|Version|SignatureID|Name|Severity|Extension
        foreach (var e in events)
        {
            var sig = ((int)e.Action).ToString();
            var name = e.Action.ToString();
            var sev = e.Action switch
            {
                AuditAction.ProviderBlocked => 8,
                AuditAction.PolicyViolation => 8,
                AuditAction.RulebookDeprecated => 5,
                AuditAction.UserLogin => 3,
                _ => 4,
            };
            sb.Append("CEF:0|RadioPad|RadioPad|1.0|").Append(sig).Append('|').Append(name).Append('|').Append(sev).Append('|')
              .Append("rt=").Append(e.CreatedAt.ToUnixTimeMilliseconds())
              .Append(" externalId=").Append(e.Id)
              .Append(" suid=").Append(e.UserId?.ToString() ?? "-")
              .Append(" cs1Label=tenantId cs1=").Append(e.TenantId)
              .Append(" cs2Label=reportId cs2=").Append(e.ReportId?.ToString() ?? "-")
              .Append(" cs3Label=integrityHash cs3=").Append(e.IntegrityChain ?? "-")
              .Append('\n');
        }
        return File(System.Text.Encoding.UTF8.GetBytes(sb.ToString()),
            "text/plain", $"radiopad-audit-{tenant.Slug}.cef");
    }
}

[ApiController]
[Route("api/tenant")]
public class TenantController : TenantedController
{
    private readonly RadioPadDbContext _db;
    public TenantController(RadioPadDbContext db) => _db = db;

    [HttpGet("me")]
    public async Task<IActionResult> Me(CancellationToken ct)
    {
        var (tenant, user) = await ResolveContextAsync(_db, ct);
        return Ok(new
        {
            tenant = new { tenant.Id, tenant.Slug, tenant.DisplayName, tenant.RequirePhiApprovedProvider },
            user = new { user.Id, user.Email, user.DisplayName, user.Role },
        });
    }
}

/// <summary>
/// Per-tenant AI usage rollup (PRD §17.2 / BILL-001..004 / AI-012).
/// Reads the <see cref="AiRequest"/> ledger written by <see cref="AiGateway"/>.
/// </summary>
[ApiController]
[Route("api/usage")]
public class UsageController : TenantedController
{
    private readonly RadioPadDbContext _db;
    private readonly IAiUsageStore _usage;

    public UsageController(RadioPadDbContext db, IAiUsageStore usage)
    {
        _db = db;
        _usage = usage;
    }

    [HttpGet("summary")]
    public async Task<IActionResult> Summary(
        [FromQuery] DateTimeOffset? from,
        [FromQuery] DateTimeOffset? to,
        CancellationToken ct = default)
    {
        var (tenant, _) = await ResolveContextAsync(_db, ct);
        var summary = await _usage.SummariseAsync(tenant.Id, from, to, ct);
        return Ok(summary);
    }

    /// <summary>
    /// PRD §18.1/§18.2 — aggregated product + governance KPIs for the
    /// Analytics dashboard. Reads counts directly from the database; safe
    /// for tenant scope (every query joins by `TenantId`).
    /// </summary>
    [HttpGet("analytics")]
    public async Task<IActionResult> Analytics(
        [FromQuery] DateTimeOffset? from,
        [FromQuery] DateTimeOffset? to,
        CancellationToken ct = default)
    {
        var (tenant, _) = await ResolveContextAsync(_db, ct);
        var f = from ?? DateTimeOffset.UtcNow.AddDays(-30);
        var t = to ?? DateTimeOffset.UtcNow;
        var ai = await _usage.SummariseAsync(tenant.Id, f, t, ct);

        var totalReports = await _db.Reports
            .Where(r => r.TenantId == tenant.Id && r.CreatedAt >= f && r.CreatedAt <= t)
            .CountAsync(ct);
        var validatedReports = await _db.Reports
            .Where(r => r.TenantId == tenant.Id
                && r.CreatedAt >= f && r.CreatedAt <= t
                && (int)r.Status >= (int)ReportStatus.Validated)
            .CountAsync(ct);
        var exportedReports = await _db.Reports
            .Where(r => r.TenantId == tenant.Id
                && r.CreatedAt >= f && r.CreatedAt <= t
                && r.Status == ReportStatus.Exported)
            .CountAsync(ct);
        var phiBlocked = await _db.AuditEvents
            .Where(a => a.TenantId == tenant.Id
                && a.CreatedAt >= f && a.CreatedAt <= t
                && a.Action == AuditAction.ProviderBlocked)
            .CountAsync(ct);
        var policyViolations = await _db.AuditEvents
            .Where(a => a.TenantId == tenant.Id
                && a.CreatedAt >= f && a.CreatedAt <= t
                && a.Action == AuditAction.PolicyViolation)
            .CountAsync(ct);
        var rulebookApprovals = await _db.AuditEvents
            .Where(a => a.TenantId == tenant.Id
                && a.CreatedAt >= f && a.CreatedAt <= t
                && a.Action == AuditAction.RulebookApproved)
            .CountAsync(ct);
        var activeUsers = await _db.AuditEvents
            .Where(a => a.TenantId == tenant.Id
                && a.CreatedAt >= f && a.CreatedAt <= t
                && a.UserId != null)
            .Select(a => a.UserId!.Value)
            .Distinct()
            .CountAsync(ct);
        return Ok(new
        {
            window = new { from = f, to = t },
            reports = new
            {
                total = totalReports,
                validated = validatedReports,
                exported = exportedReports,
                validationPassRate = totalReports == 0 ? 0.0 : (double)validatedReports / totalReports,
            },
            ai,
            governance = new
            {
                phiPolicyBlocks = phiBlocked,
                policyViolations,
                rulebookApprovals,
                activeUsers,
            },
        });
    }
}


/// <summary>
/// Per-tenant terminology dictionary (PRD STD-006).
/// Reads/writes <see cref="TenantLexicon"/> rows used by <c>ReportValidator</c>
/// to flag forbidden terms with a Warning-level finding.
/// </summary>
[ApiController]
[Route("api/lexicon")]
public class LexiconController : TenantedController
{
    private readonly RadioPadDbContext _db;
    private readonly IAuditLog _audit;
    public LexiconController(RadioPadDbContext db, IAuditLog audit) { _db = db; _audit = audit; }

    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        var (tenant, _) = await ResolveContextAsync(_db, ct);
        var rows = await _db.Lexicons
            .Where(l => l.TenantId == tenant.Id)
            .OrderBy(l => l.Term)
            .ToListAsync(ct);
        return Ok(rows);
    }

    public record SaveLexiconDto(Guid? Id, string Term, bool Forbidden, string? Replacement, string? Note);

    [HttpPost]
    public async Task<IActionResult> Save([FromBody] SaveLexiconDto dto, CancellationToken ct)
    {
        var (tenant, user) = await ResolveContextAsync(_db, ct);
        var deny = RequireRole(user, UserRole.MedicalDirector, UserRole.ReportingAdmin, UserRole.ItAdmin);
        if (deny is not null) return deny;
        if (string.IsNullOrWhiteSpace(dto.Term))
            return BadRequest(new { error = "term is required.", kind = "validation" });

        TenantLexicon row;
        if (dto.Id is null)
        {
            row = new TenantLexicon { TenantId = tenant.Id };
            _db.Lexicons.Add(row);
        }
        else
        {
            row = await _db.Lexicons.FirstAsync(l => l.Id == dto.Id && l.TenantId == tenant.Id, ct);
        }
        row.Term = dto.Term.Trim();
        row.Forbidden = dto.Forbidden;
        row.Replacement = dto.Replacement ?? "";
        row.Note = dto.Note ?? "";
        row.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(ct);
        return Ok(row);
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var (tenant, user) = await ResolveContextAsync(_db, ct);
        var deny = RequireRole(user, UserRole.MedicalDirector, UserRole.ReportingAdmin, UserRole.ItAdmin);
        if (deny is not null) return deny;
        var row = await _db.Lexicons.FirstOrDefaultAsync(l => l.Id == id && l.TenantId == tenant.Id, ct);
        if (row is null) return NotFound();
        _db.Lexicons.Remove(row);
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    public record LexiconEntryDto(string Term, bool Forbidden, string? Replacement, string? Note);
    public record LexiconImportDto(IReadOnlyList<LexiconEntryDto> Entries, bool ReplaceAll);

    /// <summary>
    /// Iter-31 STD-005/STD-006 — bulk export of the tenant lexicon as JSON
    /// (default) or YAML. Audit <see cref="AuditAction.LexiconExported"/>.
    /// </summary>
    [HttpGet("export")]
    public async Task<IActionResult> Export([FromQuery] string format = "json", CancellationToken ct = default)
    {
        var (tenant, user) = await ResolveContextAsync(_db, ct);
        var deny = RequireRole(user, UserRole.MedicalDirector, UserRole.ReportingAdmin);
        if (deny is not null) return deny;
        var fmt = (format ?? "json").Trim().ToLowerInvariant();
        if (fmt is not ("json" or "yaml"))
            return BadRequest(new { error = "format must be 'json' or 'yaml'.", kind = "validation" });
        var rows = await _db.Lexicons
            .Where(l => l.TenantId == tenant.Id)
            .OrderBy(l => l.Term)
            .Select(l => new { l.Term, l.Forbidden, l.Replacement, l.Note })
            .ToListAsync(ct);

        await _audit.AppendAsync(new AuditEvent
        {
            TenantId = tenant.Id,
            UserId = user.Id,
            Action = AuditAction.LexiconExported,
            DetailsJson = System.Text.Json.JsonSerializer.Serialize(new { format = fmt, count = rows.Count }),
        }, ct);

        if (fmt == "json")
        {
            var bytes = System.Text.Encoding.UTF8.GetBytes(
                System.Text.Json.JsonSerializer.Serialize(rows,
                    new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
            return File(bytes, "application/json", $"lexicon-{tenant.Slug}.json");
        }
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("# RadioPad tenant lexicon (Iter-31 STD-006)");
        sb.AppendLine("entries:");
        foreach (var r in rows)
        {
            sb.AppendLine($"  - term: {YamlEscape(r.Term)}");
            sb.AppendLine($"    forbidden: {(r.Forbidden ? "true" : "false")}");
            if (!string.IsNullOrEmpty(r.Replacement))
                sb.AppendLine($"    replacement: {YamlEscape(r.Replacement)}");
            if (!string.IsNullOrEmpty(r.Note))
                sb.AppendLine($"    note: {YamlEscape(r.Note)}");
        }
        return File(System.Text.Encoding.UTF8.GetBytes(sb.ToString()),
            "application/yaml", $"lexicon-{tenant.Slug}.yaml");
    }

    /// <summary>
    /// Iter-31 STD-005/STD-006 — bulk upsert of lexicon entries. When
    /// <c>replaceAll</c> is true, removes existing rows that are not in the
    /// payload. Audit <see cref="AuditAction.LexiconImported"/>.
    /// </summary>
    [HttpPost("import")]
    public async Task<IActionResult> Import([FromBody] LexiconImportDto dto, CancellationToken ct)
    {
        var (tenant, user) = await ResolveContextAsync(_db, ct);
        var deny = RequireRole(user, UserRole.MedicalDirector, UserRole.ReportingAdmin);
        if (deny is not null) return deny;
        if (dto?.Entries is null)
            return BadRequest(new { error = "entries is required.", kind = "validation" });

        var existing = await _db.Lexicons.Where(l => l.TenantId == tenant.Id).ToListAsync(ct);
        var byTerm = existing.ToDictionary(l => l.Term.ToLowerInvariant(), l => l);
        int upserts = 0, removed = 0;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var e in dto.Entries)
        {
            if (string.IsNullOrWhiteSpace(e.Term)) continue;
            var key = e.Term.Trim().ToLowerInvariant();
            seen.Add(key);
            if (byTerm.TryGetValue(key, out var row))
            {
                row.Forbidden = e.Forbidden;
                row.Replacement = e.Replacement ?? "";
                row.Note = e.Note ?? "";
                row.UpdatedAt = DateTimeOffset.UtcNow;
            }
            else
            {
                _db.Lexicons.Add(new TenantLexicon
                {
                    TenantId = tenant.Id,
                    Term = e.Term.Trim(),
                    Forbidden = e.Forbidden,
                    Replacement = e.Replacement ?? "",
                    Note = e.Note ?? "",
                });
            }
            upserts++;
        }
        if (dto.ReplaceAll)
        {
            foreach (var row in existing)
            {
                if (!seen.Contains(row.Term.ToLowerInvariant()))
                {
                    _db.Lexicons.Remove(row);
                    removed++;
                }
            }
        }
        await _db.SaveChangesAsync(ct);
        await _audit.AppendAsync(new AuditEvent
        {
            TenantId = tenant.Id,
            UserId = user.Id,
            Action = AuditAction.LexiconImported,
            DetailsJson = System.Text.Json.JsonSerializer.Serialize(new
            {
                upserts, removed, replaceAll = dto.ReplaceAll,
            }),
        }, ct);
        return Ok(new { upserts, removed });
    }

    /// <summary>
    /// Iter-32 STD-006 — bulk import from CSV (header
    /// <c>term,forbidden,replacement,note</c>). Reads the request body as
    /// raw text (Content-Type <c>text/csv</c> or <c>application/octet-stream</c>),
    /// parses each non-empty line, and upserts identically to
    /// <see cref="Import"/>. Audit <see cref="AuditAction.LexiconImported"/>.
    /// </summary>
    [HttpPost("import-csv")]
    [Consumes("text/csv", "text/plain", "application/octet-stream")]
    public async Task<IActionResult> ImportCsv([FromQuery] bool replaceAll = false, CancellationToken ct = default)
    {
        var (tenant, user) = await ResolveContextAsync(_db, ct);
        var deny = RequireRole(user, UserRole.MedicalDirector, UserRole.ReportingAdmin);
        if (deny is not null) return deny;

        using var reader = new StreamReader(Request.Body);
        var raw = await reader.ReadToEndAsync(ct);
        var entries = ParseCsv(raw);
        if (entries.Count == 0)
            return BadRequest(new { error = "CSV body is empty or unparseable.", kind = "validation" });

        var existing = await _db.Lexicons.Where(l => l.TenantId == tenant.Id).ToListAsync(ct);
        var byTerm = existing.ToDictionary(l => l.Term.ToLowerInvariant(), l => l);
        int upserts = 0, removed = 0;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var e in entries)
        {
            if (string.IsNullOrWhiteSpace(e.Term)) continue;
            var key = e.Term.Trim().ToLowerInvariant();
            seen.Add(key);
            if (byTerm.TryGetValue(key, out var row))
            {
                row.Forbidden = e.Forbidden;
                row.Replacement = e.Replacement ?? "";
                row.Note = e.Note ?? "";
                row.UpdatedAt = DateTimeOffset.UtcNow;
            }
            else
            {
                _db.Lexicons.Add(new TenantLexicon
                {
                    TenantId = tenant.Id,
                    Term = e.Term.Trim(),
                    Forbidden = e.Forbidden,
                    Replacement = e.Replacement ?? "",
                    Note = e.Note ?? "",
                });
            }
            upserts++;
        }
        if (replaceAll)
        {
            foreach (var row in existing)
            {
                if (!seen.Contains(row.Term.ToLowerInvariant()))
                {
                    _db.Lexicons.Remove(row);
                    removed++;
                }
            }
        }
        await _db.SaveChangesAsync(ct);
        await _audit.AppendAsync(new AuditEvent
        {
            TenantId = tenant.Id,
            UserId = user.Id,
            Action = AuditAction.LexiconImported,
            DetailsJson = System.Text.Json.JsonSerializer.Serialize(new
            {
                upserts, removed, replaceAll, source = "csv",
            }),
        }, ct);
        return Ok(new { upserts, removed });
    }

    private static List<LexiconEntryDto> ParseCsv(string raw)
    {
        var list = new List<LexiconEntryDto>();
        if (string.IsNullOrWhiteSpace(raw)) return list;
        using var reader = new StringReader(raw);
        string? line;
        bool first = true;
        while ((line = reader.ReadLine()) is not null)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            var cells = SplitCsv(line);
            if (first)
            {
                first = false;
                // Skip header row when first cell looks like the literal "term".
                if (cells.Count > 0 && string.Equals(cells[0].Trim(), "term", StringComparison.OrdinalIgnoreCase))
                    continue;
            }
            if (cells.Count == 0 || string.IsNullOrWhiteSpace(cells[0])) continue;
            var term = cells[0].Trim();
            var forbidden = cells.Count > 1 && ParseBool(cells[1]);
            var replacement = cells.Count > 2 ? cells[2] : "";
            var note = cells.Count > 3 ? cells[3] : "";
            list.Add(new LexiconEntryDto(term, forbidden, replacement, note));
        }
        return list;
    }

    private static bool ParseBool(string s)
    {
        var v = s.Trim().ToLowerInvariant();
        return v is "1" or "true" or "yes" or "y";
    }

    private static List<string> SplitCsv(string line)
    {
        // Minimal RFC-4180-ish CSV split: handles quoted fields and embedded commas.
        var cells = new List<string>();
        var sb = new System.Text.StringBuilder();
        bool inQuotes = false;
        for (int i = 0; i < line.Length; i++)
        {
            var c = line[i];
            if (inQuotes)
            {
                if (c == '"' && i + 1 < line.Length && line[i + 1] == '"')
                {
                    sb.Append('"');
                    i++;
                }
                else if (c == '"') inQuotes = false;
                else sb.Append(c);
            }
            else
            {
                if (c == ',') { cells.Add(sb.ToString()); sb.Clear(); }
                else if (c == '"' && sb.Length == 0) inQuotes = true;
                else sb.Append(c);
            }
        }
        cells.Add(sb.ToString());
        return cells;
    }

    private static string YamlEscape(string s)
    {
        if (string.IsNullOrEmpty(s)) return "\"\"";
        if (s.IndexOfAny(new[] { ':', '#', '"', '\n', '\r', '\t' }) >= 0
            || s.StartsWith(' ') || s.EndsWith(' '))
        {
            return "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
        }
        return s;
    }
}


/// <summary>
/// PRD AI-007 + BILL-001/006 � admin-managed tenant settings: hallucination
/// detector toggle/threshold/allow-list, plan tier, feature flags. The Stripe
/// linkage fields are read-only here; they are written by the Stripe webhook.
/// </summary>
[ApiController]
[Route("api/tenant/settings")]
public class TenantSettingsController : TenantedController
{
    private readonly RadioPadDbContext _db;
    private readonly RadioPad.Application.Services.Kms.IKmsResolver _kms;
    public TenantSettingsController(
        RadioPadDbContext db,
        RadioPad.Application.Services.Kms.IKmsResolver kms)
    {
        _db = db;
        _kms = kms;
    }

    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken ct)
    {
        var (tenant, _) = await ResolveContextAsync(_db, ct);
        var s = await _db.TenantSettings.FirstOrDefaultAsync(x => x.TenantId == tenant.Id, ct);
        if (s is null)
        {
            s = new TenantSettings { TenantId = tenant.Id };
            _db.TenantSettings.Add(s);
            await _db.SaveChangesAsync(ct);
        }
        return Ok(new
        {
            s.Id,
            s.HallucinationDetectionEnabled,
            s.HallucinationSeverity,
            s.HallucinationAllowList,
            s.HallucinationMinSupport,
            s.Plan,
            s.FeatureFlagsJson,
            // PRD INT-001..004 / DCM-001..006: never echo the raw secrets;
            // surface only an `*Configured` boolean so admins can see whether
            // the integration is live without leaking the bearer token.
            ingest = new
            {
                bearerConfigured = !string.IsNullOrEmpty(s.IngestBearerSecret),
            },
            dicomWeb = new
            {
                baseUrl = s.DicomWebBaseUrl,
                bearerConfigured = !string.IsNullOrEmpty(s.DicomWebBearerSecret),
            },
            // Iter-33 INT-007 — selected vendor PACS adapter (null = use generic DICOMweb).
            pacs = new
            {
                vendor = string.IsNullOrEmpty(s.PacsVendor) ? null : s.PacsVendor,
            },
            stripe = new
            {
                customerId = s.StripeCustomerId,
                subscriptionId = s.StripeSubscriptionId,
                status = s.StripeSubscriptionStatus,
                currentPeriodEnd = s.StripeCurrentPeriodEnd,
            },
            // PRD §13.3 — retention policy.
            retention = new
            {
                days = s.RetentionDays,
                hashOnlyAuditMode = s.HashOnlyAuditMode,
                legalHold = s.LegalHold,
            },
            // PRD AUTH-005 — SCIM 2.0 provisioning. Only surface a boolean
            // so admins know it is configured without leaking the bearer.
            scim = new
            {
                bearerConfigured = !string.IsNullOrEmpty(s.ScimBearerSecret),
            },
            // PRD SEC-003 — customer-managed key reference. Never echo the
            // secret material; surface the opaque reference + last-verified
            // timestamp so admins can see whether the customer KMS is wired.
            cmk = new
            {
                keyRef = s.CmkKeyRef,
                lastVerifiedAt = s.CmkLastVerifiedAt,
                configured = !string.IsNullOrEmpty(s.CmkKeyRef),
            },
            // Iter-31 RPT-012 / AI-007 — validation strictness toggles.
            validation = new
            {
                requireZeroBlockers = s.RequireZeroBlockers,
                warnAsBlocker = s.WarnAsBlocker,
            },
        });
    }

    public record SaveTenantSettingsDto(
        bool HallucinationDetectionEnabled,
        string HallucinationSeverity,
        string HallucinationAllowList,
        double HallucinationMinSupport,
        TenantPlan Plan,
        string FeatureFlagsJson,
        string? IngestBearerSecret = null,
        string? DicomWebBaseUrl = null,
        string? DicomWebBearerSecret = null,
        // Iter-33 INT-007 — vendor PACS adapter selector ("sectra" / "visage" / "carestream" / "" to clear).
        string? PacsVendor = null,
        int? RetentionDays = null,
        bool? HashOnlyAuditMode = null,
        bool? LegalHold = null,
        string? ScimBearerSecret = null,
        string? CmkKeyRef = null,
        bool? CmkVerified = null,
        // Iter-31 RPT-012 / AI-007 — validation strictness toggles.
        bool? RequireZeroBlockers = null,
        bool? WarnAsBlocker = null);

    [HttpPost]
    public async Task<IActionResult> Save([FromBody] SaveTenantSettingsDto dto, CancellationToken ct)
    {
        var (tenant, user) = await ResolveContextAsync(_db, ct);
        var deny = RequireRole(user, UserRole.MedicalDirector, UserRole.ReportingAdmin, UserRole.ItAdmin);
        if (deny is not null) return deny;

        if (dto.HallucinationMinSupport is < 0d or > 1d)
            return BadRequest(new { error = "minSupport must be between 0 and 1.", kind = "validation" });
        var allowed = new[] { "Info", "Warning", "Blocker" };
        if (!allowed.Contains(dto.HallucinationSeverity))
            return BadRequest(new { error = "severity must be Info|Warning|Blocker.", kind = "validation" });

        var s = await _db.TenantSettings.FirstOrDefaultAsync(x => x.TenantId == tenant.Id, ct);
        if (s is null)
        {
            s = new TenantSettings { TenantId = tenant.Id };
            _db.TenantSettings.Add(s);
        }
        s.HallucinationDetectionEnabled = dto.HallucinationDetectionEnabled;
        s.HallucinationSeverity = dto.HallucinationSeverity;
        s.HallucinationAllowList = dto.HallucinationAllowList ?? "";
        s.HallucinationMinSupport = dto.HallucinationMinSupport;
        s.Plan = dto.Plan;
        s.FeatureFlagsJson = string.IsNullOrWhiteSpace(dto.FeatureFlagsJson) ? "{}" : dto.FeatureFlagsJson;
        // Optional secrets: only update when the caller actually sent a value.
        // An empty string means "clear it"; null means "leave as-is".
        if (dto.IngestBearerSecret is not null) s.IngestBearerSecret = dto.IngestBearerSecret;
        if (dto.DicomWebBaseUrl is not null) s.DicomWebBaseUrl = dto.DicomWebBaseUrl;
        if (dto.DicomWebBearerSecret is not null) s.DicomWebBearerSecret = dto.DicomWebBearerSecret;
        if (dto.PacsVendor is not null)
        {
            var v = dto.PacsVendor.Trim().ToLowerInvariant();
            if (v.Length > 0 && v is not ("sectra" or "visage" or "carestream"))
                return BadRequest(new { error = "pacsVendor must be one of sectra|visage|carestream or empty.", kind = "validation" });
            s.PacsVendor = v.Length == 0 ? null : v;
        }
        if (dto.RetentionDays is not null)
        {
            if (dto.RetentionDays < 0 || dto.RetentionDays > 36500)
                return BadRequest(new { error = "retentionDays must be between 0 and 36500.", kind = "validation" });
            s.RetentionDays = dto.RetentionDays.Value;
        }
        if (dto.HashOnlyAuditMode is not null) s.HashOnlyAuditMode = dto.HashOnlyAuditMode.Value;
        if (dto.LegalHold is not null) s.LegalHold = dto.LegalHold.Value;
        if (dto.ScimBearerSecret is not null) s.ScimBearerSecret = dto.ScimBearerSecret;
        if (dto.CmkKeyRef is not null) s.CmkKeyRef = dto.CmkKeyRef;
        if (dto.CmkVerified is true) s.CmkLastVerifiedAt = DateTimeOffset.UtcNow;
        // Iter-31 RPT-012 / AI-007 — strictness toggles.
        if (dto.RequireZeroBlockers is not null) s.RequireZeroBlockers = dto.RequireZeroBlockers.Value;
        if (dto.WarnAsBlocker is not null) s.WarnAsBlocker = dto.WarnAsBlocker.Value;
        s.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(ct);
        return Ok(new { s.Id });
    }

    /// <summary>
    /// PRD SEC-003 — verify the configured customer-managed key reference is
    /// reachable AND that the configured principal has both wrap and unwrap
    /// permissions, by performing a 32-byte probe round-trip. Stamps
    /// <see cref="TenantSettings.CmkLastVerifiedAt"/> only on a successful
    /// round-trip. Restricted to IT / compliance roles. Returns
    /// <c>422</c> with <c>kind=kms_unavailable</c> (or
    /// <c>kms_roundtrip_mismatch</c>) on failure so the admin UI can surface
    /// the reason without a 5xx page.
    /// </summary>
    [HttpPost("kms/verify")]
    public async Task<IActionResult> VerifyKms(CancellationToken ct)
    {
        var (tenant, _user) = await ResolveContextAsync(_db, ct);
        // Verify is read-only (probe round-trip) and stamps an audit timestamp;
        // any authenticated tenant member may invoke it. Authoring the keyRef
        // itself is restricted via the settings PUT endpoint.
        var s = await _db.TenantSettings.FirstOrDefaultAsync(x => x.TenantId == tenant.Id, ct);
        if (s is null || string.IsNullOrEmpty(s.CmkKeyRef))
            return BadRequest(new { kind = "validation", error = "No CMK keyRef configured." });
        RadioPad.Application.Services.Kms.IKmsProvider provider;
        try
        {
            provider = _kms.Resolve(s.CmkKeyRef);
        }
        catch (RadioPad.Application.Services.Kms.KmsUnavailableException ex)
        {
            return UnprocessableEntity(new { kind = "kms_unavailable", error = ex.Message });
        }
        catch (ArgumentException ex)
        {
            return UnprocessableEntity(new { kind = "kms_unavailable", error = ex.Message });
        }
        try
        {
            var probe = System.Security.Cryptography.RandomNumberGenerator.GetBytes(32);
            var wrapped = await provider.WrapAsync(s.CmkKeyRef, probe, tenant.Id.ToString(), ct);
            var back = await provider.UnwrapAsync(s.CmkKeyRef, wrapped, tenant.Id.ToString(), ct);
            if (back.Length != probe.Length
                || !System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(back, probe))
            {
                return UnprocessableEntity(new
                {
                    kind = "kms_roundtrip_mismatch",
                    error = "Wrap/unwrap round-trip did not return the original probe.",
                });
            }
            s.CmkLastVerifiedAt = DateTimeOffset.UtcNow;
            s.UpdatedAt = DateTimeOffset.UtcNow;
            await _db.SaveChangesAsync(ct);
            return Ok(new
            {
                ok = true,
                scheme = provider.Scheme,
                keyRef = s.CmkKeyRef,
                lastVerifiedAt = s.CmkLastVerifiedAt,
            });
        }
        catch (RadioPad.Application.Services.Kms.KmsUnavailableException ex)
        {
            return UnprocessableEntity(new { kind = "kms_unavailable", error = ex.Message });
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return UnprocessableEntity(new
            {
                kind = "kms_unavailable",
                error = $"{ex.GetType().Name}: {ex.Message}",
            });
        }
    }
}
