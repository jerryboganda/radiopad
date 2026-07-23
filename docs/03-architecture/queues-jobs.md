# Queues & Background Jobs

**Status:** Fully shipped — async AI generation (durable rows + SSE + streaming + UBAG re-attach + per-tenant fairness), the Hangfire cron platform (5 migrated crons + 4 new maintenance jobs), and the NOTIF-001 notification pipeline (producers + channels + inbox)  ·  **Owner:** Engineering  ·  **Last Updated:** 2026-07-23

Most operations remain synchronous on the request thread. Two exceptions run on job platforms: **async AI report generation** (a first-class durable subsystem, below) and **cron-shaped background work**, which runs on **Hangfire** (below). Every job named in this document is shipped — nothing here is a roadmap.

## Shipped: durable AI generation jobs

Impression/rewrite (`kind = "ai"`), whole-report (`kind = "generate"`), dictation-cleanup (`kind = "ai"`, `mode = "cleanup"` — the input-carrying route), and cross-check medical review (`kind = "crosscheck"`) generations are minutes-long provider calls that must survive a dropped tab, a proxy timeout, and a process restart. They run as durable jobs:

- **Durable row:** one `AiJobs` row per attempt (`RadioPad.Domain.Entities.AiJob`; migration `20260723090000_AddAiJobs`). Status vocabulary `queued | running | ok | error | cancelled`. Indexed by `(TenantId, UserId, CreatedAt)`, `(TenantId, ReportId, Status)`, `(Status, CompletedAt)`.
- **Hot cache:** the in-memory `AiJobRegistry` serves the ~2s poll from memory so a poll never touches the DB. `AiJobCoordinator` writes through **registry-first, DB-second** on every transition; a crash between the two leaves a `running` row that boot recovery marks `server_restart` — the safe direction (never a phantom success).
- **Runner:** `AiJobCoordinator.SubmitAsync` writes a `queued` row and enqueues an `AiJobWork` onto an unbounded `Channel<AiJobWork>`. The hosted `AiJobRunner` (`BackgroundService`) dequeues with bounded parallelism (`AiJobs:MaxConcurrency`, default 4). Each job runs under a per-job CTS = linked(`ApplicationStopping`) + safety timeout (`AiJobs:SafetyTimeoutSeconds`, default 600) + a registry-registered cancel signal.
- **Per-tenant fairness (PR-B2):** a `ConcurrentDictionary<Guid, SemaphoreSlim>` gate per tenant (created lazily, never disposed), capped at `AiJobs:PerTenantMaxConcurrency` (default 2, clamped to `1..MaxConcurrency`). Gates acquire **tenant-first, then global** — never the reverse, which is load-bearing: acquiring the global gate first would let a flooding tenant's excess jobs sit holding global slots while blocked on their own per-tenant cap, reintroducing the exact starvation the gate exists to prevent. A single flooding tenant can occupy at most `PerTenantMaxConcurrency` of the `MaxConcurrency` global slots, leaving the rest for everyone else. The dequeue thread never waits on either gate — each dequeued item spawns its own dispatch task immediately, so one saturated tenant never head-of-line-blocks another tenant's job sitting behind it in the channel; `CancelAfter(SafetyTimeoutSeconds)` starts only after both gates are held, so time spent queued behind a busy tenant is never charged against the job's own safety budget.
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

Cron-shaped background work runs on **Hangfire** (single-process, in-C#, on the same database). Bootstrap lives in `RadioPad.Api.Jobs.HangfireSetup` (`AddRadioPadHangfire` / `UseRadioPadRecurringJobs`); the job classes live in `RadioPad.Api.Jobs.*Job`. **Queue-engine choice:** the PRD names candidate stacks (NATS / River) as options to evaluate; this architecture doc and the shipped implementation standardize on Hangfire — it stays in C#, needs no extra infrastructure process, and reuses the same database connection EF Core already has open (Postgres in prod, InMemory on SQLite/desktop). Alternatives considered and not taken: Quartz.NET, MassTransit (introduces RabbitMQ — heavier, only justified if multi-process scheduling becomes a requirement).

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

## Shipped: maintenance & cost/audit cron jobs (PR-N2)

Four new Hangfire recurring jobs (`RadioPad.Api.Jobs`, all on the `maintenance` queue, all idempotent by natural key so a retry or a missed InMemory schedule after a restart is never harmful) plus one migration (`20260724090001_AddCronPlatformTables`: `TenantWebhookEndpoints`, `AiUsageRollups`, `AuditExportBundles`, `Report.ArchivedAt`, `Tenant.DraftAutoArchiveDays`, `Tenant.CriticalNotificationCategoriesCsv`):

- **`AuditExportRollupJob`** (`0 2 * * *`, 02:00 UTC daily) — per-tenant fan-out producing a signed, SIEM-shaped export bundle: one PHI-minimized JSONL line per audit event (`id, tenantId, userId, reportId, action, actionCode, createdAt, integrityHash` — `DetailsJson` is deliberately never included) plus an HMAC-SHA256 signature over the bundle body, keyed by `AuditExport:SigningKey` (unset → the bundle is stored unsigned, logged, never blocked). Idempotent by `(TenantId, Date)` — a re-run upserts the same `AuditExportBundle` row in place. Retrieved via `GET /api/audit/exports` (list) and `GET /api/audit/exports?date=YYYY-MM-DD` (full bundle).
- **`WebhookDispatchJob`** (`[Queue("default")]`, enqueue-only — not itself scheduled) — delivers ONE audit event to ONE tenant webhook endpoint. Enqueued by `WebhookEnqueueingAuditLog`, a decorator over `IAuditLog` that appends the row first (through the real `EfAuditLog`, stamping id + integrity chain), then best-effort enqueues one job per active, audit-subscribed `TenantWebhookEndpoint`. Payload is PHI-minimized (`id, action, tenantId, createdAt, integrityChain` — never `DetailsJson`, Title, or Body), signed `X-RadioPad-Signature: sha256=<hmac-hex>` (HMAC-SHA256 over the UTF-8 body, keyed by the endpoint's own secret). An endpoint auto-disables (`DisabledAt` set, `Active = false`, an `AuditAction.WebhookEndpointDisabled` row appended) at **20 consecutive failures** — delivery is then abandoned rather than retried forever against a dead receiver.
- **`AiCostRollupJob`** (`30 1 * * *`, 01:30 UTC daily — before the audit export, after the day's traffic) — aggregates `AiRequests` grouped by `(TenantId, Provider, Model)` into a per-day `AiUsageRollup` upsert keyed by the 4-tuple `(TenantId, Date, Provider, Model)`. Retrieved via `GET /api/usage/ai-rollup?from=&to=` (defaults to the last 30 days).
- **`OrphanedDraftCleanupJob`** (`0 3 * * 0`, 03:00 UTC every Sunday) — **opt-in per tenant** via `Tenant.DraftAutoArchiveDays > 0` (0/unset = never runs for that tenant). Sets `Report.ArchivedAt` only — the report `Status` enum is deliberately untouched, so this is additive/reversible, never a state-machine transition. Capped at 500 reports/tenant/run (`MaxPerTenantPerRun`); naturally idempotent (`ArchivedAt == null` is part of the selection filter, so an already-archived draft is simply skipped next run). Worklist queries add `?archived=` (`false` = today's default view, `true` = the recovery view); `PATCH /api/reports/{id}/unarchive` clears `ArchivedAt` to restore a draft. Every archive appends an `AuditAction.ReportDraftArchived` row.

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

## Config keys (all read-with-default; unset = the default shown)

| Key | Default | Governs |
| --- | --- | --- |
| `AiJobs:MaxConcurrency` | 4 | Global cap on concurrently-running AI jobs (`AiJobRunner`) |
| `AiJobs:PerTenantMaxConcurrency` | 2 | Per-tenant cap within the global one (PR-B2 fairness), clamped `1..MaxConcurrency` |
| `AiJobs:SafetyTimeoutSeconds` | 600 | Per-job hard CTS timeout, starts after both fairness gates are held |
| `AiJobs:SseKeepAliveSeconds` | 15 | `: keep-alive` comment cadence on `/api/events/stream` |
| `AiJobs:SseSubscriberBuffer` | 64 | Per-subscriber bounded drop-oldest channel depth on the SSE bus |
| `AiJobs:StreamPublishMinIntervalMs` | 150 | Min time between coalesced `progress`/`partial` bus publishes while a token stream is running |
| `AiJobs:StreamPublishTokenBatch` | 20 | Min new tokens between the same coalesced publishes (whichever threshold trips first) |
| `AiJobs:PartialBufferMaxChars` | 262144 | Registry partial-text tail cap per active job |
| `AiJobs:ReattachTimeoutSeconds` | 300 | Budget for a detached UBAG re-attach poll before falling back to `server_restart` |
| `AiJobs:ReattachPollSeconds` | 2 | Re-attach poll cadence (mirrors the live UBAG poll) |
| `AuditExport:SigningKey` | unset | HMAC-SHA256 key for daily audit export bundles; unset → bundles are stored unsigned |
| `RADIOPAD_DRIFT_CHECK_INTERVAL_HOURS` | 6 | `ModelDriftDetectionJob` cron interval, clamped `1..24` |

## Frontend consumption

The Next.js frontend is a first-class consumer of everything above, not just the report-editor sync
endpoints:

- **`lib/sse.ts` + `lib/events.ts`** — the incremental SSE parser and the ref-counted `EventStreamManager`
  singleton (`hostedEvents`) that opens `GET /api/events/stream` via `fetch()`/`ReadableStream` (never
  `EventSource` — it cannot send the bearer/`X-RadioPad-*` headers this app authenticates with), with
  jittered reconnect backoff and a 45s silence-abort.
- **`components/jobs/JobsProvider.tsx`** — the single shared tracked-job list + poll ticker. Pauses hosted
  polling while the SSE stream is healthy (local/sidecar jobs keep polling — no local stream yet); a
  first-terminal-wins reducer makes duplicate delivery across SSE + poll + reconnect-rehydrate safe by
  construction. `trackExternal(job)` registers a job created OUTSIDE the normal `submit()` path — used by
  the cross-check audio half (a direct sidecar multipart call) and the hosted review half it triggers.
- **Cross-check (FE-PR6)** is a two-job pattern, not one: a LOCAL sidecar job (audio/ASR re-run, polled via
  a dedicated `api.reports.crossCheckStatus` branch in `pollOne` — a different endpoint than
  `local-generate` jobs) triggers a HOSTED review job on success (`api.reports.submitCrossCheckJob`). Both
  are suggestion sets only — `frontend/lib/dictation/crossCheckJob.ts` holds the pure poll-patch mapping and
  the two-job → badge-state derivation; corrections are merged and handed to the SAME per-item Accept/Reject
  panel the report editor already had, never bulk/auto-applied.
- **`components/notifications/NotificationsProvider.tsx`** subscribes directly to the same `hostedEvents`
  singleton for `notification` events (typed, not a re-dispatched window event) and falls back to a 60s
  unread-count poll while the stream is down.

See [design.md](../02-design/design.md) for the visual/interaction spec (`.rp-jobs-*`, `.rp-stream-preview`,
`.rp-inbox-*`) and [PROGRESS.md](../../PROGRESS.md) for the full PR-by-PR build log.
