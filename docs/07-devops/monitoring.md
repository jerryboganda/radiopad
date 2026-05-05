# Monitoring

**Status:** Draft  ·  **Owner:** Ops  ·  **Last Updated:** 2026-05-04

## Today

- Health endpoints (`/api/health`, `/api/health/ready`).
- Structured logs.
- Audit chain integrity check via `radiopad audit verify`.

## Planned (Phase 2)

### Metrics (Prometheus)

| Metric | Type | Labels |
| --- | --- | --- |
| `radiopad_requests_total` | counter | tenant, route, method, status |
| `radiopad_request_duration_seconds` | histogram | tenant, route |
| `radiopad_ai_calls_total` | counter | tenant, provider, phi_class, outcome |
| `radiopad_ai_tokens` | counter | tenant, provider, direction |
| `radiopad_ai_call_duration_seconds` | histogram | tenant, provider |
| `radiopad_validation_findings_total` | counter | tenant, severity |
| `radiopad_audit_chain_length` | gauge | tenant |
| `radiopad_provider_blocked_total` | counter | tenant, provider |

### Dashboards (Grafana)

- API health.
- AI usage & cost.
- Audit integrity.
- Validation finding mix.
- Tenant quotas (Phase 2).

### Logs

- Structured JSON via Serilog (Phase 2).
- Aggregated to Loki / Elastic / customer SIEM.
- Retention 30 days hot, 1 year cold.

### Tracing

- OpenTelemetry traces with W3C trace context.
- Sampled at 10% by default; 100% for failing requests.

## Alerts

| Alert | Threshold | Severity |
| --- | --- | --- |
| 5xx rate > 1% | over 5 min | P1 |
| `audit verify` failure | any | P1 |
| Provider failure rate > 25% | over 10 min | P2 |
| AI cost > 200% daily budget | trigger | P3 |
| Pod readiness flapping | > 1 min | P2 |
| DB connection pool exhausted | > 30 s | P2 |

See [slo-sla.md](slo-sla.md).
