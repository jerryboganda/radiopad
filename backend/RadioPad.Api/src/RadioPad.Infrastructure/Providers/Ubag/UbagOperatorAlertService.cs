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

    /// <summary>
    /// Targets whose recent real traffic is ALL failures (the router's
    /// circuit-breaker signal). Tracked separately from login state because the
    /// gateway's topology login_state can be synthetic — real traffic failing
    /// is the trustworthy signal.
    /// </summary>
    public IReadOnlyDictionary<string, DateTimeOffset> FailingTargets =>
        new Dictionary<string, DateTimeOffset>(_failingSince, StringComparer.OrdinalIgnoreCase);

    private readonly ConcurrentDictionary<string, DateTimeOffset> _failingSince = new(StringComparer.OrdinalIgnoreCase);

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
    /// <summary>
    /// Rehydrates login-lost state from the tenant audit trail after a restart
    /// (audit fix 2026-07-18: this state was process-memory only, so restarts
    /// cleared Hub banners, lost "since" timestamps, and re-armed the 1/day email
    /// throttle — duplicating operator emails during multi-day outages). Seeds
    /// both maps only when absent; live sweep signals always win, and
    /// <see cref="RecordTargetAuthenticated"/> clears a stale entry on the first
    /// sweep that sees the target authenticated again.
    /// </summary>
    public void RehydrateLoginLost(string targetId, DateTimeOffset since, DateTimeOffset lastAlertAt)
    {
        _loggedOutSince.TryAdd(targetId, since);
        _lastEmailByTarget.TryAdd(targetId, lastAlertAt);
    }

    public Task NotifyTargetLoggedOutAsync(string targetId, CancellationToken ct)
    {
        var since = _loggedOutSince.GetOrAdd(targetId, DateTimeOffset.UtcNow);
        return SendThrottledAlertAsync(
            targetId,
            $"[RadioPad] UBAG provider '{targetId}' needs re-login",
            $"<p>The UBAG browser session for <b>{targetId}</b> is logged out (detected {since:u}).</p>" +
            "<p>RadioPad has stopped routing AI traffic to it; auto-routed requests fail over to the " +
            "next-ranked provider. To restore it, open the UBAG browser viewer (noVNC) and log the " +
            "provider back in — discovery re-enables it automatically within 5 minutes.</p>",
            $"UBAG target {targetId} logged out since {since:u}",
            ct);
    }

    /// <summary>
    /// Records that a target's recent REAL traffic is all failures (router
    /// circuit-breaker signal) and emails the operator, throttled per target.
    /// This fires even when the gateway's login_state reporting is fiction —
    /// failing traffic is the ground truth.
    /// </summary>
    public Task NotifyTargetFailingAsync(string targetId, CancellationToken ct)
    {
        var since = _failingSince.GetOrAdd(targetId, DateTimeOffset.UtcNow);
        return SendThrottledAlertAsync(
            targetId,
            $"[RadioPad] UBAG provider '{targetId}' is failing all requests",
            $"<p>Every recent AI request to <b>{targetId}</b> has failed (since {since:u}).</p>" +
            "<p>The routing circuit breaker has excluded it, and auto-routed requests fail over to the " +
            "next-ranked provider. Likely causes: an expired login (check the UBAG browser viewer over " +
            "noVNC), a drifted provider UI, or the browser worker being wedged. It re-enters rotation " +
            "automatically once a request succeeds.</p>",
            $"UBAG target {targetId} failing all recent requests since {since:u}",
            ct);
    }

    /// <summary>Clears the failing banner once a target's traffic recovers.</summary>
    public void RecordTargetRecovered(string targetId)
    {
        if (_failingSince.TryRemove(targetId, out var since))
            _log.LogInformation("UBAG target {Target} serving again (was failing since {Since:u})", targetId, since);
    }

    private async Task SendThrottledAlertAsync(
        string targetId, string subject, string bodyHtml, string logLine, CancellationToken ct)
    {
        var last = _lastEmailByTarget.GetValueOrDefault(targetId, DateTimeOffset.MinValue);
        var now = DateTimeOffset.UtcNow;
        if (now - last < EmailThrottle) return;
        if (!_lastEmailByTarget.TryAdd(targetId, now) && !_lastEmailByTarget.TryUpdate(targetId, now, last))
            return; // another sweep won the race — its email suffices

        var to = Environment.GetEnvironmentVariable(OperatorEmailEnvVar);
        if (string.IsNullOrWhiteSpace(to))
        {
            _log.LogWarning("{LogLine} — set {EnvVar} to receive operator emails", logLine, OperatorEmailEnvVar);
            return;
        }

        try
        {
            var body = bodyHtml +
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
