# Capacity Planning

**Status:** Draft  ·  **Owner:** Ops  ·  **Last Updated:** 2026-05-04

## Sizing assumptions (per tenant)

| Dimension | Small | Medium | Large |
| --- | --- | --- | --- |
| Active radiologists | ≤ 5 | 5–25 | 25–100 |
| Reports / day | ≤ 200 | 200–1k | 1k–5k |
| Audit events / day | ≤ 4k | 4k–20k | 20k–100k |
| Storage / year (text only) | ≤ 1 GiB | 1–5 GiB | 5–20 GiB |
| AI calls / day | ≤ 600 | 600–3k | 3k–15k |

## Resource budgets

| Tier | API replicas | Postgres | Egress |
| --- | --- | --- | --- |
| Single-tenant on-prem | 1–2 (4 vCPU, 8 GiB) | 4 vCPU, 16 GiB, 100 GiB SSD | LAN |
| Hosted small | 2 | shared, dedicated DB user, 50 GiB | Internet |
| Hosted medium | 3–4 | dedicated, 4 vCPU, 16 GiB, 200 GiB | Internet |
| Hosted large | 5–10 (autoscale) | 8 vCPU, 32 GiB, 500 GiB + replica | Internet |

## Observable signals to drive scaling

- API CPU > 70% for 5 min → add a replica.
- DB connection pool saturation > 70% → reduce per-pod pool / scale DB.
- Audit append latency p95 > 200 ms → shard tenants across replicas (Phase 3).
- AI request queue (Phase 2) growth → add background worker.

## Quotas (hosted SKU)

- Per-tenant daily AI token budget (configurable).
- Per-IP rate limit (Phase 2).
- API rate limit group `[EnableRateLimiting("ai")]` = 60 req/min/tenant.

## Cost levers

- Provider choice (local vs remote).
- DB tier sizing.
- Object storage class (Phase 2).
- Log retention windows.

## Reviewing the plan

- Quarterly review using actual usage data + projected growth.
- Update this file with empirical numbers once we have them.
