# SLO / SLA

**Status:** Draft (hosted SKU)  ·  **Owner:** Ops + Product  ·  **Last Updated:** 2026-05-04

## Service Level Objectives

| SLO | Target | Window |
| --- | --- | --- |
| API availability | 99.5% | 30-day rolling |
| Read p95 latency | < 300 ms | 30-day rolling |
| Write p95 latency | < 800 ms | 30-day rolling |
| AI provider success rate (per provider) | ≥ 98% | 30-day rolling |
| Audit chain verification | 100% | per nightly run |

## Service Level Agreements (hosted SKU, planned)

| Tier | Uptime SLA | Support response (P1) | Maintenance window |
| --- | --- | --- | --- |
| Standard | 99.5% | 1 business hour | Sunday 02:00–06:00 local |
| Enterprise | 99.9% | 30 min, 24×7 | Negotiated |

> On-prem deployments do not carry an uptime SLA from RadioPad; we provide best-practice runbooks + commercial support windows.

## Error budget

- Standard: 0.5% / 30d ≈ 3.6 hours.
- Enterprise: 0.1% / 30d ≈ 43 minutes.
- Burning > 50% of budget in a week triggers a hold on non-critical changes.

## Incidents that consume budget

- Full or partial outage of the API.
- Latency above target sustained > 5 min.
- Audit verify failure (zero-tolerance — 100% budget consumption).

## Reporting

- Monthly SLO report per tenant on the hosted SKU (Phase 3).
- Public status page (planned) summarises platform-wide SLO.

## Tracking

- Metrics from [monitoring.md](monitoring.md).
- Audit verification from `radiopad audit verify` runs (nightly).
