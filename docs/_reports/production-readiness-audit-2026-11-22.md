# Production-readiness audit — 2026-11-22

Scope: every stack (backend API + core, desktop/web/mobile frontend, Tauri/Rust, marketing, deploy, CLI, connectors).
Method: six read-only code audits + a route-by-route trace of every frontend call against the backend, then fixes.
**Nothing here was run against a live backend.** There is no `dotnet` on the dev machine: every C# change below is
uncompiled and CI is the verdict. Frontend: the 40 touched test files (229 tests) pass locally; typecheck/lint/build are CI's.

Route contract result: 259 frontend call sites (incl. 2 SSE, 1 WebSocket) → 259 match a backend route and method.
The defects below are payload/behaviour problems on routes that exist, not dead endpoints.

## Fixed in this pass (uncommitted, see PROGRESS Iter-66)

| Area | Was | Now |
| --- | --- | --- |
| SignalR dictation/transcript events | `Clients.All` broadcast to every connection; hub unauthenticated | report-group only; **hub no longer mapped** (`Program.cs`); live push moved to the authenticated tenant SSE stream (`dictation` event) |
| Transcription of a missing audio file | fake `{1,2,3,4}` stream transcribed, marked Completed | dictation marked Failed, no engine runs |
| Mobile recorder | uploaded a fabricated "mock audio" blob; showed "recording" with no microphone | refuses to start without a mic, never uploads an empty take |
| `MockAiAdapter` | registered everywhere, canned "No acute intracranial abnormality" impression | dev/test/`RADIOPAD_DEV_SEED=1` only; Production fails closed; providers page hides it |
| `POST /api/prompts/test-golden` | `actual = expected` — every case passed | real `ReportValidator` via `ValidationPackService.EvaluateAsync` |
| Hosted AI output cap | hard-coded 1024 tokens → long reports cut mid-sentence | `AiCompletionRequest.DefaultMaxOutputTokens = 4096`; Anthropic/Vertex now honour `request.Temperature` |
| Regulated-feature toggles | UI showed OFF, server enforced ON (first click wrote the opposite) | UI matches `RegulatedFeatures.IsEnabled` (opt-out) |
| Billing buttons | posted `price_*_placeholder` to Stripe | explicit "checkout not configured" error |
| CORS | `X-Total-Count` not exposed → worklist silently stopped at page 1 cross-origin | `WithExposedHeaders("X-Total-Count")` |
| Signature list | `radiologistEmail` never sent → blank "Report author" | sent by `GET signatures` and `POST sign` |
| Dictation audio playback | `<audio src>` = server path / unauthenticated relative URL | authenticated blob URL; visible error state |
| Desktop pages | audit-verify false tamper alarm (>500 events); hard-coded template modalities; RADS keys 404; "Next case" capped at 100 rows; fabricated prior comparison; fake "live audio test" | server-side chain verify; catalogs; real keys; `listAll`; ErrorState; removed |
| Web/mobile/shared pages | security alerts filter never matched; random Radiology ID; hard-coded 0 columns; fake `$0.0000`/"candidate" badge; dead "Backup methods"; "provider policy gates" tooltip; dead `GenerationOverlay` | fixed/removed (see git diff) |
| `openapi.yaml` | wrong servers; two `/save` routes that do not exist | production server; routes folded into `POST /api/rulebooks` / `/api/templates` |

## Open — needs an owner decision or infra work

Security
1. **Desktop sidecar runs `ASPNETCORE_ENVIRONMENT=Development`** (`desktop/src-tauri/src/sidecar_manager.rs:43`): public constant token-signing secret, `dev` tenant + MedicalDirector seed user, mock provider, Swagger, dev encryption key. Fix: per-install random `RADIOPAD_AUTH_SECRET` from the OS keyring; longer term a "Desktop" environment.
2. Desktop adopts any process answering on `127.0.0.1:7457` (`sidecar_manager.rs:142`, `backend_health.rs:74`). Fix: per-launch token echoed by the health probe.
3. A tenant admin can set their own `Plan` and `CmkVerified` via tenant settings (`OtherControllers.cs` ~1911/1936) — billing bypass. Frontend has a "Change plan manually" box that uses it.
4. `/api/ingest/*` and `/api/integrations/orthanc/*` are not in `IsPublicApi` → 401 in Production; Orthanc bridge defaults tenant `dev`.
5. `DictationSettingsController`: `ControllerBase`, no RBAC, one process-wide singleton, **nothing reads it**. Either persist per tenant + wire into transcription, or delete controller/page. **→ Removed 2026-11-22 (owner decision): controller, service, tests and admin page deleted.**
6. `POST /api/drift/run` returns every tenant's provider/rulebook ids to one tenant admin (`DriftController.cs:46`).
7. HL7 listener picks the tenant from unauthenticated MSH-4 (`Hl7MessageHandler.cs:68`).
8. UBAG default allow-list includes `mock`; UBAG host falls back to `ubag.polytronx.com` (`UbagProviderAdapter.cs:187`, `Program.cs:145`).
9. `web-deploy-images.yml` runs `ssh-keyscan` each deploy (host key not pinned). Public Android download is the **debug** APK (`attach-android-release.yml:131`).

Durability / infra
10. Production = SQLite + **in-memory Hangfire** (`deploy/vps/docker-compose.yml:45`, `HangfireSetup.cs`): queued push/email/webhooks lost on every restart. A `radiopad-postgres` service exists unused (repo compose only — the live VPS compose has no Postgres). **Re-queue-on-startup was investigated and NOT implemented:** neither job has a durable per-delivery marker (see "Needs a schema change" below), so a sweep over existing columns would re-send push/email/webhooks on every pass.
11. Dictation audio is written under `/app/uploads` (not a volume) → lost on redeploy; transcription is a fire-and-forget `Task.Run`; engine defaults to `medASR-6gram`, unavailable on the hosted server (`MobileReportingService.cs`).
12. In-memory stores on production paths: dictation audit store (`Program.cs` ~297), cross-check job store, HL7 outbox (SR→HL7 is acknowledged but never sent), `NoopOAuthTokenIssuer` (stored refresh tokens never rotate).
13. The API still returns the server `StoragePath` in dictation DTOs (information leak; client no longer uses it).

Clinical integrity
14. Signature hash covers ids/time/note, **not report text**; `PATCH` only locks bindings; addenda are not in any export and are purged by the retention sweep.
15. HL7/FHIR export: `PatientRef` never stored, PID-3 "UNKNOWN", random accession fallback, OBR status hard-coded `F`.
16. `GET /prior` / `compare-prior` pick "prior" by body part / accession prefix, not patient — can show another patient's report.
17. 92 generated rulebooks (`content/build-library.mjs`) are seeded `status: approved` with an invented committee owner.
18. Quality/Validation pages call `POST /validate`, which flips clean Drafts to Validated.
19. "Model drift" scores the deterministic validator, not a model (`ModelDriftDetectionJob.cs:172`).
20. Prompt Studio version history/diff are stubs (`Iter31Controllers.cs` `ListVersions`/`Diff`); the golden tab ignores unsaved prompt edits. **→ Removed 2026-11-22 (owner decision): `versions`/`diff` actions deleted; the golden tab's unsaved-edit limitation remains.**
21. "Rewrite in my style" always 400 (no `Samples`/`in_my_style` on the backend). **→ Removed 2026-11-22 (owner decision): UI panel and client call deleted.**
22. Rulebook editor offers 13 rule ids the validator ignores (`RulesPanel.tsx` vs `ReportValidator.cs`).
23. MCP registry: manifest signature never verified yet shows "trusted"; Custom tools never execute (HTTP 200 `status:"error"`); connector manifests name classes that do not exist. PACS vendor adapters (Sectra/Visage/Carestream) are not wired to anything. **→ Removed 2026-11-22 (owner decision): the whole MCP registry (controller, sandbox, verifiers, `mcp-connectors/`, `scripts/McpSignTool`, tests) deleted; the PACS vendor adapters remain unwired.**
24. Analytics KPIs are proxies (`impressionsLightlyEdited = aiDraftsAcknowledged`, edit distance 0.0, "time saved" = turnaround).
25. Addendum `note` is collected by the UI and dropped by `AddendumDto`. No `stop_reason` check for truncated provider output.
26. `lm_6gram.fst` has an empty SHA-256 (`LocalSttModels.cs:37`); FIDO MDS roots file is `[]`; perf-smoke harness env vars are read by nothing.
27. CLI: `eval` posts to a route that does not exist; plugin `verify` hashes the manifest including its own digest; MCP server defaults tenant `dev`; `PhiGuard` still blocks PHI client-side (operator removed the gate).
28. `RADIOPAD_LOCAL_FORMATTER_ENABLED=1` is set but `NullLocalReportFormatter` always 503s.

Frontend leftovers
29. Offline drafts page: nothing writes drafts. Worklist wait column uses `updatedAt`. Dashboard/quality KPIs capped at 100 rows. Audit labels cover 11 of ~62 actions. Notifications inbox not live. Marketplace: no loading/error state, Review tab shown to every role. Device-pairing "On-device active" is static. `CatalogManager` error → empty list. `DictationAudioCard` hard-codes an engine fallback list. Biometric lock fails open and has no enable UI. SSO page lists only the caller's own passkeys. `/governance` is an orphan duplicate of `/admin/governance`.
30. CI does not set `NEXT_PUBLIC_STRIPE_PRICE_*` or `NEXT_PUBLIC_ENABLE_SSO` (`hostinger-deploy.yml`): upgrade buttons now say "not configured"; SSO button stays hidden.

Marketing / legal (known placeholders + new)
31. Placeholder mailboxes, `[RadioPad legal entity]`, counsel TODOs, fictional blog bylines, `SECURITY.md` example address. The DPA/privacy/security copy lists Claude/Azure/Ollama/vLLM only, but UBAG Gemini/DeepSeek are seeded enabled for every org. Pricing table contradicts backend plan gating. iOS is marketed but only an unsigned archive exists. Demo form has no endpoint (falls back to `mailto:`). Three fictional testimonial photos are deployed unreferenced.

## Needs a schema change (re-queue of push/email/webhooks)
`Notification` has only ReadAt/AcknowledgedAt/DedupeKey — no per-channel sent state, and success is not audited (only `NotificationDeliveryFailed`). Webhooks have no delivery row at all (`TenantWebhookEndpoint.FailureCount` is per endpoint, not per event). To make a re-queue sweep safe add (EF migration, needs `dotnet ef` in CI): `Notification.PushDeliveredAt` / `EmailDeliveredAt` (nullable, set by `DeliverPushAsync` / `DeliverEmailAsync` on success) and a `WebhookDelivery(EndpointId, AuditEventId, DeliveredAt?, Attempts)` row written by `WebhookEnqueueingAuditLog` before the enqueue and updated by `DeliverAuditEventAsync`. Then a batch-capped recurring sweep (registered in `UseRadioPadRecurringJobs`) re-enqueues rows with `DeliveredAt IS NULL` older than ~2 min. Moving Hangfire to a persistent store would remove the need for the sweep.

## Not verified
- All C# (never compiled); the SSE `dictation` path end to end; the Windows desktop; anything against the live VPS.
- `frontend/lib/api.ts` (≈3.9k lines) and `ReportClient.tsx` were pattern-checked, not read in full; `Scim`/`WebAuthn`/`FindingsLibrary` controllers were not read line by line.
- "No hardcoded/demo dependency remains" cannot be proven by search; this list is what the audits found.

## MySQL provider (stage 1) — code only, nothing deployed

Goal: let the API use a Hostinger MariaDB/MySQL database (API stays on the VPS). Opt-in; SQLite stays the default and
the desktop sidecar is unchanged. **All of it is uncompiled and untested** (no dotnet here) — CI is the verdict.

What exists
- `DbProviderSelector` (`RadioPad.Infrastructure/Persistence`): `RADIOPAD_DB_PROVIDER` = sqlite | postgres | mysql | mariadb;
  unset keeps the legacy sniff (`Host=`/`Server=` → Postgres, else SQLite). Used by EF (`Program.cs`) and Hangfire.
- `PrepareMySqlConnectionString`: in production refuses SslMode other than Required/VerifyCA/VerifyFull
  (escape hatch `RADIOPAD_DB_ALLOW_INSECURE=1`, UNSAFE), forces `UseAffectedRows=false` (AiJob concurrency token),
  caps the pool at 20 (Hostinger: 75 connections/user).
- `MySqlRadioPadDbContext : RadioPadDbContext` registered as the `RadioPadDbContext` service for MySQL; it owns a
  separate migration history in `Migrations/MySql/` (generated by CI, empty today → MySQL boot fails with a clear message).
- MySQL-only model shaping in `RadioPadDbContext.ConfigureMySql` (utf8mb4, case-sensitive collation on the unique
  keys that are case-sensitive on SQLite, 190-char cap on indexed strings, decimal(18,6), no filtered index).
- Hangfire on `Hangfire.MySqlStorage` 2.0.3 (persistent jobs → makes the re-queue-on-restart work unnecessary on MySQL).
- Boot waits up to ~25 s for a hosted DB (Postgres/MySQL) before migrating; dictation audio falls back to `/data/uploads`.

Not verified / known risks
- Everything compiles only in CI. Pomelo 8.0.2 + EF 8.0.10 compatibility is from NuGet metadata, not a build.
- `Hangfire.MySqlStorage` 2.0.3 (last published 2020, built for Hangfire 1.6 / MySqlConnector 1.x; we run 1.8.24 / 2.3.x):
  the type and option names were read from the package's metadata, behaviour is unproven.
- Whether Hostinger offers TLS on remote MySQL, its row format, RTT from the VPS, and BAA/DPA terms are unknown.
- `Database.CanConnectAsync()` is the "unreachable" probe; a missing database also counts as unreachable.
- Deviation from the original plan: no separate `RadioPad.Infrastructure.MySql` project — the Docker build copies each
  csproj by name from `deploy/vps/api.Dockerfile`, and editing that file triggers a production deploy on push.

## MySQL provider (stage 2) — copier, CI, smoke tests (still uncompiled)

What exists
- `SqliteToMySqlCopier` (`RadioPad.Infrastructure/Persistence`) + a copy mode at the very top of `Program.cs`
  (`RADIOPAD_COPY_FROM_SQLITE=<file>`): opens the SQLite file read-only, walks every entity type in FK order, streams
  batches of 500 through EF (so value converters, owned types and the AES-GCM column encryption behave exactly as in the
  app), **refuses** a target that is not blank/migrated, then compares per-table row counts. `RADIOPAD_COPY_DRY_RUN=1`
  counts only. It never logs row values; failures print the error class / MySQL error number only. Copy mode needs the
  same `RADIOPAD_COLUMN_KEY_REF`/`_WRAPPED`/`_KEK` as the API (env:/local: KMS schemes only).
- Test support in `RadioPad.Api.Tests/MariaDb/`: `[MySqlFact]` (skipped unless `RADIOPAD_TEST_MYSQL` is set),
  `MySqlTestDatabase` (a `radiopad_test_<guid>` database per test), `MySqlSmokeTests` (model DDL on a blank MariaDB,
  migrations once they exist, case-sensitive unique indexes, DateTimeOffset instant round trip incl. a +05:00 offset,
  matched-row concurrency token, Hangfire.MySqlStorage jobs surviving a storage restart) and `SqliteToMySqlCopierTests`.
- `RadioPadAppFactory` can run the whole API on MariaDB per host (provider chosen through `UseSetting`, which
  `Program.cs` now reads via `IConfiguration`) when `RADIOPAD_TEST_MYSQL` is set — used by the informational CI step.
- Workflows: `mysql-migrations` (workflow_dispatch; generates the MySQL migration with dotnet-ef, builds, applies it to a
  MariaDB service, then pushes it to a new `bot/mysql-migrations-<run id>` branch) and `ci-mysql` (PR/push on `backend/**`;
  required: `Category=MySql`; informational: the full suite on MariaDB).

Not verified / known risks
- Nothing here was compiled or run. Workflow YAML parses but was not executed; `dotnet ef ... --startup-project` against the
  minimal-hosting `Program.cs` (with the new top-of-file copy block) is the first thing to watch in the first run.
- DateTimeOffset precision: SQLite keeps 100 ns ticks, MySQL `datetime(6)` keeps microseconds — values lose their last digit.
  The audit hash chain does not hash timestamps, but `VerifyChainAsync` orders by `CreatedAt` then `Id`, so two events
  inside the same microsecond could reorder. Run `GET /api/audit/verify` after the cutover.
- `ExecuteSqlRawAsync("SET FOREIGN_KEY_CHECKS=0")` is only issued when the model has a self-reference or FK cycle.
- Source strings longer than the 190-char MySQL cap on indexed columns (e.g. a very long rulebook id) fail the copy with
  MySQL error 1406 — pre-check `MAX(LENGTH(col))` on the SQLite copy if in doubt.
- The informational full-suite run will show SQLite-isms (raw `Data Source=` access to `factory.DbPath`, SQLite-only
  assumptions); it is a to-do list, not a gate.
