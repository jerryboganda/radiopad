using System.Collections.Concurrent;
using System.Text.Json;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using RadioPad.Api.Jobs;
using RadioPad.Application.Abstractions;
using RadioPad.Application.Security;
using RadioPad.Domain.Entities;
using RadioPad.Domain.Enums;
using RadioPad.Infrastructure.Persistence;

namespace RadioPad.Api.Services;

/// <summary>
/// NOTIF-001 — the single <see cref="INotificationProducer"/>. A singleton hosted
/// service that (a) drains terminal AI-job bus events into AiJob-category
/// notifications and (b) is called directly by workflow call sites (PR-N4) to
/// produce category-specific notifications.
///
/// Every produce runs on its OWN service scope with an UNCANCELLED write token
/// (mirroring the durable-job <c>MarkTerminalDbAsync</c> doctrine): the inbox row
/// and its audit must land even when the originating request's token is already
/// cancelled. A produce writes the row, audits <see cref="AuditAction.NotificationCreated"/>
/// (workflow metadata only — never Title/Body), and publishes to the SSE bus.
///
/// Suppression paths (all return <c>null</c>): a muted category for a non-critical
/// class (NOTIF-009); a DedupeKey collision (idempotency); the per-recipient storm
/// cap (60/min) which coalesces overflow into one System/Warning row. DND
/// (NOTIF-007) never suppresses the row/SSE — it only records which dispatch
/// channels would be held (channel dispatch itself is PR-N4).
/// </summary>
public sealed class NotificationProducer : INotificationProducer, IHostedService
{
    private const int StormCapPerMinute = 60;
    private static readonly TimeSpan StormWindow = TimeSpan.FromSeconds(60);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IAiJobEventBus _bus;
    private readonly ILogger<NotificationProducer> _log;

    // Per-(tenant,user) sliding window of recent produce timestamps for the storm guard.
    private readonly ConcurrentDictionary<(Guid Tenant, Guid User), Queue<DateTimeOffset>> _stormWindows = new();

    private readonly CancellationTokenSource _cts = new();
    private IAiJobEventSubscription? _subscription;
    private Task? _consumeLoop;

    public NotificationProducer(IServiceScopeFactory scopeFactory, IAiJobEventBus bus, ILogger<NotificationProducer> log)
    {
        _scopeFactory = scopeFactory;
        _bus = bus;
        _log = log;
    }

    // ---- IHostedService: terminal AI-job bus subscription ------------------

    public Task StartAsync(CancellationToken cancellationToken)
    {
        // Firehose (null,null) — the producer is an internal, cross-tenant consumer.
        _subscription = _bus.Subscribe(null, null);
        _consumeLoop = Task.Run(() => ConsumeLoopAsync(_cts.Token));
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _cts.Cancel();
        _subscription?.Dispose();
        if (_consumeLoop is not null)
        {
            try { await _consumeLoop.WaitAsync(cancellationToken); }
            catch (OperationCanceledException) { /* shutdown */ }
            catch (Exception ex) { _log.LogWarning(ex, "notification producer consume loop faulted on shutdown"); }
        }
    }

    private async Task ConsumeLoopAsync(CancellationToken ct)
    {
        var reader = _subscription!.Reader;
        try
        {
            while (await reader.WaitToReadAsync(ct))
            {
                while (reader.TryRead(out var evt))
                {
                    try { await HandleBusEventAsync(evt, ct); }
                    catch (Exception ex)
                    {
                        // A producer failure must never take the bus loop down.
                        _log.LogWarning(ex, "notification producer failed to handle a bus event");
                    }
                }
            }
        }
        catch (OperationCanceledException) { /* shutdown */ }
    }

    /// <summary>
    /// Terminal AI-job → AiJob-category notification. Only <c>ok</c>/<c>error</c> job
    /// events map to a row (a user-initiated <c>cancelled</c> would be self-notification
    /// noise). Body carries NO clinical text — the bus payload does not carry
    /// modality/body-part, so Body stays empty (NOTIF-004).
    /// </summary>
    private async Task HandleBusEventAsync(AiJobBusEvent evt, CancellationToken ct)
    {
        if (evt.EventType != "job") return;

        using var doc = JsonSerializer.SerializeToDocument(evt.Payload);
        var root = doc.RootElement;

        var status = TryGetString(root, "status");
        if (status is not ("ok" or "error")) return;

        if (!TryGetGuid(root, "tenantId", out var tenantId) || !TryGetGuid(root, "userId", out var userId))
            return; // pre-PR-N3 payloads without routing ids — nothing to produce
        if (!TryGetGuid(root, "jobId", out var jobId)) return;
        TryGetGuid(root, "reportId", out var reportId);
        var kind = TryGetString(root, "kind") ?? "";
        var mode = TryGetString(root, "mode") ?? "";

        var ok = status == "ok";
        var descriptor = DescribeJob(kind, mode);
        var title = ok ? $"AI {descriptor} ready" : $"AI {descriptor} failed";
        var link = ok
            ? $"/reports/view?id={reportId}&aiJob={jobId}"
            : $"/reports/view?id={reportId}";

        await CreateAsync(new NotificationDraft(
            tenantId, userId, NotificationCategory.AiJob,
            ok ? NotificationUrgency.Info : NotificationUrgency.Warning,
            title, Body: "",
            LinkHref: link, SourceKind: "aiJob", SourceId: jobId,
            RequiresAck: false, DedupeKey: $"aijob:{jobId}:{status}"), ct);
    }

    private static string DescribeJob(string kind, string mode) =>
        string.Equals(kind, "generate", StringComparison.OrdinalIgnoreCase)
            ? "report generation"
            : (string.IsNullOrWhiteSpace(mode) ? "task" : mode);

    // ---- INotificationProducer --------------------------------------------

    public async Task<Notification?> CreateAsync(NotificationDraft draft, CancellationToken ct)
    {
        // Storm guard: register this attempt; over the per-minute cap we swap the real
        // draft for one coalescing System/Warning row (deduped per minute bucket).
        if (RegisterAndCheckStorm(draft.TenantId, draft.UserId, out var minuteBucket))
        {
            draft = new NotificationDraft(
                draft.TenantId, draft.UserId, NotificationCategory.System, NotificationUrgency.Warning,
                Title: "Notifications paused",
                Body: "Additional notifications were suppressed to keep your inbox readable.",
                LinkHref: "/notifications",
                SourceKind: "system",
                DedupeKey: $"storm:{draft.TenantId:N}:{draft.UserId:N}:{minuteBucket}");
        }

        return await InsertAsync(draft, ct);
    }

    public async Task NotifyPermissionHoldersAsync(
        Guid tenantId, RbacPermission permission, Guid? excludeUserId,
        Func<Guid, NotificationDraft> draftFor, CancellationToken ct)
    {
        var roles = RolePermissionMap.RolesFor(permission).ToArray();
        if (roles.Length == 0) return;

        List<Guid> recipients;
        using (var scope = _scopeFactory.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RadioPadDbContext>();
            recipients = await db.Users
                .Where(u => u.TenantId == tenantId && u.IsActive && roles.Contains(u.Role))
                .Select(u => u.Id)
                .ToListAsync(CancellationToken.None);
        }

        foreach (var uid in recipients)
        {
            if (excludeUserId is Guid ex && ex == uid) continue;
            await CreateAsync(draftFor(uid), ct);
        }
    }

    public async Task NotifyRoleAcrossTenantsAsync(
        UserRole role, Func<Guid, Guid, NotificationDraft> draftFor, CancellationToken ct)
    {
        List<(Guid TenantId, Guid UserId)> recipients;
        using (var scope = _scopeFactory.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RadioPadDbContext>();
            var rows = await db.Users
                .Where(u => u.IsActive && u.Role == role)
                .Select(u => new { u.TenantId, u.Id })
                .ToListAsync(CancellationToken.None);
            recipients = rows.Select(r => (r.TenantId, r.Id)).ToList();
        }

        foreach (var (tenantId, userId) in recipients)
            await CreateAsync(draftFor(tenantId, userId), ct);
    }

    // ---- write path -------------------------------------------------------

    private async Task<Notification?> InsertAsync(NotificationDraft draft, CancellationToken ct)
    {
        // Uncancelled write token — the row + audit must land even if the caller's token
        // has already been cancelled (MarkTerminalDbAsync doctrine).
        var writeCt = CancellationToken.None;

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RadioPadDbContext>();
        var audit = scope.ServiceProvider.GetRequiredService<IAuditLog>();

        var pref = await db.NotificationPreferences
            .FirstOrDefaultAsync(p => p.TenantId == draft.TenantId && p.UserId == draft.UserId, writeCt);
        var criticalCsv = await db.Tenants
            .Where(t => t.Id == draft.TenantId)
            .Select(t => t.CriticalNotificationCategoriesCsv)
            .FirstOrDefaultAsync(writeCt) ?? "";

        // NOTIF-009 — Critical urgency OR a tenant-designated critical category is never
        // suppressible by a mute (and bypasses DND below).
        var isCriticalClass = draft.Urgency == NotificationUrgency.Critical
            || CsvContains(criticalCsv, draft.Category.ToString());

        if (!isCriticalClass && pref is not null && CsvContains(pref.MutedCategoriesCsv, draft.Category.ToString()))
            return null; // muted non-critical category — suppress the in-app row entirely

        // Dedupe pre-check (portable across SQLite + Npgsql, and avoids a wasted INSERT);
        // the unique filtered index below is the race backstop.
        if (draft.DedupeKey is not null)
        {
            var exists = await db.Notifications.AnyAsync(
                n => n.TenantId == draft.TenantId && n.UserId == draft.UserId && n.DedupeKey == draft.DedupeKey, writeCt);
            if (exists) return null;
        }

        // NOTIF-007 DND — never suppresses the row/SSE; only the dispatch channels, and only
        // for non-critical classes. Here we compute + record the decision (dispatch is PR-N4).
        var dndSuppressedChannels = (!isCriticalClass && IsWithinDnd(pref))
            ? new[] { "push", "osToast", "email" }
            : Array.Empty<string>();

        var now = DateTimeOffset.UtcNow;
        var notification = new Notification
        {
            TenantId = draft.TenantId,
            UserId = draft.UserId,
            Category = draft.Category,
            Urgency = draft.Urgency,
            Title = draft.Title,
            Body = draft.Body,
            LinkHref = draft.LinkHref,
            SourceKind = draft.SourceKind,
            SourceId = draft.SourceId,
            RequiresAck = draft.RequiresAck,
            DedupeKey = draft.DedupeKey,
            CreatedAt = now,
            UpdatedAt = now,
        };
        db.Notifications.Add(notification);
        try
        {
            await db.SaveChangesAsync(writeCt);
        }
        catch (DbUpdateException) when (draft.DedupeKey is not null)
        {
            return null; // lost the dedupe race against a concurrent producer
        }

        // NOTIF-008 — creation is always audited. Workflow metadata only; never Title/Body.
        await audit.AppendAsync(new AuditEvent
        {
            TenantId = draft.TenantId,
            UserId = draft.UserId,
            Action = AuditAction.NotificationCreated,
            DetailsJson = JsonSerializer.Serialize(new
            {
                notificationId = notification.Id,
                category = draft.Category.ToString(),
                urgency = draft.Urgency.ToString(),
                requiresAck = draft.RequiresAck,
                sourceKind = draft.SourceKind,
                sourceId = draft.SourceId,
                dndSuppressedChannels,
            }),
        }, writeCt);

        _bus.PublishNotification(new NotificationEvent(draft.TenantId, draft.UserId, NotificationView.Of(notification)));

        // NOTIF-003/004 — out-of-app channel dispatch (push + email) is Critical-urgency
        // ONLY, and runs on the Hangfire "critical" queue so a slow/failed sender never
        // blocks the produce or the SSE. IBackgroundJobClient is resolved optionally: under
        // Testing (no Hangfire) it is null and this is a no-op, exactly like the
        // WebhookEnqueueingAuditLog decorator. Each delivery re-checks the recipient's
        // NotificationPreference (PushEnabled/EmailEnabled) before sending. Never throws:
        // an enqueue failure must not fail an already-persisted notification.
        if (notification.Urgency == NotificationUrgency.Critical)
        {
            try
            {
                var jobs = scope.ServiceProvider.GetService<IBackgroundJobClient>();
                if (jobs is not null)
                {
                    var id = notification.Id;
                    jobs.Enqueue<NotificationChannelDispatchJob>(j => j.DeliverPushAsync(id, CancellationToken.None));
                    jobs.Enqueue<NotificationChannelDispatchJob>(j => j.DeliverEmailAsync(id, CancellationToken.None));
                }
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "failed to enqueue notification channel dispatch for {NotificationId}", notification.Id);
            }
        }

        return notification;
    }

    // ---- storm / DND / csv helpers ----------------------------------------

    private bool RegisterAndCheckStorm(Guid tenantId, Guid userId, out long minuteBucket)
    {
        var now = DateTimeOffset.UtcNow;
        minuteBucket = now.ToUnixTimeSeconds() / 60;
        var queue = _stormWindows.GetOrAdd((tenantId, userId), _ => new Queue<DateTimeOffset>());
        lock (queue)
        {
            var cutoff = now - StormWindow;
            while (queue.Count > 0 && queue.Peek() < cutoff) queue.Dequeue();
            queue.Enqueue(now);
            return queue.Count > StormCapPerMinute;
        }
    }

    private static bool IsWithinDnd(NotificationPreference? pref)
    {
        if (pref?.DndStartMinutes is not int start || pref.DndEndMinutes is not int end) return false;
        if (start == end) return false;

        var tz = TimeZoneInfo.Utc;
        if (!string.IsNullOrWhiteSpace(pref.DndTimeZone))
        {
            try { tz = TimeZoneInfo.FindSystemTimeZoneById(pref.DndTimeZone); }
            catch { tz = TimeZoneInfo.Utc; }
        }

        var local = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, tz);
        var nowMin = local.Hour * 60 + local.Minute;
        return start <= end
            ? nowMin >= start && nowMin < end          // same-day window
            : nowMin >= start || nowMin < end;          // overnight window
    }

    private static bool CsvContains(string csv, string value) =>
        !string.IsNullOrEmpty(csv)
        && csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
              .Any(v => string.Equals(v, value, StringComparison.OrdinalIgnoreCase));

    private static string? TryGetString(JsonElement root, string name) =>
        root.TryGetProperty(name, out var el) && el.ValueKind == JsonValueKind.String ? el.GetString() : null;

    private static bool TryGetGuid(JsonElement root, string name, out Guid value)
    {
        value = Guid.Empty;
        return root.TryGetProperty(name, out var el) && el.ValueKind == JsonValueKind.String && el.TryGetGuid(out value);
    }
}

/// <summary>
/// NOTIF-001 — the single camelCase wire shape for a notification row, shared by
/// <c>NotificationsController</c> (REST responses) and <c>NotificationProducer</c>
/// (the SSE bus snapshot) so live and polled shapes never drift. Property names are
/// already camelCase so they survive any serializer naming policy unchanged.
/// </summary>
public static class NotificationView
{
    public static object Of(Notification n) => new
    {
        id = n.Id,
        category = n.Category.ToString(),
        urgency = n.Urgency.ToString(),
        title = n.Title,
        body = n.Body,
        linkHref = n.LinkHref,
        sourceKind = n.SourceKind,
        sourceId = n.SourceId,
        requiresAck = n.RequiresAck,
        readAt = n.ReadAt,
        acknowledgedAt = n.AcknowledgedAt,
        createdAt = n.CreatedAt,
    };
}
