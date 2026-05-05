namespace RadioPad.Application.Services.Pacs;

/// <summary>
/// Iter-33 INT-007 — vendor-specific PACS adapter contract.
///
/// Complements (does NOT replace) the generic DICOMweb client by
/// orchestrating vendor-bespoke flows that QIDO/WADO/STOW alone cannot
/// express: worklist pull, prior-study fetch, KOS series flagging, and
/// signed-report sendback. Implementations live in
/// <c>RadioPad.Infrastructure/Pacs/</c>; one file per vendor.
///
/// Tenant isolation: every call must be made on behalf of a tenant whose
/// <c>TenantSettings.PacsVendor</c> matches <see cref="Vendor"/>. The
/// router (<see cref="IPacsVendorRouter"/>) enforces this.
///
/// Secrets: adapters MUST resolve their bearer / API key through the
/// <c>env:NAME</c> indirection (see <c>ProviderSecretResolver</c>) and
/// MUST NOT log it. PHI must not appear in any audit / log payload.
/// </summary>
public interface IPacsVendorAdapter
{
    /// <summary>Stable vendor key — one of <c>"sectra"</c>, <c>"visage"</c>, <c>"carestream"</c>.</summary>
    string Vendor { get; }

    Task<PacsWorklistEntry[]> FetchWorklistAsync(PacsWorklistQuery q, CancellationToken ct);
    Task<PacsStudySummary?> FetchPriorAsync(string accessionNumber, CancellationToken ct);
    Task<bool> SendReportAsync(PacsReportSendback report, CancellationToken ct);
    Task<PacsAdapterHealth> ProbeAsync(CancellationToken ct);
}

/// <summary>Request DTO for the worklist pull.</summary>
public sealed record PacsWorklistQuery(
    Guid TenantId,
    string? Modality = null,
    string? Status = null,
    DateTimeOffset? ScheduledFrom = null,
    DateTimeOffset? ScheduledTo = null,
    int Limit = 50);

/// <summary>One row of the unified worklist projection.</summary>
public sealed record PacsWorklistEntry(
    string AccessionNumber,
    string PatientId,
    string StudyInstanceUid,
    string Modality,
    string Status,
    string Description);

/// <summary>Summary of a prior study used to seed the report editor's "priors" rail.</summary>
public sealed record PacsStudySummary(
    string AccessionNumber,
    string PatientId,
    string StudyInstanceUid,
    string Modality,
    string Status,
    string Description);

/// <summary>Outbound report sendback payload. Body is plain text or HTML — never PHI in logs.</summary>
public sealed record PacsReportSendback(
    Guid TenantId,
    string AccessionNumber,
    string StudyInstanceUid,
    string ReportText,
    string Status = "final",
    string? RadiologistEmail = null);

/// <summary>Health-probe outcome for the adapter.</summary>
public sealed record PacsAdapterHealth(
    string Vendor,
    PacsAdapterHealthStatus Status,
    string? Detail = null);

public enum PacsAdapterHealthStatus
{
    Healthy = 0,
    Degraded = 1,
    Unreachable = 2,
    NotConfigured = 3,
}

/// <summary>
/// Picks the correct vendor adapter for a tenant based on
/// <c>TenantSettings.PacsVendor</c>. Returns null when the tenant
/// has not selected a vendor (callers fall back to the default
/// DICOMweb path with a warning).
/// </summary>
public interface IPacsVendorRouter
{
    IPacsVendorAdapter? Resolve(string? pacsVendor);
}
