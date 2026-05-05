using System.Collections.Concurrent;

namespace RadioPad.Application.Services.Hl7Bridge;

/// <summary>
/// Iter-33 INT-008 — minimal in-process outbox used by the Orthanc bridge to
/// hand off ORU^R01 messages converted from a DICOM SR back into the RIS
/// pipeline. The v0.1 bridge keeps the outbox in memory: a future iteration
/// will swap in a database-backed implementation behind the same interface
/// without changing the controller surface or its tests.
/// </summary>
public interface IHl7Outbox
{
    /// <summary>Append an ORU^R01 message for a tenant. Append-only.</summary>
    void Enqueue(Guid tenantId, string accession, string hl7);

    /// <summary>Snapshot of the current queue (for tests / admin tooling).</summary>
    IReadOnlyList<Hl7OutboxEntry> Snapshot();
}

public sealed record Hl7OutboxEntry(
    Guid Id,
    Guid TenantId,
    string AccessionNumber,
    string Hl7,
    DateTimeOffset EnqueuedAt);

public sealed class InMemoryHl7Outbox : IHl7Outbox
{
    private readonly ConcurrentQueue<Hl7OutboxEntry> _entries = new();

    public void Enqueue(Guid tenantId, string accession, string hl7)
    {
        if (string.IsNullOrEmpty(hl7))
            throw new ArgumentException("HL7 message is empty.", nameof(hl7));
        _entries.Enqueue(new Hl7OutboxEntry(
            Guid.NewGuid(), tenantId, accession ?? string.Empty, hl7, DateTimeOffset.UtcNow));
    }

    public IReadOnlyList<Hl7OutboxEntry> Snapshot() => _entries.ToArray();
}
