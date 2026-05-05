# Non-Functional Requirements (NFR)

**Status:** Current  ·  **Owner:** Engineering  ·  **Last Updated:** 2026-05-04

## Performance

| Surface | Metric | Target |
| --- | --- | --- |
| Report list (≤25 items) | p95 latency | < 200 ms |
| Report PATCH | p95 latency | < 250 ms |
| Validation run | p95 latency | < 400 ms |
| AI gateway (mock) | p95 latency | < 100 ms |
| AI gateway (network) | p95 latency | < 4 s |
| Frontend Time-to-Interactive | desktop, fast 3G | < 3 s |

## Scalability

- Single-tenant baseline: 50 concurrent radiologists, 20k reports/year.
- Multi-tenant baseline: 100 tenants × 50 radiologists per backend pod.
- DB: vertical scale to PostgreSQL 14 (32 vCPU / 128 GB) before sharding.

## Availability

- Hosted SLO: 99.5% monthly. Error budget: 3h 39m / month.
- On-prem availability is the customer's responsibility; we publish a deployment runbook ([../07-devops/runbook.md](../07-devops/runbook.md)).

## Reliability

- Audit chain integrity: 100% verifiable across all events.
- Zero PHI leakage to non-compliant providers (binary metric).
- Rulebook golden-case pass rate on `main`: 100%.

## Security

See [../04-security/security-architecture.md](../04-security/security-architecture.md). Critical / High vulnerabilities patched within 30 days.

## Privacy

See [../04-security/privacy.md](../04-security/privacy.md). Personal data minimised; PHI routing logged.

## Maintainability

- Test coverage targets: backend 70% line / 80% branch on `RadioPad.Validation` and `RadioPad.Application`.
- Cyclomatic complexity ≤ 15 per method.
- ADRs required for any architectural decision affecting ≥ 2 layers.

## Observability

- Structured logs with `X-Request-Id` correlation.
- `/api/health` (live) and `/api/health/ready` (DB-aware).
- Future: OpenTelemetry traces and metrics (planned).

## Accessibility

- WCAG 2.1 AA target on web/desktop. See [../02-design/accessibility.md](../02-design/accessibility.md).

## Localization

- v0.1: English (en-US) only. i18n hooks left in copy strings; full localisation is a Phase 3 deliverable.

## Compliance

- HIPAA-compatible architecture (BAA required for PHI use).
- GDPR data-subject support via tenant export/delete (planned).
- SOC 2 Type I readiness in Phase 3.
