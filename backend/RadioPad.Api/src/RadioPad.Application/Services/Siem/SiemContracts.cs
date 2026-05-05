using System.Collections.Concurrent;
using RadioPad.Domain.Entities;

namespace RadioPad.Application.Services.Siem;

/// <summary>
/// Iter-32 INT-010 — SIEM-bound minimal event view.
///
/// PHI minimisation: ids, action codes, timestamps, integrity hash. The
/// audit log's <see cref="AuditEvent.DetailsJson"/> is intentionally
/// excluded — it may carry provider-routing context, hashed PII, or
/// correlation ids that operators do not need to ship to a third-party SIEM.
/// </summary>
public sealed record SiemEvent(
    Guid Id,
    Guid TenantId,
    Guid? UserId,
    Guid? ReportId,
    int ActionCode,
    string ActionName,
    DateTimeOffset CreatedAt,
    string IntegrityHash)
{
    public static SiemEvent FromAudit(AuditEvent e) => new(
        e.Id, e.TenantId, e.UserId, e.ReportId,
        (int)e.Action, e.Action.ToString(), e.CreatedAt, e.IntegrityChain);
}

public interface ISiemSink
{
    string Name { get; }
    bool Configured { get; }
    Task PushAsync(IReadOnlyList<SiemEvent> events, CancellationToken ct);
}

/// <summary>Process-local push status surfaced to <c>/admin/security</c>.</summary>
public sealed class SiemSinkStatus
{
    public DateTimeOffset? LastPushAt { get; set; }
    public string? LastError { get; set; }
    public long TotalPushed { get; set; }
    public long TotalErrors { get; set; }
}

/// <summary>In-memory per-sink push status tracker (singleton).</summary>
public sealed class SiemStatusRegistry
{
    private readonly ConcurrentDictionary<string, SiemSinkStatus> _byName = new();

    public SiemSinkStatus Get(string sinkName) =>
        _byName.GetOrAdd(sinkName, _ => new SiemSinkStatus());

    public IReadOnlyDictionary<string, SiemSinkStatus> Snapshot()
        => _byName.ToDictionary(kv => kv.Key, kv => kv.Value);
}
