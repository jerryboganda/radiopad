# Queues & Background Jobs

**Status:** One durable job subsystem shipped (async AI generation); Hangfire still planned for Phase-2 cron work  ·  **Owner:** Engineering  ·  **Last Updated:** 2026-07-23

Most operations remain synchronous on the request thread. The one exception is **async AI report generation**, which now runs on a first-class durable job platform (below). Cron-style background work (the "Planned jobs" table) is still unbuilt and remains a Phase-2 Hangfire candidate.

## Shipped: durable AI generation jobs

Impression/rewrite (`kind = "ai"`) and whole-report (`kind = "generate"`) generations are minutes-long provider calls that must survive a dropped tab, a proxy timeout, and a process restart. They run as durable jobs:

- **Durable row:** one `AiJobs` row per attempt (`RadioPad.Domain.Entities.AiJob`; migration `20260723090000_AddAiJobs`). Status vocabulary `queued | running | ok | error | cancelled`. Indexed by `(TenantId, UserId, CreatedAt)`, `(TenantId, ReportId, Status)`, `(Status, CompletedAt)`.
- **Hot cache:** the in-memory `AiJobRegistry` serves the ~2s poll from memory so a poll never touches the DB. `AiJobCoordinator` writes through **registry-first, DB-second** on every transition; a crash between the two leaves a `running` row that boot recovery marks `server_restart` — the safe direction (never a phantom success).
- **Runner:** `AiJobCoordinator.SubmitAsync` writes a `queued` row and enqueues an `AiJobWork` onto an unbounded `Channel<AiJobWork>`. The hosted `AiJobRunner` (`BackgroundService`) dequeues with bounded parallelism (`AiJobs:MaxConcurrency`, default 4). Each job runs under a per-job CTS = linked(`ApplicationStopping`) + safety timeout (`AiJobs:SafetyTimeoutSeconds`, default 600) + a registry-registered cancel signal.
- **Single-flight:** at most one non-terminal job per `(tenant, report, kind, mode)` — a duplicate submit attaches to the active one (DB query is authoritative; the registry gives a fast in-memory short-circuit).
- **Cancel:** queued → immediate `cancelled` (never dequeued, zero provider cost); running → `CancelRequested` + fire the CTS. UBAG additionally best-effort cancels the gateway job on the created job id before the `OperationCanceledException` propagates.
- **Retry:** only from `error`/`cancelled`, always a **new** row (`Attempt+1`, `RetryOfJobId` set) — never resurrects the old one, and re-runs the regulated-feature gate so it cannot be bypassed via retry.
- **Recovery:** `AiJobRecoveryHostedService` runs once at boot (before the runner consumes), marking every orphaned `queued`/`running` row `error` / errorKind `server_restart`. It NEVER re-enqueues — a re-run would spend provider time with nobody watching and could overwrite a report the radiologist is now editing.
- **Retention:** `RetentionWorker` nulls `ResultJson` 24h after completion and deletes rows 30 days after completion.
- **Wire contract:** the report-scoped submit (`POST /api/reports/{id}/ai/jobs`, `.../generate/jobs`) and poll (`GET .../ai/jobs/{jobId}`) endpoints are unchanged and additive — submit still returns `202 { jobId, status }` (status may now be `queued`); poll returns the same `{ jobId, kind, mode, status, elapsedMs, result, error, errorKind }` envelope and now falls back to the durable row on a registry miss.

**Not durable by design:** the desktop sidecar's local-generation jobs stay in-memory only (its SQLite is throwaway).

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
