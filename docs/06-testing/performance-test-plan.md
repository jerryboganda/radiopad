# Performance Test Plan

**Status:** Draft  ·  **Owner:** Engineering + Ops  ·  **Last Updated:** 2026-05-16

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

## Desktop measurements

Run these on a representative Windows 11, macOS, and Linux workstation before a
desktop release:

| Measurement | Method | Target |
| --- | --- | --- |
| Cold start | Launch from a stopped state; measure first usable dashboard paint. | Record baseline; no regression > 20%. |
| Warm start | Relaunch after one successful run with app data present. | Faster than cold start. |
| Sidecar readiness | Time from app launch to `/api/health/ready` 200 and hidden desktop status banner. | Record baseline; failures block release. |
| Idle CPU/GPU | Leave app on dashboard for 5 minutes after ready; use Task Manager / Activity Monitor / `top`. | Effectively near zero. |
| Idle memory | Same 5-minute idle window. | Stable; no unbounded growth. |
| Navigation latency | Dashboard -> report editor -> validation -> audit. | Instant-feeling; investigate visible stalls. |
| Secure clipboard overhead | Copy a section and wait for TTL clear. | No persistent CPU usage after clear. |
| Bundle size | Inspect installer and `frontend/out` sizes. | Record baseline; investigate large jumps. |

The desktop sidecar health check runs every 5 seconds with a sub-second timeout.
Do not add faster polling, permanent animation loops, or repeated renderer work
without documenting the performance reason here.
