using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RadioPad.Application.Abstractions;
using RadioPad.Application.Services;
using RadioPad.Domain.Entities;
using RadioPad.Domain.Enums;
using RadioPad.Infrastructure.Persistence;

namespace RadioPad.Api.Controllers;

/// <summary>
/// Resolves the active dev tenant + user from headers (`X-RadioPad-Tenant`,
/// `X-RadioPad-User`). In production this is replaced by the OIDC pipeline,
/// but the controller surface stays unchanged.
/// </summary>
public abstract class TenantedController : ControllerBase
{
    protected async Task<(Tenant tenant, User user)> ResolveContextAsync(RadioPadDbContext db, CancellationToken ct)
    {
        var slug = Request.Headers["X-RadioPad-Tenant"].FirstOrDefault() ?? "dev";
        var email = Request.Headers["X-RadioPad-User"].FirstOrDefault() ?? "radiologist@radiopad.local";
        var tenant = await db.Tenants.FirstOrDefaultAsync(t => t.Slug == slug, ct)
            ?? throw new InvalidOperationException($"Tenant '{slug}' not found.");
        var user = await db.Users.FirstOrDefaultAsync(u => u.TenantId == tenant.Id && u.Email == email, ct)
            ?? throw new InvalidOperationException($"User '{email}' not found in tenant '{slug}'.");
        return (tenant, user);
    }

    /// <summary>
    /// Minimal RBAC enforcement (PRD AUTH-002). Returns a 403 problem result
    /// when the active user's role is not in the allow-list. Higher-fidelity
    /// per-permission RBAC ships with OIDC integration in Phase 3.
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
}

[ApiController]
[Route("api/reports")]
public class ReportsController : TenantedController
{
    private readonly RadioPadDbContext _db;
    private readonly ReportingService _reporting;
    private readonly IAuditLog _audit;

    public ReportsController(RadioPadDbContext db, ReportingService reporting, IAuditLog audit)
    {
        _db = db;
        _reporting = reporting;
        _audit = audit;
    }

    public record CreateReportDto(
        string? Modality, string? BodyPart, string? Indication,
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
        var (tenant, _) = await ResolveContextAsync(_db, ct);
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

        // Iter-32 TMP-005 — gate non-Approved templates the same way RB-010
        // gates rulebooks. Tenants that opt in to AllowSandboxRulebooks may
        // freely use Draft/Review templates; production tenants may only
        // pin Approved templates.
        if (dto.TemplateId is { } tplId)
        {
            var tpl = await _db.Templates.FirstOrDefaultAsync(t => t.Id == tplId && t.TenantId == tenant.Id, ct);
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
        }

        var report = new Report
        {
            TenantId = tenant.Id,
            CreatedByUserId = user.Id,
            RulebookId = dto.RulebookId,
            TemplateId = dto.TemplateId,
            Status = ReportStatus.Draft,
            Indication = dto.Indication ?? "",
            Study = new StudyContext
            {
                Modality = dto.Modality ?? "",
                BodyPart = dto.BodyPart ?? "",
                Indication = dto.Indication ?? "",
                Comparison = dto.Comparison ?? "",
                AccessionNumber = dto.AccessionNumber ?? Guid.NewGuid().ToString("n")[..10],
            },
        };
        _db.Reports.Add(report);
        await _db.SaveChangesAsync(ct);
        return CreatedAtAction(nameof(Get), new { id = report.Id }, report);
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
    {
        var (tenant, _) = await ResolveContextAsync(_db, ct);
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
        string? AiHighlightsJson, Guid? RulebookId);

    [HttpPatch("{id:guid}")]
    public async Task<IActionResult> Patch(Guid id, [FromBody] PatchReportDto dto, CancellationToken ct)
    {
        var (tenant, user) = await ResolveContextAsync(_db, ct);
        var report = await _db.Reports.FirstOrDefaultAsync(r => r.Id == id && r.TenantId == tenant.Id, ct);
        if (report is null) return NotFound();
        if (dto.Indication is not null) report.Indication = dto.Indication;
        if (dto.Technique is not null) report.Technique = dto.Technique;
        if (dto.Comparison is not null) report.Comparison = dto.Comparison;
        if (dto.Findings is not null) report.Findings = dto.Findings;
        if (dto.Impression is not null) report.Impression = dto.Impression;
        if (dto.Recommendations is not null) report.Recommendations = dto.Recommendations;
        if (dto.AiHighlightsJson is not null) report.AiHighlightsJson = dto.AiHighlightsJson;
        if (dto.RulebookId is not null) report.RulebookId = dto.RulebookId;
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
        var (tenant, _) = await ResolveContextAsync(_db, ct);
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
        var (tenant, _) = await ResolveContextAsync(_db, ct);
        var report = await _db.Reports.FirstOrDefaultAsync(r => r.Id == id && r.TenantId == tenant.Id, ct);
        if (report is null) return NotFound();

        int score = 100;
        var reasons = new List<string>();
        if (string.IsNullOrWhiteSpace(report.Study.Indication) && string.IsNullOrWhiteSpace(report.Indication))
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
        var (tenant, _) = await ResolveContextAsync(_db, ct);
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

    [HttpPost("{id:guid}/acknowledge")]
    public async Task<IActionResult> Acknowledge(Guid id, CancellationToken ct)
    {
        var (tenant, user) = await ResolveContextAsync(_db, ct);
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
        var (tenant, _) = await ResolveContextAsync(_db, ct);
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
        var (tenant, _) = await ResolveContextAsync(_db, ct);
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
        var (tenant, _) = await ResolveContextAsync(_db, ct);
        var report = await _db.Reports.FirstOrDefaultAsync(r => r.Id == id && r.TenantId == tenant.Id, ct);
        if (report is null) return NotFound();

        var bySection = extraction.ExtractFromReport(report);
        var flat = bySection.Values.SelectMany(v => v).ToList();
        return Ok(flat);
    }
}
