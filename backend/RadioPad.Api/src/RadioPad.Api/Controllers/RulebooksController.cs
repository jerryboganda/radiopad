using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RadioPad.Application.Abstractions;
using RadioPad.Application.Security;
using RadioPad.Domain.Entities;
using RadioPad.Domain.Enums;
using RadioPad.Infrastructure.Persistence;
using RadioPad.Validation.Engine;
using RadioPad.Validation.Rulebook;

namespace RadioPad.Api.Controllers;

[ApiController]
[Route("api/rulebooks")]
public class RulebooksController : TenantedController
{
    private readonly RadioPadDbContext _db;
    private readonly IRulebookStore _store;
    private readonly IAuditLog _audit;
    private readonly ReportValidator _validator;
    private readonly INotificationProducer _producer;
    private readonly ILogger<RulebooksController> _log;

    public RulebooksController(
        RadioPadDbContext db, IRulebookStore store, IAuditLog audit, ReportValidator validator,
        INotificationProducer producer, ILogger<RulebooksController> log)
    {
        _db = db;
        _store = store;
        _audit = audit;
        _validator = validator;
        _producer = producer;
        _log = log;
    }

    /// <summary>
    /// Fans a RulebookApproval/Info notification to every RulebooksManage holder (excluding the
    /// acting user). Wrapped + logged so a producer failure never fails the rulebook transition.
    /// Rulebook.Owner is a free-text string, not a resolvable user — so only permission holders
    /// are notified, never the "owner".
    /// </summary>
    private async Task NotifyRulebookManagersAsync(
        Guid tenantId, Guid actorUserId, Guid rulebookRowId, string title, string dedupeKey, CancellationToken ct)
    {
        try
        {
            await _producer.NotifyPermissionHoldersAsync(
                tenantId, RbacPermission.RulebooksManage, excludeUserId: actorUserId,
                uid => new NotificationDraft(
                    tenantId, uid, NotificationCategory.RulebookApproval, NotificationUrgency.Info,
                    title, "A validation rulebook changed status.", "/rulebooks",
                    "rulebook", rulebookRowId, RequiresAck: false, DedupeKey: dedupeKey), ct);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "rulebook manager notification fan-out failed for {RulebookRowId}", rulebookRowId);
        }
    }

    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        var (tenant, _) = await ResolveContextAsync(_db, ct);
        var items = await _store.ListAsync(tenant.Id, ct);
        return Ok(items.Select(r => new
        {
            r.Id, r.RulebookId, r.Name, r.Version, r.Owner, r.Status,
            r.AppliesToModalities, r.AppliesToBodyParts, r.UpdatedAt,
        }));
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
    {
        var (tenant, _) = await ResolveContextAsync(_db, ct);
        var rb = await _db.Rulebooks.FirstOrDefaultAsync(r => r.Id == id && r.TenantId == tenant.Id, ct);
        return rb is null ? NotFound() : Ok(rb);
    }

    public record SaveRulebookDto(string Yaml);

    [HttpPost]
    public async Task<IActionResult> Save([FromBody] SaveRulebookDto dto, CancellationToken ct)
    {
        var (tenant, user) = await ResolveContextAsync(_db, ct);
        var deny = RequirePermission(user, RbacPermission.RulebooksManage);
        if (deny is not null) return deny;
        RulebookSpec spec;
        try { spec = RulebookSpec.FromYaml(dto.Yaml); }
        catch (Exception ex) { return BadRequest(new { error = $"Invalid YAML: {ex.Message}" }); }
        if (string.IsNullOrEmpty(spec.RulebookId))
            return BadRequest(new { error = "rulebook_id is required." });

        var rb = new Rulebook
        {
            TenantId = tenant.Id,
            RulebookId = spec.RulebookId,
            Name = spec.Name,
            Version = spec.Version,
            Owner = spec.Owner,
            Status = spec.Status.Equals("approved", StringComparison.OrdinalIgnoreCase)
                ? RulebookStatus.Approved : RulebookStatus.Draft,
            SourceYaml = dto.Yaml,
            CompiledJson = System.Text.Json.JsonSerializer.Serialize(spec),
            AppliesToModalities = string.Join(',', spec.AppliesTo.Modalities),
            AppliesToBodyParts = string.Join(',', spec.AppliesTo.BodyParts),
        };
        await _store.SaveAsync(rb, ct);
        return Ok(rb);
    }

    public record ValidateDto(string Yaml);

    [HttpPost("validate")]
    public IActionResult ValidateYaml([FromBody] ValidateDto dto)
    {
        try
        {
            var spec = RulebookSpec.FromYaml(dto.Yaml);
            var problems = new List<string>();
            if (string.IsNullOrEmpty(spec.RulebookId)) problems.Add("rulebook_id is required");
            if (string.IsNullOrEmpty(spec.Name)) problems.Add("name is required");
            if (string.IsNullOrEmpty(spec.Version)) problems.Add("version is required");
            if (spec.RequiredSections.Count == 0) problems.Add("required_sections must list at least one section");
            return Ok(new { ok = problems.Count == 0, problems, spec });
        }
        catch (Exception ex)
        {
            return Ok(new { ok = false, problems = new[] { ex.Message } });
        }
    }

    public record GoldenCase(string Name, Report Report, string[] ExpectFlagged);
    public record TestRunDto(string Yaml, GoldenCase[] Cases);

    [HttpPost("test")]
    public IActionResult RunTests([FromBody] TestRunDto dto)
    {
        var spec = RulebookSpec.FromYaml(dto.Yaml);
        var results = dto.Cases.Select(c =>
        {
            var v = _validator.Validate(c.Report, spec);
            var actual = v.Findings.Select(f => f.RuleId).Distinct().ToArray();
            var missing = c.ExpectFlagged.Except(actual).ToArray();
            var unexpected = actual.Except(c.ExpectFlagged).ToArray();
            return new
            {
                c.Name,
                pass = missing.Length == 0 && unexpected.Length == 0,
                missing,
                unexpected,
                findings = v.Findings,
            };
        }).ToArray();
        return Ok(new { passed = results.Count(r => r.pass), total = results.Length, results });
    }

    [HttpPost("{id:guid}/approve")]
    public async Task<IActionResult> Approve(Guid id, CancellationToken ct)
    {
        var (tenant, user) = await ResolveContextAsync(_db, ct);
        var deny = RequirePermission(user, RbacPermission.RulebooksApprove);
        if (deny is not null) return deny;
        var rb = await _db.Rulebooks.FirstOrDefaultAsync(r => r.Id == id && r.TenantId == tenant.Id, ct);
        if (rb is null) return NotFound();
        rb.Status = RulebookStatus.Approved;
        rb.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(ct);
        await _audit.AppendAsync(new AuditEvent
        {
            TenantId = tenant.Id,
            UserId = user.Id,
            Action = AuditAction.RulebookApproved,
            DetailsJson = System.Text.Json.JsonSerializer.Serialize(new { rb.Id, rb.RulebookId, rb.Version }),
        }, ct);
        await NotifyRulebookManagersAsync(tenant.Id, user.Id, rb.Id, "Rulebook approved", $"rb-approve:{rb.Id}", ct);
        return Ok(rb);
    }

    [HttpPost("{id:guid}/deprecate")]
    public async Task<IActionResult> Deprecate(Guid id, CancellationToken ct)
    {
        var (tenant, user) = await ResolveContextAsync(_db, ct);
        var deny = RequirePermission(user, RbacPermission.RulebooksApprove);
        if (deny is not null) return deny;
        var rb = await _db.Rulebooks.FirstOrDefaultAsync(r => r.Id == id && r.TenantId == tenant.Id, ct);
        if (rb is null) return NotFound();
        rb.Status = RulebookStatus.Deprecated;
        rb.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(ct);
        await _audit.AppendAsync(new AuditEvent
        {
            TenantId = tenant.Id,
            UserId = user.Id,
            Action = AuditAction.RulebookDeprecated,
            DetailsJson = System.Text.Json.JsonSerializer.Serialize(new { rb.Id, rb.RulebookId, rb.Version }),
        }, ct);
        await NotifyRulebookManagersAsync(tenant.Id, user.Id, rb.Id, "Rulebook deprecated", $"rb-deprecate:{rb.Id}", ct);
        return Ok(rb);
    }

    public record RollbackDto(string Version);

    /// <summary>
    /// PRD RB-008: roll back to a prior approved version of the same
    /// <c>RulebookId</c>. The prior version must already exist in the tenant
    /// and have <see cref="RulebookStatus.Approved"/>. We do not mutate
    /// historical rows; instead we materialise a new approved row whose
    /// <c>Version</c> is the rolled-back version with a "+rollback" suffix.
    /// </summary>
    [HttpPost("{id:guid}/rollback")]
    public async Task<IActionResult> Rollback(Guid id, [FromBody] RollbackDto dto, CancellationToken ct)
    {
        var (tenant, user) = await ResolveContextAsync(_db, ct);
        var deny = RequirePermission(user, RbacPermission.RulebooksApprove);
        if (deny is not null) return deny;
        var current = await _db.Rulebooks.FirstOrDefaultAsync(r => r.Id == id && r.TenantId == tenant.Id, ct);
        if (current is null) return NotFound();
        var prior = await _db.Rulebooks.FirstOrDefaultAsync(
            r => r.TenantId == tenant.Id
                && r.RulebookId == current.RulebookId
                && r.Version == dto.Version
                && r.Status == RulebookStatus.Approved, ct);
        if (prior is null)
            return BadRequest(new { error = "Prior approved version not found.", kind = "validation" });

        var copy = new Rulebook
        {
            TenantId = tenant.Id,
            RulebookId = prior.RulebookId,
            Name = prior.Name,
            Version = $"{prior.Version}+rollback-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}",
            Owner = prior.Owner,
            Status = RulebookStatus.Approved,
            SourceYaml = prior.SourceYaml,
            CompiledJson = prior.CompiledJson,
            AppliesToModalities = prior.AppliesToModalities,
            AppliesToBodyParts = prior.AppliesToBodyParts,
        };
        _db.Rulebooks.Add(copy);
        await _db.SaveChangesAsync(ct);
        await _audit.AppendAsync(new AuditEvent
        {
            TenantId = tenant.Id,
            UserId = user.Id,
            Action = AuditAction.RulebookApproved,
            DetailsJson = System.Text.Json.JsonSerializer.Serialize(new
            {
                rolledBackFromId = current.Id,
                rolledBackToVersion = prior.Version,
                newVersion = copy.Version,
            }),
        }, ct);
        await NotifyRulebookManagersAsync(tenant.Id, user.Id, copy.Id, "Rulebook rolled back", $"rb-rollback:{copy.Id}", ct);
        return Ok(copy);
    }
}
