using Microsoft.EntityFrameworkCore;
using RadioPad.Application.Services.Siem;
using RadioPad.Infrastructure.Persistence;

namespace RadioPad.Api.Services;

/// <summary>
/// Iter-32 INT-010 — drains the append-only audit log to up-to-four
/// configured SIEM sinks (Splunk HEC, Sentinel Log Analytics, Elastic
/// <c>_bulk</c>, Syslog UDP). Watermarked by <see cref="DateTimeOffset"/>
/// per-process; on a clean restart we may resend a small batch (idempotent
/// in every supported SIEM via the audit event id).
///
/// Locks honoured:
///   - audit log stays append-only — this service only READS.
///   - failures retry up to 3× with backoff and never block <c>/api/*</c>.
///   - PHI minimisation: only ids + action codes + timestamps + integrity
///     hash leave the process; <see cref="Domain.Entities.AuditEvent.DetailsJson"/>
///     is intentionally excluded.
/// </summary>
public sealed class SiemPushService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IEnumerable<ISiemSink> _sinks;
    private readonly SiemStatusRegistry _status;
    private readonly ILogger<SiemPushService> _log;

    private const int BatchSize = 100;
    private static readonly TimeSpan FlushInterval = TimeSpan.FromSeconds(5);

    private DateTimeOffset _watermark = DateTimeOffset.MinValue;

    public SiemPushService(
        IServiceScopeFactory scopeFactory,
        IEnumerable<ISiemSink> sinks,
        SiemStatusRegistry status,
        ILogger<SiemPushService> log)
    {
        _scopeFactory = scopeFactory;
        _sinks = sinks;
        _status = status;
        _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        // Initialise watermark to "now" so a fresh process does not flood the
        // SIEM with historical events on first boot.
        _watermark = DateTimeOffset.UtcNow;
        try { await Task.Delay(TimeSpan.FromSeconds(15), ct); }
        catch (OperationCanceledException) { return; }

        var configured = _sinks.Where(s => s.Configured).ToArray();
        if (configured.Length == 0)
        {
            _log.LogInformation("SIEM push: no sinks configured (env vars unset). Worker idle.");
            return;
        }
        _log.LogInformation("SIEM push: configured sinks = {Sinks}",
            string.Join(", ", configured.Select(s => s.Name)));

        while (!ct.IsCancellationRequested)
        {
            try
            {
                await DrainAsync(configured, ct);
            }
            catch (OperationCanceledException) { return; }
            catch (Exception ex)
            {
                _log.LogError(ex, "SIEM push: unexpected error in drain loop");
            }
            try { await Task.Delay(FlushInterval, ct); }
            catch (OperationCanceledException) { return; }
        }
    }

    private async Task DrainAsync(ISiemSink[] sinks, CancellationToken ct)
    {
        // Read the next batch of audit events strictly after the current
        // watermark. We use CreatedAt rather than rowid so a Postgres /
        // SQLite-portable query remains correct.
        List<SiemEvent> batch;
        DateTimeOffset newWatermark;
        using (var scope = _scopeFactory.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RadioPadDbContext>();
            var rows = await db.AuditEvents.AsNoTracking()
                .Where(a => a.CreatedAt > _watermark)
                .OrderBy(a => a.CreatedAt)
                .Take(BatchSize)
                .ToListAsync(ct);
            if (rows.Count == 0) return;
            batch = rows.Select(SiemEvent.FromAudit).ToList();
            newWatermark = rows[^1].CreatedAt;
        }

        foreach (var sink in sinks)
        {
            await PushWithRetryAsync(sink, batch, ct);
        }
        _watermark = newWatermark;
    }

    private async Task PushWithRetryAsync(ISiemSink sink, IReadOnlyList<SiemEvent> batch, CancellationToken ct)
    {
        var status = _status.Get(sink.Name);
        Exception? lastError = null;
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            try
            {
                await sink.PushAsync(batch, ct);
                status.LastPushAt = DateTimeOffset.UtcNow;
                status.LastError = null;
                status.TotalPushed += batch.Count;
                return;
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                lastError = ex;
                _log.LogWarning(ex,
                    "SIEM push: sink={Sink} attempt={Attempt} failed", sink.Name, attempt);
                try { await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, attempt)), ct); }
                catch (OperationCanceledException) { throw; }
            }
        }
        status.LastError = lastError?.Message;
        status.TotalErrors += 1;
    }
}
