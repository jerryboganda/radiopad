using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RadioPad.Api.Services;
using RadioPad.Application.Abstractions;
using RadioPad.Domain.Entities;
using RadioPad.Domain.Enums;
using RadioPad.Infrastructure.Persistence;

namespace RadioPad.Api.Controllers;

/// <summary>
/// NOTIF-001 — the signed-in user's own notification inbox. Every action resolves
/// the tenant + user context and scopes to own rows
/// (<c>n.TenantId == tenant.Id &amp;&amp; n.UserId == user.Id</c>): a notification is a
/// signed-in right on every surface, so there is NO extra read permission and the
/// endpoints are NOT rate-limited (poll-doctrine). Ack/bulk carry the NOTIF-011
/// confirmation + CriticalResultsRead guards for clinical/compliance rows.
///
/// Audit policy (NOTIF-008): Created/Ack/Bulk are always audited; Read is audited
/// only for <c>RequiresAck</c> or <c>Critical</c> rows (for routine rows the
/// <c>ReadAt</c> column is the record). Details never carry Title/Body text.
/// </summary>
[ApiController]
[Route("api/notifications")]
public class NotificationsController : TenantedController
{
    private readonly RadioPadDbContext _db;
    private readonly IAuditLog _audit;

    public NotificationsController(RadioPadDbContext db, IAuditLog audit)
    {
        _db = db;
        _audit = audit;
    }

    public sealed record BulkRequest(List<Guid>? Ids, string? Action, bool Confirm);

    public sealed record PrefsRequest(
        string? MutedCategoriesCsv, int? DndStartMinutes, int? DndEndMinutes,
        string? DndTimeZone, bool? PushEnabled, bool? EmailEnabled);

    /// <summary>The caller's own notifications, newest first, keyset-paged by CreatedAt ticks.</summary>
    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery] bool? unread,
        [FromQuery] string? category,
        [FromQuery] string? urgency,
        [FromQuery] bool? requiresAck,
        [FromQuery] long? cursor,
        [FromQuery] int limit = 50,
        CancellationToken ct = default)
    {
        var (tenant, user) = await ResolveContextAsync(_db, ct);
        if (limit < 1) limit = 50;
        if (limit > 100) limit = 100;

        var q = _db.Notifications.Where(n => n.TenantId == tenant.Id && n.UserId == user.Id);
        if (unread == true) q = q.Where(n => n.ReadAt == null);
        if (requiresAck == true) q = q.Where(n => n.RequiresAck);
        if (Enum.TryParse<NotificationCategory>(category, ignoreCase: true, out var cat))
            q = q.Where(n => n.Category == cat);
        if (Enum.TryParse<NotificationUrgency>(urgency, ignoreCase: true, out var urg))
            q = q.Where(n => n.Urgency == urg);
        if (cursor is long c)
        {
            var cutoff = new DateTimeOffset(c, TimeSpan.Zero);
            q = q.Where(n => n.CreatedAt < cutoff);
        }

        var rows = await q
            .OrderByDescending(n => n.CreatedAt).ThenByDescending(n => n.Id)
            .Take(limit + 1)
            .ToListAsync(ct);

        string? nextCursor = null;
        if (rows.Count > limit)
        {
            nextCursor = rows[limit - 1].CreatedAt.UtcTicks.ToString();
            rows = rows.Take(limit).ToList();
        }

        return Ok(new { notifications = rows.Select(NotificationView.Of), nextCursor });
    }

    /// <summary>Unread + unacknowledged counts for the bell badge.</summary>
    [HttpGet("unread-count")]
    public async Task<IActionResult> UnreadCount(CancellationToken ct)
    {
        var (tenant, user) = await ResolveContextAsync(_db, ct);
        var own = _db.Notifications.Where(n => n.TenantId == tenant.Id && n.UserId == user.Id);
        var unread = await own.CountAsync(n => n.ReadAt == null, ct);
        var unacked = await own.CountAsync(n => n.RequiresAck && n.AcknowledgedAt == null, ct);
        return Ok(new { unread, unacked });
    }

    /// <summary>Idempotently mark one notification read.</summary>
    [HttpPost("{id:guid}/read")]
    public async Task<IActionResult> MarkRead(Guid id, CancellationToken ct)
    {
        var (tenant, user) = await ResolveContextAsync(_db, ct);
        var n = await OwnAsync(tenant.Id, user.Id, id, ct);
        if (n is null) return NotFound();

        if (n.ReadAt is null)
        {
            var now = DateTimeOffset.UtcNow;
            n.ReadAt = now;
            n.UpdatedAt = now;
            await _db.SaveChangesAsync(ct);

            // NOTIF-008 — read is audited only for RequiresAck / Critical rows.
            if (n.RequiresAck || n.Urgency == NotificationUrgency.Critical)
                await AuditRowAsync(tenant.Id, user.Id, AuditAction.NotificationRead, n, ct);
        }

        return Ok(NotificationView.Of(n));
    }

    /// <summary>Acknowledge a RequiresAck notification (400 <c>not_ackable</c> otherwise).</summary>
    [HttpPost("{id:guid}/ack")]
    public async Task<IActionResult> Acknowledge(Guid id, CancellationToken ct)
    {
        var (tenant, user) = await ResolveContextAsync(_db, ct);
        var n = await OwnAsync(tenant.Id, user.Id, id, ct);
        if (n is null) return NotFound();
        if (!n.RequiresAck)
            return BadRequest(new { error = "This notification does not require acknowledgement.", kind = "not_ackable" });

        if (n.AcknowledgedAt is null)
        {
            var now = DateTimeOffset.UtcNow;
            n.AcknowledgedAt = now;
            n.AcknowledgedByUserId = user.Id;
            if (n.ReadAt is null) n.ReadAt = now;
            n.UpdatedAt = now;
            await _db.SaveChangesAsync(ct);

            // NOTIF-008 — an acknowledgement is always audited.
            await AuditRowAsync(tenant.Id, user.Id, AuditAction.NotificationAcknowledged, n, ct);
        }

        return Ok(NotificationView.Of(n));
    }

    /// <summary>Bulk mark-read / ack over up to 100 of the caller's own notifications.</summary>
    [HttpPost("bulk")]
    public async Task<IActionResult> Bulk([FromBody] BulkRequest req, CancellationToken ct)
    {
        var (tenant, user) = await ResolveContextAsync(_db, ct);

        var ids = (req?.Ids ?? new List<Guid>()).Distinct().ToList();
        if (ids.Count == 0)
            return BadRequest(new { error = "No notification ids supplied.", kind = "invalid" });
        if (ids.Count > 100)
            return BadRequest(new { error = "At most 100 notifications may be updated at once.", kind = "too_many" });

        var action = (req!.Action ?? "").Trim().ToLowerInvariant();
        if (action != "read" && action != "ack")
            return BadRequest(new { error = "action must be 'read' or 'ack'.", kind = "invalid_action" });

        var rows = await _db.Notifications
            .Where(n => n.TenantId == tenant.Id && n.UserId == user.Id && ids.Contains(n.Id))
            .ToListAsync(ct);

        // NOTIF-011 — clinical/compliance rows need explicit confirmation.
        var offending = rows
            .Where(n => n.RequiresAck
                        || n.Category == NotificationCategory.CriticalResult
                        || n.Category == NotificationCategory.System)
            .Select(n => n.Id)
            .ToList();
        if (offending.Count > 0 && !req.Confirm)
            return BadRequest(new
            {
                error = "Some selected notifications require confirmation before this bulk action.",
                kind = "confirmation_required",
                ids = offending,
            });

        // NOTIF-011 — bulk-acking critical-result rows requires CriticalResultsRead.
        if (action == "ack" && rows.Any(n => n.Category == NotificationCategory.CriticalResult))
        {
            var deny = RequirePermission(user, RbacPermission.CriticalResultsRead);
            if (deny is not null) return deny;
        }

        var now = DateTimeOffset.UtcNow;
        var affected = new List<Notification>();
        foreach (var n in rows)
        {
            if (action == "read")
            {
                if (n.ReadAt is null)
                {
                    n.ReadAt = now;
                    n.UpdatedAt = now;
                    affected.Add(n);
                }
            }
            else // ack — only rows that actually require ack are acknowledged
            {
                if (!n.RequiresAck || n.AcknowledgedAt is not null) continue;
                n.AcknowledgedAt = now;
                n.AcknowledgedByUserId = user.Id;
                if (n.ReadAt is null) n.ReadAt = now;
                n.UpdatedAt = now;
                affected.Add(n);
            }
        }
        await _db.SaveChangesAsync(ct);

        // One NotificationBulkAction row for the batch...
        await _audit.AppendAsync(new AuditEvent
        {
            TenantId = tenant.Id,
            UserId = user.Id,
            Action = AuditAction.NotificationBulkAction,
            DetailsJson = JsonSerializer.Serialize(new
            {
                action,
                count = affected.Count,
                ids = affected.Select(n => n.Id),
            }),
        }, ct);
        // ...plus a per-row NotificationAcknowledged for each acked row.
        if (action == "ack")
            foreach (var n in affected)
                await AuditRowAsync(tenant.Id, user.Id, AuditAction.NotificationAcknowledged, n, ct);

        return Ok(new { updated = affected.Count });
    }

    /// <summary>The caller's notification preferences (defaults when none saved yet).</summary>
    [HttpGet("prefs")]
    public async Task<IActionResult> GetPrefs(CancellationToken ct)
    {
        var (tenant, user) = await ResolveContextAsync(_db, ct);
        var pref = await _db.NotificationPreferences
            .FirstOrDefaultAsync(p => p.TenantId == tenant.Id && p.UserId == user.Id, ct);
        return Ok(PrefsView(pref));
    }

    /// <summary>Upsert the caller's preferences (400 <c>mandatory_category</c> on muting a critical class).</summary>
    [HttpPut("prefs")]
    public async Task<IActionResult> PutPrefs([FromBody] PrefsRequest req, CancellationToken ct)
    {
        var (tenant, user) = await ResolveContextAsync(_db, ct);

        var muted = ParseCsv(req?.MutedCategoriesCsv)
            .Where(m => Enum.TryParse<NotificationCategory>(m, ignoreCase: true, out _))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        // NOTIF-009 — critical classes are mandatory; they may never be muted.
        var mandatory = muted
            .Where(m => CsvContains(tenant.CriticalNotificationCategoriesCsv, m))
            .ToList();
        if (mandatory.Count > 0)
            return BadRequest(new
            {
                error = "These notification categories are mandatory and cannot be muted.",
                kind = "mandatory_category",
                categories = mandatory,
            });

        var pref = await _db.NotificationPreferences
            .FirstOrDefaultAsync(p => p.TenantId == tenant.Id && p.UserId == user.Id, ct);
        if (pref is null)
        {
            pref = new NotificationPreference { TenantId = tenant.Id, UserId = user.Id };
            _db.NotificationPreferences.Add(pref);
        }

        pref.MutedCategoriesCsv = string.Join(",", muted);
        pref.DndStartMinutes = req?.DndStartMinutes;
        pref.DndEndMinutes = req?.DndEndMinutes;
        pref.DndTimeZone = req?.DndTimeZone ?? "";
        if (req?.PushEnabled is bool push) pref.PushEnabled = push;
        if (req?.EmailEnabled is bool email) pref.EmailEnabled = email;
        pref.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(ct);

        return Ok(PrefsView(pref));
    }

    // ---- helpers ----------------------------------------------------------

    private Task<Notification?> OwnAsync(Guid tenantId, Guid userId, Guid id, CancellationToken ct) =>
        _db.Notifications.FirstOrDefaultAsync(
            n => n.Id == id && n.TenantId == tenantId && n.UserId == userId, ct);

    private Task AuditRowAsync(Guid tenantId, Guid userId, AuditAction action, Notification n, CancellationToken ct) =>
        _audit.AppendAsync(new AuditEvent
        {
            TenantId = tenantId,
            UserId = userId,
            Action = action,
            DetailsJson = JsonSerializer.Serialize(new
            {
                notificationId = n.Id,
                category = n.Category.ToString(),
                urgency = n.Urgency.ToString(),
                requiresAck = n.RequiresAck,
                sourceKind = n.SourceKind,
                sourceId = n.SourceId,
            }),
        }, ct);

    private static object PrefsView(NotificationPreference? p) => new
    {
        mutedCategoriesCsv = p?.MutedCategoriesCsv ?? "",
        dndStartMinutes = p?.DndStartMinutes,
        dndEndMinutes = p?.DndEndMinutes,
        dndTimeZone = p?.DndTimeZone ?? "",
        pushEnabled = p?.PushEnabled ?? true,
        emailEnabled = p?.EmailEnabled ?? false,
    };

    private static IEnumerable<string> ParseCsv(string? csv) =>
        string.IsNullOrWhiteSpace(csv)
            ? Array.Empty<string>()
            : csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static bool CsvContains(string csv, string value) =>
        !string.IsNullOrEmpty(csv)
        && csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
              .Any(v => string.Equals(v, value, StringComparison.OrdinalIgnoreCase));
}
