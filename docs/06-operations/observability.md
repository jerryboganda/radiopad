# Observability

**Status:** Living  **Owner:** Platform / SRE  **Last Updated:** 2026-05-04

RadioPad emits OpenTelemetry metrics from a single shared `Meter` named
`RadioPad.PerfBudgets`. The OTLP exporter is wired only when the operator
sets `RADIOPAD_OTEL_OTLP_ENDPOINT`; otherwise the metrics live entirely in
process and are observable via a `MeterListener` (this is how the
integration tests assert on histogram emissions).

## Histograms (P95 SLO budgets)

| Instrument | Unit | Description |
| --- | --- | --- |
| `radiopad.report.validate.duration_ms` | ms | `ReportingService.ValidateAsync` wall-clock duration. Budget: P95 < 250 ms. |
| `radiopad.report.sign.duration_ms` | ms | `POST /api/reports/{id}/sign` wall-clock duration. Budget: P95 < 500 ms. |
| `radiopad.ai.draft.duration_ms` | ms | `IAiGateway.RouteAsync` (draft / suggest / cleanup). Budget: P95 < 4 s. |
| `radiopad.dicom.qido.duration_ms` | ms | DICOMweb QIDO-RS study search. Budget: P95 < 600 ms. |
| `radiopad.api.request.duration_ms` | ms | Per-route HTTP request duration (tagged route, tenant, status). |

Burn-rate alerts on the validate / sign / draft / QIDO histograms can be
delivered to RadioPad via the Alertmanager-compatible webhook at
`POST /api/admin/observability/slo-alerts`. The endpoint records exactly
one append-only `SystemAlert` audit row per webhook call (with the alert
names + payload SHA-256 — the full payload is never persisted).

## Iter-35 PERF-004 — synthetic availability monitor

`AvailabilityMonitorService` is a `BackgroundService` that probes a
configurable list of relative paths against the local backend (default
`/api/health/ready`) at a configurable cadence and maintains a 5-minute
rolling failure window.

### Configuration

| Env var | Default | Notes |
| --- | --- | --- |
| `RADIOPAD_AVAILABILITY_PROBE_INTERVAL_SEC` | `30` | Cadence between probe passes. |
| `RADIOPAD_AVAILABILITY_PROBE_TARGETS` | `/api/health/ready` | CSV of relative paths. **Do not** add PHI-bearing routes — by policy this monitor only probes platform health endpoints. |
| `RADIOPAD_AVAILABILITY_BURN_RATE_THRESHOLD` | `0.05` | Failure-rate fraction (0..1) above which a burn-rate alert is recorded. |
| `RADIOPAD_AVAILABILITY_AUDIT_TENANT` | _(unset)_ | Tenant slug used to attribute `SystemAlert` audit rows. When unset, no audit row is written — only metrics + the snapshot endpoint. |
| `RADIOPAD_BIND` | `http://127.0.0.1:7457` | Probes are issued against this URL; the named `availability` HttpClient inherits it. |

### Metrics

| Instrument | Type | Tags | Description |
| --- | --- | --- | --- |
| `radiopad.availability.probe.duration_ms` | Histogram&lt;double&gt; | `target`, `outcome` | Wall-clock duration per probe. |
| `radiopad.availability.probe.success` | Counter&lt;long&gt; | `target`, `outcome` | Probe outcome counter (`outcome=ok\|error`). |

### Burn-rate audit row

When the rolling failure rate exceeds the configured threshold and an
audit tenant is configured, a single append-only audit event is appended:

- `Action`: `AuditAction.SystemAlert` (40)
- `DetailsJson`:
  ```json
  {
    "kind": "availability_burn_rate",
    "windowSec": 300,
    "errorRate": 1.0,
    "target": "/api/health/ready"
  }
  ```

Alerts are de-duplicated to at most one per rolling window so a sustained
outage cannot flood the audit log. The audit chain SHA-256 invariant is
preserved through `IAuditLog.AppendAsync` exactly like every other
audit-emitting code path.

> **Operator action required for production.** The burn-rate audit row
> only fires when `RADIOPAD_AVAILABILITY_AUDIT_TENANT` is set to a real
> tenant slug at deployment time. If the variable is unset (the default
> for dev), `AvailabilityMonitorService` still emits the OTel histogram
> + counter and the snapshot endpoint still returns live data, but the
> append-only audit chain will **not** receive a `SystemAlert` row when
> the failure rate breaches the threshold. Set this variable in every
> production environment that needs the SLO breach to land in the
> compliance audit feed.

### HTTP surface

`GET /api/admin/observability/availability` returns the current snapshot:

```json
{
  "windowSec": 300,
  "totalProbes": 10,
  "errorCount": 1,
  "errorRate": 0.1,
  "lastCheckedAt": "2026-05-04T12:00:00.000Z",
  "targets": ["/api/health/ready"]
}
```

RBAC: `ItAdmin` / `ComplianceReviewer`. The admin dashboard at
`/admin/security` renders this snapshot using the locked Open Design
tokens (`.rp-stat-tile`, `.rp-banner.warn`).

### PHI policy

The monitor probes only platform health endpoints — never report or
patient routes. Operators MUST NOT add PHI-bearing paths to
`RADIOPAD_AVAILABILITY_PROBE_TARGETS`. Probe URLs are captured in the
snapshot response and the audit row, both of which are tenant-readable.
