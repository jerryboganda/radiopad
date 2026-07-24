using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RadioPad.Api.Auth;
using RadioPad.Application.Abstractions;
using RadioPad.Application.Security;
using RadioPad.Application.Services;
using RadioPad.Domain.Entities;
using RadioPad.Application.Governance;
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
    private readonly Services.AiJobCoordinator _coordinator;

    public ReportsController(
        RadioPadDbContext db,
        ReportingService reporting,
        IAuditLog audit,
        Services.AiJobRegistry aiJobs,
        Services.AiJobCoordinator coordinator)
    {
        _db = db;
        _reporting = reporting;
        _audit = audit;
        _aiJobs = aiJobs;
        _coordinator = coordinator;
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
        [FromQuery] bool archived = false,
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
        // PR-N2 — the worklist hides soft-archived drafts by default; `archived=true` surfaces
        // exactly the archived set for recovery (unarchive via PATCH /reports/{id}/unarchive).
        query = archived
            ? query.Where(r => r.ArchivedAt != null)
            : query.Where(r => r.ArchivedAt == null);
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
            archivedAt = report.ArchivedAt,
            signatures,
        });
    }

    /// <summary>
    /// PR-N2 — recover a soft-archived draft: clears <see cref="Report.ArchivedAt"/> and
    /// refreshes <see cref="Report.UpdatedAt"/> so the report re-enters the active worklist
    /// with a fresh staleness window. Restricted to <see cref="RbacPermission.ReportsEdit"/>.
    /// </summary>
    [HttpPatch("{id:guid}/unarchive")]
    public async Task<IActionResult> Unarchive(Guid id, CancellationToken ct)
    {
        var (tenant, user) = await ResolveContextAsync(_db, ct);
        var deny = RequirePermission(user, RbacPermission.ReportsEdit);
        if (deny is not null) return deny;
        var report = await _db.Reports.FirstOrDefaultAsync(r => r.Id == id && r.TenantId == tenant.Id, ct);
        if (report is null) return NotFound();
        if (report.ArchivedAt is not null)
        {
            report.ArchivedAt = null;
            report.UpdatedAt = DateTimeOffset.UtcNow;
            await _db.SaveChangesAsync(ct);
        }
        return Ok(new { report.Id, archivedAt = report.ArchivedAt });
    }

    /// <summary>
    /// Soft-delete a report: sets <see cref="Report.ArchivedAt"/> so it drops out of the
    /// default worklist (the <c>archived=true</c> filter surfaces it again, and PATCH
    /// <c>/{id}/unarchive</c> recovers it). RadioPad never hard-deletes a clinical record —
    /// this mirrors the background <c>OrphanedDraftCleanupJob</c> archive and keeps the
    /// append-only audit trail intact. Restricted to <see cref="RbacPermission.ReportsEdit"/>.
    /// </summary>
    [HttpPatch("{id:guid}/archive")]
    public async Task<IActionResult> Archive(Guid id, CancellationToken ct)
    {
        var (tenant, user) = await ResolveContextAsync(_db, ct);
        var deny = RequirePermission(user, RbacPermission.ReportsEdit);
        if (deny is not null) return deny;
        var report = await _db.Reports.FirstOrDefaultAsync(r => r.Id == id && r.TenantId == tenant.Id, ct);
        if (report is null) return NotFound();
        if (report.ArchivedAt is null)
        {
            report.ArchivedAt = DateTimeOffset.UtcNow;
            report.UpdatedAt = report.ArchivedAt.Value;
            await _db.SaveChangesAsync(ct);
            await _audit.AppendAsync(new AuditEvent
            {
                TenantId = tenant.Id,
                UserId = user.Id,
                Action = AuditAction.ReportDraftArchived,
                ReportId = report.Id,
                DetailsJson = System.Text.Json.JsonSerializer.Serialize(new { mode = "manual", status = report.Status.ToString() }),
            }, ct);
        }
        return Ok(new { report.Id, archivedAt = report.ArchivedAt });
    }

    public record PatchReportDto(
        string? Indication, string? Technique, string? Comparison,
        string? Findings, string? Impression, string? Recommendations,
        string? AiHighlightsJson, Guid? RulebookId,
        // Iter-36 — study-context fields editable from the reporting panel.
        string? Modality, string? BodyPart, int? Age, string? Gender, string? Contrast,
        // Manual binding overrides — an explicit id pins the binding; sending
        // TemplatePinned/RulebookPinned = false clears the pin (reset-to-auto).
        Guid? TemplateId = null, bool? TemplatePinned = null, bool? RulebookPinned = null,
        // F8 — RIS/worklist priority (Routine | Urgent | Stat).
        string? Priority = null);

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
        if (dto.Priority is not null
            && Enum.TryParse<RadioPad.Domain.Enums.ReportPriority>(dto.Priority, ignoreCase: true, out var prio))
            report.Priority = prio;

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
        // Shape MUST match ComparePriorResult in frontend/lib/api.ts. It previously returned
        // hasPrior/priorId/currentReport/priorReport while the client read current/prior/sections —
        // nothing lined up, so `data.current` was undefined and the priors panel threw on EVERY
        // load, with or without a prior. The client's shape is the useful one (a per-section diff
        // the panel renders with change flags), so the server computes it rather than dumping two
        // report bodies for the UI to re-derive.
        var current = new { id = report.Id, bodyPart = report.Study.BodyPart ?? string.Empty };

        if (prior is null)
            return Ok(new { current, prior = (object?)null, sections = Array.Empty<object>() });

        var sections = new[]
        {
            SectionDiff("Indication", report.Indication, prior.Indication),
            SectionDiff("Technique", report.Technique, prior.Technique),
            SectionDiff("Comparison", report.Comparison, prior.Comparison),
            SectionDiff("Findings", report.Findings, prior.Findings),
            SectionDiff("Impression", report.Impression, prior.Impression),
            SectionDiff("Recommendations", report.Recommendations, prior.Recommendations),
        };

        return Ok(new
        {
            current,
            prior = new
            {
                id = prior.Id,
                bodyPart = prior.Study.BodyPart ?? string.Empty,
                createdAt = prior.CreatedAt,
            },
            sections,
        });
    }

    /// <summary>
    /// One section's current-vs-prior comparison. <c>changed</c> is whitespace-insensitive so
    /// reformatting alone is not reported as a clinical change — the flag drives the panel's
    /// "N sections changed" count, and inflating it with cosmetic diffs would train the
    /// radiologist to ignore it.
    /// </summary>
    private static object SectionDiff(string section, string? current, string? prior) => new
    {
        section,
        current = current ?? string.Empty,
        prior = prior ?? string.Empty,
        changed = !string.Equals(
            Normalize(current), Normalize(prior), StringComparison.OrdinalIgnoreCase),
    };

    private static string Normalize(string? s) =>
        string.IsNullOrWhiteSpace(s)
            ? string.Empty
            : System.Text.RegularExpressions.Regex.Replace(s.Trim(), @"\s+", " ");

    public record AiActionDto(string Mode, Guid? ProviderId);

    /// <summary>
    /// Phase 3 — deny a regulated capability the tenant has explicitly switched off, else null.
    ///
    /// <para>Enabled by DEFAULT (the deploying organisation holds the applicable UKCA/MHRA/CE/FDA
    /// clearances), so this only refuses when an administrator has actively turned the feature off.
    /// Shared rather than inlined because the same capability is reachable from several endpoints —
    /// auto-impression from both the synchronous <c>/ai</c> route and the queued <c>/ai/jobs</c>
    /// route — and gating one entry point while leaving another open is the failure mode that made
    /// this gate worthless in the first place.</para>
    /// </summary>
    private async Task<IActionResult?> DenyIfRegulatedFeatureDisabledAsync(
        Tenant tenant, RegulatedFeature feature, string label, CancellationToken ct)
    {
        // Flags live on TenantSettings, not Tenant. (The gate's own doc comment named the wrong
        // entity — undetected because nothing ever called it.) Absent settings ⇒ no explicit
        // override ⇒ enabled.
        var settings = await _db.TenantSettings.AsNoTracking()
            .FirstOrDefaultAsync(s => s.TenantId == tenant.Id, ct);
        if (RegulatedFeatures.IsEnabled(settings?.FeatureFlagsJson, feature)) return null;
        return StatusCode(StatusCodes.Status403Forbidden, new
        {
            error = $"{label} is switched off for this organisation.",
            kind = "regulated_feature_disabled",
            feature = RegulatedFeatures.KeyFor(feature),
        });
    }

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

        // Phase 3 — auto-impression is a regulated capability; honour the tenant switch. Applied
        // at BOTH the sync /ai and queued /ai/jobs routes: gating one and leaving the other open
        // is exactly how a gate becomes decorative.
        if (string.Equals(mode, "impression", StringComparison.OrdinalIgnoreCase))
        {
            var regulatedDeny = await DenyIfRegulatedFeatureDisabledAsync(
                tenant, RegulatedFeature.AutoImpression, "Automatic impression drafting", ct);
            if (regulatedDeny is not null) return regulatedDeny;
        }
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

            await Services.AiJobCoordinator.ApplyStructuredResultAsync(_db, report, user, result, ct);

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

        // Phase 3 — auto-impression is a regulated capability; honour the tenant switch. Applied
        // at BOTH the sync /ai and queued /ai/jobs routes: gating one and leaving the other open
        // is exactly how a gate becomes decorative.
        if (string.Equals(mode, "impression", StringComparison.OrdinalIgnoreCase))
        {
            var regulatedDeny = await DenyIfRegulatedFeatureDisabledAsync(
                tenant, RegulatedFeature.AutoImpression, "Automatic impression drafting", ct);
            if (regulatedDeny is not null) return regulatedDeny;
        }
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

        // Fast in-memory single-flight: attach to an already-running identical job
        // instead of stacking a second generation (retry-after-disconnect is the
        // expected recovery path; the first job is still running by design). The
        // coordinator's DB-level single-flight is the authoritative backstop for the
        // queued state the registry doesn't know about yet.
        if (_aiJobs.TryGetRunning(tenant.Id, id, "ai", mode, out var running))
            return Accepted(new { jobId = running.Id, status = running.Status });

        // Enqueue onto the durable job platform; the runner picks it up. Status may
        // now be "queued" on a fresh submit (additive — the client only polls, never
        // reads submit-status).
        var (jobId, status, _) = await _coordinator.SubmitAsync(tenant, user, id, "ai", mode, dto.ProviderId, ct);
        return Accepted(new { jobId, status });
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

        var (jobId, status, _) = await _coordinator.SubmitAsync(tenant, user, id, "generate", "generate", dto.ProviderId, ct);
        return Accepted(new { jobId, status });
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

        // Hot path: serve from the in-memory registry without touching the DB.
        if (_aiJobs.TryGet(jobId, out var job) && job.TenantId == tenant.Id && job.ReportId == id)
        {
            return Ok(new
            {
                jobId = job.Id,
                kind = job.Kind,
                mode = job.Mode,
                status = job.Status,
                elapsedMs = (long)((job.CompletedAt ?? DateTimeOffset.UtcNow) - job.CreatedAt).TotalMilliseconds,
                result = job.Status == "ok" ? job.Payload : null,
                // PR-B1 (additive): live token/partial progress for a running job, hot-cache
                // only. Omitted (WhenWritingNull) for non-running jobs and when no progress
                // has been recorded — old clients are unaffected.
                progress = ProgressShape(job.Status, jobId),
                error = job.Error,
                errorKind = job.ErrorKind,
            });
        }

        // Registry miss — evicted, or the process restarted mid-generation. Fall
        // back to the durable row so a restart surfaces status=error,
        // errorKind=server_restart instead of a bare 404, and a still-queued job is
        // visible before the runner has created its hot entry. The envelope shape is
        // identical to the hot path so old and new bodies are indistinguishable.
        var row = await _db.AiJobs
            .FirstOrDefaultAsync(j => j.Id == jobId && j.TenantId == tenant.Id && j.ReportId == id, ct);
        if (row is not null)
        {
            object? result = null;
            if (row.Status == "ok")
            {
                if (string.Equals(row.Kind, "generate", StringComparison.Ordinal))
                {
                    // Generate has no ResultJson — the report row IS the result, mirroring
                    // the registry payload the hot path returns for a generate job.
                    result = await _db.Reports
                        .FirstOrDefaultAsync(r => r.Id == row.ReportId && r.TenantId == tenant.Id, ct);
                }
                else if (!string.IsNullOrWhiteSpace(row.ResultJson))
                {
                    result = System.Text.Json.JsonSerializer
                        .Deserialize<System.Text.Json.JsonElement>(row.ResultJson);
                }
            }
            return Ok(new
            {
                jobId = row.Id,
                kind = row.Kind,
                mode = row.Mode,
                status = row.Status,
                elapsedMs = (long)((row.CompletedAt ?? DateTimeOffset.UtcNow) - row.CreatedAt).TotalMilliseconds,
                result,
                // Progress is hot-cache-only; the DB fallback (registry miss = evicted or
                // restarted) never carries it. Always null → omitted (WhenWritingNull).
                progress = (object?)null,
                error = row.Error,
                errorKind = row.ErrorKind,
            });
        }

        return NotFound(new
        {
            error = "AI job not found — the server may have restarted mid-generation. Please try again.",
            kind = "job_not_found",
        });
    }

    /// <summary>PR-B1 — the additive <c>progress</c> field for a poll envelope:
    /// <c>{ tokens, percent }</c> from the registry hot cache when the job is running and
    /// has recorded progress, else null (omitted by WhenWritingNull). <c>percent</c> is
    /// itself null on every streaming path in v1.</summary>
    private object? ProgressShape(string status, Guid jobId)
    {
        if (status != "running") return null;
        var p = _aiJobs.ProgressOf(jobId);
        return p is null ? null : new { tokens = p.Tokens, percent = p.Percent };
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
    /// Copy drafted section text onto a report instance so the rulebook can be run over it (F6).
    /// Only the five canonical dictation sections are mapped; an unknown key is ignored rather than
    /// guessed at, and a section the formatter did not produce leaves the report's own text alone.
    /// </summary>
    private static void ApplyDraftSections(Report target, IReadOnlyDictionary<string, string> sections)
    {
        foreach (var (key, value) in sections)
        {
            if (value is null) continue;
            switch (key.Trim().ToLowerInvariant())
            {
                case "indication": target.Indication = value; break;
                case "technique": target.Technique = value; break;
                case "findings": target.Findings = value; break;
                case "impression": target.Impression = value; break;
                case "recommendations": target.Recommendations = value; break;
            }
        }
    }

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

            // F6 — run the tenant's rulebook over the DRAFTED text.
            //
            // The dictation pipeline's own guards (§5.2/§5.3/§5.6) answer "did the formatter invent
            // or lose anything?", which is a different question from "is this a complete, compliant
            // report?" — the rulebook's severities, required sections and forbidden terms. Those ran
            // only at /validate, so a radiologist saw them after applying the draft and clicking
            // Validate, never while reviewing the draft they were deciding whether to accept.
            //
            // Validated against an UNTRACKED copy: `report` is tracked by EF, and the §5.7 audit
            // append later in this request calls SaveChanges — mutating the tracked entity would
            // silently persist an unapplied draft into the report.
            // Fully qualified: System.ComponentModel.DataAnnotations.ValidationResult is also in
            // scope here and silently wins the unqualified name.
            RadioPad.Domain.ValueObjects.ValidationResult? validation = null;
            var probe = await _db.Reports.AsNoTracking()
                .FirstOrDefaultAsync(r => r.Id == id && r.TenantId == tenant.Id, ct);
            if (probe is not null)
            {
                ApplyDraftSections(probe, draft.DraftSections);
                validation = await _reporting.ValidateAsync(tenant, probe, lexicon, ct);
            }

            return Ok(new
            {
                sections = draft.DraftSections,
                accepted = draft.Accepted,
                usedFallback = draft.UsedFallback,
                requiresReview = draft.RequiresReview,
                violations = draft.Violations.Select(v => new { reason = v.Reason.ToString(), detail = v.Detail }),
                sentinelWarnings = draft.SentinelWarnings.Select(w => new { kind = w.Kind.ToString(), detail = w.Detail }),
                // Null when no rulebook binds — distinct from "validated and clean", so the UI can
                // say which is true instead of implying a pass nobody performed.
                validation = validation is null ? null : new
                {
                    blockerPresent = validation.BlockerPresent,
                    qualityScore = validation.QualityScore,
                    findings = validation.Findings.Select(f => new
                    {
                        ruleId = f.RuleId,
                        severity = f.Severity,
                        message = f.Message,
                        section = f.Section,
                    }),
                },
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
    /// PR-B5 — durable-job sibling of <see cref="DictationCleanup"/>. Runs the dictation cleanup
    /// pipeline as a restart-surviving async job (Kind <c>ai</c>, Mode <c>cleanup</c>) instead of a
    /// blocking request, so a reload or server restart never loses track of it. RBAC parity with the
    /// sync route (<see cref="UserRole.Radiologist"/> / <see cref="UserRole.MedicalDirector"/> — NOT
    /// the ReportsEdit permission). The raw dictation is persisted in <c>AiJob.InputJson</c> so a
    /// Retry can re-run it. The result is a suggestion set in ResultJson — the job NEVER writes the
    /// report; the preview/accept gate stays entirely client-side. The sync route stays live so old
    /// desktop builds keep working.
    /// </summary>
    [HttpPost("{id:guid}/dictation/cleanup/jobs")]
    [Microsoft.AspNetCore.RateLimiting.EnableRateLimiting("ai")]
    public async Task<IActionResult> DictationCleanupJob(
        Guid id,
        [FromBody] DictationCleanupDto dto,
        CancellationToken ct)
    {
        var (tenant, user) = await ResolveContextAsync(_db, ct);
        var deny = RequireRole(user, UserRole.Radiologist, UserRole.MedicalDirector);
        if (deny is not null) return deny;
        var report = await _db.Reports.FirstOrDefaultAsync(r => r.Id == id && r.TenantId == tenant.Id, ct);
        if (report is null) return NotFound();
        if (string.IsNullOrWhiteSpace(dto.RawDictation))
            return BadRequest(new { error = "rawDictation is required.", kind = "validation" });

        // Single-flight: one live cleanup per report attaches rather than stacks.
        if (_aiJobs.TryGetRunning(tenant.Id, id, "ai", "cleanup", out var running))
            return Accepted(new { jobId = running.Id, status = running.Status });

        var inputJson = System.Text.Json.JsonSerializer.Serialize(new { rawDictation = dto.RawDictation });
        var (jobId, status, _) = await _coordinator.SubmitAsync(
            tenant, user, id, "ai", "cleanup", null, ct, inputJson: inputJson);
        return Accepted(new { jobId, status });
    }

    /// <summary>
    /// PR-B5 — durable-job sibling of <see cref="CrossCheckReview"/>. Runs the LLM medical-accuracy
    /// review as a restart-surviving async job (Kind <c>crosscheck</c>, Mode = the normalized section
    /// key, or <c>report</c> when none). Per-section single-flight: two different sections cross-check
    /// concurrently; a second submit of the SAME section attaches. RBAC parity + InputJson handling
    /// mirror <see cref="DictationCleanupJob"/>. Corrections are a suggestion set in ResultJson —
    /// never written to the report. The sync route stays live.
    /// </summary>
    [HttpPost("{id:guid}/crosscheck/review/jobs")]
    [Microsoft.AspNetCore.RateLimiting.EnableRateLimiting("ai")]
    public async Task<IActionResult> CrossCheckReviewJob(
        Guid id,
        [FromBody] CrossCheckReviewDto dto,
        CancellationToken ct)
    {
        var (tenant, user) = await ResolveContextAsync(_db, ct);
        var deny = RequireRole(user, UserRole.Radiologist, UserRole.MedicalDirector);
        if (deny is not null) return deny;
        var report = await _db.Reports.FirstOrDefaultAsync(r => r.Id == id && r.TenantId == tenant.Id, ct);
        if (report is null) return NotFound();
        if (string.IsNullOrWhiteSpace(dto.Text))
            return BadRequest(new { error = "text is required.", kind = "validation" });

        // Mode scopes the single-flight to the section, so distinct sections run concurrently.
        var mode = dto.SectionKey?.Trim().ToLowerInvariant() is { Length: > 0 } s ? s : "report";
        if (_aiJobs.TryGetRunning(tenant.Id, id, "crosscheck", mode, out var running))
            return Accepted(new { jobId = running.Id, status = running.Status });

        var inputJson = System.Text.Json.JsonSerializer.Serialize(new
        {
            text = dto.Text,
            sectionKey = dto.SectionKey,
            useUbag = dto.UseUbag,
        });
        var (jobId, status, _) = await _coordinator.SubmitAsync(
            tenant, user, id, "crosscheck", mode, null, ct, inputJson: inputJson);
        return Accepted(new { jobId, status });
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

        // Phase 3 — follow-up standardisation (Fleischner / LI-RADS / TI-RADS / Bosniak) is a
        // regulated capability; honour the tenant switch.
        var regulatedDeny = await DenyIfRegulatedFeatureDisabledAsync(
            tenant, RegulatedFeature.FollowUpStandardisation, "Follow-up standardisation", ct);
        if (regulatedDeny is not null) return regulatedDeny;

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
