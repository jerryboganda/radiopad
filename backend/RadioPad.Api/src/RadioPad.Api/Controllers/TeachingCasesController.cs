using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RadioPad.Application.Abstractions;
using RadioPad.Application.Security;
using RadioPad.Application.Teaching;
using RadioPad.Domain.Entities;
using RadioPad.Domain.Enums;
using RadioPad.Infrastructure.Persistence;

namespace RadioPad.Api.Controllers;

/// <summary>
/// PRD §14.14 (TF-001..008) — the teaching file &amp; education module.
///
/// Two invariants govern everything below:
/// <list type="number">
/// <item><b>De-identification is not optional.</b> The only path from a report
/// into the library is <see cref="CreateFromReport"/>, which scrubs every text
/// field through <see cref="TeachingCaseDeidentifier"/> and then asserts the
/// post-condition before saving. Callers cannot pass raw report text through
/// the create/update DTOs either — those are scrubbed too.</item>
/// <item><b>Tenant isolation on every query.</b> Every read and write is
/// filtered by <c>TenantId == tenant.Id</c>; there is no query in this file
/// that can reach another tenant's row, including the by-id lookups.</item>
/// </list>
/// </summary>
[ApiController]
[Route("api/teaching-cases")]
public sealed class TeachingCasesController : TenantedController
{
    private readonly RadioPadDbContext _db;
    private readonly IAuditLog _audit;

    public TeachingCasesController(RadioPadDbContext db, IAuditLog audit)
    {
        _db = db;
        _audit = audit;
    }

    /// <summary>
    /// Roles that may edit, publish, or delete a case they did not author.
    /// Deliberately narrow: content moderation, not general staff access.
    /// </summary>
    private static bool IsLibraryAdmin(User user) =>
        user.Role is UserRole.ItAdmin or UserRole.MedicalDirector or UserRole.ReportingAdmin;

    public record CreateTeachingCaseDto(
        string? Title,
        string? Modality,
        string? BodyPart,
        string? Diagnosis,
        string? TeachingPoints,
        string? ClinicalHistory,
        string? FindingsText,
        string? ImpressionText,
        string? Tags,
        TeachingDifficulty? Difficulty);

    public record CreateFromReportDto(
        string? Title,
        string? Diagnosis,
        string? TeachingPoints,
        string? Tags,
        TeachingDifficulty? Difficulty);

    public record UpdateTeachingCaseDto(
        string? Title,
        string? Modality,
        string? BodyPart,
        string? Diagnosis,
        string? TeachingPoints,
        string? ClinicalHistory,
        string? FindingsText,
        string? ImpressionText,
        string? Tags,
        TeachingDifficulty? Difficulty);

    /// <summary>
    /// TF-004 / TF-007 — search the library. Visibility is applied in the same
    /// expression as the tenant filter so no filter combination can widen it:
    /// a caller sees tenant-published cases plus their own private drafts.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery] string? modality,
        [FromQuery] string? bodyPart,
        [FromQuery] TeachingDifficulty? difficulty,
        [FromQuery] string? tag,
        [FromQuery] string? q,
        [FromQuery] bool mine = false,
        [FromQuery] int skip = 0,
        [FromQuery] int take = 100,
        CancellationToken ct = default)
    {
        var (tenant, user) = await ResolveContextAsync(_db, ct);
        var deny = RequirePermission(user, RbacPermission.TeachingCasesRead);
        if (deny is not null) return deny;

        take = Math.Clamp(take, 1, 200);
        skip = Math.Max(0, skip);

        var query = _db.TeachingCases.Where(c =>
            c.TenantId == tenant.Id
            && (c.Visibility == TeachingVisibility.Tenant || c.CreatedByUserId == user.Id));

        if (mine)
            query = query.Where(c => c.CreatedByUserId == user.Id);
        if (!string.IsNullOrWhiteSpace(modality))
        {
            var m = modality.Trim();
            query = query.Where(c => c.Modality.ToLower() == m.ToLower());
        }
        if (!string.IsNullOrWhiteSpace(bodyPart))
        {
            var bp = bodyPart.Trim();
            query = query.Where(c => c.BodyPart.ToLower() == bp.ToLower());
        }
        if (difficulty is not null)
            query = query.Where(c => c.Difficulty == difficulty);
        if (!string.IsNullOrWhiteSpace(tag))
        {
            // CSV containment. Commas are wrapped around both sides so "MRI" cannot
            // match "MRI-safe" and a tag in the middle of the list still matches.
            var needle = $",{tag.Trim().ToLower()},";
            query = query.Where(c => ("," + c.Tags.Replace(" ", "").ToLower() + ",").Contains(needle));
        }
        if (!string.IsNullOrWhiteSpace(q))
        {
            var needle = q.Trim().ToLower();
            query = query.Where(c =>
                c.Title.ToLower().Contains(needle)
                || c.Diagnosis.ToLower().Contains(needle)
                || c.TeachingPoints.ToLower().Contains(needle)
                || c.Tags.ToLower().Contains(needle)
                || c.FindingsText.ToLower().Contains(needle)
                || c.ImpressionText.ToLower().Contains(needle));
        }

        var total = await query.CountAsync(ct);
        var rows = await query
            .OrderByDescending(c => c.UpdatedAt)
            .Skip(skip)
            .Take(take)
            .ToListAsync(ct);

        return Ok(new
        {
            total,
            items = rows.Select(c => Shape(c, user)).ToArray(),
        });
    }

    /// <summary>
    /// TF-008 — read one case. Fetching a case authored by someone else counts
    /// as a view; re-reading your own case does not, so the counter measures
    /// teaching reach rather than the author's own edit loop.
    /// </summary>
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
    {
        var (tenant, user) = await ResolveContextAsync(_db, ct);
        var deny = RequirePermission(user, RbacPermission.TeachingCasesRead);
        if (deny is not null) return deny;

        var row = await _db.TeachingCases.FirstOrDefaultAsync(
            c => c.Id == id && c.TenantId == tenant.Id, ct);
        if (row is null) return NotFound();
        // A private case is invisible to everyone but its author — 404, not 403,
        // so the response cannot confirm that the id exists.
        if (row.Visibility == TeachingVisibility.Private && row.CreatedByUserId != user.Id)
            return NotFound();

        if (row.CreatedByUserId != user.Id)
        {
            row.ViewCount += 1;
            await _db.SaveChangesAsync(ct);
        }

        return Ok(Shape(row, user));
    }

    /// <summary>Create a hand-authored case. Text still runs through the scrubber.</summary>
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateTeachingCaseDto dto, CancellationToken ct)
    {
        var (tenant, user) = await ResolveContextAsync(_db, ct);
        var deny = RequirePermission(user, RbacPermission.TeachingCasesManage);
        if (deny is not null) return deny;
        if (dto is null || string.IsNullOrWhiteSpace(dto.Title))
            return BadRequest(new { error = "title is required.", kind = "validation" });

        var row = new TeachingCase
        {
            TenantId = tenant.Id,
            CreatedByUserId = user.Id,
            Title = TeachingCaseDeidentifier.Scrub(dto.Title),
            Modality = (dto.Modality ?? "").Trim(),
            BodyPart = (dto.BodyPart ?? "").Trim(),
            Diagnosis = TeachingCaseDeidentifier.Scrub(dto.Diagnosis),
            TeachingPoints = TeachingCaseDeidentifier.Scrub(dto.TeachingPoints),
            ClinicalHistory = TeachingCaseDeidentifier.Scrub(dto.ClinicalHistory),
            FindingsText = TeachingCaseDeidentifier.Scrub(dto.FindingsText),
            ImpressionText = TeachingCaseDeidentifier.Scrub(dto.ImpressionText),
            Tags = NormalizeTags(dto.Tags),
            Difficulty = dto.Difficulty ?? TeachingDifficulty.Intermediate,
            Visibility = TeachingVisibility.Private,
        };

        _db.TeachingCases.Add(row);
        await _db.SaveChangesAsync(ct);
        await AuditAsync(tenant, user, AuditAction.TeachingCaseCreated, new
        {
            teachingCaseId = row.Id,
            sourceReportId = (Guid?)null,
            deidentified = true,
            origin = "blank",
        }, ct);

        return CreatedAtAction(nameof(Get), new { id = row.Id }, Shape(row, user));
    }

    /// <summary>
    /// TF-001 / TF-002 — one-click "add to teaching file" with MANDATORY
    /// de-identification. Nothing that could identify a patient crosses this
    /// boundary: the accession number and patient reference are handed to the
    /// scrubber as literals to strip, the narrative is scrubbed, and the result
    /// is re-checked before it is written. If the check ever fails we refuse to
    /// save rather than persist a partially-scrubbed case.
    /// </summary>
    [HttpPost("from-report/{reportId:guid}")]
    public async Task<IActionResult> CreateFromReport(
        Guid reportId, [FromBody] CreateFromReportDto? dto, CancellationToken ct)
    {
        var (tenant, user) = await ResolveContextAsync(_db, ct);
        var deny = RequirePermission(user, RbacPermission.TeachingCasesManage);
        if (deny is not null) return deny;

        var report = await _db.Reports.FirstOrDefaultAsync(
            r => r.Id == reportId && r.TenantId == tenant.Id, ct);
        if (report is null) return NotFound();

        // Identifiers known for this study. They are passed to the scrubber so it
        // can strip them verbatim even when their shape matches no generic pattern.
        var identifiers = new[]
        {
            report.Study.AccessionNumber,
            report.Study.PatientReference,
        };

        string Scrub(string? text) => TeachingCaseDeidentifier.Scrub(text, identifiers);

        var row = new TeachingCase
        {
            TenantId = tenant.Id,
            CreatedByUserId = user.Id,
            Title = Scrub(string.IsNullOrWhiteSpace(dto?.Title)
                ? $"{report.Study.Modality} {report.Study.BodyPart}".Trim()
                : dto!.Title),
            // Modality and body part are study *classification*, never patient
            // identity — they are copied straight across so the library filters work.
            Modality = report.Study.Modality,
            BodyPart = report.Study.BodyPart,
            Diagnosis = Scrub(dto?.Diagnosis),
            TeachingPoints = Scrub(dto?.TeachingPoints),
            ClinicalHistory = Scrub(report.Indication),
            FindingsText = Scrub(report.Findings),
            ImpressionText = Scrub(report.Impression),
            Tags = NormalizeTags(dto?.Tags),
            Difficulty = dto?.Difficulty ?? TeachingDifficulty.Intermediate,
            SourceReportId = report.Id,
            Visibility = TeachingVisibility.Private,
        };

        if (string.IsNullOrWhiteSpace(row.Title))
            row.Title = "Untitled teaching case";

        // Post-condition. A scrubber regression must fail loudly here rather than
        // quietly writing an identifier into the teaching library.
        foreach (var field in new[] { row.Title, row.Diagnosis, row.TeachingPoints, row.ClinicalHistory, row.FindingsText, row.ImpressionText })
        {
            if (TeachingCaseDeidentifier.ContainsAny(field, identifiers))
            {
                await AuditAsync(tenant, user, AuditAction.PolicyViolation, new
                {
                    reason = "teaching_case_deidentification_failed",
                    reportId = report.Id,
                }, ct);
                return StatusCode(StatusCodes.Status500InternalServerError, new
                {
                    error = "De-identification did not fully scrub the source report; the teaching case was not created.",
                    kind = "deidentification_failed",
                });
            }
        }

        _db.TeachingCases.Add(row);
        await _db.SaveChangesAsync(ct);
        await AuditAsync(tenant, user, AuditAction.TeachingCaseCreated, new
        {
            teachingCaseId = row.Id,
            sourceReportId = report.Id,
            deidentified = true,
            origin = "report",
        }, ct);

        return CreatedAtAction(nameof(Get), new { id = row.Id }, Shape(row, user));
    }

    [HttpPatch("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateTeachingCaseDto dto, CancellationToken ct)
    {
        var (tenant, user) = await ResolveContextAsync(_db, ct);
        var deny = RequirePermission(user, RbacPermission.TeachingCasesManage);
        if (deny is not null) return deny;
        if (dto is null) return BadRequest(new { error = "A body is required.", kind = "validation" });

        var row = await _db.TeachingCases.FirstOrDefaultAsync(
            c => c.Id == id && c.TenantId == tenant.Id, ct);
        if (row is null) return NotFound();
        var forbid = RequireOwnerOrAdmin(row, user, "edit");
        if (forbid is not null) return forbid;

        if (dto.Title is not null) row.Title = TeachingCaseDeidentifier.Scrub(dto.Title);
        if (dto.Modality is not null) row.Modality = dto.Modality.Trim();
        if (dto.BodyPart is not null) row.BodyPart = dto.BodyPart.Trim();
        if (dto.Diagnosis is not null) row.Diagnosis = TeachingCaseDeidentifier.Scrub(dto.Diagnosis);
        if (dto.TeachingPoints is not null) row.TeachingPoints = TeachingCaseDeidentifier.Scrub(dto.TeachingPoints);
        if (dto.ClinicalHistory is not null) row.ClinicalHistory = TeachingCaseDeidentifier.Scrub(dto.ClinicalHistory);
        if (dto.FindingsText is not null) row.FindingsText = TeachingCaseDeidentifier.Scrub(dto.FindingsText);
        if (dto.ImpressionText is not null) row.ImpressionText = TeachingCaseDeidentifier.Scrub(dto.ImpressionText);
        if (dto.Tags is not null) row.Tags = NormalizeTags(dto.Tags);
        if (dto.Difficulty is not null) row.Difficulty = dto.Difficulty.Value;
        row.UpdatedAt = DateTimeOffset.UtcNow;

        await _db.SaveChangesAsync(ct);
        return Ok(Shape(row, user));
    }

    /// <summary>TF-007 — opt-in publication to the tenant library.</summary>
    [HttpPost("{id:guid}/publish")]
    public Task<IActionResult> Publish(Guid id, CancellationToken ct) =>
        SetVisibilityAsync(id, TeachingVisibility.Tenant, ct);

    /// <summary>TF-007 — withdraw a published case back to private.</summary>
    [HttpPost("{id:guid}/unpublish")]
    public Task<IActionResult> Unpublish(Guid id, CancellationToken ct) =>
        SetVisibilityAsync(id, TeachingVisibility.Private, ct);

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var (tenant, user) = await ResolveContextAsync(_db, ct);
        var deny = RequirePermission(user, RbacPermission.TeachingCasesManage);
        if (deny is not null) return deny;

        var row = await _db.TeachingCases.FirstOrDefaultAsync(
            c => c.Id == id && c.TenantId == tenant.Id, ct);
        if (row is null) return NotFound();
        var forbid = RequireOwnerOrAdmin(row, user, "delete");
        if (forbid is not null) return forbid;

        _db.TeachingCases.Remove(row);
        await _db.SaveChangesAsync(ct);
        await AuditAsync(tenant, user, AuditAction.TeachingCaseDeleted, new
        {
            teachingCaseId = row.Id,
            wasPublished = row.Visibility == TeachingVisibility.Tenant,
        }, ct);
        return NoContent();
    }

    private async Task<IActionResult> SetVisibilityAsync(
        Guid id, TeachingVisibility visibility, CancellationToken ct)
    {
        var (tenant, user) = await ResolveContextAsync(_db, ct);
        var deny = RequirePermission(user, RbacPermission.TeachingCasesManage);
        if (deny is not null) return deny;

        var row = await _db.TeachingCases.FirstOrDefaultAsync(
            c => c.Id == id && c.TenantId == tenant.Id, ct);
        if (row is null) return NotFound();
        var forbid = RequireOwnerOrAdmin(
            row, user, visibility == TeachingVisibility.Tenant ? "publish" : "unpublish");
        if (forbid is not null) return forbid;

        row.Visibility = visibility;
        row.PublishedAt = visibility == TeachingVisibility.Tenant ? DateTimeOffset.UtcNow : null;
        row.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(ct);

        await AuditAsync(tenant, user,
            visibility == TeachingVisibility.Tenant
                ? AuditAction.TeachingCasePublished
                : AuditAction.TeachingCaseUnpublished,
            new { teachingCaseId = row.Id, visibility = visibility.ToString() }, ct);

        return Ok(Shape(row, user));
    }

    private IActionResult? RequireOwnerOrAdmin(TeachingCase row, User user, string verb)
    {
        if (row.CreatedByUserId == user.Id || IsLibraryAdmin(user)) return null;
        return new ObjectResult(new
        {
            error = $"Only the author of a teaching case, or a library administrator, may {verb} it.",
            kind = "forbidden",
        })
        { StatusCode = StatusCodes.Status403Forbidden };
    }

    private Task AuditAsync(Tenant tenant, User user, AuditAction action, object details, CancellationToken ct) =>
        _audit.AppendAsync(new AuditEvent
        {
            TenantId = tenant.Id,
            UserId = user.Id,
            Action = action,
            DetailsJson = System.Text.Json.JsonSerializer.Serialize(details),
        }, ct);

    /// <summary>Trim, de-duplicate, and lower-case the CSV tag list.</summary>
    private static string NormalizeTags(string? tags)
    {
        if (string.IsNullOrWhiteSpace(tags)) return "";
        var parts = tags
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(t => t.ToLowerInvariant())
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        return string.Join(",", parts);
    }

    /// <summary>
    /// Wire shape. <c>sourceReportId</c> is exposed only to the author (and a
    /// library admin): to every other reader the case is a standalone
    /// de-identified artifact with no link back to a patient's study.
    /// </summary>
    private static object Shape(TeachingCase c, User viewer)
    {
        var privileged = c.CreatedByUserId == viewer.Id || IsLibraryAdmin(viewer);
        return new
        {
            c.Id,
            c.Title,
            c.Modality,
            c.BodyPart,
            c.Diagnosis,
            c.TeachingPoints,
            c.ClinicalHistory,
            c.FindingsText,
            c.ImpressionText,
            c.Tags,
            difficulty = (int)c.Difficulty,
            difficultyName = c.Difficulty.ToString(),
            visibility = (int)c.Visibility,
            visibilityName = c.Visibility.ToString(),
            c.PublishedAt,
            c.ViewCount,
            sourceReportId = privileged ? c.SourceReportId : null,
            isOwner = c.CreatedByUserId == viewer.Id,
            canEdit = privileged,
            c.CreatedAt,
            c.UpdatedAt,
        };
    }
}
