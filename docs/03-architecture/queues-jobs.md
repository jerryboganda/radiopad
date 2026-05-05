# Queues & Background Jobs

**Status:** Draft (no queue in v0.x)  ·  **Owner:** Engineering  ·  **Last Updated:** 2026-05-04

RadioPad has no background job system in v0.x — every operation is synchronous on the request thread. The architecture is intentionally simple while clinical-safety primitives mature.

## Planned jobs

| Job | Trigger | Purpose |
| --- | --- | --- |
| Audit export rollup | Cron daily | Produce a signed JSON-Lines bundle for the tenant. |
| Webhook dispatcher | After audit append | POST event to tenant webhook endpoints. |
| AI cost rollup | Cron daily | Aggregate token counts per tenant for billing. |
| Rulebook golden retest | Push to `main` | Run all rulebook golden suites in CI (already in CI, not a runtime job). |
| Cleanup of orphaned drafts | Cron weekly | Archive drafts not touched in N days (configurable per tenant). |

## Queue choice (planned, Phase 2)

- **Hangfire** is the leading candidate for in-process background jobs because it stays in C# and can use the same Postgres database.
- Alternatives: Quartz.NET, MassTransit (introduces RabbitMQ — heavier, only if multi-process scheduling is required).

## Retry rules

- Default: exponential backoff, max 5 attempts, jittered.
- Idempotency required — every job uses `auditEventId` or `tenantId+date` as a natural key.
- Failed jobs land in a dead-letter queue (DLQ) after the final attempt.

## DLQ strategy

- DLQ visible in the admin UI (Phase 3).
- Operators can inspect the payload (with PHI redacted) and re-queue.
- DLQ items older than 30 days are purged after a manual review.

## Scheduling

- Cron expressions stored per-tenant for tenant-specific jobs (e.g. cleanup window).
- Server-wide jobs (audit export rollup) run on a single leader pod (Phase 3 leader election).
