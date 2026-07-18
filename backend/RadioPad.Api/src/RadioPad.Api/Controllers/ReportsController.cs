using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RadioPad.Api.Auth;
using RadioPad.Application.Abstractions;
using RadioPad.Application.Security;
using RadioPad.Application.Services;
using RadioPad.Domain.Entities;
using RadioPad.Domain.Enums;
using RadioPad.Infrastructure.Persistence;

namespace RadioPad.Api.Controllers;

/// <summary>
/// Resolves the active tenant + user from a server-verified request context.
/// Dev/test headers remain available only when explicitly enabled.
/// </summary>
public abstract class TenantedController : ControllerBase
{
    protected async Task<(Tenant tenant, User user)> ResolveContextAsync(RadioPadDbContext db, CancellationToken ct)
    {
        string? slug;
        string? email;

        if (RadioPadRequestIdentity.TryGet(HttpContext, out var identity))
        {
            slug = identity.TenantSlug;
            email = identity.UserEmail;
        }
        else if (!RadioPadRequestIdentity.RequireAuthEnabled(HttpContext)
            && RadioPadRequestIdentity.DevHeadersEnabled(HttpContext))
        {
            slug = Request.Headers["X-RadioPad-Tenant"].FirstOrDefault() ?? "dev";
            email = Request.Headers["X-RadioPad-User"].FirstOrDefault() ?? "radiologist@radiopad.local";
        }
        else
        {
            throw new UnauthorizedAccessException("Authenticated tenant context is required.");
        }

        var tenant = await db.Tenants.FirstOrDefaultAsync(t => t.Slug == slug, ct)
            ?? throw new UnauthorizedAccessException("Authenticated tenant context is invalid.");
        var user = await db.Users.FirstOrDefaultAsync(u => u.TenantId == tenant.Id && u.Email == email, ct)
            ?? throw new UnauthorizedAccessException("Authenticated tenant context is invalid.");
        if (!user.IsActive)
            throw new UnauthorizedAccessException("User has been deprovisioned.");
        if (user.LockedUntil is not null && user.LockedUntil > DateTimeOffset.UtcNow)
            throw new UnauthorizedAccessException("Account locked.");
        return (tenant, user);
    }

    /// <summary>
    /// Legacy role allow-list helper for endpoints whose policy has not yet
    /// been migrated to the canonical permission matrix. Prefer
    /// <see cref="RequirePermission"/> for new or high-risk endpoint gates.
    /// </summary>
    protected static IActionResult? RequireRole(User user, params UserRole[] allowed)
    {
        if (allowed.Length == 0 || allowed.Contains(user.Role)) return null;
        return new ObjectResult(new
        {
            error = $"Role '{user.Role}' is not permitted to perform this action.",
            kind = "forbidden",
            requiredRoles = allowed.Select(r => r.ToString()).ToArray(),
        })
        { StatusCode = StatusCodes.Status403Forbidden };
    }

    protected IActionResult? RequirePermission(User user, RbacPermission permission)
    {
        var service = HttpContext.RequestServices.GetRequiredService<IPermissionService>();
        var decision = service.Authorize(user, permission);
        if (decision.Allowed) return null;

        return new ObjectResult(new
        {
            error = $"Role '{user.Role}' is not permitted to perform this action.",
            kind = "forbidden",
            requiredPermissions = new[] { decision.PermissionKey },
            requiredRoles = decision.CompatibleRoles.Select(r => r.ToString()).ToArray(),
        })
        { StatusCode = StatusCodes.Status403Forbidden };
    }
}

[ApiController]
[Route("api/reports")]
public class ReportsController : TenantedController
{
    private readonly RadioPadDbContext _db;
    private readonly ReportingService _reporting;
    private readonly IAuditLog _audit;
    private readonly Services.AiJobRegistry _aiJobs;
    private readonly IServiceScopeFactory _scopes;
    private readonly IHostApplicationLifetime _lifetime;

    public ReportsController(
        RadioPadDbContext db,
        ReportingService reporting,
        IAuditLog audit,
        Services.AiJobRegistry aiJobs,
        IServiceScopeFactory scopes,
        IHostApplicationLifetime lifetime)
    {
        _db = db;
        _reporting = reporting;
        _audit = audit;
        _aiJobs = aiJobs;
        _scopes = scopes;
        _lifetime = lifetime;
    }

    public record CreateReportDto(
        string? Modality, string? BodyPart, string? Contrast, string? Indication,
        int? Age, string? Gender,
        string? Comparison, string? AccessionNumber, Guid? RulebookId, Guid? TemplateId);

    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery] string? modality,
        [FromQuery] ReportStatus? status,
        [FromQuery] string? q,
        [FromQuery] int skip = 0,
        [FromQuery] int take = 100,
        CancellationToken ct = default)
    {
        var (tenant, user) = await ResolveContextAsync(_db, ct);
        var deny = RequirePermission(user, RbacPermission.ReportsRead);
        if (deny is not null) return deny;
        take = Math.Clamp(take, 1, 500);
        skip = Math.Max(0, skip);

        var query = _db.Reports.Where(r => r.TenantId == tenant.Id);
        if (!string.IsNullOrWhiteSpace(modality))
            query = query.Where(r => r.Study.Modality == modality);
        if (status is not null)
            query = query.Where(r => r.Status == status);
        if (!string.IsNullOrWhiteSpace(q))
        {
            var needle = q.Trim();
            query = query.Where(r =>
                EF.Functions.Like(r.Study.AccessionNumber, $"%{needle}%") ||
                EF.Functions.Like(r.Study.BodyPart, $"%{needle}%") ||
                EF.Functions.Like(r.Indication, $"%{needle}%"));
        }

        var total = await query.CountAsync(ct);
        var items = await query
            .OrderByDescending(r => r.UpdatedAt)
            .Skip(skip).Take(take)
            .ToListAsync(ct);
        Response.Headers["X-Total-Count"] = total.ToString();
        return Ok(items);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateReportDto dto, CancellationToken ct)
    {
        var (tenant, user) = await ResolveContextAsync(_db, ct);
        var deny = RequirePermission(user, RbacPermission.ReportsDraft);
        if (deny is not null) return deny;

        // Iter-32 TMP-005 — gate non-Approved templates the same way RB-010
        // gates rulebooks. Tenants that opt in to AllowSandboxRulebooks may
        // freely use Draft/Review templates; production tenants may only
        // pin Approved templates.
        if (dto.TemplateId is { } tplId)
        {
            var tplError = await ValidateTemplateAsync(tenant, tplId, ct);
            if (tplError is not null) return tplError;
        }
        if (dto.RulebookId is { } rbId)
        {
            var rbError = await ValidateRulebookAsync(tenant, rbId, ct);
            if (rbError is not null) return rbError;
        }

        var report = new Report
        {
            TenantId = tenant.Id,
            CreatedByUserId = user.Id,
            RulebookId = dto.RulebookId,
            TemplateId = dto.TemplateId,
            // Caller-supplied bindings are explicit choices — pin them so later
            // study-context changes never auto-rebind over them.
            RulebookPinned = dto.RulebookId is not null,
            TemplatePinned = dto.TemplateId is not null,
            Status = ReportStatus.Draft,
            Indication = dto.Indication ?? "",
            Study = new StudyContext
            {
                Modality = dto.Modality ?? "",
                BodyPart = dto.BodyPart ?? "",
                Contrast = dto.Contrast ?? "",
                Age = dto.Age,
                Gender = dto.Gender ?? "",
                Comparison = dto.Comparison ?? "",
                AccessionNumber = dto.AccessionNumber ?? Guid.NewGuid().ToString("n")[..10],
            },
        };

        // Iter-36 — modality + body part are the single selection key. When the
        // caller did not pin a template/rulebook, auto-resolve the Approved pair
        // for this (modality, body part) so scaffolding + prompts are bound from
        // creation. Caller-supplied pins always win.
        await ApplyAutoBindingsAsync(tenant, report, overwriteTemplate: false, overwriteRulebook: false, ct);

        _db.Reports.Add(report);
        await _db.SaveChangesAsync(ct);
        return CreatedAtAction(nameof(Get), new { id = report.Id }, report);
    }

    /// <summary>
    /// Iter-36 — bind the report to the Approved template + rulebook that match its
    /// (modality, body part) selection key. Loads tenant-scoped Approved candidates
    /// and delegates the pick to the pure <see cref="ReportingService.ResolveBindings"/>.
    /// Each binding refreshes independently: when its overwrite flag is false an
    /// existing binding is preserved (caller pin / manual override), otherwise it is
    /// re-resolved from the selection key. Either way a null binding is always filled.
    /// </summary>
    private async Task ApplyAutoBindingsAsync(Tenant tenant, Report report, bool overwriteTemplate, bool overwriteRulebook, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(report.Study.Modality) || string.IsNullOrWhiteSpace(report.Study.BodyPart))
            return;

        var templates = await _db.Templates
            .Where(t => t.TenantId == tenant.Id && t.Status == TemplateStatus.Approved)
            .ToListAsync(ct);
        var rulebooks = await _db.Rulebooks
            .Where(r => r.TenantId == tenant.Id && r.Status == RulebookStatus.Approved)
            .ToListAsync(ct);

        var (template, rulebook) = ReportingService.ResolveBindings(
            templates, rulebooks, report.Study.Modality, report.Study.BodyPart, report.Study.Contrast);

        if (template is not null && (overwriteTemplate || report.TemplateId is null))
            report.TemplateId = template.Id;
        if (rulebook is not null && (overwriteRulebook || report.RulebookId is null))
            report.RulebookId = rulebook.Id;
    }

    /// <summary>
    /// Iter-32 TMP-005 — shared gate for explicitly selected templates (create pin
    /// or PATCH manual override): must exist in the tenant and be Approved unless
    /// the tenant allows sandbox content. Returns null when valid.
    /// </summary>
    private async Task<IActionResult?> ValidateTemplateAsync(Tenant tenant, Guid templateId, CancellationToken ct)
    {
        var tpl = await _db.Templates.FirstOrDefaultAsync(t => t.Id == templateId && t.TenantId == tenant.Id, ct);
        if (tpl is null)
            return BadRequest(new { error = "Template not found in this tenant.", kind = "validation" });
        if (tpl.Status != TemplateStatus.Approved && !tenant.AllowSandboxRulebooks)
        {
            return BadRequest(new
            {
                error = $"Template '{tpl.TemplateId}' is in status '{tpl.Status}' and the tenant does not allow sandbox templates.",
                kind = "template_not_approved",
            });
        }
        return null;
    }

    /// <summary>
    /// Tenant-membership gate for explicitly selected rulebooks. Unlike templates
    /// (TMP-005 blocks non-Approved at attach time), RB-010 governance for draft
    /// rulebooks is enforced at AI-run time, so status is deliberately NOT checked
    /// here — only that the rulebook exists in the caller's tenant. Returns null
    /// when valid.
    /// </summary>
    private async Task<IActionResult?> ValidateRulebookAsync(Tenant tenant, Guid rulebookId, CancellationToken ct)
    {
        var exists = await _db.Rulebooks.AnyAsync(r => r.Id == rulebookId && r.TenantId == tenant.Id, ct);
        if (!exists)
            return BadRequest(new { error = "Rulebook not found in this tenant.", kind = "validation" });
        return null;
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
    {
        var (tenant, user) = await ResolveContextAsync(_db, ct);
        var deny = RequirePermission(user, RbacPermission.ReportsRead);
        if (deny is not null) return deny;
        var report = await _db.Reports.FirstOrDefaultAsync(r => r.Id == id && r.TenantId == tenant.Id, ct);
        if (report is null) return NotFound();
        var signatures = await _db.ReportSignatures
            .Where(s => s.TenantId == tenant.Id && s.ReportId == id)
            .OrderBy(s => s.SignedAt)
            .Select(s => new
            {
                id = s.Id,
                userId = s.UserId,
                role = s.Role.ToString(),
                signedAt = s.SignedAt,
                note = s.Note,
                hash = s.Hash,
            })
            .ToListAsync(ct);
        return Ok(new
        {
            id = report.Id,
            tenantId = report.TenantId,
            createdByUserId = report.CreatedByUserId,
            rulebookId = report.RulebookId,
            templateId = report.TemplateId,
            rulebookPinned = report.RulebookPinned,
            templatePinned = report.TemplatePinned,
            status = report.Status,
            study = report.Study,
            indication = report.Indication,
            technique = report.Technique,
            comparison = report.Comparison,
            findings = report.Findings,
            impression = report.Impression,
            recommendations = report.Recommendations,
            aiHighlightsJson = report.AiHighlightsJson,
            serviceRequestRef = report.ServiceRequestRef,
            createdAt = report.CreatedAt,
            updatedAt = report.UpdatedAt,
            signatures,
        });
    }

    public record PatchReportDto(
        string? Indication, string? Technique, string? Comparison,
        string? Findings, string? Impression, string? Recommendations,
        string? AiHighlightsJson, Guid? RulebookId,
        // Iter-36 — study-context fields editable from the reporting panel.
        string? Modality, string? BodyPart, int? Age, string? Gender, string? Contrast,
        // Manual binding overrides — an explicit id pins the binding; sending
        // TemplatePinned/RulebookPinned = false clears the pin (reset-to-auto).
        Guid? TemplateId = null, bool? TemplatePinned = null, bool? RulebookPinned = null);

    [HttpPatch("{id:guid}")]
    public async Task<IActionResult> Patch(Guid id, [FromBody] PatchReportDto dto, CancellationToken ct)
    {
        var (tenant, user) = await ResolveContextAsync(_db, ct);
        var deny = RequirePermission(user, RbacPermission.ReportsEdit);
        if (deny is not null) return deny;
        var report = await _db.Reports.FirstOrDefaultAsync(r => r.Id == id && r.TenantId == tenant.Id, ct);
        if (report is null) return NotFound();

        // Binding lock — once a Primary signature exists the signed hash covers the
        // bound template/rulebook, so binding + pin mutations are rejected. Section
        // text stays editable (the addendum flow governs post-sign content changes).
        var mutatesBindings = dto.TemplateId is not null || dto.RulebookId is not null
            || dto.TemplatePinned is not null || dto.RulebookPinned is not null;
        if (mutatesBindings)
        {
            var primarySigned = await _db.ReportSignatures.AnyAsync(
                s => s.TenantId == tenant.Id && s.ReportId == report.Id && s.Role == SignatureRole.Primary, ct);
            if (primarySigned)
            {
                return Conflict(new
                {
                    error = "Report has a primary signature; template/rulebook bindings are locked.",
                    kind = "signed_locked",
                });
            }
        }

        if (dto.Indication is not null) report.Indication = dto.Indication;
        if (dto.Technique is not null) report.Technique = dto.Technique;
        if (dto.Comparison is not null) report.Comparison = dto.Comparison;
        if (dto.Findings is not null) report.Findings = dto.Findings;
        if (dto.Impression is not null) report.Impression = dto.Impression;
        if (dto.Recommendations is not null) report.Recommendations = dto.Recommendations;
        if (dto.AiHighlightsJson is not null) report.AiHighlightsJson = dto.AiHighlightsJson;

        // Manual binding overrides. An explicit id is a deliberate user choice:
        // validate it, apply it, and pin it so later selection-key changes don't
        // rebind over it. Pinned=false clears the pin and re-resolves below.
        if (dto.TemplateId is { } tplId)
        {
            var tplError = await ValidateTemplateAsync(tenant, tplId, ct);
            if (tplError is not null) return tplError;
            report.TemplateId = tplId;
            report.TemplatePinned = true;
        }
        if (dto.RulebookId is { } rbId)
        {
            var rbError = await ValidateRulebookAsync(tenant, rbId, ct);
            if (rbError is not null) return rbError;
            report.RulebookId = rbId;
            report.RulebookPinned = true;
        }
        var templatePinCleared = dto.TemplatePinned == false && report.TemplatePinned;
        var rulebookPinCleared = dto.RulebookPinned == false && report.RulebookPinned;
        if (templatePinCleared) report.TemplatePinned = false;
        if (rulebookPinCleared) report.RulebookPinned = false;

        // Iter-36 — study-context demographics + the modality/body-part selection key.
        if (dto.Age is not null) report.Study.Age = dto.Age;
        if (dto.Gender is not null) report.Study.Gender = dto.Gender;
        var modalityChanged = dto.Modality is not null && dto.Modality != report.Study.Modality;
        var bodyPartChanged = dto.BodyPart is not null && dto.BodyPart != report.Study.BodyPart;
        var contrastChanged = dto.Contrast is not null && dto.Contrast != report.Study.Contrast;
        if (dto.Modality is not null) report.Study.Modality = dto.Modality;
        if (dto.BodyPart is not null) report.Study.BodyPart = dto.BodyPart;
        if (dto.Contrast is not null) report.Study.Contrast = dto.Contrast;
        // When the selection key changes (modality, body part, or contrast) — or a
        // pin was just cleared back to auto — re-resolve the matching Approved
        // template + rulebook. Each binding refreshes independently and a pinned
        // binding is never overwritten.
        if (modalityChanged || bodyPartChanged || contrastChanged || templatePinCleared || rulebookPinCleared)
        {
            await ApplyAutoBindingsAsync(tenant, report,
                overwriteTemplate: !report.TemplatePinned,
                overwriteRulebook: !report.RulebookPinned, ct);
        }

        report.UpdatedAt = DateTimeOffset.UtcNow;

        // Append a versioned snapshot so the editor can later show diff/history.
        var nextSeq = await _db.ReportVersions
            .Where(v => v.ReportId == report.Id)
            .CountAsync(ct);
        _db.ReportVersions.Add(new Domain.Entities.ReportVersion
        {
            ReportId = report.Id,
            Sequence = nextSeq + 1,
            AuthorUserId = user.Id,
            Action = "edit",
            RulebookId = report.RulebookId,
            SnapshotJson = System.Text.Json.JsonSerializer.Serialize(new
            {
                report.Indication, report.Technique, report.Comparison,
                report.Findings, report.Impression, report.Recommendations,
                report.AiHighlightsJson,
            }),
        });
        await _db.SaveChangesAsync(ct);
        return Ok(report);
    }

    [HttpPost("{id:guid}/validate")]
    public async Task<IActionResult> Validate(Guid id, CancellationToken ct)
    {
        var (tenant, user) = await ResolveContextAsync(_db, ct);
        var deny = RequirePermission(user, RbacPermission.ReportsValidate);
        if (deny is not null) return deny;
        var report = await _db.Reports.FirstOrDefaultAsync(r => r.Id == id && r.TenantId == tenant.Id, ct);
        if (report is null) return NotFound();
        var lexicon = await _db.Lexicons.Where(l => l.TenantId == tenant.Id).ToListAsync(ct);
        var settings = await _db.TenantSettings.FirstOrDefaultAsync(s => s.TenantId == tenant.Id, ct);
        // Iter-33 PERF-004 — record validate duration for SLO budget.
        var result = await Services.PerfBudgets.RecordAsync(
            Services.PerfBudgets.ValidateDurationMs,
            () => _reporting.ValidateAsync(tenant, report, lexicon, settings, ct),
            new KeyValuePair<string, object?>("tenant", tenant.Slug),
            new KeyValuePair<string, object?>("modality", report.Study?.Modality ?? "(none)"));
        // Iter-31 RPT-012 — when the tenant has accepted residual blockers
        // (RequireZeroBlockers == false), advance Status to Validated even
        // when blocker findings remain so /export/* can proceed.
        var requireZero = settings?.RequireZeroBlockers ?? true;
        if (!result.BlockerPresent || !requireZero)
        {
            if (report.Status < ReportStatus.Validated)
            {
                report.Status = ReportStatus.Validated;
                await _db.SaveChangesAsync(ct);
            }
        }
        return Ok(result);
    }

    /// <summary>
    /// PRD §18.4 — heuristic report quality score in [0..100]. Weighs
    /// validation pass-rate, presence of comparison + indication, section
    /// completeness, and AI-mark fraction. The score is read-only — it is
    /// never persisted nor used to gate export.
    /// </summary>
    [HttpGet("{id:guid}/quality")]
    public async Task<IActionResult> Quality(Guid id, CancellationToken ct)
    {
        var (tenant, user) = await ResolveContextAsync(_db, ct);
        var deny = RequirePermission(user, RbacPermission.ReportsRead);
        if (deny is not null) return deny;
        var report = await _db.Reports.FirstOrDefaultAsync(r => r.Id == id && r.TenantId == tenant.Id, ct);
        if (report is null) return NotFound();

        int score = 100;
        var reasons = new List<string>();
        if (string.IsNullOrWhiteSpace(report.Indication))
        { score -= 10; reasons.Add("missing_indication"); }
        if (string.IsNullOrWhiteSpace(report.Comparison) && string.IsNullOrWhiteSpace(report.Study.Comparison))
        { score -= 5; reasons.Add("missing_comparison"); }

        var sectionTexts = new[] { report.Indication, report.Technique, report.Comparison, report.Findings, report.Impression, report.Recommendations };
        var totalSections = sectionTexts.Length;
        var emptySections = sectionTexts.Count(string.IsNullOrWhiteSpace);
        var emptyFrac = (double)emptySections / totalSections;
        score -= (int)(20 * emptyFrac);
        if (emptySections > 0) reasons.Add($"empty_sections:{emptySections}/{totalSections}");

        var lexicon = await _db.Lexicons.Where(l => l.TenantId == tenant.Id).ToListAsync(ct);
        var settings = await _db.TenantSettings.FirstOrDefaultAsync(s => s.TenantId == tenant.Id, ct);
        var validation = await _reporting.ValidateAsync(tenant, report, lexicon, settings, ct);
        if (validation.BlockerPresent) { score -= 30; reasons.Add("blocker_finding"); }
        score -= Math.Min(20, validation.Findings.Count(f => f.Severity == "Warning") * 3);

        score = Math.Clamp(score, 0, 100);
        return Ok(new { score, reasons, validationFindings = validation.Findings.Count });
    }

    /// <summary>
    /// PRD §18.5 — prior-report compare. Given a report, finds the most
    /// recent prior report for the same patient (matched by accession-number
    /// stem) and returns a side-by-side text diff. Read-only; no AI involved.
    /// </summary>
    [HttpGet("{id:guid}/compare-prior")]
    public async Task<IActionResult> ComparePrior(Guid id, CancellationToken ct)
    {
        var (tenant, user) = await ResolveContextAsync(_db, ct);
        var deny = RequirePermission(user, RbacPermission.ReportsRead);
        if (deny is not null) return deny;
        var report = await _db.Reports.FirstOrDefaultAsync(r => r.Id == id && r.TenantId == tenant.Id, ct);
        if (report is null) return NotFound();

        // Use accession number prefix (everything before the last '-') as a
        // stable patient/study key. Customers with a true MRN can swap this
        // for `report.Study.PatientMrn` when that field is populated.
        var stem = report.Study.AccessionNumber;
        if (!string.IsNullOrEmpty(stem) && stem.Contains('-'))
            stem = stem[..stem.LastIndexOf('-')];

        var prior = await _db.Reports
            .Where(r => r.TenantId == tenant.Id
                && r.Id != report.Id
                && r.Study.AccessionNumber.StartsWith(stem)
                && r.CreatedAt < report.CreatedAt)
            .OrderByDescending(r => r.CreatedAt)
            .FirstOrDefaultAsync(ct);
        if (prior is null) return Ok(new { hasPrior = false });

        return Ok(new
        {
            hasPrior = true,
            priorId = prior.Id,
            priorAccession = prior.Study.AccessionNumber,
            priorCreatedAt = prior.CreatedAt,
            currentReport = new { report.Indication, report.Technique, report.Comparison, report.Findings, report.Impression, report.Recommendations },
            priorReport = new { prior.Indication, prior.Technique, prior.Comparison, prior.Findings, prior.Impression, prior.Recommendations },
        });
    }

    public record AiActionDto(string Mode, Guid? ProviderId);

    [HttpPost("{id:guid}/ai")]
    [Microsoft.AspNetCore.RateLimiting.EnableRateLimiting("ai")]
    public async Task<IActionResult> RunAi(Guid id, [FromBody] AiActionDto dto, CancellationToken ct)
    {
        var (tenant, user) = await ResolveContextAsync(_db, ct);
        var deny = RequirePermission(user, RbacPermission.ReportsEdit);
        if (deny is not null) return deny;
        var report = await _db.Reports.FirstOrDefaultAsync(r => r.Id == id && r.TenantId == tenant.Id, ct);
        if (report is null) return NotFound();

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

        try
        {
            ProviderConfig provider;
            if (dto.ProviderId is { } pid && pid != Guid.Empty)
            {
                provider = await _db.Providers.FirstOrDefaultAsync(
                    p => p.Id == pid && p.TenantId == tenant.Id, ct)
                    ?? throw new InvalidOperationException("provider_not_found");
                var result = await _reporting.RunAsync(tenant, user, report, provider, mode, ct);
                return Ok(new
                {
                    text = result.Text,
                    provider = result.Provider,
                    model = result.Model,
                    latencyMs = result.LatencyMs,
                    promptVersion = result.PromptVersion,
                    mode,
                    routedBy = "manual",
                });
            }
            else
            {
                var (result, picked) = await _reporting.RunAutoAsync(tenant, user, report, mode, ct);
                return Ok(new
                {
                    text = result.Text,
                    provider = result.Provider,
                    model = result.Model,
                    latencyMs = result.LatencyMs,
                    promptVersion = result.PromptVersion,
                    mode,
                    routedBy = "auto",
                    selectedProviderId = picked.Id,
                });
            }
        }
        catch (InvalidOperationException ioe) when (ioe.Message == "provider_not_found")
        {
            return BadRequest(new { error = "Provider not found.", kind = "not_found" });
        }
        catch (Application.Services.ProviderPolicyException pex)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { error = pex.Message, kind = "provider_policy" });
        }
        catch (Application.Services.RulebookGovernanceException rge)
        {
            return Conflict(new { error = rge.Message, kind = "rulebook_governance" });
        }
    }

    public record GenerateReportDto(Guid? ProviderId);

    /// <summary>
    /// Whole-report generation for the guided intake flow (`/reports/new`). Runs the
    /// structured generation prompt through the selected provider (or auto-routes when
    /// no provider id is supplied), then adopts each non-empty AI section onto the
    /// report — marking it <c>.ai-mark</c> — and snapshots a version so the change is
    /// auditable. Sections the model leaves empty keep whatever the intake seeded
    /// (e.g. the dictated positive findings). Returns the updated report so the wizard
    /// can redirect straight into the pre-populated editor.
    /// </summary>
    [HttpPost("{id:guid}/generate")]
    [Microsoft.AspNetCore.RateLimiting.EnableRateLimiting("ai")]
    public async Task<IActionResult> Generate(Guid id, [FromBody] GenerateReportDto dto, CancellationToken ct)
    {
        var (tenant, user) = await ResolveContextAsync(_db, ct);
        var deny = RequirePermission(user, RbacPermission.ReportsEdit);
        if (deny is not null) return deny;
        var report = await _db.Reports.FirstOrDefaultAsync(r => r.Id == id && r.TenantId == tenant.Id, ct);
        if (report is null) return NotFound();

        try
        {
            ReportingService.StructuredReportResult result;
            if (dto.ProviderId is { } pid && pid != Guid.Empty)
            {
                var provider = await _db.Providers.FirstOrDefaultAsync(
                    p => p.Id == pid && p.TenantId == tenant.Id, ct)
                    ?? throw new InvalidOperationException("provider_not_found");
                result = await _reporting.GenerateStructuredAsync(tenant, user, report, provider, ct);
            }
            else
            {
                (result, _) = await _reporting.GenerateStructuredAutoAsync(tenant, user, report, ct);
            }

            await ApplyStructuredResultAsync(_db, report, user, result, ct);

            return Ok(report);
        }
        catch (InvalidOperationException ioe) when (ioe.Message == "provider_not_found")
        {
            return BadRequest(new { error = "Provider not found.", kind = "not_found" });
        }
        catch (Application.Services.ProviderPolicyException pex)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { error = pex.Message, kind = "provider_policy" });
        }
        catch (Application.Services.ProviderTransportException tex)
        {
            return StatusCode(StatusCodes.Status502BadGateway, new
            {
                error = $"The AI provider could not complete the report: {tex.Message}",
                kind = "provider_transport",
            });
        }
        catch (Application.Services.RulebookGovernanceException rge)
        {
            return Conflict(new { error = rge.Message, kind = "rulebook_governance" });
        }
    }

    /// <summary>
    /// Adopts each non-empty AI section onto the report — flagging it for the
    /// .ai-mark review style; empty sections keep whatever the intake seeded —
    /// and snapshots a version (mirrors PATCH) so the editor's history/diff
    /// captures the generation as an authored step. Shared by the synchronous
    /// <see cref="Generate"/> endpoint and the async generate job.
    /// </summary>
    private static async Task ApplyStructuredResultAsync(
        RadioPadDbContext db, Report report, User user, ReportingService.StructuredReportResult result, CancellationToken ct)
    {
        var highlights = ParseHighlights(report.AiHighlightsJson);
        void Adopt(string key, string value, Action<string> set)
        {
            if (string.IsNullOrWhiteSpace(value)) return;
            set(value);
            highlights[key] = true;
        }
        Adopt("indication", result.Indication, v => report.Indication = v);
        Adopt("technique", result.Technique, v => report.Technique = v);
        Adopt("comparison", result.Comparison, v => report.Comparison = v);
        Adopt("findings", result.Findings, v => report.Findings = v);
        Adopt("impression", result.Impression, v => report.Impression = v);
        Adopt("recommendations", result.Recommendations, v => report.Recommendations = v);
        report.AiHighlightsJson = System.Text.Json.JsonSerializer.Serialize(highlights);
        report.UpdatedAt = DateTimeOffset.UtcNow;

        var nextSeq = await db.ReportVersions.Where(v => v.ReportId == report.Id).CountAsync(ct);
        db.ReportVersions.Add(new Domain.Entities.ReportVersion
        {
            ReportId = report.Id,
            Sequence = nextSeq + 1,
            AuthorUserId = user.Id,
            Action = "generate",
            RulebookId = report.RulebookId,
            SnapshotJson = System.Text.Json.JsonSerializer.Serialize(new
            {
                report.Indication, report.Technique, report.Comparison,
                report.Findings, report.Impression, report.Recommendations,
                report.AiHighlightsJson,
            }),
        });
        await db.SaveChangesAsync(ct);
    }

    // -----------------------------------------------------------------
    // Async AI jobs — submit + poll (2026-07-12).
    //
    // The synchronous /ai and /generate endpoints hold the HTTP request open
    // for the entire provider call (minutes for UBAG browser-driven targets);
    // any proxy timeout surfaces as a raw "Failed to fetch" AND the aborted
    // request cancels the in-flight job via RequestAborted. These endpoints
    // decouple generation from the connection. The sync endpoints stay for
    // older desktop builds (deploy order: backend before desktop).
    // -----------------------------------------------------------------

    [HttpPost("{id:guid}/ai/jobs")]
    [Microsoft.AspNetCore.RateLimiting.EnableRateLimiting("ai")]
    public async Task<IActionResult> RunAiJob(Guid id, [FromBody] AiActionDto dto, CancellationToken ct)
    {
        var (tenant, user) = await ResolveContextAsync(_db, ct);
        var deny = RequirePermission(user, RbacPermission.ReportsEdit);
        if (deny is not null) return deny;
        var report = await _db.Reports.FirstOrDefaultAsync(r => r.Id == id && r.TenantId == tenant.Id, ct);
        if (report is null) return NotFound();

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
        // Fast-fail an unknown provider id at submit so the client gets the
        // same 400 the sync endpoint gives, instead of a poll-side error.
        if (dto.ProviderId is { } pid && pid != Guid.Empty
            && !await _db.Providers.AnyAsync(p => p.Id == pid && p.TenantId == tenant.Id, ct))
        {
            return BadRequest(new { error = "Provider not found.", kind = "not_found" });
        }

        // Single-flight: attach to an already-running identical job instead of
        // stacking a second generation (retry-after-disconnect is the expected
        // recovery path; the first job is still running by design).
        if (_aiJobs.TryGetRunning(tenant.Id, id, "ai", mode, out var running))
            return Accepted(new { jobId = running.Id, status = running.Status });

        var job = _aiJobs.Create(tenant.Id, id, user.Id, "ai", mode);
        _ = Task.Run(() => ExecuteAiJobAsync(job.Id, tenant.Id, user.Id, id, mode, dto.ProviderId));
        return Accepted(new { jobId = job.Id, status = job.Status });
    }

    [HttpPost("{id:guid}/generate/jobs")]
    [Microsoft.AspNetCore.RateLimiting.EnableRateLimiting("ai")]
    public async Task<IActionResult> GenerateJob(Guid id, [FromBody] GenerateReportDto dto, CancellationToken ct)
    {
        var (tenant, user) = await ResolveContextAsync(_db, ct);
        var deny = RequirePermission(user, RbacPermission.ReportsEdit);
        if (deny is not null) return deny;
        var report = await _db.Reports.FirstOrDefaultAsync(r => r.Id == id && r.TenantId == tenant.Id, ct);
        if (report is null) return NotFound();
        if (dto.ProviderId is { } pid && pid != Guid.Empty
            && !await _db.Providers.AnyAsync(p => p.Id == pid && p.TenantId == tenant.Id, ct))
        {
            return BadRequest(new { error = "Provider not found.", kind = "not_found" });
        }

        // Single-flight — see RunAiJob.
        if (_aiJobs.TryGetRunning(tenant.Id, id, "generate", "generate", out var running))
            return Accepted(new { jobId = running.Id, status = running.Status });

        var job = _aiJobs.Create(tenant.Id, id, user.Id, "generate", "generate");
        _ = Task.Run(() => ExecuteGenerateJobAsync(job.Id, tenant.Id, user.Id, id, dto.ProviderId));
        return Accepted(new { jobId = job.Id, status = job.Status });
    }

    /// <summary>
    /// Poll endpoint for both job kinds. Fast request — safe under any proxy
    /// timeout. Deliberately NOT rate-limited by the "ai" policy: polls are
    /// frequent and must not consume the AI submission budget.
    /// </summary>
    [HttpGet("{id:guid}/ai/jobs/{jobId:guid}")]
    public async Task<IActionResult> AiJobStatus(Guid id, Guid jobId, CancellationToken ct)
    {
        var (tenant, user) = await ResolveContextAsync(_db, ct);
        var deny = RequirePermission(user, RbacPermission.ReportsEdit);
        if (deny is not null) return deny;
        if (!_aiJobs.TryGet(jobId, out var job) || job.TenantId != tenant.Id || job.ReportId != id)
        {
            return NotFound(new
            {
                error = "AI job not found — the server may have restarted mid-generation. Please try again.",
                kind = "job_not_found",
            });
        }
        return Ok(new
        {
            jobId = job.Id,
            kind = job.Kind,
            mode = job.Mode,
            status = job.Status,
            elapsedMs = (long)((job.CompletedAt ?? DateTimeOffset.UtcNow) - job.CreatedAt).TotalMilliseconds,
            result = job.Status == "ok" ? job.Payload : null,
            error = job.Error,
            errorKind = job.ErrorKind,
        });
    }

    /// <summary>Safety ceiling for a background AI job; generous multiple of the provider timeouts.</summary>
    private static readonly TimeSpan AiJobSafetyTimeout = TimeSpan.FromMinutes(10);

    private async Task ExecuteAiJobAsync(Guid jobId, Guid tenantId, Guid userId, Guid reportId, string mode, Guid? providerId)
    {
        await ExecuteJobCoreAsync(jobId, tenantId, async (scope, ct) =>
        {
            var db = scope.ServiceProvider.GetRequiredService<RadioPadDbContext>();
            var reporting = scope.ServiceProvider.GetRequiredService<ReportingService>();
            var (tenant, user, report) = await LoadJobContextAsync(db, tenantId, userId, reportId, ct);

            if (providerId is { } pid && pid != Guid.Empty)
            {
                var provider = await db.Providers.FirstOrDefaultAsync(p => p.Id == pid && p.TenantId == tenantId, ct)
                    ?? throw new InvalidOperationException("provider_not_found");
                var result = await reporting.RunAsync(tenant, user, report, provider, mode, ct);
                _aiJobs.Complete(jobId, new
                {
                    text = result.Text,
                    provider = result.Provider,
                    model = result.Model,
                    latencyMs = result.LatencyMs,
                    promptVersion = result.PromptVersion,
                    mode,
                    routedBy = "manual",
                });
            }
            else
            {
                var (result, picked) = await reporting.RunAutoAsync(tenant, user, report, mode, ct);
                _aiJobs.Complete(jobId, new
                {
                    text = result.Text,
                    provider = result.Provider,
                    model = result.Model,
                    latencyMs = result.LatencyMs,
                    promptVersion = result.PromptVersion,
                    mode,
                    routedBy = "auto",
                    selectedProviderId = picked.Id,
                });
            }
        });
    }

    private async Task ExecuteGenerateJobAsync(Guid jobId, Guid tenantId, Guid userId, Guid reportId, Guid? providerId)
    {
        await ExecuteJobCoreAsync(jobId, tenantId, async (scope, ct) =>
        {
            var db = scope.ServiceProvider.GetRequiredService<RadioPadDbContext>();
            var reporting = scope.ServiceProvider.GetRequiredService<ReportingService>();
            var (tenant, user, report) = await LoadJobContextAsync(db, tenantId, userId, reportId, ct);

            // Freshness guard: the provider call runs for minutes DETACHED from
            // the client, so the radiologist may edit the report meanwhile (the
            // old sync endpoint could not hit this — disconnect cancelled it).
            // A stale write-back would silently clobber authored medical text.
            var updatedAtAtSubmit = report.UpdatedAt;

            ReportingService.StructuredReportResult result;
            if (providerId is { } pid && pid != Guid.Empty)
            {
                var provider = await db.Providers.FirstOrDefaultAsync(p => p.Id == pid && p.TenantId == tenantId, ct)
                    ?? throw new InvalidOperationException("provider_not_found");
                result = await reporting.GenerateStructuredAsync(tenant, user, report, provider, ct);
            }
            else
            {
                (result, _) = await reporting.GenerateStructuredAutoAsync(tenant, user, report, ct);
            }

            await db.Entry(report).ReloadAsync(ct);
            if (report.UpdatedAt != updatedAtAtSubmit)
                throw new InvalidOperationException("report_modified");

            await ApplyStructuredResultAsync(db, report, user, result, ct);
            _aiJobs.Complete(jobId, report);
        });
    }

    private static async Task<(Tenant tenant, User user, Report report)> LoadJobContextAsync(
        RadioPadDbContext db, Guid tenantId, Guid userId, Guid reportId, CancellationToken ct)
    {
        var tenant = await db.Tenants.FirstOrDefaultAsync(t => t.Id == tenantId, ct)
            ?? throw new InvalidOperationException("context_gone");
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId && u.TenantId == tenantId, ct)
            ?? throw new InvalidOperationException("context_gone");
        var report = await db.Reports.FirstOrDefaultAsync(r => r.Id == reportId && r.TenantId == tenantId, ct)
            ?? throw new InvalidOperationException("context_gone");
        return (tenant, user, report);
    }

    /// <summary>
    /// Runs a job body in its own DI scope, detached from the HTTP request:
    /// the client disconnecting no longer cancels the provider call (the 2026-07-12
    /// incident). Cancellation comes only from app shutdown or the safety ceiling.
    /// Every failure path lands in the registry so a poll always terminates.
    /// </summary>
    private async Task ExecuteJobCoreAsync(Guid jobId, Guid tenantId, Func<IServiceScope, CancellationToken, Task> body)
    {
        try
        {
            using var scope = _scopes.CreateScope();
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.ApplicationStopping);
            cts.CancelAfter(AiJobSafetyTimeout);
            await body(scope, cts.Token);
        }
        catch (InvalidOperationException ioe) when (ioe.Message == "provider_not_found")
        {
            _aiJobs.Fail(jobId, "Provider not found.", "not_found");
        }
        catch (InvalidOperationException ioe) when (ioe.Message == "context_gone")
        {
            _aiJobs.Fail(jobId, "The report, user, or organisation was removed while the job was queued.", "not_found");
        }
        catch (InvalidOperationException ioe) when (ioe.Message == "report_modified")
        {
            _aiJobs.Fail(
                jobId,
                "The report was edited while the AI generation was running, so the AI result was discarded to protect your edits. Re-run generation if you still want it.",
                "report_modified");
        }
        catch (Application.Services.QuotaExceededException qex)
        {
            // Sync endpoints surface this via the global handler as 402
            // quota_exceeded; without this catch it would masquerade as a
            // server_error and be logged as an unexpected fault.
            _aiJobs.Fail(jobId, qex.Message, "quota_exceeded");
        }
        catch (Application.Services.ProviderPolicyException pex)
        {
            _aiJobs.Fail(jobId, pex.Message, "provider_policy");
        }
        catch (Application.Services.ProviderTransportException tex)
        {
            _aiJobs.Fail(jobId, tex.Message, "provider_transport");
        }
        catch (Application.Services.RulebookGovernanceException rge)
        {
            _aiJobs.Fail(jobId, rge.Message, "rulebook_governance");
        }
        catch (OperationCanceledException)
        {
            _aiJobs.Fail(jobId, "The AI generation timed out or the server is shutting down.", "timeout");
        }
        catch (Exception ex)
        {
            try
            {
                using var logScope = _scopes.CreateScope();
                logScope.ServiceProvider.GetRequiredService<ILogger<ReportsController>>()
                    .LogError(ex, "Async AI job {JobId} (tenant {TenantId}) failed unexpectedly", jobId, tenantId);
            }
            catch { /* logging must never mask the job failure */ }
            _aiJobs.Fail(jobId, "Unexpected server error during AI generation.", "server_error");
        }
    }

    /// <summary>Read the report's AiHighlights map (section key → generated?) tolerantly.</summary>
    private static Dictionary<string, bool> ParseHighlights(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new();
        try
        {
            return System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, bool>>(json) ?? new();
        }
        catch (System.Text.Json.JsonException)
        {
            return new();
        }
    }

    [HttpPost("{id:guid}/acknowledge")]
    public async Task<IActionResult> Acknowledge(Guid id, CancellationToken ct)
    {
        var (tenant, user) = await ResolveContextAsync(_db, ct);
        var deny = RequirePermission(user, RbacPermission.ReportsEdit);
        if (deny is not null) return deny;
        var report = await _db.Reports.FirstOrDefaultAsync(r => r.Id == id && r.TenantId == tenant.Id, ct);
        if (report is null) return NotFound();

        var settings = await _db.TenantSettings.FirstOrDefaultAsync(s => s.TenantId == tenant.Id, ct);
        var requireZero = settings?.RequireZeroBlockers ?? true;
        if (requireZero)
        {
            var lexicon = await _db.Lexicons.Where(l => l.TenantId == tenant.Id).ToListAsync(ct);
            var validation = await _reporting.ValidateAsync(tenant, report, lexicon, settings, ct);
            if (validation.BlockerPresent)
            {
                return Conflict(new
                {
                    error = "Report has validation blockers and cannot be acknowledged.",
                    kind = "validation_blockers",
                    blockerCount = validation.Findings.Count(f => f.Severity == nameof(ValidationSeverity.Blocker)),
                });
            }
        }

        await _reporting.AcknowledgeAsync(tenant, user, report, ct);
        await _db.SaveChangesAsync(ct);
        return Ok(report);
    }

    [HttpGet("{id:guid}/export/fhir")]
    public async Task<IActionResult> ExportFhir(Guid id, CancellationToken ct)
    {
        var (tenant, user) = await ResolveContextAsync(_db, ct);
        var deny = RequirePermission(user, RbacPermission.ReportsExport);
        if (deny is not null) return deny;
        var report = await _db.Reports.FirstOrDefaultAsync(r => r.Id == id && r.TenantId == tenant.Id, ct);
        if (report is null) return NotFound();
        var gate = RequireAcknowledgedForExport(report, "FHIR");
        if (gate is not null) return gate;
        var json = FhirDiagnosticReportSerializer.Serialize(report, tenant.Slug);
        await MarkExportedAsync(tenant, user, report, "fhir", ct);
        return Content(json, "application/fhir+json");
    }

    [HttpGet("{id:guid}/export/json")]
    public async Task<IActionResult> ExportJson(Guid id, CancellationToken ct)
    {
        var (tenant, user) = await ResolveContextAsync(_db, ct);
        var deny = RequirePermission(user, RbacPermission.ReportsExport);
        if (deny is not null) return deny;
        var report = await _db.Reports.FirstOrDefaultAsync(r => r.Id == id && r.TenantId == tenant.Id, ct);
        if (report is null) return NotFound();
        var gate = RequireAcknowledgedForExport(report, "JSON");
        if (gate is not null) return gate;

        await MarkExportedAsync(tenant, user, report, "json", ct);
        var exported = new
        {
            report.Id,
            report.Status,
            tenant = tenant.Slug,
            report.Study,
            report.Indication,
            report.Technique,
            report.Comparison,
            report.Findings,
            report.Impression,
            report.Recommendations,
            report.ServiceRequestRef,
            report.CreatedAt,
            report.UpdatedAt,
        };
        return Ok(exported);
    }

    [HttpGet("{id:guid}/export/text")]
    public async Task<IActionResult> ExportText(Guid id, [FromQuery] bool preview = false, CancellationToken ct = default)
    {
        var (tenant, user) = await ResolveContextAsync(_db, ct);
        if (!preview)
        {
            var deny = RequirePermission(user, RbacPermission.ReportsExport);
            if (deny is not null) return deny;
        }
        var report = await _db.Reports.FirstOrDefaultAsync(r => r.Id == id && r.TenantId == tenant.Id, ct);
        if (report is null) return NotFound();
        // `preview=true` returns the narrative without auditing or moving status forward,
        // so the editor can render plain-text drafts. Only the non-preview path counts as
        // an export per RPT-012 + §13.2.
        if (!preview)
        {
            var gate = RequireAcknowledgedForExport(report, "text");
            if (gate is not null) return gate;
        }
        var text = FhirDiagnosticReportSerializer.BuildNarrative(report);
        if (!preview) await MarkExportedAsync(tenant, user, report, "text", ct);
        return Content(text, "text/plain");
    }

    /// <summary>PRD RPT-011 — PDF export via QuestPDF. Subject to RPT-012 gating.</summary>
    [HttpGet("{id:guid}/export/pdf")]
    public async Task<IActionResult> ExportPdf(Guid id, CancellationToken ct = default)
    {
        var (tenant, user) = await ResolveContextAsync(_db, ct);
        var deny = RequirePermission(user, RbacPermission.ReportsExport);
        if (deny is not null) return deny;
        var report = await _db.Reports.FirstOrDefaultAsync(r => r.Id == id && r.TenantId == tenant.Id, ct);
        if (report is null) return NotFound();
        var gate = RequireAcknowledgedForExport(report, "PDF");
        if (gate is not null) return gate;
        var bytes = Services.ReportDocumentRenderer.RenderPdf(report, tenant);
        await MarkExportedAsync(tenant, user, report, "pdf", ct);
        return File(bytes, "application/pdf", $"report-{report.Study.AccessionNumber}.pdf");
    }

    /// <summary>PRD RPT-011 — DOCX export via OpenXML SDK. Subject to RPT-012 gating.</summary>
    [HttpGet("{id:guid}/export/docx")]
    public async Task<IActionResult> ExportDocx(Guid id, CancellationToken ct = default)
    {
        var (tenant, user) = await ResolveContextAsync(_db, ct);
        var deny = RequirePermission(user, RbacPermission.ReportsExport);
        if (deny is not null) return deny;
        var report = await _db.Reports.FirstOrDefaultAsync(r => r.Id == id && r.TenantId == tenant.Id, ct);
        if (report is null) return NotFound();
        var gate = RequireAcknowledgedForExport(report, "DOCX");
        if (gate is not null) return gate;
        var bytes = Services.ReportDocumentRenderer.RenderDocx(report, tenant);
        await MarkExportedAsync(tenant, user, report, "docx", ct);
        return File(bytes,
            "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            $"report-{report.Study.AccessionNumber}.docx");
    }

    /// <summary>PRD §19.1 / Beta — HL7 v2.5 ORU^R01 export. Subject to RPT-012 gating.</summary>
    [HttpGet("{id:guid}/export/hl7")]
    public async Task<IActionResult> ExportHl7(Guid id, CancellationToken ct = default)
    {
        var (tenant, user) = await ResolveContextAsync(_db, ct);
        var deny = RequirePermission(user, RbacPermission.ReportsExport);
        if (deny is not null) return deny;
        var report = await _db.Reports.FirstOrDefaultAsync(r => r.Id == id && r.TenantId == tenant.Id, ct);
        if (report is null) return NotFound();
        var gate = RequireAcknowledgedForExport(report, "HL7");
        if (gate is not null) return gate;
        var oru = Hl7OruSerializer.Serialize(report, tenant);
        await MarkExportedAsync(tenant, user, report, "hl7", ct);
        var bytes = System.Text.Encoding.UTF8.GetBytes(oru);
        return File(bytes, "application/hl7-v2", $"report-{report.Study.AccessionNumber}.hl7");
    }

    private ConflictObjectResult? RequireAcknowledgedForExport(Report report, string format)
    {
        if (report.Status >= ReportStatus.Acknowledged) return null;

        return Conflict(new
        {
            error = $"Report must be acknowledged before {format} export.",
            kind = "report_state",
            currentStatus = report.Status.ToString(),
        });
    }

    private async Task MarkExportedAsync(Tenant tenant, User user, Report report, string format, CancellationToken ct)
    {
        await _audit.AppendAsync(new AuditEvent
        {
            TenantId = tenant.Id,
            UserId = user.Id,
            ReportId = report.Id,
            Action = AuditAction.ReportExported,
            DetailsJson = System.Text.Json.JsonSerializer.Serialize(new { format }),
        }, ct);
        if (report.Status != ReportStatus.Exported)
        {
            report.Status = ReportStatus.Exported;
            report.UpdatedAt = DateTimeOffset.UtcNow;
            await _db.SaveChangesAsync(ct);
        }
    }

    [HttpGet("{id:guid}/versions")]
    public async Task<IActionResult> Versions(Guid id, CancellationToken ct)
    {
        var (tenant, user) = await ResolveContextAsync(_db, ct);
        var deny = RequirePermission(user, RbacPermission.ReportsRead);
        if (deny is not null) return deny;
        var report = await _db.Reports.FirstOrDefaultAsync(r => r.Id == id && r.TenantId == tenant.Id, ct);
        if (report is null) return NotFound();
        var versions = await _db.ReportVersions
            .Where(v => v.ReportId == id)
            .OrderByDescending(v => v.Sequence)
            .Take(50)
            .ToListAsync(ct);
        return Ok(versions);
    }

    /// <summary>
    /// Surfaces the most recent acknowledged/exported report in the same tenant
    /// for the same body part, for side-by-side prior-report comparison
    /// (PRD RPT-009 / AI-002 prior context).
    /// </summary>
    [HttpGet("{id:guid}/prior")]
    public async Task<IActionResult> Prior(Guid id, CancellationToken ct)
    {
        var (tenant, user) = await ResolveContextAsync(_db, ct);
        var deny = RequirePermission(user, RbacPermission.ReportsRead);
        if (deny is not null) return deny;
        var report = await _db.Reports.FirstOrDefaultAsync(r => r.Id == id && r.TenantId == tenant.Id, ct);
        if (report is null) return NotFound();
        var prior = await _db.Reports
            .Where(r => r.TenantId == tenant.Id
                && r.Id != report.Id
                && r.Status >= ReportStatus.Acknowledged
                && r.Study.BodyPart == report.Study.BodyPart)
            .OrderByDescending(r => r.UpdatedAt)
            .Select(r => new
            {
                r.Id,
                r.Study.AccessionNumber,
                r.Study.Modality,
                r.Study.BodyPart,
                r.Indication,
                r.Findings,
                r.Impression,
                r.UpdatedAt,
                r.Status,
            })
            .FirstOrDefaultAsync(ct);
        return Ok(new { current = new { report.Id, report.Study.BodyPart }, prior });
    }

    /// <summary>
    /// PRD DCM-001..006 — fetch DICOMweb study context for the report's
    /// accession number using the tenant's configured PACS. Always returns
    /// 200; <c>study</c> is null when DICOMweb is not configured or the study
    /// is not found. The fetch is audited as <see cref="AuditAction.DicomContextFetched"/>.
    /// </summary>
    [HttpGet("{id:guid}/dicom-context")]
    public async Task<IActionResult> DicomContext(Guid id,
        [FromServices] Services.IDicomWebClient dicom,
        CancellationToken ct)
    {
        var (tenant, user) = await ResolveContextAsync(_db, ct);
        var deny = RequirePermission(user, RbacPermission.ReportsRead);
        if (deny is not null) return deny;
        var report = await _db.Reports.FirstOrDefaultAsync(r => r.Id == id && r.TenantId == tenant.Id, ct);
        if (report is null) return NotFound();
        var settings = await _db.TenantSettings.FirstOrDefaultAsync(s => s.TenantId == tenant.Id, ct);
        if (settings is null || string.IsNullOrEmpty(settings.DicomWebBaseUrl))
            return Ok(new { configured = false, study = (object?)null });

        var study = await dicom.FetchStudyAsync(settings, report.Study.AccessionNumber, ct);
        await _audit.AppendAsync(new AuditEvent
        {
            TenantId = tenant.Id,
            UserId = user.Id,
            ReportId = report.Id,
            Action = AuditAction.DicomContextFetched,
            DetailsJson = System.Text.Json.JsonSerializer.Serialize(new
            {
                accession = report.Study.AccessionNumber,
                found = study is not null,
            }),
        }, ct);
        return Ok(new { configured = true, study });
    }

    /// <summary>
    /// Iter-31 DCM-007 — WADO-RS retrieval of a single instance's metadata
    /// (DICOM JSON Model). Always returns 200; <c>metadata</c> is null when
    /// DICOMweb is not configured or the instance cannot be fetched. Audited
    /// as <see cref="AuditAction.DicomContextFetched"/> with
    /// <c>scope=instance</c>.
    /// </summary>
    [HttpGet("{id:guid}/dicom-context/instance")]
    public async Task<IActionResult> DicomInstanceMetadata(Guid id,
        [FromQuery] string studyUid,
        [FromQuery] string seriesUid,
        [FromQuery] string instanceUid,
        [FromServices] Services.IDicomWebClient dicom,
        CancellationToken ct)
    {
        var (tenant, user) = await ResolveContextAsync(_db, ct);
        var forbid = RequireRole(user, UserRole.Radiologist, UserRole.MedicalDirector, UserRole.ReportingAdmin);
        if (forbid is not null) return forbid;
        var report = await _db.Reports.FirstOrDefaultAsync(r => r.Id == id && r.TenantId == tenant.Id, ct);
        if (report is null) return NotFound();
        if (string.IsNullOrWhiteSpace(studyUid) || string.IsNullOrWhiteSpace(seriesUid) || string.IsNullOrWhiteSpace(instanceUid))
            return BadRequest(new { error = "studyUid, seriesUid and instanceUid are required.", kind = "validation" });

        var settings = await _db.TenantSettings.FirstOrDefaultAsync(s => s.TenantId == tenant.Id, ct);
        if (settings is null || string.IsNullOrEmpty(settings.DicomWebBaseUrl))
            return Ok(new { configured = false, metadata = (object?)null });

        using var doc = await dicom.RetrieveInstanceMetadataAsync(settings, studyUid, seriesUid, instanceUid, ct);
        object? metadata = null;
        if (doc is not null)
        {
            metadata = System.Text.Json.JsonSerializer.Deserialize<object>(doc.RootElement.GetRawText());
        }
        await _audit.AppendAsync(new AuditEvent
        {
            TenantId = tenant.Id,
            UserId = user.Id,
            ReportId = report.Id,
            Action = AuditAction.DicomContextFetched,
            DetailsJson = System.Text.Json.JsonSerializer.Serialize(new
            {
                scope = "instance",
                studyUid,
                seriesUid,
                instanceUid,
                found = metadata is not null,
            }),
        }, ct);
        return Ok(new { configured = true, metadata });
    }

    public record DictationCleanupDto(string RawDictation);

    /// <summary>
    /// Iter-31 AI-001 — runs the dictation cleanup pipeline. Routes through
    /// <see cref="Application.Services.AiGateway"/> so PHI policy + audit
    /// trail apply. RBAC: <see cref="UserRole.Radiologist"/> or
    /// <see cref="UserRole.MedicalDirector"/>.
    /// </summary>
    [HttpPost("{id:guid}/dictation/cleanup")]
    [Microsoft.AspNetCore.RateLimiting.EnableRateLimiting("ai")]
    public async Task<IActionResult> DictationCleanup(
        Guid id,
        [FromBody] DictationCleanupDto dto,
        [FromServices] Application.Abstractions.IDictationCleanupService service,
        CancellationToken ct)
    {
        var (tenant, user) = await ResolveContextAsync(_db, ct);
        var deny = RequireRole(user, UserRole.Radiologist, UserRole.MedicalDirector);
        if (deny is not null) return deny;
        var report = await _db.Reports.FirstOrDefaultAsync(r => r.Id == id && r.TenantId == tenant.Id, ct);
        if (report is null) return NotFound();
        if (string.IsNullOrWhiteSpace(dto.RawDictation))
            return BadRequest(new { error = "rawDictation is required.", kind = "validation" });

        try
        {
            var result = await service.CleanupAsync(tenant, user, report, dto.RawDictation, ct);
            return Ok(new
            {
                cleanedSections = new
                {
                    indication = result.Indication,
                    technique = result.Technique,
                    findings = result.Findings,
                    impression = result.Impression,
                    recommendations = result.Recommendations,
                },
                provider = result.Provider,
                model = result.Model,
                latencyMs = result.LatencyMs,
                promptVersion = result.PromptVersion,
            });
        }
        catch (Application.Services.ProviderPolicyException pex)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { error = pex.Message, kind = "provider_policy" });
        }
        catch (Application.Services.ProviderTransportException tex)
        {
            // UBAG (and other web/HTTP) adapters raise this on a failed job,
            // empty output, or timeout. Surface a clear 502 with the reason so
            // the editor shows it on the Fix button instead of a generic 500.
            return StatusCode(StatusCodes.Status502BadGateway, new
            {
                error = $"The AI provider could not complete the cleanup: {tex.Message}",
                kind = "provider_transport",
            });
        }
    }

    public record DictationDraftDto(string RawDictation);

    /// <summary>
    /// Dictation-engine brief §4.2 — the safety-wrapped dictation→report pipeline: §5.2 deterministic
    /// pass-through → formatter (PHI-gated AiGateway) → §5.3 validation-diff (fail-safe fallback) →
    /// §5.6 laterality/negation/gender sentinel → §5.7 local audit. Returns an editable draft that
    /// still requires the §5.5 sign-off gate. RBAC: <see cref="UserRole.Radiologist"/> /
    /// <see cref="UserRole.MedicalDirector"/>.
    /// </summary>
    [HttpPost("{id:guid}/dictation/draft")]
    [Microsoft.AspNetCore.RateLimiting.EnableRateLimiting("ai")]
    public async Task<IActionResult> DictationDraft(
        Guid id,
        [FromBody] DictationDraftDto dto,
        [FromServices] RadioPad.Application.Dictation.IDictationDraftService service,
        CancellationToken ct)
    {
        var (tenant, user) = await ResolveContextAsync(_db, ct);
        var deny = RequireRole(user, UserRole.Radiologist, UserRole.MedicalDirector);
        if (deny is not null) return deny;
        var report = await _db.Reports.FirstOrDefaultAsync(r => r.Id == id && r.TenantId == tenant.Id, ct);
        if (report is null) return NotFound();
        if (string.IsNullOrWhiteSpace(dto.RawDictation))
            return BadRequest(new { error = "rawDictation is required.", kind = "validation" });

        // §6/F7 — apply the org lexicon + the user's personal corrections deterministically before
        // the LLM (the user's entry wins for the same term).
        var lexicon = await _db.Lexicons.Where(l => l.TenantId == tenant.Id).ToListAsync(ct);
        var userCorrections = await _db.UserCorrections
            .Where(c => c.TenantId == tenant.Id && c.UserId == user.Id).ToListAsync(ct);
        var corrections = RadioPad.Application.Dictation.CorrectionDictionary.Resolve(lexicon, userCorrections);

        try
        {
            var draft = await service.BuildDraftAsync(tenant, user, report, dto.RawDictation, corrections, ct);
            return Ok(new
            {
                sections = draft.DraftSections,
                accepted = draft.Accepted,
                usedFallback = draft.UsedFallback,
                requiresReview = draft.RequiresReview,
                violations = draft.Violations.Select(v => new { reason = v.Reason.ToString(), detail = v.Detail }),
                sentinelWarnings = draft.SentinelWarnings.Select(w => new { kind = w.Kind.ToString(), detail = w.Detail }),
                provider = draft.Provider,
                model = draft.Model,
                latencyMs = draft.LatencyMs,
            });
        }
        catch (Application.Services.ProviderPolicyException pex)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { error = pex.Message, kind = "provider_policy" });
        }
        catch (Application.Services.ProviderTransportException tex)
        {
            return StatusCode(StatusCodes.Status502BadGateway, new
            {
                error = $"The AI provider could not complete the draft: {tex.Message}",
                kind = "provider_transport",
            });
        }
    }

    public record CrossCheckReviewDto(string Text, string? SectionKey, bool UseUbag);

    /// <summary>
    /// Cross-check LLM medical-accuracy review of already-transcribed text — returns
    /// suggested original→corrected edits for the editor. Mirrors
    /// <see cref="DictationCleanup"/>: RBAC <see cref="UserRole.Radiologist"/> /
    /// <see cref="UserRole.MedicalDirector"/>, the "ai" rate limit, and a
    /// <see cref="Application.Services.ProviderPolicyException"/> → 403
    /// <c>provider_policy</c>. When <c>useUbag</c> is set and the tenant has an
    /// enabled UBAG provider, the review routes through it (opt-in cloud web-AI).
    /// </summary>
    [HttpPost("{id:guid}/crosscheck/review")]
    [Microsoft.AspNetCore.RateLimiting.EnableRateLimiting("ai")]
    public async Task<IActionResult> CrossCheckReview(
        Guid id,
        [FromBody] CrossCheckReviewDto dto,
        [FromServices] Application.Abstractions.ICrossCheckReviewService service,
        CancellationToken ct)
    {
        var (tenant, user) = await ResolveContextAsync(_db, ct);
        var deny = RequireRole(user, UserRole.Radiologist, UserRole.MedicalDirector);
        if (deny is not null) return deny;
        var report = await _db.Reports.FirstOrDefaultAsync(r => r.Id == id && r.TenantId == tenant.Id, ct);
        if (report is null) return NotFound();
        if (string.IsNullOrWhiteSpace(dto.Text))
            return BadRequest(new { error = "text is required.", kind = "validation" });

        // Opt-in UBAG: force an enabled UBAG provider when the tenant has one.
        RadioPad.Domain.Entities.ProviderConfig? forced = null;
        if (dto.UseUbag)
        {
            forced = await _db.Providers.FirstOrDefaultAsync(
                p => p.TenantId == tenant.Id
                     && p.Enabled
                     && p.Adapter == RadioPad.Infrastructure.Providers.Ubag.UbagProviderAdapter.AdapterId, ct);
        }

        try
        {
            var result = await service.ReviewAsync(tenant, report, dto.Text, dto.SectionKey, forced, ct);
            return Ok(new
            {
                provider = result.Provider,
                model = result.Model,
                latencyMs = result.LatencyMs,
                corrections = result.Corrections.Select(c => new
                {
                    id = c.Id,
                    sectionKey = c.SectionKey,
                    originalText = c.OriginalText,
                    correctedText = c.CorrectedText,
                    startOffset = c.StartOffset,
                    endOffset = c.EndOffset,
                    reason = c.Reason,
                    category = c.Category,
                    source = c.Source,
                    confidence = c.Confidence,
                    severity = c.Severity,
                }),
            });
        }
        catch (Application.Services.ProviderPolicyException pex)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { error = pex.Message, kind = "provider_policy" });
        }
    }

    /// <summary>
    /// Phase B (dictation transcription) — accepts a dictation audio file
    /// (multipart form, field <c>audio</c>) and returns a free-text transcript
    /// scraped via the UBAG <c>medical_transcription</c> flow. Mirrors
    /// <see cref="DictationCleanup"/>: RBAC is <see cref="UserRole.Radiologist"/>
    /// or <see cref="UserRole.MedicalDirector"/>, the "ai" rate limit applies,
    /// and a <see cref="Application.Services.ProviderPolicyException"/> maps to a
    /// 403 with the same <c>kind=provider_policy</c> envelope the frontend
    /// already handles. The request body is capped at 32 MiB.
    /// </summary>
    [HttpPost("{id:guid}/dictation/transcribe")]
    [Microsoft.AspNetCore.RateLimiting.EnableRateLimiting("ai")]
    [RequestSizeLimit(33_554_432)]
    [RequestFormLimits(MultipartBodyLengthLimit = 33_554_432)]
    public async Task<IActionResult> DictationTranscribe(
        Guid id,
        [FromForm] IFormFile? audio,
        [FromServices] Application.Abstractions.ITranscriptionService service,
        CancellationToken ct,
        [FromForm] string? mode = null)
    {
        var (tenant, user) = await ResolveContextAsync(_db, ct);
        var deny = RequireRole(user, UserRole.Radiologist, UserRole.MedicalDirector);
        if (deny is not null) return deny;
        var report = await _db.Reports.FirstOrDefaultAsync(r => r.Id == id && r.TenantId == tenant.Id, ct);
        if (report is null) return NotFound();

        if (audio is null || audio.Length <= 0)
            return BadRequest(new { error = "audio file is required.", kind = "validation" });
        if (audio.Length > 33_554_432)
            return BadRequest(new { error = "audio file exceeds the 32 MiB limit.", kind = "validation" });

        var allowedTypes = new[] { "audio/webm", "audio/wav", "audio/mpeg", "audio/mp4", "audio/ogg" };
        var contentType = (audio.ContentType ?? string.Empty).Split(';')[0].Trim();
        if (!allowedTypes.Contains(contentType, StringComparer.OrdinalIgnoreCase))
            return BadRequest(new { error = $"unsupported audio content type '{audio.ContentType}'.", kind = "validation" });

        try
        {
            await using var stream = audio.OpenReadStream();
            var result = await service.TranscribeAsync(
                tenant, user, report, stream, audio.FileName ?? "dictation.webm",
                audio.Length, contentType, ct, mode);
            return Ok(new
            {
                transcript = result.Text,
                provider = result.Provider,
                model = result.Model,
                latencyMs = result.LatencyMs,
                // Phase 2 ensemble — per-word review spans (null for single-engine /
                // cloud paths). Flagged spans are rendered as .ai-mark review marks.
                spans = result.Spans?.Select(s => new
                {
                    text = s.Text,
                    flagged = s.Flagged,
                    reason = s.Reason,
                    source = s.Source,
                }),
            });
        }
        catch (Application.Services.ProviderPolicyException pex)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { error = pex.Message, kind = "provider_policy" });
        }
    }

    /// <summary>
    /// Iter-31 AI-008 — up to three suggested follow-up phrases for a report's
    /// recommendations section. Routes through the auto-router (cheapest
    /// matching provider). Returns an empty list when no provider is wired.
    /// </summary>
    [HttpGet("{id:guid}/followup-suggestions")]
    [Microsoft.AspNetCore.RateLimiting.EnableRateLimiting("ai")]
    public async Task<IActionResult> FollowUpSuggestions(Guid id, CancellationToken ct)
    {
        var (tenant, user) = await ResolveContextAsync(_db, ct);
        var deny = RequirePermission(user, RbacPermission.ReportsRead);
        if (deny is not null) return deny;
        var report = await _db.Reports.FirstOrDefaultAsync(r => r.Id == id && r.TenantId == tenant.Id, ct);
        if (report is null) return NotFound();
        try
        {
            var suggestions = await _reporting.SuggestFollowUpAsync(tenant, user, report, ct);
            return Ok(new { suggestions });
        }
        catch (Application.Services.ProviderPolicyException pex)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { error = pex.Message, kind = "provider_policy" });
        }
    }

    /// <summary>PRD Beta #7 — extract structured measurements from report text.</summary>
    [HttpGet("{id:guid}/measurements")]
    public async Task<IActionResult> Measurements(
        Guid id,
        [FromServices] MeasurementExtractionService extraction,
        CancellationToken ct)
    {
        var (tenant, user) = await ResolveContextAsync(_db, ct);
        var deny = RequirePermission(user, RbacPermission.ReportsRead);
        if (deny is not null) return deny;
        var report = await _db.Reports.FirstOrDefaultAsync(r => r.Id == id && r.TenantId == tenant.Id, ct);
        if (report is null) return NotFound();

        var bySection = extraction.ExtractFromReport(report);
        var flat = bySection.Values.SelectMany(v => v).ToList();
        return Ok(flat);
    }
}
