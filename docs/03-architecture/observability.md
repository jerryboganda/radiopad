# Observability

**Status:** Current (basic) → Roadmap (deep)  ·  **Owner:** Engineering + Ops  ·  **Last Updated:** 2026-05-04

## Today

- **Logs:** Structured, console-formatted, correlation-id'd. See [logging.md](logging.md).
- **Health probes:** `GET /api/health` (live), `GET /api/health/ready` (DB-aware).
- **Audit log:** Append-only with SHA-256 chain; verifiable via `radiopad audit verify`.

## Planned (Phase 2)

### Metrics

- `radiopad_requests_total{tenant, route, status}`
- `radiopad_request_duration_seconds_bucket{tenant, route}`
- `radiopad_ai_calls_total{tenant, provider, phi_class, outcome}`
- `radiopad_ai_tokens{tenant, provider}`
- `radiopad_audit_chain_length{tenant}`
- `radiopad_validation_findings_total{tenant, severity}`

Expose via `/metrics` (Prometheus format) gated by `RADIOPAD_METRICS_ENABLED`.

### Traces

- OpenTelemetry SDK with W3C trace context.
- One trace per HTTP request → middleware → controller → service → adapter span.
- AI calls produce a child span with `phi_class`, `provider`, `tokens` attributes.

### Dashboards

- **API health:** request rate, error rate, p50/p95/p99 latency.
- **Clinical safety:** `provider.blocked` count must remain accurate; never zero unexpectedly.
- **AI quality:** acceptance rate of suggestions, hallucination rate (from evals).
- **Audit:** chain length per tenant, last verify timestamp.

### Alerts

| Alert | Threshold | Severity |
| --- | --- | --- |
| 5xx error rate | > 1% over 5 min | P1 |
| `radiopad audit verify` failure | any | P1 |
| Provider call failure rate | > 25% over 10 min | P2 |
| Readiness probe failing on > 1 pod | > 1 min | P2 |
| AI cost burn vs daily budget | > 200% | P3 |

### SLOs

See [../07-devops/slo-sla.md](../07-devops/slo-sla.md).

## Where to look first

1. `radiopad audit verify` — chain integrity.
2. `/api/health/ready` — DB connectivity.
3. Logs filtered by `requestId` from a user-reported error.
4. Audit events for the affected report.

## Continuous P95 budgets (PERF-004)

**Status:** Implemented (iter-33).

The backend exposes an OpenTelemetry [`Meter`](../../backend/RadioPad.Api/src/RadioPad.Api/Services/PerfBudgets.cs) named `RadioPad.PerfBudgets` carrying five histograms backing the latency SLOs:

| Histogram | PRD P95 target | Source |
| --- | --- | --- |
| `radiopad.report.validate.duration_ms` | < 250 ms | `POST /api/reports/{id}/validate` (wraps `ReportingService.ValidateAsync`) |
| `radiopad.report.sign.duration_ms` | < 500 ms | `POST /api/reports/{id}/sign` |
| `radiopad.ai.draft.duration_ms` | < 4 s | `IAiGateway.RouteAsync` (decorator `PerfInstrumentedAiGateway`) |
| `radiopad.dicom.qido.duration_ms` | < 600 ms | `DicomWebClient.SearchStudiesAsync` / `FetchStudyAsync` |
| `radiopad.api.request.duration_ms` | per-route | `PerfBudgetMiddleware` (tags: `route`, `tenant`, `status`) |

### Environment variables

| Var | Default | Effect |
| --- | --- | --- |
| `RADIOPAD_OTEL_OTLP_ENDPOINT` | unset | When set (e.g. `http://127.0.0.1:4318`) the meter is exported via OTLP HTTP/protobuf. When unset, metrics live in-process only — no network calls — and tests can observe them with `MeterListener`. |

### Recording rules + alerts

Prometheus rule files: [deploy/observability/slo-recording-rules.yaml](../../deploy/observability/slo-recording-rules.yaml) — P50/P95/P99 over 5m / 30m / 1h plus `radiopad_slo_burn_rate_5m` / `_30m` per histogram, and Alertmanager-compatible alert groups (multi-burn-rate: 2% fast, 0.2% slow).

Grafana dashboard: [deploy/observability/grafana-radiopad-slo.json](../../deploy/observability/grafana-radiopad-slo.json) — five stat panels (one per histogram) + two burn-rate timeseries; data source pinned to `${DS_PROMETHEUS}`.

### Webhook alert sink

Alertmanager (or any compatible source) can POST its webhook payload to `POST /api/admin/observability/slo-alerts`. The endpoint is RBAC-gated (`ItAdmin`, `MedicalDirector`, `ComplianceReviewer`); accepted payloads are appended to the audit log as `AuditAction.SystemAlert` with a SHA-256 of the raw payload (no PHI-leak risk — full body never persisted).

### Runbook (breach response)

1. Confirm the alert source — check the latest `SystemAlert` audit row for the tenant via `radiopad audit query --tenant <slug> --action SystemAlert`.
2. Open the Grafana dashboard and inspect the stat panel that crossed the threshold + the per-route burn-rate panel for the offending route.
3. Cross-reference with `radiopad.api.request.duration_ms{route=...}` to scope the breach to a specific endpoint.
4. If `ai.draft` is breaching, check `IProviderRouter` decisions — a single slow provider can dominate the histogram. Use `GET /api/ai/routing/preview` to confirm routing weights.
5. If `dicom.qido` is breaching, the upstream PACS is likely the culprit — `IDicomWebClient.HealthAsync` should already be alerting.
6. Mitigation playbooks live in [../07-devops/runbook.md](../07-devops/runbook.md).
