using System.Text.Json;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using RadioPad.Api.Services;
using RadioPad.Application.Abstractions;
using RadioPad.Application.Services.Push;
using RadioPad.Domain.Entities;
using RadioPad.Domain.Enums;
using RadioPad.Infrastructure.Persistence;

namespace RadioPad.Api.Jobs;

/// <summary>
/// PR-N4 (NOTIF-003 subset / NOTIF-004 tiers) — out-of-app channel delivery for a
/// single notification. Enqueued (NOT recurring) by <c>NotificationProducer.InsertAsync</c>
/// on the <c>critical</c> queue, for <b>Critical-urgency notifications only</b>, as two
/// independent jobs (<see cref="DeliverPushAsync"/> + <see cref="DeliverEmailAsync"/>) so a
/// slow email can never delay a push and each retries on its own.
///
/// PHI tiers (NOTIF-004 — see <see cref="NotificationPhiTier"/>): the in-app row Body may
/// carry a modality/body-part/FindingSummary-class descriptor, but every channel here is
/// stricter — Title is a generic category phrase and Body is AT MOST modality+body-part,
/// NEVER the FindingSummary, accession, patient name, or MRN. Both delivery methods derive
/// their strings from <see cref="NotificationPhiTier"/> so the two channels can never drift.
///
/// Both methods re-check the recipient's <see cref="NotificationPreference"/>
/// (PushEnabled / EmailEnabled) before sending. A missing preference row falls back to the
/// entity defaults (push on, email off).
///
/// Retry: a not-configured push sender is a CONFIG error, not transient — it audits
/// <see cref="AuditAction.NotificationDeliveryFailed"/> and does NOT retry. A transport
/// failure is allowed to throw so PR-N1's global <c>JitteredRetryAttribute</c> re-runs it.
///
/// Channel switch seam (future SMS sender): the per-device <c>Platform</c> switch inside
/// <see cref="DeliverPushAsync"/> (via <see cref="PushSenderRegistry"/>) is where an
/// <c>ISmsSender</c> would slot in — no SMS sender exists in the codebase today, so SMS is
/// deliberately out of scope here.
///
/// SIEM (NOTIF-008): nothing to dispatch — every audit row this job writes is already
/// streamed to the tenant SIEM by <c>SiemPushService</c>; there is no separate SIEM channel.
///
/// Registered as a singleton (holds only <see cref="IServiceScopeFactory"/> + logger and
/// opens its own scope per delivery). Under Testing no Hangfire server runs, so the producer's
/// enqueue is a no-op; tests drive <see cref="DeliverPushAsync"/> / <see cref="DeliverEmailAsync"/>
/// directly.
/// </summary>
[Queue(HangfireSetup.QueueCritical)]
public sealed class NotificationChannelDispatchJob
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<NotificationChannelDispatchJob> _log;

    public NotificationChannelDispatchJob(
        IServiceScopeFactory scopeFactory, ILogger<NotificationChannelDispatchJob> log)
    {
        _scopeFactory = scopeFactory;
        _log = log;
    }

    /// <summary>
    /// Mobile push (NOTIF-003). Only when the recipient's pref allows push. Looks up their
    /// registered devices, resolves the platform sender, and sends a PHI-minimised payload.
    /// A not-configured sender audits a delivery failure and does NOT retry (config, not
    /// transient); a transport error is re-thrown so the global jittered-retry filter re-runs.
    /// </summary>
    public async Task DeliverPushAsync(Guid notificationId, CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RadioPadDbContext>();

        var n = await db.Notifications.AsNoTracking().FirstOrDefaultAsync(x => x.Id == notificationId, ct);
        if (n is null) return;

        var pref = await db.NotificationPreferences.AsNoTracking()
            .FirstOrDefaultAsync(p => p.TenantId == n.TenantId && p.UserId == n.UserId, ct);
        // Missing pref → entity default (push on).
        if (!(pref?.PushEnabled ?? true)) return;

        var devices = await db.PushDevices.AsNoTracking()
            .Where(d => d.TenantId == n.TenantId && d.UserId == n.UserId)
            .ToListAsync(ct);
        if (devices.Count == 0) return;

        var registry = scope.ServiceProvider.GetRequiredService<PushSenderRegistry>();
        var payload = new PushPayload(
            NotificationPhiTier.SafeTitle(n),
            NotificationPhiTier.SafeBody(n),
            n.Category.ToString().ToLowerInvariant(),
            notificationId.ToString());

        foreach (var device in devices)
        {
            var sender = registry.Resolve(device.Platform);
            if (sender is null)
            {
                // No sender registered for this platform — a config gap, not transient.
                await AuditDeliveryFailedAsync(scope, n, "push", device.Platform, "sender_not_configured", ct);
                continue;
            }

            try
            {
                await sender.SendAsync(device.Token, payload, ct);
            }
            catch (PushNotConfiguredException)
            {
                // Credentials/env not configured — a config error, not transient: audit and
                // move on WITHOUT re-throwing, so Hangfire does not burn retries on it.
                await AuditDeliveryFailedAsync(scope, n, "push", device.Platform, "sender_not_configured", ct);
            }
            // Any OTHER exception (transport/HTTP) intentionally propagates so the global
            // JitteredRetryAttribute re-runs the whole delivery.
        }
    }

    /// <summary>
    /// Email (NOTIF-003). Only when the recipient's pref allows email. Sends a non-PHI summary
    /// plus an "Open RadioPad" call-to-action — no deep content (NOTIF-004 email-preview
    /// doctrine: email previews default to non-PHI). Best-effort: a false/failed send logs but
    /// does not throw (email is a courtesy channel; the in-app row + SSE already delivered).
    /// </summary>
    public async Task DeliverEmailAsync(Guid notificationId, CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RadioPadDbContext>();

        var n = await db.Notifications.AsNoTracking().FirstOrDefaultAsync(x => x.Id == notificationId, ct);
        if (n is null) return;

        var pref = await db.NotificationPreferences.AsNoTracking()
            .FirstOrDefaultAsync(p => p.TenantId == n.TenantId && p.UserId == n.UserId, ct);
        // Missing pref → entity default (email off).
        if (!(pref?.EmailEnabled ?? false)) return;

        var to = await db.Users.AsNoTracking()
            .Where(u => u.TenantId == n.TenantId && u.Id == n.UserId)
            .Select(u => u.Email)
            .FirstOrDefaultAsync(ct);
        if (string.IsNullOrWhiteSpace(to)) return;

        var subject = NotificationPhiTier.SafeTitle(n);
        var summary = NotificationPhiTier.SafeBody(n);
        var html = $"<p>{System.Net.WebUtility.HtmlEncode(summary)}</p><p>Open RadioPad to review.</p>";
        var plain = $"{summary}\n\nOpen RadioPad to review.";

        try
        {
            var sender = scope.ServiceProvider.GetRequiredService<IEmailSender>();
            var sent = await sender.SendAsync(new EmailMessage(to.Trim(), subject, html, plain), ct);
            if (!sent)
                _log.LogWarning("notification email for {NotificationId} was not sent (sender returned false)", notificationId);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Email is best-effort — never bubble a courtesy-channel failure into a retry storm.
            _log.LogWarning(ex, "notification email delivery failed for {NotificationId}", notificationId);
        }
    }

    /// <summary>
    /// NOTIF-008 — a delivery failure is audited (append-only) with workflow metadata only:
    /// notification id, channel, platform, reason. Never Title/Body/finding text.
    /// </summary>
    private static async Task AuditDeliveryFailedAsync(
        IServiceScope scope, Notification n, string channel, string platform, string reason, CancellationToken ct)
    {
        var audit = scope.ServiceProvider.GetRequiredService<IAuditLog>();
        await audit.AppendAsync(new AuditEvent
        {
            TenantId = n.TenantId,
            UserId = n.UserId,
            Action = AuditAction.NotificationDeliveryFailed,
            DetailsJson = JsonSerializer.Serialize(new
            {
                notificationId = n.Id,
                channel,
                platform,
                reason,
            }),
        }, ct);
    }
}
