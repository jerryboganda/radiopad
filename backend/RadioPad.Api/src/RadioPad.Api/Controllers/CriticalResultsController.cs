using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RadioPad.Application.Abstractions;
using RadioPad.Application.Security;
using RadioPad.Domain.Entities;
using RadioPad.Domain.Enums;
using RadioPad.Infrastructure.Persistence;

namespace RadioPad.Api.Controllers;

/// <summary>
/// PRD §14.15 (CR-001..010) — PowerScribe-style critical-results communication
/// tracking. A radiologist logs a critical finding against a report, records the
/// call/secure message to the ordering clinician, captures the read-back
/// acknowledgement, and closes the loop. A deadline is derived from the
/// criticality class (<see cref="CriticalResult.DeadlineFor"/>) and an
/// un-communicated result that blows it is escalated — manually here, or by the
/// background sweep in <c>CriticalResultEscalationService</c>.
///
/// Safety boundaries: RadioPad never communicates or acknowledges on the
/// clinician's behalf — every transition below is an explicit human action.
/// Every transition appends an append-only audit row via
/// <see cref="IAuditLog.AppendAsync"/>, and audit details deliberately carry the
/// ids/criticality/method only, never the finding narrative. Every query is
/// filtered by the resolved tenant, so a cross-tenant id is a 404, not a leak.
/// </summary>
[ApiController]
[Route("api/critical-results")]
public class CriticalResultsController : TenantedController
{
    private readonly RadioPadDbContext _db;
    private readonly IAuditLog _audit;

    public CriticalResultsController(RadioPadDbContext db, IAuditLog audit)
    {
        _db = db;
        _audit = audit;
    }

    public record CreateCriticalResultDto(
        Guid ReportId, string? Criticality, string? FindingSummary, string? Notes);

    public record CommunicateDto(string? CommunicatedTo, string? Method, string? Notes);

    public record AcknowledgeDto(string? AcknowledgedBy, string? Notes);

    public record NoteOnlyDto(string? Notes);

    /// <summary>Wire shape for one critical result. Enums are serialised as their names so the client never depends on ordinals.</summary>
    public record CriticalResultDto(
        Guid Id,
        Guid ReportId,
        string Criticality,
        string Status,
        string FindingSummary,
        string? CommunicatedTo,
        string? CommunicationMethod,
        DateTimeOffset? CommunicatedAt,
        string? AcknowledgedBy,
        DateTimeOffset? AcknowledgedAt,
        DateTimeOffset DueAt,
        DateTimeOffset? EscalatedAt,
        DateTimeOffset? ClosedAt,
        string Notes,
        bool IsOverdue,
        DateTimeOffset CreatedAt,
        DateTimeOffset UpdatedAt);

    /// <summary>
    /// "Overdue" means the communication loop is still open (never communicated)
    /// AND the criticality deadline has lapsed. A result communicated late is no
    /// longer chased by the queue — it is already the audit trail's problem.
    /// </summary>
    private static bool IsOverdue(CriticalResult c, DateTimeOffset now) =>
        (c.Status == CriticalResultStatus.Open || c.Status == CriticalResultStatus.Escalated)
        && c.DueAt < now;

    private static CriticalResultDto ToDto(CriticalResult c, DateTimeOffset now) => new(
        c.Id,
        c.ReportId,
        c.Criticality.ToString(),
        c.Status.ToString(),
        c.FindingSummary,
        c.CommunicatedTo,
        c.CommunicationMethod?.ToString(),
        c.CommunicatedAt,
        c.AcknowledgedBy,
        c.AcknowledgedAt,
        c.DueAt,
        c.EscalatedAt,
        c.ClosedAt,
        c.Notes,
        IsOverdue(c, now),
        c.CreatedAt,
        c.UpdatedAt);

    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery] string? status,
        [FromQuery] string? criticality,
        [FromQuery] Guid? reportId,
        [FromQuery] bool? overdue,
        [FromQuery] int take = 200,
        CancellationToken ct = default)
    {
        var (tenant, user) = await ResolveContextAsync(_db, ct);
        var deny = RequirePermission(user, RbacPermission.CriticalResultsRead);
        if (deny is not null) return deny;

        take = Math.Clamp(take, 1, 500);
        var now = DateTimeOffset.UtcNow;

        var query = _db.CriticalResults.AsNoTracking().Where(c => c.TenantId == tenant.Id);

        if (!string.IsNullOrWhiteSpace(status))
        {
            if (!Enum.TryParse<CriticalResultStatus>(status, ignoreCase: true, out var parsedStatus))
                return BadRequest(new { error = $"Unknown status '{status}'.", kind = "invalid_status" });
            query = query.Where(c => c.Status == parsedStatus);
        }

        if (!string.IsNullOrWhiteSpace(criticality))
        {
            if (!Enum.TryParse<Criticality>(criticality, ignoreCase: true, out var parsedCriticality))
                return BadRequest(new { error = $"Unknown criticality '{criticality}'.", kind = "invalid_criticality" });
            query = query.Where(c => c.Criticality == parsedCriticality);
        }

        if (reportId is not null)
            query = query.Where(c => c.ReportId == reportId);

        if (overdue == true)
        {
            query = query.Where(c =>
                (c.Status == CriticalResultStatus.Open || c.Status == CriticalResultStatus.Escalated)
                && c.DueAt < now);
        }

        // Most urgent first: soonest deadline at the top, newest as the tiebreak.
        var items = await query
            .OrderBy(c => c.DueAt)
            .ThenByDescending(c => c.CreatedAt)
            .Take(take)
            .ToListAsync(ct);

        return Ok(items.Select(c => ToDto(c, now)).ToList());
    }

    /// <summary>PRD §14.15 (CR-007) — everything past its communication deadline that was never communicated.</summary>
    [HttpGet("overdue")]
    public async Task<IActionResult> Overdue([FromQuery] int take = 200, CancellationToken ct = default)
    {
        var (tenant, user) = await ResolveContextAsync(_db, ct);
        var deny = RequirePermission(user, RbacPermission.CriticalResultsRead);
        if (deny is not null) return deny;

        take = Math.Clamp(take, 1, 500);
        var now = DateTimeOffset.UtcNow;

        var items = await _db.CriticalResults.AsNoTracking()
            .Where(c => c.TenantId == tenant.Id
                && (c.Status == CriticalResultStatus.Open || c.Status == CriticalResultStatus.Escalated)
                && c.DueAt < now)
            .OrderBy(c => c.DueAt)
            .Take(take)
            .ToListAsync(ct);

        return Ok(items.Select(c => ToDto(c, now)).ToList());
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
    {
        var (tenant, user) = await ResolveContextAsync(_db, ct);
        var deny = RequirePermission(user, RbacPermission.CriticalResultsRead);
        if (deny is not null) return deny;

        var found = await _db.CriticalResults.AsNoTracking()
            .FirstOrDefaultAsync(c => c.TenantId == tenant.Id && c.Id == id, ct);
        if (found is null) return NotFound(new { error = "Critical result not found.", kind = "not_found" });

        return Ok(ToDto(found, DateTimeOffset.UtcNow));
    }

    /// <summary>PRD §14.15 (CR-001) — log a critical finding against a report in this tenant.</summary>
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateCriticalResultDto dto, CancellationToken ct)
    {
        var (tenant, user) = await ResolveContextAsync(_db, ct);
        var deny = RequirePermission(user, RbacPermission.CriticalResultsManage);
        if (deny is not null) return deny;

        if (!Enum.TryParse<Criticality>(dto.Criticality, ignoreCase: true, out var criticality))
            return BadRequest(new { error = $"Unknown criticality '{dto.Criticality}'.", kind = "invalid_criticality" });

        var summary = (dto.FindingSummary ?? "").Trim();
        if (summary.Length == 0)
            return BadRequest(new { error = "A finding summary is required.", kind = "missing_finding_summary" });

        // Tenant isolation: the report must belong to the resolved tenant. A report
        // id from another tenant is indistinguishable from one that does not exist.
        var reportExists = await _db.Reports
            .AnyAsync(r => r.TenantId == tenant.Id && r.Id == dto.ReportId, ct);
        if (!reportExists)
            return NotFound(new { error = "Report not found.", kind = "report_not_found" });

        var now = DateTimeOffset.UtcNow;
        var entity = new CriticalResult
        {
            TenantId = tenant.Id,
            ReportId = dto.ReportId,
            Criticality = criticality,
            FindingSummary = summary,
            Status = CriticalResultStatus.Open,
            DueAt = now + CriticalResult.DeadlineFor(criticality),
            CreatedAt = now,
            UpdatedAt = now,
            Notes = "",
        };
        entity.Notes = AppendNote(entity.Notes, dto.Notes, user, now);

        _db.CriticalResults.Add(entity);
        await _db.SaveChangesAsync(ct);

        await AuditAsync(tenant, user, entity, AuditAction.CriticalResultCreated, new
        {
            criticalResultId = entity.Id,
            reportId = entity.ReportId,
            criticality = entity.Criticality.ToString(),
            dueAt = entity.DueAt,
        }, ct);

        return Ok(ToDto(entity, now));
    }

    /// <summary>PRD §14.15 (CR-004) — record that the ordering/referring clinician was notified.</summary>
    [HttpPost("{id:guid}/communicate")]
    public async Task<IActionResult> Communicate(Guid id, [FromBody] CommunicateDto dto, CancellationToken ct)
    {
        var (tenant, user) = await ResolveContextAsync(_db, ct);
        var deny = RequirePermission(user, RbacPermission.CriticalResultsManage);
        if (deny is not null) return deny;

        var entity = await FindInTenantAsync(tenant.Id, id, ct);
        if (entity is null) return NotFound(new { error = "Critical result not found.", kind = "not_found" });
        if (entity.Status == CriticalResultStatus.Closed)
            return Conflict(new { error = "This critical result is already closed.", kind = "already_closed" });

        if (!Enum.TryParse<CriticalCommunicationMethod>(dto.Method, ignoreCase: true, out var method))
            return BadRequest(new { error = $"Unknown communication method '{dto.Method}'.", kind = "invalid_method" });

        var recipient = (dto.CommunicatedTo ?? "").Trim();
        if (recipient.Length == 0)
            return BadRequest(new { error = "Record who the result was communicated to.", kind = "missing_recipient" });

        var now = DateTimeOffset.UtcNow;
        entity.Status = CriticalResultStatus.Communicated;
        entity.CommunicatedTo = recipient;
        entity.CommunicationMethod = method;
        entity.CommunicatedAt = now;
        entity.UpdatedAt = now;
        entity.Notes = AppendNote(entity.Notes, dto.Notes, user, now);
        await _db.SaveChangesAsync(ct);

        await AuditAsync(tenant, user, entity, AuditAction.CriticalResultCommunicated, new
        {
            criticalResultId = entity.Id,
            reportId = entity.ReportId,
            criticality = entity.Criticality.ToString(),
            method = method.ToString(),
            communicatedTo = recipient,
            onTime = now <= entity.DueAt,
        }, ct);

        return Ok(ToDto(entity, now));
    }

    /// <summary>PRD §14.15 (CR-005) — capture the receiver's read-back acknowledgement.</summary>
    [HttpPost("{id:guid}/acknowledge")]
    public async Task<IActionResult> Acknowledge(Guid id, [FromBody] AcknowledgeDto dto, CancellationToken ct)
    {
        var (tenant, user) = await ResolveContextAsync(_db, ct);
        var deny = RequirePermission(user, RbacPermission.CriticalResultsManage);
        if (deny is not null) return deny;

        var entity = await FindInTenantAsync(tenant.Id, id, ct);
        if (entity is null) return NotFound(new { error = "Critical result not found.", kind = "not_found" });
        if (entity.Status == CriticalResultStatus.Closed)
            return Conflict(new { error = "This critical result is already closed.", kind = "already_closed" });
        // The loop cannot be acknowledged before it was communicated — an
        // acknowledgement with no recorded communication is not a closed loop.
        if (entity.CommunicatedAt is null)
            return Conflict(new { error = "Record the communication before acknowledging it.", kind = "not_communicated" });

        var now = DateTimeOffset.UtcNow;
        entity.Status = CriticalResultStatus.Acknowledged;
        entity.AcknowledgedBy = string.IsNullOrWhiteSpace(dto.AcknowledgedBy)
            ? entity.CommunicatedTo
            : dto.AcknowledgedBy.Trim();
        entity.AcknowledgedAt = now;
        entity.UpdatedAt = now;
        entity.Notes = AppendNote(entity.Notes, dto.Notes, user, now);
        await _db.SaveChangesAsync(ct);

        await AuditAsync(tenant, user, entity, AuditAction.CriticalResultAcknowledged, new
        {
            criticalResultId = entity.Id,
            reportId = entity.ReportId,
            criticality = entity.Criticality.ToString(),
            acknowledgedBy = entity.AcknowledgedBy,
        }, ct);

        return Ok(ToDto(entity, now));
    }

    /// <summary>PRD §14.15 (CR-007) — escalate a result whose loop is still open.</summary>
    [HttpPost("{id:guid}/escalate")]
    public async Task<IActionResult> Escalate(Guid id, [FromBody] NoteOnlyDto? dto, CancellationToken ct)
    {
        var (tenant, user) = await ResolveContextAsync(_db, ct);
        var deny = RequirePermission(user, RbacPermission.CriticalResultsManage);
        if (deny is not null) return deny;

        var entity = await FindInTenantAsync(tenant.Id, id, ct);
        if (entity is null) return NotFound(new { error = "Critical result not found.", kind = "not_found" });
        if (entity.Status is CriticalResultStatus.Closed or CriticalResultStatus.Acknowledged)
            return Conflict(new { error = "This critical result is already resolved.", kind = "already_resolved" });

        var now = DateTimeOffset.UtcNow;
        entity.Status = CriticalResultStatus.Escalated;
        entity.EscalatedAt = now;
        entity.UpdatedAt = now;
        entity.Notes = AppendNote(entity.Notes, dto?.Notes, user, now);
        await _db.SaveChangesAsync(ct);

        await AuditAsync(tenant, user, entity, AuditAction.CriticalResultEscalated, new
        {
            criticalResultId = entity.Id,
            reportId = entity.ReportId,
            criticality = entity.Criticality.ToString(),
            reason = "manual",
            dueAt = entity.DueAt,
        }, ct);

        return Ok(ToDto(entity, now));
    }

    /// <summary>PRD §14.15 (CR-008) — close the loop out. Terminal.</summary>
    [HttpPost("{id:guid}/close")]
    public async Task<IActionResult> Close(Guid id, [FromBody] NoteOnlyDto? dto, CancellationToken ct)
    {
        var (tenant, user) = await ResolveContextAsync(_db, ct);
        var deny = RequirePermission(user, RbacPermission.CriticalResultsManage);
        if (deny is not null) return deny;

        var entity = await FindInTenantAsync(tenant.Id, id, ct);
        if (entity is null) return NotFound(new { error = "Critical result not found.", kind = "not_found" });
        if (entity.Status == CriticalResultStatus.Closed)
            return Conflict(new { error = "This critical result is already closed.", kind = "already_closed" });

        var now = DateTimeOffset.UtcNow;
        entity.Status = CriticalResultStatus.Closed;
        entity.ClosedAt = now;
        entity.UpdatedAt = now;
        entity.Notes = AppendNote(entity.Notes, dto?.Notes, user, now);
        await _db.SaveChangesAsync(ct);

        await AuditAsync(tenant, user, entity, AuditAction.CriticalResultClosed, new
        {
            criticalResultId = entity.Id,
            reportId = entity.ReportId,
            criticality = entity.Criticality.ToString(),
            acknowledged = entity.AcknowledgedAt is not null,
        }, ct);

        return Ok(ToDto(entity, now));
    }

    private Task<CriticalResult?> FindInTenantAsync(Guid tenantId, Guid id, CancellationToken ct) =>
        _db.CriticalResults.FirstOrDefaultAsync(c => c.TenantId == tenantId && c.Id == id, ct);

    private static string AppendNote(string existing, string? note, User user, DateTimeOffset at)
    {
        var trimmed = (note ?? "").Trim();
        if (trimmed.Length == 0) return existing;
        var line = $"[{at:O}] {user.Email}: {trimmed}";
        return string.IsNullOrWhiteSpace(existing) ? line : existing + "\n" + line;
    }

    /// <summary>
    /// Append-only audit for one state change. Details carry ids + workflow
    /// metadata ONLY — the finding narrative never reaches the audit log.
    /// </summary>
    private Task AuditAsync(
        Tenant tenant, User user, CriticalResult entity, AuditAction action, object detail, CancellationToken ct) =>
        _audit.AppendAsync(new AuditEvent
        {
            TenantId = tenant.Id,
            UserId = user.Id,
            ReportId = entity.ReportId,
            Action = action,
            DetailsJson = JsonSerializer.Serialize(detail),
        }, ct);
}
