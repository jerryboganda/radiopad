using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RadioPad.Application.Abstractions;
using RadioPad.Domain.Entities;
using RadioPad.Domain.Enums;
using RadioPad.Infrastructure.Persistence;

namespace RadioPad.Api.Services;

/// <summary>
/// Iter-35 PERF-004 — in-process synthetic availability monitor. Probes a
/// configurable set of relative paths against the local backend (default
/// <c>/api/health/ready</c>) at a configurable cadence and maintains a
/// 5-minute rolling success/failure window.
///
/// When the failure rate within the window exceeds
/// <c>RADIOPAD_AVAILABILITY_BURN_RATE_THRESHOLD</c> (default 5%) the
/// service appends an <see cref="AuditAction.SystemAlert"/> row with
/// <c>kind = "availability_burn_rate"</c>, attributed to the tenant
/// referenced by <c>RADIOPAD_AVAILABILITY_AUDIT_TENANT</c> (no audit row
/// is written when that env var is unset). Alerts are de-duplicated to
/// at most one per rolling window so a sustained burn does not flood
/// the audit log.
///
/// Probe targets are intentionally restricted to platform health
/// endpoints — never to PHI-bearing routes — so the synthetic monitor
/// can never leak patient data.
/// </summary>
public sealed class AvailabilityMonitorService : BackgroundService
{
    /// <summary>Named HTTP client used by the availability prober.</summary>
    public const string HttpClientName = "availability";

    /// <summary>Rolling window over which failure rate is computed.</summary>
    public static readonly TimeSpan WindowSize = TimeSpan.FromMinutes(5);

    public static readonly Histogram<double> ProbeDurationMs = PerfBudgets.Meter.CreateHistogram<double>(
        name: "radiopad.availability.probe.duration_ms",
        unit: "ms",
        description: "Synthetic availability probe wall-clock duration (tagged target, outcome).");

    public static readonly Counter<long> ProbeSuccess = PerfBudgets.Meter.CreateCounter<long>(
        name: "radiopad.availability.probe.success",
        unit: "1",
        description: "Synthetic availability probe outcome counter (tagged target, outcome).");

    private readonly IHttpClientFactory _httpFactory;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly AvailabilitySnapshotProvider _snapshot;
    private readonly ILogger<AvailabilityMonitorService> _log;

    private readonly TimeSpan _interval;
    private readonly double _burnThreshold;
    private readonly string[] _targets;
    private readonly string? _auditTenantSlug;

    private readonly object _gate = new();
    private readonly Queue<ProbeEvent> _events = new();
    private DateTimeOffset _lastAlertAt = DateTimeOffset.MinValue;

    public AvailabilityMonitorService(
        IHttpClientFactory httpFactory,
        IServiceScopeFactory scopeFactory,
        IAvailabilitySnapshotProvider snapshot,
        IConfiguration config,
        ILogger<AvailabilityMonitorService> log)
    {
        _httpFactory = httpFactory;
        _scopeFactory = scopeFactory;
        _snapshot = (AvailabilitySnapshotProvider)snapshot;
        _log = log;

        var intervalSec = ParseInt(config["RADIOPAD_AVAILABILITY_PROBE_INTERVAL_SEC"], 30);
        if (intervalSec < 1) intervalSec = 30;
        _interval = TimeSpan.FromSeconds(intervalSec);

        _burnThreshold = ParseDouble(config["RADIOPAD_AVAILABILITY_BURN_RATE_THRESHOLD"], 0.05);
        if (_burnThreshold < 0) _burnThreshold = 0.05;

        var rawTargets = config["RADIOPAD_AVAILABILITY_PROBE_TARGETS"];
        _targets = string.IsNullOrWhiteSpace(rawTargets)
            ? new[] { "/api/health/ready" }
            : rawTargets.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        var auditTenant = config["RADIOPAD_AVAILABILITY_AUDIT_TENANT"];
        _auditTenantSlug = string.IsNullOrWhiteSpace(auditTenant) ? null : auditTenant.Trim();

        _snapshot.Set(new AvailabilitySnapshot(
            WindowSec: (int)WindowSize.TotalSeconds,
            TotalProbes: 0,
            ErrorCount: 0,
            ErrorRate: 0,
            LastCheckedAt: null,
            Targets: _targets));
    }

    /// <summary>Effective probe targets (read-only).</summary>
    public IReadOnlyList<string> Targets => _targets;

    /// <summary>Effective burn-rate threshold (0..1).</summary>
    public double BurnRateThreshold => _burnThreshold;

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        // Wait for the API to fully warm up before issuing the first probe so
        // we don't false-alarm on the cold start of the integration tests.
        try { await Task.Delay(TimeSpan.FromSeconds(5), ct); }
        catch (OperationCanceledException) { return; }

        while (!ct.IsCancellationRequested)
        {
            try
            {
                await ProbeOnceAsync(ct);
            }
            catch (OperationCanceledException) { return; }
            catch (Exception ex)
            {
                _log.LogError(ex, "Availability probe pass failed");
            }

            try { await Task.Delay(_interval, ct); }
            catch (OperationCanceledException) { return; }
        }
    }

    /// <summary>
    /// Issues one round of probes (one HTTP GET per target). Visible to
    /// integration tests so we can drive the loop deterministically.
    /// </summary>
    internal async Task ProbeOnceAsync(CancellationToken ct)
    {
        var client = _httpFactory.CreateClient(HttpClientName);
        if (client.BaseAddress is null)
        {
            var bind = Environment.GetEnvironmentVariable("RADIOPAD_BIND") ?? "http://127.0.0.1:7457";
            // RADIOPAD_BIND may be a bare host:port — normalise to a URL.
            if (!bind.Contains("://", StringComparison.Ordinal)) bind = "http://" + bind;
            client.BaseAddress = new Uri(bind);
        }

        foreach (var target in _targets)
        {
            if (ct.IsCancellationRequested) return;
            var sw = Stopwatch.StartNew();
            bool ok;
            try
            {
                using var resp = await client.GetAsync(target, HttpCompletionOption.ResponseHeadersRead, ct);
                ok = (int)resp.StatusCode >= 200 && (int)resp.StatusCode < 300;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                _log.LogDebug(ex, "Availability probe error for {Target}", target);
                ok = false;
            }
            sw.Stop();
            await RecordOutcomeAsync(target, ok, sw.Elapsed.TotalMilliseconds, ct);
        }
    }

    /// <summary>
    /// Records a single probe outcome on the rolling window, emits the
    /// metrics, refreshes the snapshot, and writes a burn-rate audit row
    /// when the threshold is exceeded. Internal so tests can drive the
    /// pipeline without standing up the HTTP loop.
    /// </summary>
    internal async Task RecordOutcomeAsync(string target, bool success, double durationMs, CancellationToken ct)
    {
        var outcome = success ? "ok" : "error";
        ProbeDurationMs.Record(durationMs,
            new KeyValuePair<string, object?>("target", target),
            new KeyValuePair<string, object?>("outcome", outcome));
        ProbeSuccess.Add(1,
            new KeyValuePair<string, object?>("target", target),
            new KeyValuePair<string, object?>("outcome", outcome));

        AvailabilitySnapshot snap;
        bool shouldAudit = false;
        var now = DateTimeOffset.UtcNow;
        lock (_gate)
        {
            _events.Enqueue(new ProbeEvent(now, success, target));
            var cutoff = now - WindowSize;
            while (_events.Count > 0 && _events.Peek().At < cutoff) _events.Dequeue();

            int total = _events.Count;
            int errors = 0;
            foreach (var e in _events) if (!e.Success) errors++;
            double rate = total == 0 ? 0.0 : (double)errors / total;
            snap = new AvailabilitySnapshot(
                WindowSec: (int)WindowSize.TotalSeconds,
                TotalProbes: total,
                ErrorCount: errors,
                ErrorRate: rate,
                LastCheckedAt: now,
                Targets: _targets);

            if (!success
                && total >= 1
                && rate > _burnThreshold
                && (now - _lastAlertAt) >= WindowSize
                && _auditTenantSlug is not null)
            {
                _lastAlertAt = now;
                shouldAudit = true;
            }
        }

        _snapshot.Set(snap);

        if (shouldAudit)
        {
            await WriteBurnRateAuditAsync(snap, target, ct);
        }
    }

    private async Task WriteBurnRateAuditAsync(AvailabilitySnapshot snap, string target, CancellationToken ct)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<RadioPadDbContext>();
            var audit = scope.ServiceProvider.GetRequiredService<IAuditLog>();
            var tenant = await db.Tenants.FirstOrDefaultAsync(t => t.Slug == _auditTenantSlug, ct);
            if (tenant is null)
            {
                _log.LogWarning("Availability audit tenant '{Slug}' not found; skipping burn-rate alert", _auditTenantSlug);
                return;
            }

            var detailsJson = JsonSerializer.Serialize(new
            {
                kind = "availability_burn_rate",
                windowSec = snap.WindowSec,
                errorRate = snap.ErrorRate,
                target,
            });

            await audit.AppendAsync(new AuditEvent
            {
                TenantId = tenant.Id,
                Action = AuditAction.SystemAlert,
                DetailsJson = detailsJson,
            }, ct);

            _log.LogWarning(
                "Availability burn-rate alert: target={Target} errorRate={Rate:0.000} threshold={Threshold:0.000}",
                target, snap.ErrorRate, _burnThreshold);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            _log.LogError(ex, "Failed to append availability_burn_rate audit row");
        }
    }

    private static int ParseInt(string? raw, int fallback)
        => int.TryParse(raw, out var v) ? v : fallback;

    private static double ParseDouble(string? raw, double fallback)
        => double.TryParse(raw, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : fallback;

    private readonly record struct ProbeEvent(DateTimeOffset At, bool Success, string Target);
}

/// <summary>
/// Last-computed snapshot of the synthetic availability monitor.
/// Used by both the API endpoint and the admin dashboard.
/// </summary>
public sealed record AvailabilitySnapshot(
    int WindowSec,
    int TotalProbes,
    int ErrorCount,
    double ErrorRate,
    DateTimeOffset? LastCheckedAt,
    IReadOnlyList<string> Targets);

public interface IAvailabilitySnapshotProvider
{
    AvailabilitySnapshot Current { get; }
}

internal sealed class AvailabilitySnapshotProvider : IAvailabilitySnapshotProvider
{
    private AvailabilitySnapshot _current = new(
        WindowSec: (int)AvailabilityMonitorService.WindowSize.TotalSeconds,
        TotalProbes: 0,
        ErrorCount: 0,
        ErrorRate: 0,
        LastCheckedAt: null,
        Targets: Array.Empty<string>());

    public AvailabilitySnapshot Current => Volatile.Read(ref _current);

    internal void Set(AvailabilitySnapshot snapshot)
        => Volatile.Write(ref _current, snapshot);
}
