**Status:** Living  **Owner:** Integration  **Last Updated:** 2026-05-04

# PACS vendor adapters (INT-007)

RadioPad's vendor PACS adapters complement (do **not** replace) the generic
[`IDicomWebClient`](../../03-architecture/backend-architecture.md). The
DICOMweb client handles QIDO-RS / WADO-RS / STOW-RS for retrieval; vendor
adapters orchestrate the bespoke flows that DICOMweb cannot express:
worklist pull, prior-study fetch, KOS series flagging, and signed-report
sendback.

## Routing

A tenant's selected vendor lives on `TenantSettings.PacsVendor`
(`"sectra"`, `"visage"`, `"carestream"`, or `null`). The
`IPacsVendorRouter.Resolve(...)` method picks the keyed adapter; `null`
means "fall back to the generic DICOMweb path with a warning".

Adapter contract:

```csharp
public interface IPacsVendorAdapter
{
    string Vendor { get; }
    Task<PacsWorklistEntry[]> FetchWorklistAsync(PacsWorklistQuery q, CancellationToken ct);
    Task<PacsStudySummary?> FetchPriorAsync(string accessionNumber, CancellationToken ct);
    Task<bool> SendReportAsync(PacsReportSendback report, CancellationToken ct);
    Task<PacsAdapterHealth> ProbeAsync(CancellationToken ct);
}
```

## Endpoint matrix

### Sectra IDS7

| Operation   | Method | Path                                            |
| ----------- | ------ | ----------------------------------------------- |
| Probe       | `GET`  | `/ids7/api/v1/health`                           |
| Worklist    | `POST` | `/ids7/api/v1/worklist/query`                   |
| Prior       | `GET`  | `/ids7/api/v1/studies/{accession}/prior`        |
| SendReport  | `POST` | `/ids7/api/v1/reports`                          |

- **Auth:** `Authorization: Bearer {RADIOPAD_PACS_SECTRA_TOKEN}`.
  Override with `RADIOPAD_PACS_SECTRA_TOKEN_REF=env:CUSTOM_NAME` to point
  at a different env var.
- **Base URL:** `RADIOPAD_PACS_SECTRA_BASE`.
- **Known gaps:** KOS series flagging is not yet implemented;
  push-to-modality bridging requires a future iter.

### Visage 7

Visage exposes a single GraphQL endpoint at `/graphql`. All four ops
flatten onto queries / mutations.

| Operation   | GraphQL                                                                |
| ----------- | ---------------------------------------------------------------------- |
| Probe       | `query { ping }`                                                       |
| Worklist    | `query Worklist($input: WorklistInput!) { worklist(input: $input) {…} }` |
| Prior       | `query Prior($accession: String!) { prior(accession: $accession) {…} }`  |
| SendReport  | `mutation Report($input: ReportInput!) { reportSend(input: $input) { ok } }` |

- **Auth:** `Authorization: Bearer {RADIOPAD_PACS_VISAGE_TOKEN}`.
- **Base URL:** `RADIOPAD_PACS_VISAGE_BASE`.
- **Known gaps:** Subscription channel for live status updates (Visage's
  WebSocket transport) is not implemented; KOS-flag mutation TBD.

### Carestream Vue

Vue speaks DICOMweb for retrieval (handled by `IDicomWebClient`) plus a
thin REST surface at `/api/vue/v1` for orchestration.

| Operation   | Method  | Path                                            |
| ----------- | ------- | ----------------------------------------------- |
| Probe       | `GET`   | `/api/vue/v1/health`                            |
| Worklist    | `GET`   | `/api/vue/v1/worklist?…`                        |
| Prior       | `GET`   | `/api/vue/v1/studies/{accession}/prior`         |
| SendReport  | `POST`  | `/api/vue/v1/reports`                           |
| Status patch| `PATCH` | `/api/vue/v1/studies/{accession}/status`        |

- **Auth:** `Authorization: Bearer {RADIOPAD_PACS_CARESTREAM_TOKEN}`.
- **Base URL:** `RADIOPAD_PACS_CARESTREAM_BASE`.
- **Status patch** is a best-effort companion to `SendReport`; failures
  do not roll back the report sendback (the report has already been
  accepted by the time we PATCH).
- **Known gaps:** Vue's modality-worklist (DIMSE) bridge is out of
  scope; use Vue's REST worklist or the generic DICOMweb QIDO-RS.

## Security

- **Secrets:** Vendor bearer tokens **must** be referenced via the
  `env:NAME` indirection (`PacsSecretResolver`). The adapters never
  log or echo the raw value. Cloud-secret-manager schemes (`aws:`,
  `azkv:`, `gcp:`) are reserved — they fall through to the env-var
  fallback today and will gain real implementations in a later iter.
- **PHI:** Adapters MUST NOT include patient-identifying free text in
  log messages or audit details. Only accession numbers and study
  instance UIDs (which are not PHI on their own) are logged.
- **Tenant isolation:** Every call carries the tenant id in the
  worklist query / report sendback payload. The router enforces that
  the tenant's selected vendor matches the adapter being invoked.
- **Defaults:** Without configuration the base URLs resolve to
  `https://*.example.invalid`, which guarantees `ProbeAsync` returns
  `Unreachable` rather than silently calling a third party.

## Health probe semantics

| Status         | Meaning                                                  |
| -------------- | -------------------------------------------------------- |
| `Healthy`      | HTTP 2xx on the vendor's health endpoint.                |
| `Degraded`     | HTTP non-2xx (4xx or 5xx).                               |
| `Unreachable`  | Network exception (DNS, TCP, TLS, timeout).              |
| `NotConfigured`| Reserved for the router to flag when a tenant has no vendor selected. |
