using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using RadioPad.Application.Abstractions;

namespace RadioPad.Infrastructure.Providers.Ubag;

/// <summary>
/// 2026-07-11 UBAG hardening — operator alerting for the two conditions that
/// need a HUMAN (UBAG policy forbids automated web-AI login): a provider's
/// browser session flipping to logged-out, and the gateway itself being
/// unreachable. Holds the banner state surfaced by <c>GET /api/ubag/status</c>
/// and sends a throttled operator email (at most one per provider per
/// <see cref="EmailThrottle"/>; operator decision 2026-07-11). Singleton — the
/// state must survive across the scoped discovery sweeps that feed it.
/// </summary>
public sealed class UbagOperatorAlertService
{
    public static readonly TimeSpan EmailThrottle = TimeSpan.FromDays(1);

    /// <summary>Operator inbox for alerts; alerts are banner-only when unset.</summary>
    public const string OperatorEmailEnvVar = "RADIOPAD_OPERATOR_ALERT_EMAIL";

    private readonly IEmailSender _email;
    private readonly ILogger<UbagOperatorAlertService> _log;
    private readonly ConcurrentDictionary<string, DateTimeOffset> _loggedOutSince = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, DateTimeOffset> _lastEmailByTarget = new(StringComparer.OrdinalIgnoreCase);
    private DateTimeOffset? _gatewayUnreachableSince;
    private DateTimeOffset _lastUnreachableWarn = DateTimeOffset.MinValue;

    public UbagOperatorAlertService(IEmailSender email, ILogger<UbagOperatorAlertService> log)
    {
        _email = email;
        _log = log;
    }

    /// <summary>Targets currently logged out, with when the sweep first saw it.</summary>
    public IReadOnlyDictionary<string, DateTimeOffset> LoggedOutTargets =>
        new Dictionary<string, DateTimeOffset>(_loggedOutSince, StringComparer.OrdinalIgnoreCase);

    /// <summary>Set while discovery sweeps cannot reach the gateway; null when healthy.</summary>
    public DateTimeOffset? GatewayUnreachableSince => _gatewayUnreachableSince;

    /// <summary>
    /// Called by every discovery sweep with the gateway's reachability. A flip
    /// to unreachable logs at WARNING (throttled to once per 15 min so a
    /// multi-day outage is visible without flooding); recovery logs once.
    /// </summary>
    public void RecordGatewayState(bool reachable)
    {
        if (reachable)
        {
            if (_gatewayUnreachableSince is { } since)
                _log.LogInformation("UBAG gateway reachable again (was unreachable since {Since:u})", since);
            _gatewayUnreachableSince = null;
            return;
        }

        _gatewayUnreachableSince ??= DateTimeOffset.UtcNow;
        if (DateTimeOffset.UtcNow - _lastUnreachableWarn >= TimeSpan.FromMinutes(15))
        {
            _lastUnreachableWarn = DateTimeOffset.UtcNow;
            _log.LogWarning(
                "UBAG gateway unreachable since {Since:u} — UBAG providers will not serve until it returns",
                _gatewayUnreachableSince);
        }
    }

    /// <summary>Clears the logged-out banner state for a target that authenticated again.</summary>
    public void RecordTargetAuthenticated(string targetId)
    {
        if (_loggedOutSince.TryRemove(targetId, out var since))
            _log.LogInformation("UBAG target {Target} authenticated again (was logged out since {Since:u})", targetId, since);
    }

    /// <summary>
    /// Records that a previously-serving target is now logged out (a discovery
    /// sweep just disabled its provider row) and emails the operator, throttled
    /// per target. The audit event is appended by the caller — it owns the
    /// tenant scope; this service owns the global banner + email state.
    /// </summary>
    public async Task NotifyTargetLoggedOutAsync(string targetId, CancellationToken ct)
    {
        var since = _loggedOutSince.GetOrAdd(targetId, DateTimeOffset.UtcNow);

        var last = _lastEmailByTarget.GetValueOrDefault(targetId, DateTimeOffset.MinValue);
        var now = DateTimeOffset.UtcNow;
        if (now - last < EmailThrottle) return;
        if (!_lastEmailByTarget.TryAdd(targetId, now) && !_lastEmailByTarget.TryUpdate(targetId, now, last))
            return; // another sweep won the race — its email suffices

        var to = Environment.GetEnvironmentVariable(OperatorEmailEnvVar);
        if (string.IsNullOrWhiteSpace(to))
        {
            _log.LogWarning(
                "UBAG target {Target} logged out since {Since:u} — set {EnvVar} to receive operator emails",
                targetId, since, OperatorEmailEnvVar);
            return;
        }

        try
        {
            var subject = $"[RadioPad] UBAG provider '{targetId}' needs re-login";
            var body =
                $"<p>The UBAG browser session for <b>{targetId}</b> is logged out (detected {since:u}).</p>" +
                "<p>RadioPad has stopped routing AI traffic to it; auto-routed requests fail over to the " +
                "next-ranked provider. To restore it, open the UBAG browser viewer (noVNC) and log the " +
                "provider back in — discovery re-enables it automatically within 5 minutes.</p>" +
                $"<p>This alert is throttled to one email per provider per {EmailThrottle.TotalHours:0} h.</p>";
            var sent = await _email.SendAsync(new EmailMessage(to.Trim(), subject, body), ct);
            if (!sent)
                _log.LogWarning("Operator alert email for UBAG target {Target} was not sent (sender returned false)", targetId);
        }
        catch (Exception ex)
        {
            // Alerting must never break the sweep; the Hub banner still shows it.
            _log.LogWarning(ex, "Failed to send operator alert email for UBAG target {Target}", targetId);
        }
    }
}
