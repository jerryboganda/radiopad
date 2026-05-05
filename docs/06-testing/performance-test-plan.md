# Performance Test Plan

**Status:** Draft  ·  **Owner:** Engineering + Ops  ·  **Last Updated:** 2026-05-04

## Goals

Verify the platform meets the latency / throughput targets in [non-functional requirements](../00-product/nfr.md) on a representative deployment.

## Tooling

- `k6` (planned) for load + stress.
- `dotnet-counters` and `dotnet-trace` for in-process profiling.
- Postgres `pg_stat_statements` for query analysis.

## Scenarios

| Scenario | Target |
| --- | --- |
| Dashboard list (1k reports / tenant) | p95 < 300 ms |
| Validate report (chest CT v1) | p95 < 500 ms |
| AI request (Mock) | p95 < 200 ms |
| AI request (Anthropic — informative only) | p95 < 6 s |
| Export text | p95 < 250 ms |
| Audit stream (1 day window) | p95 < 1 s |

## Profile

- Single API pod, 4 vCPU, 8 GiB RAM.
- Postgres 14 with default tuning.
- 100 concurrent users; 1 RPS / user steady state.

## Stress

- Ramp to 500 concurrent users. Acceptance: no 5xx; latency degrades gracefully (within 3× p95 target).
- Soak: 4 hours at steady state. Acceptance: no memory leak (RSS stable within 20%).

## Long-running risks

- N+1 EF queries on dashboard — protected by selecting only required columns.
- Audit chain write contention under bursty AI usage — mitigated by per-tenant lock; revisit if QPS climbs.

## Reporting

- Nightly performance run (planned) emits a JSON file with p50/p95/p99 per scenario.
- Regression > 20% on a target metric blocks the release.
