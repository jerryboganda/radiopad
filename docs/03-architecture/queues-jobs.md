# Queues & Background Jobs

**Status:** Two job subsystems shipped — async AI generation (durable rows + SSE) and the Hangfire cron platform (PR-N1)  ·  **Owner:** Engineering  ·  **Last Updated:** 2026-07-23

Most operations remain synchronous on the request thread. Two exceptions run on job platforms: **async AI report generation** (a first-class durable subsystem, below) and **cron-shaped background work**, which as of PR-N1 runs on **Hangfire** (below). The "Planned jobs" table lists the four new jobs still to be built on top of that platform (PR-N2).

## Shipped: durable AI generation jobs

Impression/rewrite (`kind = "ai"`), whole-report (`kind = "generate"`), dictation-cleanup (`kind = "ai"`, `mode = "cleanup"` — the input-carrying route), and cross-check medical review (`kind = "crosscheck"`) generations are minutes-long provider calls that must survive a dropped tab, a proxy timeout, and a process restart. They run as durable jobs:

- **Durable row:** one `AiJobs` row per attempt (`RadioPad.Domain.Entities.AiJob`; migration `20260723090000_AddAiJobs`). Status vocabulary `queued | running | ok | error | cancelled`. Indexed by `(TenantId, UserId, CreatedAt)`, `(TenantId, ReportId, Status)`, `(Status, CompletedAt)`.
- **Hot cache:** the in-memory `AiJobRegistry` serves the ~2s poll from memory so a poll never touches the DB. `AiJobCoordinator` writes through **registry-first, DB-second** on every transition; a crash between the two leaves a `running` row that boot recovery marks `server_restart` — the safe direction (never a phantom success).
- **Runner:** `AiJobCoordinator.SubmitAsync` writes a `queued` row and enqueues an `AiJobWork` onto an unbounded `Channel<AiJobWork>`. The hosted `AiJobRunner` (`BackgroundService`) dequeues with bounded parallelism (`AiJobs:MaxConcurrency`, default 4). Each job runs under a per-job CTS = linked(`ApplicationStopping`) + safety timeout (`AiJobs:SafetyTimeoutSeconds`, default 600) + a registry-registered cancel signal.
- **Single-flight:** at most one non-terminal job per `(tenant, report, kind, mode)` — a duplicate submit attaches to the active one (DB query is authoritative; the registry gives a fast in-memory short-circuit).
- **Cancel:** queued → immediate `cancelled` (never dequeued, zero provider cost); running → `CancelRequested` + fire the CTS. UBAG additionally best-effort cancels the gateway job on the created job id before the `OperationCanceledException` propagates.
- **Retry:** only from `error`/`cancelled`, always a **new** row (`Attempt+1`, `RetryOfJobId` set) — never resurrects the old one, and re-runs the regulated-feature gate so it cannot be bypassed via retry. The retry copies the prior row's `InputJson` so an input-carrying kind re-runs on the same input. A `crosscheck` retry whose `InputJson` has been retention-swept (24h) can no longer be reconstructed → `409 { kind: "job_input_expired" }`; a swept `ai`+`cleanup` retry degrades to the legacy generic single-text path rather than hard-failing (the row alone can't distinguish a swept input-carrying cleanup from a legacy input-less one, and legacy retries must keep working).
- **Recovery:** `AiJobRecoveryHostedService` runs once at boot (before the runner consumes), **partitioning** the orphaned `queued`/`running` rows. `queued` rows, and `running` rows with no captured gateway job id, are marked `error` / errorKind `server_restart` (as before). It NEVER re-enqueues them — a re-run would spend provider time with nobody watching and could overwrite a report the radiologist is now editing.
- **UBAG re-attach (PR-B4):** a `running` row whose `ProviderJobId` is set (only the UBAG adapter captures one, via `AiCompletionRequest.OnProviderJobCreated` → `AiJobCoordinator.PersistProviderJobIdAsync`, an `ExecuteUpdate` that touches only that column, never `Status`) may still be running on the gateway. Recovery LEAVES the row `running` and launches one **detached** `AiJobCoordinator.ReattachUbagAsync` per candidate (startup is never blocked; the row staying `running` keeps single-flight holding duplicate submits off). Re-attach is **re-poll only — it never issues a fresh AI call**: it re-`GetJobAsync`-polls the gateway job (`AiJobs:ReattachPollSeconds`, default 2 s, mirroring the live UBAG poll cadence) to terminal, then commits through the SAME registry-first/DB-second terminal path a live run uses. `ai` kind → the `{ text, provider, model, latencyMs, promptVersion, mode, routedBy: "reattach" }` envelope; `generate` kind → `ReportingService.ShapeStructuredResult` (the recommendations backfill is skipped — boot must not fan out a second AI call) applied under a freshness guard that uses `StartedAt` as the conservative stand-in for the lost submit-time snapshot (a report edited after the job started discards the AI result, `report_modified`). The job is cancellable throughout (its budget CTS is registered, so a user Cancel best-effort `CancelJobAsync`s the gateway). Budget (`AiJobs:ReattachTimeoutSeconds`, default 300) exhaustion, a second shutdown, or an unreachable gateway all fall back to `server_restart` — the same safe state the sweep produces; a gateway job that concludes as a definitive failure maps to `provider_transport`.
- **Retention:** `RetentionSweepJob` (migrated from the former `RetentionWorker`) nulls **both** `ResultJson` and `InputJson` 24h after completion in the same `ExecuteUpdate` chain — `InputJson` (the raw dictation / cross-check text on the input-carrying kinds) is the same clinical-text-at-rest class as the result — and deletes rows 30 days after completion. Consequence: a cleanup/cross-check job older than 24h loses the input needed to reconstruct it (`crosscheck` becomes non-retryable; see Retry).
- **Input-carrying kinds (PR-B5):** two additive report-scoped submit routes, `POST /api/reports/{id}/dictation/cleanup/jobs` and `POST /api/reports/{id}/crosscheck/review/jobs` (both `[EnableRateLimiting("ai")]`, both RBAC `Radiologist`/`MedicalDirector` — the SAME role gate as their sync siblings, NOT `ReportsEdit`). They persist the request payload in the new `AiJobs.InputJson` column (migration `20260724100000_AddAiJobInput`) so Retry can re-run them. Cleanup single-flights on `(tenant, report, "ai", "cleanup")`; cross-check single-flights **per section** — `mode` = the normalized `sectionKey` (or `report`), so two different sections review concurrently while a second submit of the same section attaches. `AiJobCoordinator.RunCleanupAsync` / `RunCrossCheckAsync` resolve `IDictationCleanupService` / `ICrossCheckReviewService` from the run scope and produce ResultJson envelopes **byte-identical to the sync endpoints** (cleanup: `{ cleanedSections{…}, provider, model, latencyMs, promptVersion }`; cross-check: `{ provider, model, latencyMs, corrections[…] }`). The result is a **suggestion set only** — the job NEVER writes the report; the preview/accept gate stays entirely client-side. `InputJson` is never serialized into any API response (the client already holds the raw input). The pre-existing generic `/ai/jobs` `mode=cleanup` path is preserved byte-for-byte by an `InputJson`-presence guard in the run dispatch.
- **Wire contract:** the report-scoped submit (`POST /api/reports/{id}/ai/jobs`, `.../generate/jobs`) and poll (`GET .../ai/jobs/{jobId}`) endpoints are unchanged and additive — submit still returns `202 { jobId, status }` (status may now be `queued`); poll returns the same `{ jobId, kind, mode, status, elapsedMs, result, error, errorKind }` envelope and now falls back to the durable row on a registry miss. Additive since PR-B1: a `progress = { tokens, percent }` field is present on the poll body (both `GET .../ai/jobs/{jobId}` and `GET /api/jobs/{id}`) **only** while `status == "running"` and only from the hot cache — omitted (WhenWritingNull) otherwise; `percent` is null on every streaming path in v1 (indeterminate — no provider reports an honest expected-total). `GET /api/jobs` (list) stays light with no progress.
- **Live event stream (SSE, PR-B1):** `GET /api/events/stream` is one long-lived Server-Sent-Events connection per signed-in client, fed by the in-process `AiJobEventBus` (singleton) and replacing the jobs-widget poll. Events are `event: job` (a JobSummary-shaped terminal patch, `JobsController.List` row shape minus `report`), `event: progress` (`{ jobId, tokens, percent }`), `event: partial` (`{ jobId, delta }` — streamed model text), and `event: notification`. **User-scoped by design:** job/progress/partial events filter on BOTH `tenantId` AND `userId` (a user sees only their own jobs) — `partial` carries raw clinical output, so it must never fan out tenant-wide. Each subscription is a bounded DropOldest channel (`AiJobs:SseSubscriberBuffer`, default 64) so a slow client never back-pressures a live job; keep-alive comments every `AiJobs:SseKeepAliveSeconds` (default 15) defeat idle proxy timeouts; the connection is exempt from the perf-budget route histogram. Auth only (no extra permission gate — notifications reach every role, job events already self-filter). A browser `EventSource` cannot send headers, so the desktop webview may authenticate via a path-scoped `?access_token=` query param honoured **only** on this exact GET route (mirrors `/ws/companion`); first-party clients use `fetch()` + `ReadableStream` with real headers.
- **Progress side-map:** streamed token counts + partial text live ONLY in `AiJobRegistry` (`UpdateProgress` / `ProgressOf`), never on the durable `AiJobs` row (contract D) — lost on eviction/restart by design. A non-monotonic token count resets the partial buffer (a provider-failover retry restarts counting at ~1, discarding the failed attempt's text); the buffer is capped at `AiJobs:PartialBufferMaxChars` (default 262144), keeping the tail.
- **Token streaming producers (AI-013, PR-B3):** `AiCompletionRequest` carries an optional `IProgress<AiStreamChunk>? OnStream` (contract C). An adapter switches to stream mode **only** when it is non-null; otherwise every request is byte-identical to before. Streaming adapters: the OpenAI-family (`openai` / `azure-openai` / `openai-compatible` / `vllm`, via `OpenAiChatHelpers.SendChatStreamingAsync` — SSE `choices[].delta.content`, real counts from a final `stream_options.include_usage` chunk, else chunk-count fallback), `anthropic` (Messages SSE: `message_start` input tokens, `content_block_delta` text, `message_delta` output tokens), `ollama` (NDJSON, `done:true` carries `prompt_eval_count`/`eval_count`), `llama-cpp` (llama-server SSE; grammar/stop/repeat params stay in the body during streaming), and `mock` (a deterministic 3-chunk synthetic stream). UBAG, the CLI adapters, Bedrock, and Vertex ignore `OnStream` and run unchanged. Producers MUST report **synchronously, in arrival order** via `SynchronousProgress<T>` — never `System.Progress<T>` (it posts to the ThreadPool and can reorder chunks). Hooks (`AiRunHooks`) thread from `AiJobCoordinator` through `ReportingService` (`RunAsync` / `RunAutoAsync` / `GenerateStructuredAsync` / `GenerateStructuredAutoAsync`) as an optional trailing param; the secondary recommendations backfill stays non-streaming.
- **Coordinator publish throttle (PR-B3):** `AiJobCoordinator.OnStreamChunk` feeds EVERY chunk to the registry (poll path) but coalesces bus fan-out — it publishes a `progress` (+ `partial`) event only once `AiJobs:StreamPublishMinIntervalMs` (default 150) has elapsed OR `AiJobs:StreamPublishTokenBatch` (default 20) new tokens have accrued, and always flushes the buffered delta immediately before the terminal `job` event so no streamed text is lost. `Percent` is ALWAYS null (design §3.10 — `n_predict`/`max_tokens` is a ceiling, not a target, so there is no honest ratio). Stop = existing `Cancel` (the adapter read loop throws `OperationCanceledException`; partial text is discarded, never applied); regenerate = existing retry — no new endpoints.

**Not durable by design:** the desktop sidecar's local-generation jobs stay in-memory only (its SQLite is throwaway).

## Shipped: Hangfire cron platform (PR-N1)

Cron-shaped background work runs on **Hangfire** (single-process, in-C#, on the same database). Bootstrap lives in `RadioPad.Api.Jobs.HangfireSetup` (`AddRadioPadHangfire` / `UseRadioPadRecurringJobs`); the job classes live in `RadioPad.Api.Jobs.*Job`.

### Storage matrix

Hangfire's storage is chosen by the same connection-string sniff EF Core uses (`RadioPad.Api.Jobs.HangfireStorageSelector.Select`):

| Deployment | Connection string | Hangfire storage |
| --- | --- | --- |
| Postgres (VPS / prod) | `Host=` / `Server=` | `Hangfire.PostgreSql` — Hangfire provisions and owns its own `hangfire` schema (`PrepareSchemaIfNecessary` default true, independent of EF's hand-written migrations; the DB role needs CREATE on first boot) |
| SQLite dev workstation + desktop sidecar | anything else | `Hangfire.InMemory` |

**InMemory loses schedules + delayed retries on restart — accepted.** Every recurring job is re-registered at boot via idempotent `RecurringJob.AddOrUpdate`, and every job body is safe to run twice, so a restart simply re-arms the schedules. The desktop sidecar therefore runs all five crons in-process exactly as the old BackgroundServices did — no behavioural regression.

### Server + retry / DLQ

- **Server:** `WorkerCount = clamp(ProcessorCount * 2, 4, 16)`, three queues drained in priority order `critical > default > maintenance`, `SchedulePollingInterval = 15s`, `JobExpirationTimeout = 30 days`.
- **Retry:** one global policy — `JitteredRetryAttribute` (exponential `30s * 2^attempt` + `0..30s` jitter, 5 attempts). Hangfire's built-in 10-attempt `AutomaticRetry` is removed at bootstrap so this is the only retry filter in effect.
- **DLQ:** the Hangfire Failed set (retained 30 days). Payloads are ids only, so it is inherently PHI-redacted. Requeue/triage is a future tenanted admin surface over `Hangfire.MonitoringApi` (named seam: `JobsAdminController`, Phase-3).
- **Dashboard:** intentionally NOT mapped — its filter-based auth does not compose with RadioPad's custom `HttpContext.Items` identity, its pages would expose PHI-adjacent job arguments, and the backend binds 127.0.0.1.
- **Testing:** `AddRadioPadHangfire` / `UseRadioPadRecurringJobs` are skipped under the `Testing` environment (mirrors the DevSeed / UBAG-discovery precedent). The job classes are still registered in DI, and tests drive their sweep/scan methods directly — fully deterministic, no processing server.

### Migrated crons (PR-N1)

Five former `BackgroundService`s were extracted into recurring jobs; their sweep bodies are byte-identical and their public test-entry methods are preserved (existing tests re-point with a one-line type change):

| Job (`RadioPad.Api.Jobs`) | Method | Cron | Migrated from |
| --- | --- | --- | --- |
| `RetentionSweepJob` | `SweepAsync` | `0 */6 * * *` | `RetentionWorker` |
| `CriticalResultEscalationJob` | `ScanOnceAsync` | `* * * * *` | `CriticalResultEscalationService` (+ 200-row/pass storm cap) |
| `AnomalyScanJob` | `ScanOnceAsync` | `* * * * *` | `AnomalyDetector` |
| `OAuthRefreshRotationJob` | `ScanOnceAsync` | `*/15 * * * *` | `OAuthRefreshRotationService` |
| `ModelDriftDetectionJob` | `RunAllTenantsAsync` | `0 */N * * *` (N from `RADIOPAD_DRIFT_CHECK_INTERVAL_HOURS`, default 6) | `ModelDriftDetectionService` |

All run on the `maintenance` queue. `ModelDriftDetectionJob` also keeps `GetStatusAsync` + the manual-trigger `RunAllTenantsAsync` for `DriftController`. Every sweep is idempotent (absolute-deadline scans, not tick counts), so moving 1-minute services onto a 15s scheduler poll preserves semantics.

### Kept as BackgroundServices (deliberately NOT migrated)

| Service | Why it is not cron-shaped |
| --- | --- |
| `AiJobRunner` | channel consumer, not scheduled |
| `AiJobRecoveryHostedService`, `SttModelProvisionHostedService` | boot-once semantics |
| `Hl7MllpListener` | socket listener |
| `AvailabilityMonitorService` | seconds-level probe + in-memory 5-minute sliding-window state (Hangfire's 1-min granularity + stateless instances would change semantics) |
| `SiemPushService` | 5s near-real-time flush with a stateful cursor + in-loop backoff |
| `UbagProviderDiscoveryHostedService` | 5-min loop, but its ~8s-after-boot first pass is startup-seed-critical |

## Notification channels (PR-N4)

`NotificationChannelDispatchJob` (`RadioPad.Api.Jobs`, `[Queue("critical")]`, enqueue-only) fans a
single **Critical-urgency** notification out to its out-of-app channels. `NotificationProducer.InsertAsync`
enqueues two jobs — `DeliverPushAsync` + `DeliverEmailAsync` — after the SSE publish, but ONLY when
`Urgency == Critical` and only via an optionally-resolved `IBackgroundJobClient` (a no-op under Testing,
exactly like the webhook decorator). Each delivery re-checks the recipient's `NotificationPreference`
(`PushEnabled` / `EmailEnabled`, defaulting to the entity defaults — push on, email off) before sending.

**PHI tiers (NOTIF-004)** — enforced by the shared `NotificationPhiTier` helper so no channel over-shares:

| Tier | Title | Body |
| --- | --- | --- |
| In-app row (authed, audited) | in-app headline | may carry modality / body-part / **FindingSummary**-class descriptor |
| OS toast / mobile push / email | generic category phrase | AT MOST modality + body-part — **never** FindingSummary, accession, name, MRN |
| Webhook (`WebhookDispatchJob`) | — | ids + category only, no Title/Body |

Because a `Notification` row carries no *structured* modality/body-part column (only the free-text in-app
Body, which may itself be a FindingSummary), the push/email-safe body collapses to a generic category
phrase — the conservative floor of "at most modality+body-part".

**Retry:** a not-configured push sender (`PushNotConfiguredException`, or no sender registered for the
device platform) is a CONFIG error, not transient — it audits `NotificationDeliveryFailed` and does NOT
re-throw, so the global jittered-retry filter never burns retries on it. A transport error is allowed to
throw so the retry filter re-runs the delivery. Email is best-effort (a false/failed send logs, never throws).

**Not built here:** **SMS** — no sender exists in the codebase; the per-`Platform` switch inside
`DeliverPushAsync` (via `PushSenderRegistry`) is the documented seam for a future `ISmsSender`. **SIEM** —
nothing to dispatch; the `NotificationCreated` / `NotificationDeliveryFailed` audit rows are already
streamed by `SiemPushService` (NOTIF-008).

**Producer wiring (PR-N4)** — the call sites that produce notifications after their state change + audit
(all wrapped so a producer failure never fails the request/job): critical-result create / escalate (manual
+ overdue sweep, deduped by `crit-esc:{id}`) / acknowledge; peer-review assign / sample / submit (author-
facing, blinding-safe — never names a blinded reviewer) / dispute; rulebook approve / deprecate / rollback
(→ `RulebooksManage` holders); template submit-for-review (→ `TemplatesApprove`) / approve (→ the submitter,
mined from the latest `TemplateSubmittedForReview` audit); and the system notices — `UbagOperatorAlertService`
(ItAdmin across all tenants, via `INotificationProducer.NotifyRoleAcrossTenantsAsync`) and `AnomalyScanJob`
(→ `SecurityManage` holders).

### Desktop notification click-through (verified platform limitation)

Clicking an OS toast on **Windows desktop** performs only the OS-default window activation — there is no
click-through / deep-link callback. This is a documented platform LIMITATION, not a deferral:
tauri-plugin-notification v2's Actions / click-activation API is **mobile-only** (iOS/Android) — verified
against `v2.tauri.app/plugin/notification` (2026-07-23). Fallback: focus + bell. On window focus /
`visibilitychange`→visible after any toast, PR-N5's `NotificationsProvider` refreshes the unread-count so the
bell badge is accurate the instant the user arrives; the toast's `LinkHref` target is then one bell-click
away. Future seam: were the plugin to ship desktop activation (or we adopt a WinRT-toast fork), the handler
is `navigate to notification.LinkHref` (the `?aiJob=` deep-link machinery already exists).

## Planned jobs

| Job | Trigger | Purpose |
| --- | --- | --- |
| Audit export rollup | Cron daily | Produce a signed JSON-Lines bundle for the tenant. |
| Webhook dispatcher | After audit append | POST event to tenant webhook endpoints. |
| AI cost rollup | Cron daily | Aggregate token counts per tenant for billing. |
| Rulebook golden retest | Push to `main` | Run all rulebook golden suites in CI (already in CI, not a runtime job). |
| Cleanup of orphaned drafts | Cron weekly | Archive drafts not touched in N days (configurable per tenant). |

## Queue choice (decided: Hangfire)

- **Hangfire** is the shipped engine for in-process background jobs as of PR-N1 — it stays in C# and uses the same database (Postgres in prod, InMemory on SQLite/desktop). The PRD names candidate stacks (NATS / River); the architecture doc + implementation standardize on Hangfire single-process.
- Alternatives considered and not taken: Quartz.NET, MassTransit (introduces RabbitMQ — heavier, only if multi-process scheduling is required).

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
