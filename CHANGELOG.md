# Changelog

All notable changes to RadioPad will be documented in this file.

The format is based on [Keep a Changelog 1.1.0](https://keepachangelog.com/en/1.1.0/), and this project adheres to [Semantic Versioning](https://semver.org/).

## [Unreleased]

### Changed
- **Desktop-first surface specialisation — one frontend, three scoped apps.**
  The single Next.js frontend now builds into three surfaces selected by a
  `RADIOPAD_SURFACE` build flag (`build:{desktop,web,mobile}` →
  `frontend/out-<surface>`): the **desktop** app is the entire reporting product;
  the **web** app is master-admin / platform operations only (no reporting, no
  clinical login — clinical users get a "download the desktop app" interstitial);
  the **mobile** app is a dictation companion only. Routes are organised into App
  Router route groups `app/(desktop|web|mobile|shared)/` and the build stages
  non-target groups out so each shell physically ships only its own routes. Tauri
  now bundles `out-desktop`, Capacitor `out-mobile`.

### Added
- **Phone dictation companion.** The mobile app pairs to a live desktop session
  (short code / QR) and streams voice dictation into the report open on that
  desktop via a cloud relay (`/api/companion/*`, `/ws/companion`). The desktop
  shows a "Pair phone" host panel; received dictation is inserted into the
  focused report section through the same path as local dictation. No editing or
  signing happens on the phone; RadioPad never auto-signs.

### Removed
- **Removed the standalone mobile reporting pages** (`/mobile/dictate`,
  `/mobile/reports/edit`, `/mobile/reports/sign`) — superseded by the pairing
  companion above.

### Removed
- **Removed all Whisper speech-to-text models and engines.** The Whisper
  large-v3-turbo, small.en, and medical large-v3 models, the `WhisperNet` /
  `MedicalWhisper` engines, the `Whisper.net` / `Whisper.net.Runtime` packages
  and embedded native DLLs, the model-manager catalog cards, and the Whisper
  legs of the dictation ensemble and Cross-Check pass are all gone. On-device
  STT now runs on Parakeet (primary) plus the platform speech engines (Windows
  Speech / Edge); the ensemble and Cross-Check reconcile whatever engines remain
  available. The Whisper engine smoke + ensemble smoke tests and the
  `desktop/whisper.md` doc were removed; the `desktop-stt-smoke` workflow now
  validates only the Parakeet engine.
- **Removed GitHub Copilot integration.** The `github-copilot-sdk` /
  `github-copilot-cli` provider adapters, server-side Copilot CLI execution,
  the desktop native Copilot session runner, and all related admin/session
  surfaces have been dropped from the active codebase.

### Fixed
- **Report editor — "Generate impression" no longer drafts from empty findings.**
  Section textareas persist on blur, but the AI endpoint
  (`POST /api/reports/{id}/ai`) reads findings from the DB, so clicking
  *Generate impression* straight from typing raced the save and the model
  replied "No findings were provided…". `frontend/app/reports/[id]/ReportClient.tsx`
  now flushes the on-screen editor state with a synchronous PATCH (reading the
  live value from refs, so the desktop-event/voice-command paths can't clobber
  newer text) before any AI call. Covered by
  `frontend/__tests__/reportGenerateFlush.test.tsx`.

### Changed
- **Iter-45 — Radiologist-friendly UI sweep**: project-wide rewrite of
  user-facing copy and a layout fix so admin/settings surfaces fill widescreen
  monitors. Highlights:
  - `frontend/app/shell.css` — `.rp-container` cap raised from `1280px` to
    `1600px`; new `.rp-page-grid` (form + sticky 320px help sidecar, collapses
    under 1080px), `.rp-help` / `.rp-help-title` (sidecar cards), and
    `.rp-advanced` (styled `<details>` to hide technical fields behind a
    single "Show advanced options" disclosure).
  - `frontend/app/admin/settings/page.tsx` — full rewrite as the canonical
    friendly template: plain-English headings ("Workspace settings", "AI
    safety check", "Your subscription", "Hospital connections", "Encryption
    key (optional, for compliance teams)"), severity rendered as a question
    ("How strict should the safety check be?") with friendly option labels
    ("Just show a note" / "Show a warning (recommended)" / "Block signing
    until reviewed"), and a right-hand help sidecar ("What you control here"
    / "Need help?" / "Privacy & safety"). All PRD codes, env-var scheme
    samples (`env:NAME`, `aws:arn:…`, `azkv:…`, `gcp:…`), and API paths
    removed from user-visible copy.
  - Same friendly-copy + jargon-removal sweep applied to
    `frontend/app/providers/page.tsx`, `frontend/app/audit/page.tsx`,
    `frontend/app/offline/page.tsx`,
    `frontend/app/admin/validation-packs/page.tsx`,
    `frontend/app/analytics/page.tsx`, `frontend/app/validation/page.tsx`,
    `frontend/app/marketplace/page.tsx`, `frontend/app/terminology/page.tsx`,
    `frontend/app/admin/governance/page.tsx`,
    `frontend/app/admin/model-eval/page.tsx`, `frontend/app/reports/page.tsx`,
    `frontend/app/rulebooks/page.tsx`, `frontend/app/templates/page.tsx`,
    `frontend/app/prompts/page.tsx`, and
    `frontend/app/admin/providers/[id]/ProviderOAuthAdminClient.tsx`.
    `COMPLIANCE_LABELS` in `frontend/lib/api.ts` re-labelled (e.g.
    "Sandbox" → "No patient data", "PHI-approved" → "Safe for patient data",
    "Local only" → "Runs on-site").
  - No new accent colours, no Tailwind/MUI, no dark mode, no emoji icons —
    only locked design tokens (`--accent: #c96442`, `--bg`, `--text`,
    `--border`, semantic green/blue/purple/red/amber). Test anchors
    (`panel-model-inventory`, `panel-eval-form`, `select-rulebook`, etc.)
    preserved.
  - Deployment: static export rebuilt and pushed to the `radiopad-web`
    container at `/usr/share/nginx/html/`, nginx reloaded. Origin returns
    HTTP 200 on every route post-deploy.

### Added
- **Iter-50 - UBAG governed automation hub**: added `ubag` as a sandbox-only
  provider adapter and a backend-only automation hub for production UBAG
  (`RADIOPAD_UBAG_BASE_URL`, API version `2026-05-22`). New `/api/ubag/*`
  admin endpoints expose gateway/browser/target readiness, single non-PHI job
  submission, job polling, and the fixed ordered workflow
  `chatgpt_web -> gemini_web -> deepseek_web`. The adapter and Hub reject PHI
  and secret-shaped prompts, send idempotency keys, audit only metadata and
  hashes, and do not automate provider login, CAPTCHA, 2FA, consent, cookies,
  or credentials. Frontend adds `/admin/ubag`, sidebar navigation, API client
  types, and a UBAG provider preset (`Sandbox`, default target `gemini_web`).
- **Iter-49 — settings/security end-to-end closeout**: tenant settings are now
  documented and implemented as partial-safe saves with `ipAllowlistJson`, PACS
  vendor, SCIM, retention, CMK, and validation fields aligned across backend,
  frontend, OpenAPI, and API docs. Per-tenant and global IP allowlists fail
  closed on malformed configured CIDRs instead of silently disabling the gate.
  SAML's unsigned-assertion dev escape hatch is ignored in Production, OIDC
  authorize URLs include `RADIOPAD_OIDC_AUDIENCE` when configured, provider
  API-key secret refs now use the column encryption converter, and lexicon CSV
  import reuses the secure-token auth retry path. Browser login now preserves
  sanitized `returnTo` targets, and the admin PACS page exposes the tenant PACS
  vendor selector.
- **Iter-48 — browser auth / live panel hardening**: magic-link consume now
  sets an HttpOnly `radiopad_session` cookie while still returning an `rp_`
  bearer for native shells, production magic-link requests require configured
  SMTP plus `RADIOPAD_PUBLIC_WEB_URL` and never expose raw `devLink` URLs,
  client-supplied callback origins are ignored in Production, logout clears
  the server cookie and local secure token cache, SSO UI is hidden unless
  explicitly enabled, and Caddy/nginx production configs now proxy SAML/SCIM
  standards routes to the API. Production Swagger is disabled unless
  `RADIOPAD_ENABLE_SWAGGER=1`. Production column encryption now fails startup
  without a configured key ref and wrapped data key, public magic-link requests
  resolve the request-body tenant for per-tenant IP allowlists, Production
  `X-Forwarded-For` trust requires trusted proxy CIDRs, OIDC-mapped identities
  are checked against active/locked RadioPad users, and operational helper
  scripts no longer contain live mailbox credentials or personal test accounts.
  Added the missing `TenantSettings.PacsVendor` migration so upgraded SQLite
  databases no longer 500 on billing/status or PACS-backed settings reads.
  Browser sign-out now posts to the trailing-slash logout route to avoid the
  static-export redirect aborting the cookie-clearing request.
- **Iter-47 — provider/auth/release hardening close-out**: tightened AI provider
  and auth edges found by the parallel QA pass. Report PHI detection now scans
  all prompt-bearing report fields and exact prompt bodies; hosted provider
  endpoint overrides are allowlisted per vendor before credentials are attached;
  RadioPad `rp_` bearers are signed expiring payloads with `iat` / `exp` / `jti`;
  CLI providers require binary allowlists in production; Codex now runs through
  `codex exec --sandbox read-only -`. The provider admin pages gained canonical
  loading/error/empty states and sidebar navigation, golden-case docs/CLI now
  align on `expectFlagged`, and release workflows no longer contain placeholder
  AWS account / TODO jobs.
- **Iter-46 — AI provider catalog hardening**: exposed `gemini-cli` and
  `openai-compatible` as aligned backend/frontend/CLI/OpenAPI provider options.
  CLI adapters now have prompt-free health probes, and OpenAI-compatible health
  probes `/v1/models` without sending clinical content.
- **Iter-46 — provider security completion pass**: production API identity now
  requires validated OIDC or `rp_` bearer tokens unless `RADIOPAD_DEV_HEADERS=1`
  is explicitly enabled; CLI providers refuse PHI/secret-shaped prompts at
  adapter level, run with a scrubbed environment, and Codex CLI is disabled
  unless `RADIOPAD_CODEX_CLI_ENABLED=1`. OpenAI-compatible endpoints are
  validated against SSRF/private-network use unless `LocalOnly`, provider
  config saves and health checks append audit rows, and provider admin UI now
  has loading/empty/error states plus test coverage.
- **Iter-36 — Stripe webhook hardening**: `POST /api/billing/webhook` now
  handles `invoice.payment_succeeded` (clears `gracePeriodUntil` +
  `suspendedAt`, sets `subscriptionStatus = "active"`) and
  `invoice.payment_failed` (opens a 7-day `gracePeriodUntil` on the first
  failure, escalates to `suspendedAt` once that window has elapsed). Both
  events flow through the same dedupe table (`StripeWebhookEvents`) and the
  same outer transaction the existing handler used; the new mapping logic
  lives in `SubscriptionLifecycleService.MarkPaymentFailed`. Audit rows are
  appended through `IBillingAudit` (action class
  `AuditAction.BillingChanged`, no new enum values introduced).
- **Iter-36 — Stripe operator runbook**: new
  [`docs/06-operations/billing-stripe.md`](docs/06-operations/billing-stripe.md)
  documenting the env-var inventory (`RADIOPAD_STRIPE_*` + legacy fallbacks),
  the webhook contract, the supported event list, and the
  `stripe listen --forward-to` local-testing recipe. Stripe added to the
  vendor-subprocessor table in
  [`docs/04-security/security-architecture.md`](docs/04-security/security-architecture.md);
  webhook entry expanded in
  [`docs/03-architecture/api-reference.md`](docs/03-architecture/api-reference.md)
  and [`openapi/openapi.yaml`](openapi/openapi.yaml).
- **Iter-36 — Desktop polish verification**: closed the frontend-side gaps in
  the Tauri shell so DESK-001..010 are end-to-end exercisable from a single
  bundle.
  - New `/pair` page at
    [`frontend/app/pair/page.tsx`](frontend/app/pair/page.tsx) drives the
    RFC 8628 device authorization grant against `POST /api/auth/device/{authorize,token}`,
    persists the bearer to the OS keyring via `setAuthToken`, and stashes a
    desktop-side copy through the existing `device_pairing_token_set` Tauri
    command. Closes DESK-008 frontend wiring.
  - [`frontend/app/ShellBridge.tsx`](frontend/app/ShellBridge.tsx) now
    listens for every `radiopad://*` event the shell already emits
    (`new-report`, `generate-impression`, `rewrite`, `dictate`,
    `secure-copy-section`, `clipboard-cleared`) and translates them into
    `radiopad:*` browser `CustomEvent`s for downstream features. Closes
    DESK-003 frontend wiring.
  - [`frontend/lib/offlineDrafts.ts`](frontend/lib/offlineDrafts.ts) prefers
    the AES-256-GCM `offline_drafts_*` Tauri commands when running under the
    desktop shell; Capacitor Preferences and `localStorage` remain the
    mobile / web-preview fallbacks. Closes DESK-006 frontend wiring.
  - [`frontend/lib/api.ts`](frontend/lib/api.ts) gains
    `auth.deviceAuthorize` and `auth.deviceToken`.
  - New runbook at
    [`docs/06-operations/desktop-runbook.md`](docs/06-operations/desktop-runbook.md)
    captures the OK/GAP table.

- **Iter-36 — Governance + model-evaluation dashboards (frontend)**: two new
  Next.js admin pages under
  [`frontend/app/admin/governance/page.tsx`](frontend/app/admin/governance/page.tsx)
  and [`frontend/app/admin/model-eval/page.tsx`](frontend/app/admin/model-eval/page.tsx).
  The governance dashboard aggregates six panels (model inventory, prompt +
  rulebook versions, AI usage, PHI routing, validation results, drift alerts)
  over endpoints that already existed; the model-eval harness composes
  `POST /api/ai/sandbox/compare` and `POST /api/validation-packs/{id}/run` into
  a side-by-side comparison and exposes the existing rulebook approval flow
  behind a Medical-Director-only *Promote rulebook* button. Role gating shared
  via [`frontend/lib/roles.ts`](frontend/lib/roles.ts) (mirrors
  `RadioPad.Domain.UserRole`). No new backend endpoints. Tests:
  [`frontend/__tests__/admin/governanceDashboard.test.tsx`](frontend/__tests__/admin/governanceDashboard.test.tsx)
  and
  [`frontend/__tests__/admin/modelEval.test.tsx`](frontend/__tests__/admin/modelEval.test.tsx).
  Docs: [`docs/06-operations/governance.md`](docs/06-operations/governance.md).
- **Iter-36 — CLI-AI provider adapters**: two new `IAiProviderAdapter`
  implementations under
  [`backend/RadioPad.Api/src/RadioPad.Infrastructure/Providers/Cli/`](backend/RadioPad.Api/src/RadioPad.Infrastructure/Providers/Cli/)
  shell out to local AI CLI binaries — `gemini-cli` (`gemini`) and
  `codex-cli` (`codex`). Both default to
  `ProviderComplianceClass.Sandbox`; the AI gateway's PHI policy is
  unchanged. The composed prompt is piped on **stdin**; arguments are passed
  via `ProcessStartInfo.ArgumentList` so prompts cannot escape into a shell.
  New `IProcessLauncher` abstraction (`DefaultProcessLauncher` + stub for
  tests) supports cancellation, configurable timeout
  (`RADIOPAD_CLI_PROVIDER_TIMEOUT_MS`, default 60s), default-deny binary
  allowlist (`RADIOPAD_CLI_PROVIDER_ALLOWED_PATHS`), and a control-character
  prompt sanitiser (refuses NUL / C0 controls). Per-provider binary override
  envs: `RADIOPAD_GEMINI_BIN`, `RADIOPAD_CODEX_BIN`.
  The existing generic `OpenAiCompatibleProvider` (`openai-compatible`) gains
  a missing-API-key guard: when `apiKeySecretRef` is set but resolves empty
  the adapter throws `ProviderPolicyException("api_key_missing")` instead of
  letting the upstream return a confusing 401. Tests: new
  `Iter36CliProviderTests` (16 cases). Docs: provider catalog table in
  [`docs/03-architecture/api-reference.md`](docs/03-architecture/api-reference.md)
  and a new "CLI-AI providers" section in
  [`docs/08-user-docs/cli-guide.md`](docs/08-user-docs/cli-guide.md). Locks
  honoured: `AiGateway.EnforcePhiPolicy`, audit chain, and
  `RadioPadDbContext` are all unchanged; no new audit actions.
- **Iter-36 — Mobile feature completion (dictation + draft edit + sign acknowledgement)**:
  three new App Router pages served by the same Next.js frontend the Capacitor
  shell wraps:
  - `/mobile/dictate/[reportId]` — Web Speech API dictation with localStorage
    offline draft buffer; falls back gracefully when `window.SpeechRecognition`
    is unavailable. Save uses new `api.reports.appendFindings` (composed
    client-side over the existing PATCH route — no new backend endpoint, so
    tenant isolation, audit chain, and PHI policy flow through unchanged).
  - `/mobile/reports/[reportId]/edit` — six collapsible per-section panels
    (Indication, Technique, Comparison, Findings, Impression, Recommendations).
    AI-drafted text continues to wear `.ai-mark`. Save uses `api.reports.patch`.
  - `/mobile/reports/[reportId]/sign` — read-only review with locked
    severity-coloured findings, two acknowledgement checkboxes (AI mandatory;
    Warning when warnings exist; Blockers always block), and an
    Acknowledge & Export action that calls `api.reports.acknowledge` then the
    chosen export. RadioPad never auto-signs — this only records the
    acknowledgement and unlocks export.
  - Mobile shell additions in [`frontend/app/radiopad.css`](frontend/app/radiopad.css)
    documented in [`docs/02-design/design.md`](docs/02-design/design.md) §4.11–4.12:
    `.rp-mobile`, `.rp-mic-btn` (`.recording`), `.rp-transcript`,
    `.rp-mobile-section`, `.rp-mobile-body`, `.rp-ack-row`, plus the canonical
    `@media (max-width: 720px)` mobile breakpoint that stacks `.rp-workspace`
    / `.rp-grid-3` / `.rp-grid-2`. **No new design tokens.**
  - Capacitor wiring: `@capacitor-community/speech-recognition` added to
    [`mobile/package.json`](mobile/package.json) within the locked Capacitor 6
    stack. Required platform permissions
    (`NSSpeechRecognitionUsageDescription`, `NSMicrophoneUsageDescription` on
    iOS; `RECORD_AUDIO` on Android) are documented in
    [`mobile/README.md`](mobile/README.md) — they apply once
    `pnpm exec cap add ios|android` has materialised the native projects.
  - Tests: new `frontend/__tests__/mobile/{dictatePage,editPage,signPage}.test.tsx`
    (12 cases). User docs: new "Mobile workflows" section in
    [`docs/08-user-docs/user-guide.md`](docs/08-user-docs/user-guide.md).
- **Iter-36 — Five new RADS-aligned rulebooks**:
  - `mammo_birads_v1` (Mammography BI-RADS, MG / Breast)
  - `lung_lungrads_v1` (Lung Cancer Screening CT — Lung-RADS, CT / Chest)
  - `liver_lirads_v1` (Liver MRI / CT — LI-RADS, MRI+CT / Liver)
  - `prostate_pirads_v1` (Prostate MRI — PI-RADS v2.1, MRI / Prostate)
  - `chest_xray_v1` (Adult Chest X-Ray, XR / Chest — non-RADS template)

  Each ships with `status: approved`, an `output_schema` block (RADS-category enum where applicable), `style.approved_followups`, and at least two passing golden cases (`01-normal.json`, `02-actionable.json`) under `rulebooks/_tests/<rulebook_id>/`. CI rulebook validation in `.github/workflows/ci.yml` picks them up automatically. No PHI in fixtures (`SYNTH-*` accession numbers).
- **Iter-35 close-out (6 parallel agents, 2026-05-05) — PRD complete at 130 ✅ / 0 🟡 / 0 🔴 / 0 ⏸ across 130 rows**:
  - **PROV-007 OAuth refresh-token vault** — new `OAuthRefreshTokenService` with AES-256-GCM at-rest encryption via the existing `IKmsProvider` chain, rotation `BackgroundService`, endpoints `POST/DELETE/GET /api/providers/{id}/oauth/refresh-token[/status]`, admin UI panel on `/admin/providers/[id]`, EF migration `20260505000100_Iter35OAuthVault`, new audit action `OAuthRefreshRotated = 41`. Tests: `Iter35OAuthVaultTests` (8 cases).
  - **PERF-004 in-process synthetic availability monitor** — `AvailabilityMonitorService` (`BackgroundService`) probes core endpoints, exports an OTel histogram on the `RadioPad.PerfBudgets` meter, exposes `GET /api/admin/observability/availability`, surfaces an Availability section on `/admin/security`, and audits burn-rate breaches as `SystemAlert{kind:"availability_burn_rate"}` (reuses int 40). Tests: `Iter35AvailabilityMonitorTests` (3 cases). Production-stack verification of the 99.9% SLO remains an operator deployment activity.
  - **Multilingual scaffolding (INTL-001)** — `next-intl` wired into the frontend with `en/es/de/fr/pt/hi` locale bundles, `TenantSettings.Locale` and `User.PreferredLocale` columns + EF migration `20260505000200_Iter35Locales`, endpoints `GET/PUT /api/tenant/settings/locale` and `PUT /api/users/me/locale`, locale switcher in tenant and user settings. Tests: `Iter35LocaleTests` (7 cases). No design-token change.
  - **Validation packs (VPK-001)** — new `ValidationPack` entity + status enum, EF migration `20260505000300_Iter35ValidationPacks`, six endpoints under `/api/validation-packs` (list / get / import / export / approve / deprecate / run), CLI `radiopad packs list|import|export|run`, admin page `/admin/validation-packs`. New audit actions `ValidationPackApproved = 42`, `ValidationPackDeprecated = 43`, `ValidationPackRun = 44`. Tests: `Iter35ValidationPackTests` (4 cases).
  - **Hardening sweep** — plugin sandbox strategy now selects `bwrap` (preferred) ⇒ `unshare`/`landlock` fallback on Linux and `sandbox-exec` on macOS, surfaced via `RADIOPAD_PLUGIN_SANDBOX=bwrap|unshare|noop`. WebAuthn attestation chain pinned against the FIDO MDS3 root set via the new `IFidoMdsMetadataSource` (embedded roots; HTTP refresh gated by `RADIOPAD_FIDO_MDS3_URL`); rejections audit `PolicyViolation{kind:"webauthn_attestation_root"}`. `OpenTelemetry.Exporter.OpenTelemetryProtocol` bumped to 1.15.3, clearing **GHSA-4625-4j76-fww9**. Tests: `Iter35WebAuthnRootPinTests` (2 cases).
  - **Frontend component tests + nightly live-suite CI** — vitest + Testing Library suites for `validationFinding`, `aiMark`, `composer`, `topbar`; new `.github/workflows/nightly-live-suites.yml` runs the AWS KMS + SIEM live smoke suites nightly, gated entirely on operator-supplied repo secrets (`RADIOPAD_RUN_AWS_KMS_LIVE`, AWS creds, SIEM HEC tokens). No-op without the secrets.
  - **Validation:** full backend suite `dotnet test backend/RadioPad.Api/RadioPad.Api.sln --no-build` ⇒ **Failed: 0, Passed: 379, Skipped: 5, Total: 384** (5 skips remain operator-gated live-infrastructure suites). Locks honoured: tenant isolation, append-only audit chain, PHI policy in `AiGateway.EnforcePhiPolicy`, locked design tokens — all unchanged.
- **Enterprise PRD close-out hardening**: final report exports now require `Acknowledged`/`Exported` status across text/JSON/FHIR/PDF/DOCX/HL7 (text `preview=true` remains draft-safe), and `GET /api/reports/{id}/export/json` is available through the API client and report toolbar. Provider API key refs are env-only at save and resolution time; PACS secret refs no longer accept inline literals. Rulebook golden cases now fail on unexpected findings as well as missing findings, with a concrete `level_consistency` resolver replacing the previous unknown-rule placeholder behavior. Clean-database migration ordering was hardened by removing duplicate operations from `SecurityHardening` and making trusted-plugin publisher migration discovery explicit. CLI audit verification now verifies oldest-to-newest, and daemon start passes a full bind URL to the API. Added [docs/00-product/enterprise-prd-remaining.md](docs/00-product/enterprise-prd-remaining.md) as the canonical residual-work list.
- **Iter-34 close-out (5 parallel agents, 2026-05-05)**: drove [docs/09-regulatory/traceability-matrix.md](docs/09-regulatory/traceability-matrix.md) to **128 ✅ / 0 🟡 / 0 🔴 / 2 ⏸ across 130 rows** — the only remaining ⏸ entries (PROV-007 OAuth refresh-token vault, PERF-004 operator-side availability SLO) are externally gated and not verifiable in this repo. New shipping code: `GET /api/billing/credits` (BILL-002 / BILL-007), `POST /api/ai/sandbox/compare` (PROV-005, PHI policy preserved — dispatches via `IAiGateway.RouteAsync`), `ProviderConfig.RetentionLabel` field (PROV-009) + EF migration `20260504110000_Iter34ProviderRetention` (additive, snapshot updated), priced `byProvider` rollup in `IAiUsageStore.SummariseAsync` (BILL-005), and the new Governance dashboard at `/admin/governance` (GOV-001, read-only aggregator over audit-verify / usage-analytics / billing-features / prompt-overrides / templates / rulebooks). New admin UI sections: `/admin/billing` Credits + Trial (with `.rp-banner.warn` countdown when trial ≤ 3 days), `/admin/usage` Cost (USD) column with lifetime + 30 d totals, `/providers` Sandbox-compare panel, retention-label free-text input. New locked CSS helpers in [`frontend/app/radiopad.css`](frontend/app/radiopad.css) documented in [`docs/02-design/design.md`](docs/02-design/design.md): `.rp-stat-tile`, `.rp-stat-tile-row`, `.rp-stat-sub`, `.rp-banner` (`.warn`/`.info`/`.danger`), `.rp-faint`. New tests: `Iter34UsageCostRollupTests`, `Iter34ProviderRetentionTests`, `Iter34SandboxCompareTests`, `Iter34BillingCreditsTests`. OpenAPI + api-reference mirrored. Locks honoured: tenant isolation via `TenantedController.ResolveContextAsync`; audit chain append-only via `IAuditLog.AppendAsync`; PHI policy enforced inside `AiGateway.EnforcePhiPolicy` for every sandbox-compare run; all new UI uses only locked Open Design tokens. Validated with `get_errors`; full backend test suite re-run is scheduled on the .NET 8 SDK build host (this workstation only carries .NET 10 SDK with the documented MVC TestHost mismatch).
- **Iter-33 close-out (regulatory pass, 2026-05-05)**: drove [docs/09-regulatory/traceability-matrix.md](docs/09-regulatory/traceability-matrix.md) to **117 ✅ / 6 🟡 / 0 🔴 / 6 ⏸** across all 129 tracked PRD ids. Promoted 11 already-shipping rows from 🟡 → ✅ with named test evidence (DESK-001, DESK-002, DESK-007, INT-007, INT-010, MCP-001..004, PROV-003, PROV-006, SEC-008, SEC-011); formally moved 6 rows from 🟡 → ⏸ with the gating external-dependency named (PROV-005 sandbox-compare UI, PROV-007 OAuth refresh-token vault, PERF-004 operator availability SLO, plus DESK / INT pilot rollouts requiring vendor contracts and signing certs). Six rows remain 🟡 — the underlying capability ships in code; the open work is admin UI surface or operator-side wiring tracked in the iter-34 backlog. Iter-33 is **docs-only**: no backend, frontend, schema, or audit-chain change. See [PROGRESS.md](PROGRESS.md) iter-33 entry.
- **Release-hardening stabilization**: SCIM 2.0 Groups are now exposed alongside Users (`/scim/v2/Groups`, `/scim/v2/Groups/{id}`) with env-backed bearer refs, group membership role projection/revocation, inactive-user list filtering, duplicate rename conflict handling, and bearer-protected discovery endpoints. Added the admin security webhook test endpoint (`POST /api/admin/security/test-webhook`) with stable unreachable-webhook handling, updated OpenAPI/API docs, and added focused integration coverage.
- **CI rulebook coverage**: GitHub Actions now validates every `rulebooks/*.yaml` file and runs every matching golden suite under `rulebooks/_tests/*` instead of a fixed allow-list.
- **INT-008 (iter-33)**: Orthanc Lua bridge now performs real HL7 v2 ↔ DICOM SR conversion (round-trip OBR-3 / OBX-5 ↔ ContentSequence + SOPInstanceUID). Two new Lua scripts (`deploy/orthanc/lua/radiopad-bridge.lua`, `radiopad-sr-store.lua`) wire `OnStableStudy` and `OnStoredInstance` to bearer-protected backend endpoints `POST /api/integrations/orthanc/study-stable` and `POST /api/integrations/orthanc/sr-stored` (`OrthancBridgeController`). Backend gains `RadioPad.Application.Services.Hl7Bridge` (`Hl7Message`, `Hl7ToDicomSrConverter`, `DicomSrToHl7Converter`, `IHl7Outbox` / `InMemoryHl7Outbox`); SR generates a fresh `2.25.<guid-as-int>` SOP Instance UID under SOP Class Basic Text SR. New `AuditAction.StudyReceived = 38`. Auth via constant-time bearer (`RADIOPAD_BRIDGE_TOKEN`) with tenant slug from `RADIOPAD_BRIDGE_TENANT`. Docs: [`docs/03-architecture/integrations/orthanc-bridge.md`](docs/03-architecture/integrations/orthanc-bridge.md). Tests: `Iter33/Hl7DicomSrRoundTripTests`, `Iter33/OrthancBridgeControllerTests`.
- **MCP-007 (iter-33)**: plugin manifest ed25519 signature chain, capability-scoped registry (deny-by-default), per-OS sandbox wrappers. New `TrustedPluginPublisher` entity (per-tenant trusted ed25519 publisher keys, append-only `RevokedAt`) + EF migration `20260504100000_TrustedPluginPublishers`. New `PluginManifestSignatureVerifier` verifies a detached ed25519 signature over the canonical-JSON serialisation of `manifest.json`, audits `AuditAction.ProviderBlocked{kind:"plugin_policy"}` on every block path, and throws `PluginPolicyException`. New `IMcpCapabilityRegistry` / `InMemoryMcpCapabilityRegistry` enforces a deny-by-default `(pluginId, capability)` allow-list (`dicomweb.read`, `report.draft.suggest`, `rulebook.lookup`). New `IPluginSandbox` with `WindowsAppContainerSandbox`, `LinuxNamespaceSandbox` (uses `unshare --net --pid --user --map-root-user --`), and a documented `MacOsNoopSandbox` placeholder; `PluginSandboxFactory.CreateForCurrentOs` picks the right wrapper at startup. Tests: `Iter33/PluginManifestSignatureTests` (4 cases) and `Iter33/McpCapabilityRegistryTests` (6 cases). Docs: [`desktop/PLUGIN_TRUST.md`](desktop/PLUGIN_TRUST.md) and [`docs/04-security/threat-model.md`](docs/04-security/threat-model.md) gain trust-chain + capability + sandbox sections.
- **PERF-004**: continuous P95 budgets — OTel histograms (`radiopad.report.validate|sign.duration_ms`, `radiopad.ai.draft.duration_ms`, `radiopad.dicom.qido.duration_ms`, `radiopad.api.request.duration_ms`) on the `RadioPad.PerfBudgets` meter, OTLP exporter gated by `RADIOPAD_OTEL_OTLP_ENDPOINT`, Prometheus SLO recording rules + multi-burn-rate alerts in `deploy/observability/slo-recording-rules.yaml`, Grafana dashboard `deploy/observability/grafana-radiopad-slo.json`, and an admin-only Alertmanager webhook ingester at `POST /api/admin/observability/slo-alerts` (audits new `AuditAction.SystemAlert = 40`).
- **INT-007**: PACS vendor adapters for Sectra IDS7, Visage 7, Carestream Vue (worklist + prior-fetch + report sendback) behind `IPacsVendorAdapter`. Per-tenant selection via `TenantSettings.PacsVendor` (`sectra` / `visage` / `carestream` / `null`); `IPacsVendorRouter` picks the keyed singleton, with `null` falling back to the generic DICOMweb path. Credentials read from `RADIOPAD_PACS_{SECTRA,VISAGE,CARESTREAM}_TOKEN[_REF]` via the `env:NAME` indirection (`PacsSecretResolver`); cloud-secret-manager schemes (`aws:`, `azkv:`, `gcp:`) reserved for a future iter. Docs: [pacs-vendor-adapters.md](docs/03-architecture/integrations/pacs-vendor-adapters.md).
- **INT-010**: Live SIEM smoke tests gated by `RADIOPAD_RUN_SIEM_LIVE=1` for Splunk HEC, Sentinel Log Analytics, Elastic Bulk, and Syslog UDP. New `EnvFactAttribute` (`tests/RadioPad.Api.Tests/Infrastructure/EnvFactAttribute.cs`) marks each fact `Skip` when the gate variable is unset; per-sink env vars further gate individual tests. Synthetic event is PHI-free (tenantId `…beef`, action `radiopad-iter33-smoke`). Runbook + Splunk dev-container instructions in [docs/04-security/siem-runbook.md](docs/04-security/siem-runbook.md).
- **AUTH-004**: Magic-link `request` endpoint rate-limited per email (5 / 15 min) and per IP (20 / 15 min); rejection returns `429` with `Retry-After` and audits `RateLimited` (new `AuditAction.RateLimited = 39`). Limiter (`MagicLinkRateLimiter`) uses `System.Threading.RateLimiting.FixedWindowRateLimiter` primitives and runs *before* the tenant/user lookup so we do not leak account existence to a flood. The audit row records a SHA-256 hash of the email — never the raw value.
- **AUTH-001**: WebAuthn attestation now verified for `none`, `packed`, and `fido-u2f` formats; unsupported formats rejected; attestation format persisted on credential.
- **DESK-002**: per-OS installer hardening — WiX/NSIS Windows config, hardened-runtime + notarization for macOS, GPG-signed deb/rpm/AppImage for Linux, post-build verify CI.
- **DESK-001**: Tauri auto-updater signing wired (ed25519, channel-aware endpoints, GitHub OIDC -> KMS signing in `desktop-release.yml`).

### Security
- **SAML ACS fail-CLOSED hardening (iter-32 closeout, post-Momus review #1)** — `SamlController.ProcessAcs` previously skipped XML signature verification when `RADIOPAD_SAML_IDP_CERT_PEM` was unset, accepting any forged `SAMLResponse` and minting a 12-hour bearer for the named tenant/user. The control flow is now inverted: unsigned assertions are rejected unless an operator explicitly opts in via `RADIOPAD_SAML_DEV_INSECURE=true`, and that escape hatch is ignored in Production. New regression test `Iter32SamlAcsTests.Acs_FailClosed_When_NoCert_And_No_DevInsecureFlag` asserts a 401 in the default-no-cert configuration.

### Added
- **Iteration 32 — Auth / SSO / MFA (Agent A)**
  - PRD **AUTH-001 / INT-001**: OIDC presets for Keycloak, Auth0, and Okta — `RADIOPAD_OIDC_PRESET=keycloak|auth0|okta` populates `RADIOPAD_OIDC_TENANT_CLAIM`, `_EMAIL_CLAIM`, and `_REQUIRE_MFA` defaults via `RadioPad.Api.Auth.OidcProfiles`. Explicit env values are never overwritten.
  - PRD **AUTH-001 / INT-002**: SAML 2.0 Service Provider — new `SamlController` exposes `GET /saml/metadata` (SP descriptor) and `POST /saml/acs`. Assertions are signature-verified with `System.Security.Cryptography.Xml.SignedXml` against `RADIOPAD_SAML_IDP_CERT_PEM`; tenant attribute defaults to `tenant_slug` (override via `RADIOPAD_SAML_TENANT_ATTRIBUTE`). Successful logins audit `UserLogin{method:"saml"}`.
  - PRD **AUTH-001**: WebAuthn / passkeys — new `WebAuthnController` (`/api/auth/webauthn/{register-options,register,signin-options,signin}`) and tenant-scoped `WebAuthnCredentials` table. Successful assertions audit `UserLogin{method:"webauthn"}`. Full FIDO2 attestation parsing (Fido2NetLib) is a P1 follow-up.
  - PRD **AUTH-004**: TOTP verify now flows through the new `LockoutPolicy`; OIDC presets default to `RADIOPAD_OIDC_REQUIRE_MFA=true`, enforced by `OidcBearerMiddleware` against the IdP's `amr` claim.
  - PRD **AUTH-006 / SEC-007**: emergency lockout + session revocation. `User` gains `FailedLoginCount`, `FailedLoginWindowStart`, `LockedUntil`, and `SessionEpoch`. `LockoutPolicy` enforces a sliding window of 5 failures / 15 minutes (auto-unlock after 15 min); admin `POST /api/users/{id}/unlock` clears the counter and audits `UserUnlocked`. `POST /api/users/{id}/revoke-sessions` (Compliance / IT-Admin) increments `SessionEpoch`, which is folded into the bearer HMAC (`v{epoch}|{tenant}|{email}`), invalidating every outstanding token in O(1) and auditing `SessionsRevoked`.
  - EF migration `20260503223000_Auth32` adds the four user columns and the `WebAuthnCredentials` table (unique on `(TenantId, CredentialIdHash)`).
  - New audit actions: `SessionsRevoked = 33`, `SecurityAlert = 34`.
  - Frontend: new `/admin/sso` page (locked design tokens) lists OIDC profiles, SAML metadata download, and registered passkeys.
  - Tests: `Iter32AuthTests` (`OidcPresetTests`, `AccountLockoutTests`, `WebAuthnFlowTests`, `SamlAcsTests`) — 11 new cases, all pass.
  - Docs: traceability matrix flips **AUTH-001 / AUTH-004 / AUTH-006 / SEC-007 / INT-001 / INT-002** from 🟡/🔴 to ✅; ADR-0004 moves from Proposed-deferred to Accepted with an iter-32 addendum; openapi.yaml + api-reference gain the new endpoints.

- **Iteration 32 — AI completeness (Agent E)**
  - PRD **AI-001** dictation cleanup is now a first-class feature on every rulebook: all 17 YAML files gained a `prompt_blocks.dictation_cleanup` block, and a `Cleanup dictation` button on the report editor calls the existing `POST /api/reports/{id}/dictation/cleanup` endpoint and renders the section map in `.ai-mark`.
  - PRD **AI-008** approved follow-ups: every rulebook gains a `style.approved_followups: [...]` allow-list and the new `unauthorized_followup` warning rule in `ReportValidator`. `ReportingService.SuggestFollowUpAsync` now drops AI-suggested lines that don't match the allow-list and audits a hash-only `PolicyViolation` for each rejected line.
  - PRD **AI-009** prompt-override approval gate: `PromptOverride` entity gained `Status` (`Draft` / `Approved`) and the new MedicalDirector-only `POST /api/prompts/overrides/{id}/approve` endpoint. `EfPromptOverrideStore.LoadAsync` now filters by `Status == Approved`, so only governance-blessed bodies reach the AI runtime. New audit action `PromptOverrideApproved = 35` carries `bodyHash = sha256(body)`.
  - PRD **AI-010** composite cost routing: `EfProviderRouter` now scores each candidate by `cost`, `quality`, and `latency` weighted via the new per-tenant `TenantSettings.RoutingWeightsJson` (default `{"cost":0.5,"quality":0.4,"latency":0.1}`). `ProviderConfig.Quality` (decimal `[0,1]`) is operator-supplied. P95 24-hour latency from `AiRequest` breaks ties. New `IRoutingPreviewService` + `GET /api/ai/routing/preview?phi=&modality=&input=&output=` (ItAdmin / MedicalDirector) returns the chosen provider plus a per-candidate `costScore` / `qualityScore` / `latencyScore` / `compositeScore` breakdown and the eligibility reason. Audits `RoutingPreviewQueried = 36`.
  - PRD **AI-011** local-model adapters: three new adapters under `RadioPad.Infrastructure/Providers/Local/` — `OllamaProvider` (`POST /api/chat`, default `http://127.0.0.1:11434`), `VLlmProvider` (OpenAI-compatible `POST /v1/chat/completions`, default `http://127.0.0.1:8000`), `LlamaCppProvider` (`POST /completion`, default `http://127.0.0.1:8080`). All three default to `ProviderComplianceClass.LocalOnly` and expose a `ProbeAsync` health check wired to the new admin `POST /api/providers/{id}/health` endpoint.
  - EF migration `20260504000100_Iter32AiCompleteness` adds `PromptOverrides.Status`, `Providers.Quality`, `TenantSettings.RoutingWeightsJson`.
  - Tests: `Iter32AiCompletenessTests` (8 cases — approved-followup positive/negative, prompt-override draft/approve/role-gate/store-filter, routing preview), `OllamaProviderTests` / `VLlmProviderTests` / `LlamaCppProviderTests` (5 cases each, stubbed `HttpMessageHandler`).
  - Docs: traceability matrix flips **AI-001 / AI-008 / AI-009 / AI-010 / AI-011** from 🟡 to ✅; api-reference + openapi.yaml gain the new endpoints; rulebook-authoring guide documents `style.approved_followups` and the `dictation_cleanup` prompt block.

- **Iteration 32 — KMS / customer-managed keys (Agent C)**
  - PRD **SEC-003**: real cloud-KMS adapters under `backend/RadioPad.Api/src/RadioPad.Infrastructure/Kms/` — `AwsKmsProvider` (AWSSDK.KeyManagementService 3.7, `aws:` scheme, `EncryptionContext = { tenantId }`), `AzureKeyVaultKmsProvider` (Azure.Security.KeyVault.Keys 4.6 + Azure.Identity 1.13, `azkv:` scheme, `RsaOaep256` wrap), `GcpKmsProvider` (Google.Cloud.Kms.V1 3.18, `gcp:` scheme, `additional_authenticated_data = utf8(tenantId)`). The previous iter-21 stubs are removed; the resolver dispatches on `env: | local: | aws: | azkv: | gcp:`.
  - `IKmsProvider` extended with tenant-aware `WrapAsync(keyRef, dek, tenantId, ct)` / `UnwrapAsync(...)` overloads (default-interface methods) so `env:` and `local:` stay backward-compatible.
  - New `TenantDekCache` (`RadioPad.Infrastructure.Kms`) — 5-minute in-memory cache of unwrapped tenant DEKs, keyed by SHA-256 of `(tenantId, keyRef, wrappedDekBase64)`. DEKs zeroed on eviction; never logged.
  - `POST /api/tenant/settings/kms/verify` now performs a real wrap+unwrap round-trip of a 32-byte probe (constant-time compare) and stamps `CmkLastVerifiedAt` only on success. Failures return `422 { kind: "kms_unavailable" | "kms_roundtrip_mismatch", error }`.
  - Frontend: new "Customer-managed encryption key (CMK)" panel on `/admin/settings` with opaque key-ref input, scheme badge, last-verified timestamp, and "Verify round-trip" action. `frontend/lib/api.ts` exposes `api.tenant.settings.verifyKms()` and adds `cmk` on the typed settings response.
  - Tests: `tests/RadioPad.Api.Tests/Kms/KmsAdapterTests.cs` — `KmsResolverDispatchTests`, `KmsAwsAdapterTests` (in-memory fake `AmazonKeyManagementServiceClient`, EncryptionContext + tenant mismatch coverage, ARN/region parsing, live-AWS test gated on `RADIOPAD_RUN_AWS_KMS_LIVE=1`), `KmsAzureAdapterTests` (mocked `IAzureCryptographyClient`), `KmsGcpAdapterTests` (mocked `IGcpKmsClient`, AAD binding + mismatch), `KmsEnvelopeRoundTripTests` (cache + invalidate semantics). 16 / 17 pass; one `[Fact(Skip)]` for the live AWS round-trip.
  - Docs: [docs/04-security/security-architecture.md](docs/04-security/security-architecture.md) gains a "Customer-managed keys (CMK / SEC-003)" section listing the four schemes, ARN/URI formats, IAM permissions (`kms:Encrypt`/`Decrypt`/`DescribeKey` for AWS; Crypto User / Officer for Azure; `roles/cloudkms.cryptoKeyEncrypterDecrypter` for GCP), tenant-binding mechanism, and verify-endpoint behaviour. [docs/09-regulatory/traceability-matrix.md](docs/09-regulatory/traceability-matrix.md) flips SEC-003 to ✅.

- **Iteration 32 — PACS bridge + SIEM pushers (Agent G)**
  - PACS bridge (PRD DESK-007 / INT-007): new `PacsController` with `GET /api/pacs/studies` (vendor-neutral QIDO-RS proxy), `POST /api/pacs/studies` (STOW-RS forwarder), and `GET /api/pacs/health` (DICOMweb + bundled-Orthanc reachability). `IDicomWebClient` extended with `SearchStudiesAsync`, `StoreInstancesAsync`, `HealthAsync`. Audit row carries upstream status code and a 12-hex prefix of `sha256(accession)` only — accession numbers are never logged.
  - Bundled Orthanc proxy (PRD DESK-007 / INT-007): new [`deploy/orthanc/`](deploy/orthanc/) (Dockerfile, `orthanc.json`, Lua HL7↔DICOM bridge stub) with an opt-in `pacs` profile in `deploy/docker-compose.yml`. Binds `127.0.0.1` by default; activated by setting `RADIOPAD_ORTHANC_URL`. Operator runbook: [docs/06-operations/pacs-bridge.md](docs/06-operations/pacs-bridge.md).
  - Signed PACS plugin SDK (PRD DESK-007): new [`desktop/plugin-sdk/`](desktop/plugin-sdk/) — README, JSON schema, example Sectra stub. New Tauri module `desktop/src-tauri/src/pacs_plugins.rs` (load + verify manifests via the iter-30 SHA-256 + Ed25519 verifier; reject any plugin whose signature fails). New CLI `radiopad pacs plugins list|verify|enable|disable`.
  - SIEM pushers (PRD INT-010): new `SiemPushService` BackgroundService draining the append-only audit log to up to four sinks: `SplunkHecSink` (HEC token), `SentinelLogAnalyticsSink` (HMAC-SHA256), `ElasticBulkSink` (Bearer/Basic), `SyslogUdpSink` (RFC 5424). 100-event batches / 5 s flush. Each sink is opt-in via env vars (`RADIOPAD_SIEM_*`). Failures retry 3× with backoff and never block `/api/*`. PHI minimisation: ids + action codes + timestamps + integrity hash only — `DetailsJson` is intentionally excluded.
  - SIEM snapshot (PRD §19): the existing `GET /api/audit/siem` is now documented as the **snapshot** export only (continuous delivery is the new BackgroundService). Status surface: `GET /api/siem/status`.
  - Frontend: new `/admin/pacs` (DICOMweb config + Orthanc badge + signed-plugin table) and `/admin/security` (per-sink push status, last-error, total pushed). Locked Open Design tokens only. New `api.pacs.*` and `api.siem.*` typed clients. Topbar nav extended.
  - Tests: `SiemSinkTests` (Splunk / Sentinel / Elastic / Syslog with stubbed `HttpMessageHandler` + `IUdpSender`), `DicomWebClientUnitTests` (QIDO / STOW / Health), `PacsPluginsVerifierTests`. All mocked — no real SIEM endpoint or PACS contacted.

- **Iteration 32 — Templates + Rulebooks polish (Agent I)**
  - Templates lifecycle (PRD TMP-005): `Template.ApprovedBy` / `Template.ApprovedAt` columns + new endpoints `POST /api/templates/{id}/submit-review`, `POST /api/templates/{id}/approve` (sets `ApprovedBy`/`ApprovedAt`), `POST /api/templates/{id}/deprecate`. Production gate: ``ReportsController.Create`` rejects non-`Approved` templates with `400 { kind: "template_not_approved" }` unless `Tenant.AllowSandboxRulebooks = true`. New `TemplateStatus.Review`. New audit actions `TemplateDeprecated = 36` and `TemplateSubmittedForReview = 37`. EF migration `Templates32`.
  - Templates analytics (PRD TMP-006): `GET /api/templates/{id}/usage` returns counts for last 7 d / 30 d / 90 d, `byUser` and `byModality` breakdowns. Surfaced on `/templates` admin page.
  - Templates UX (PRD TMP-008 / TMP-003): `/templates` admin page now shows status badges, Approve / Submit / Deprecate / Preview / Usage actions, and an inline Preview pane that renders sections with `[placeholder]` fallbacks.
  - Rulebook UI (PRD RB-002 / RB-008): rulebook detail page (`/rulebooks/[id]`) is now tabbed — YAML source mode (existing) + Visual mode (read-only summary of `required_sections`, `style.avoid_terms`, `style.approved_followups`, `rules`, and `prompt_blocks` keys). Rollback dropdown lists prior approved versions of the same `rulebookId` and POSTs to the existing `POST /api/rulebooks/{id}/rollback`.
  - Rulebook inheritance (PRD RB-007): documented four-level resolution chain in [docs/05-clinical/rulebook-authoring.md](docs/05-clinical/rulebook-authoring.md) — user-level `PromptOverride` > department-scoped `Rulebook.DepartmentTag` > tenant-wide `Report.RulebookId` > built-in YAML seed.
  - Lexicon CSV import (PRD STD-005 / STD-006): `POST /api/lexicon/import-csv` accepts `text/csv` body (header `term,forbidden,replacement,note`); audits `LexiconImported` with `source: "csv"`. Frontend wire via `api.lexicon.importCsv` for the `/admin/terminology` page.
  - CLI generate (PRD CLI-003): `radiopad generate --template <id> --input findings.txt --rulebook <id> --mode draft --out report.json` — when `--report` is omitted, creates a new report bound to the template, seeds `findings` from the local file, then routes through the same `/api/reports/{id}/ai` pipeline. Honours the existing client-side PHI guard.
  - Tests: `TemplateApprovalTests` (3 tests), `TemplateUsageAnalyticsTests`, `LexiconBulkImportTests` (CSV upsert + RBAC), `RulebookInheritanceTests` (department-scoped resolution), `CliGenerateTests` (template payload round-trip).

- **Iteration 32 — Closeout sweep (Agents A1 / A2 + lexicon fix)**
  - Test fixture: `RadioPadAppFactory` now seeds `SeedAdmin` (`UserRole.ItAdmin`), `SeedBillingAdmin`, `SeedComplianceReviewer` and exposes `CreateAdminClient()` / `CreateBillingAdminClient()` / `CreateComplianceClient()` so legacy tests can hit the admin-only endpoints introduced by the iter-32 RBAC tightening without weakening production guards.
  - PRD **AUTH-004 (TOTP)**: extracted the production Base32 decoder out of the test stub. `MfaController.Base32Decode` is now `internal`, exposed to the test assembly via `[InternalsVisibleTo("RadioPad.Api.Tests")]`. The `MfaControllerTestAccessStub` delegates to the real helper instead of throwing.
  - PRD **AUTH-001 (magic link)**: removed the AES-GCM `ValueConverter` from `MagicLinkToken.TokenHash` in `RadioPadDbContext` — hash columns are one-way and must be filterable by equality. Encryption-at-rest converters remain on actual secret columns. `MagicLinkController.Consume` once again matches the SHA-256 of the raw token. (Found via the AuthFlowsTests round-trip; no other hash column was affected.)
  - PRD **STD-006 (lexicon)**: `ReportingService.ValidateAsync` now evaluates the tenant lexicon even when the report has no rulebook bound — falls back to an empty `RulebookSpec` so `ReportValidator` still walks `lexicon` entries and emits `lexicon:<term>` warnings. Previously a missing rulebook returned `ValidationResult.Empty` and silently dropped lexicon hits.
  - PRD **BILL-002 (billing status)**: `GET /api/billing/status` always emits `subscriptionStatus` (`"active" | "trialing" | "past_due" | "canceled" | "none"`). Default `"none"` ensures the JSON key is never serialized away by `JsonIgnoreCondition.WhenWritingNull`.
  - Stripe webhook tests: payloads now include the `request: { id, idempotency_key }` envelope that `Stripe.net 46`'s `EventConverter.ReadJson` dereferences without null-checking. Controller signature handling was already correct; the omitted field was throwing `NRE → 400 BadRequest` inside the SDK before the controller ran.
  - Test fixtures: HL7-export tests now seed their own `Report` (Draft for the 409 case, Validated for the success case) instead of relying on the factory; `DicomInstanceMetadataTests` seeds the `TenantSettings` row through the sub-factory's DI scope (the parent factory's connection-string isolation made the previous seeding invisible).
  - **Result**: backend test suite is now `Failed: 0, Passed: 290, Skipped: 1, Total: 291` after the post-Momus SAML hardening (one new regression test). The skipped case is the live AWS KMS round-trip, gated on `RADIOPAD_RUN_AWS_KMS_LIVE=1`.
  - Frontend `pnpm typecheck` is unrunnable on this workstation (no Node toolchain installed); CI continues to enforce it. No frontend code changed in this closeout sweep.

- **Iteration 31 — PRD finishing pass (10 parallel agents, 89 requirement closures)**
  - Provider abstraction (PRD AI-010 / PROV-001 / CLI-007): five `IAiProvider` adapters under `RadioPad.Infrastructure/Providers/` — `AzureOpenAiProvider`, `AwsBedrockProvider` (with custom `AwsSigV4Signer`), `GoogleVertexAiProvider`, `OpenAiDirectProvider`, and `OpenAiCompatibleProvider` (covers DigitalOcean serverless inference, NVIDIA NIM, Cloudflare AI, Together, Groq, vLLM, Mistral, OpenRouter, Ollama). All key material via `ProviderSecretResolver` env-only references; PHI compliance class declared per-adapter.
  - Integrations (PRD INT-005 / INT-006 / STD-004): HL7 v2 MLLP listener (`Hl7MllpListener` `BackgroundService`, env-gated `RADIOPAD_HL7_MLLP_PORT`, default disabled, binds `127.0.0.1`); FHIR webhook hardening with `X-RadioPad-Signature: sha256=<hex>` HMAC validated via `CryptographicOperations.FixedTimeEquals`; DICOMweb WADO-RS instance-metadata retrieval at `GET /api/reports/{id}/dicom-context/instance`. New tenant fields `FhirWebhookSecret`, `Hl7SendingFacility`. New audit reason `fhir-webhook:bad_signature` on `PolicyViolation`.
  - Eight new approved rulebooks (PRD RB-006 / RB-004) with two golden cases each: `thyroid_us_v1` (TI-RADS), `prostate_mri_v1` (PI-RADS), `lung_screening_ct_v1` (Lung-RADS), `head_ct_trauma_v1`, `knee_mri_v1`, `shoulder_mri_v1`, `abdomen_ct_v1`, `pelvis_mri_v1`. Total approved rulebook count: 17. CI runs `rulebook validate` + golden-case suite for each.
  - Frontend (PRD RPT-006 / RPT-009 / RPT-010 / AI-007 / BILL-004 / BILL-006): `RewriteStylePanel.tsx` (rewrite-in-my-style), `PriorComparePanel.tsx` (side-by-side compare), `CopyToRisButton.tsx` (30 s clipboard auto-clear via `secure_copy` Tauri command), `UnsupportedClaimFinding` renderer (quotes offending sentence in `.ai-mark`, links to Findings), new admin pages `/admin/usage` and `/admin/feature-flags`. New nav links + new `RewriteMode` value `'in_my_style'`. New typed clients `api.reports.rewriteInMyStyle`, `api.reports.comparePrior`, `api.usage.summary`. New locked design tokens `.rp-grid-2`, `.rp-grid-2-row`, `.rp-diff-add`, `.rp-diff-remove`.
  - Desktop hardening (PRD DESK-003 / DESK-004 / DESK-005 / DESK-006 / DESK-008 / DESK-010): six global hotkeys `Ctrl/Cmd+Shift+{R,N,I,W,D,C}` emitting `radiopad://{focus,new-report,generate-impression,rewrite,dictate,secure-copy-section}`; AES-256-GCM encrypted offline draft store + scoped local cache (master key in OS keyring via `keyring` crate; per-entry TTL); device fingerprint + pairing-token slots; `tracing-subscriber` log redactor installed before any tracing event fires. New Tauri invoke commands `offline_drafts_*`, `local_cache_*`, `device_fingerprint`, `device_pairing_token_*`. New event `radiopad://clipboard-cleared`.
  - CLI hardening (PRD CLI-001 / CLI-002 / CLI-006 / CLI-008 / CLI-009 / CLI-010): `radiopad daemon start/stop/status` (`Daemon.cs`), `radiopad templates import/export` (`Templates.cs`), `radiopad audit sync` (`AuditSync.cs`), `radiopad login` mirror via `DeviceFlow.cs`, `radiopad providers register` (`ProviderRegister.cs`), `--headless` flag wired via `CliRuntime.cs`, server-mirroring PHI gate `PhiGuard.cs`.
  - Security (PRD SEC-002 / SEC-010): `AesGcmColumnEncryptor` for column-level secrets at rest; desktop `log_redactor.rs` regex layer feeding `tracing-subscriber`.
  - Tenant settings: new `requireZeroBlockers` (default `true`) and `warnAsBlocker` (default `false`) toggles wired into `ReportValidator` and blocker-aware report lifecycle gates. Default behaviour unchanged.
  - Regulatory artefacts: `docs/09-regulatory/baa-template.md`, `eu-aiact-gdpr-profile.md`, `pms-plan.md`, `vendor-risk-register.md`. SaMD posture re-affirmed as non-SaMD with explicit boundary conditions in the AI-Act profile.
  - Traceability matrix updated to iter-31 counts: ✅ 62 (+32) · 🟡 44 (-26) · 🔴 3 (-9) · ⏸ 3 (-4).

- **Iteration 30 — PRD finishing pass (12 features, 5 parallel agents)**
  - Reporting: `POST /api/reports/{id}/rewrite` with 4 modes (concise / formal / patient_friendly / referring_summary) (PRD RPT-007); multi-radiologist sign-off `POST /api/reports/{id}/sign` (Primary / CoSigner) and `POST /api/reports/{id}/addendum` with `IsAddendum` ReportVersions (`AuditAction.ReportSigned = 15`, `ReportAddendumAppended = 16`); `GET /api/reports/{id}/signatures`.
  - Bidirectional FHIR: tenant-bearer ingest at `POST /api/ingest/fhir/diagnosticreport` plus session-auth admin import at `POST /api/reports/import/fhir`; `Report.ServiceRequestRef` correlation; `AuditAction.ReportImported = 14`.
  - Terminology: free RadLex curated subset at `rulebooks/_terminology/radlex_subset.yaml` exposed via `GET /api/terminology/radlex/search` and a FHIR R4 `CodeSystem` stub; ACR RADS category index (BI/LI/PI/Lung/TI/O-RADS) at `rulebooks/_terminology/rads.yaml` exposed via `GET /api/terminology/rads` (PRD STD-001/STD-002).
  - Phase 2 rulebooks: `cardiac_mri_v1`, `mammography_v1`, `paediatric_chest_xray_v1`, `liver_mri_v1` with golden-case suites under `rulebooks/_tests/<id>/`.
  - Billing: `GET /api/billing/invoices/export?format=csv|zip` enterprise bulk export with SHA-256 manifest (PRD BILL-003).
  - Mobile: `POST/DELETE /api/push/devices`, `POST /api/push/test`; `PushDevice` entity; APNs ES256-JWT + FCM HTTP v1 OAuth2 senders driven by env (`RADIOPAD_APNS_KEY_P8`, `RADIOPAD_APNS_KEY_ID`, `RADIOPAD_APNS_TEAM_ID`, `RADIOPAD_APNS_BUNDLE_ID`, `RADIOPAD_FCM_PROJECT_ID`, `RADIOPAD_FCM_SERVICE_ACCOUNT_JSON`); `frontend/lib/push.ts` and `frontend/lib/biometric.ts` (Face ID / Touch ID / Android biometric); `AuditAction.PushDeviceRegistered = 17`, `PushDeviceUnregistered = 18`, `PushDeviceTested = 19` (PRD MOB-007).
  - Desktop sandbox: `desktop/src-tauri/src/sandbox.rs` with constant-time SHA-256 + Ed25519 detached-signature verification; mirror in CLI `radiopad plugin verify` (PRD DESK-009).
  - CI signing: gated Authenticode (`WINDOWS_CERTIFICATE_PFX_BASE64`), Apple Developer ID + notarytool (`APPLE_*`), Linux GPG (`GPG_PRIVATE_KEY_BASE64`), Android apksigner (`ANDROID_KEYSTORE_BASE64`), Tauri updater (`TAURI_PRIVATE_KEY`) — every step skipped when its secret is unset (PRD DESK-010..DESK-014).
  - Performance: `perf/k6/scripts/{ai-draft,impression,validate,audit-write}.js` with PRD §21 thresholds (P95<10s/5s/3s, P99<500ms); `.github/workflows/perf-smoke.yml` (PRD PERF-004).
  - CLI: `radiopad bundle export-invoices`, `radiopad plugin verify`.
  - Frontend: report-editor Rewrite menu + Sign/Addendum panel + AI-marked side preview; new `/admin/fhir-import`, `/terminology` (RadLex + RADS tabs), bulk-export panel on `/admin/billing`. Locked tokens only; new helper classes documented in `docs/02-design/design.md`.
  - Regulatory dossier skeleton under `docs/09-regulatory/` (8 files): README, intended-use, samd-classification (IMDRF), iec-62304-sdlc, iso-14971-risk-register, traceability-matrix (119 PRD ids), ce-mark-checklist, clinical-evaluation-plan; full IEC 62304 SDLC mapping; iter-30 statuses promoted to ✅ for RPT-007, STD-001, STD-002, BILL-003, DESK-009, PERF-001..003.
  - EF migration `BidiFhir` (rolled-up: ReportSignatures, ReportVersions.IsAddendum, PushDevices indexes, BidiFhir tables).
- Billing & subscription hardening (PRD BILL-002…007 / MKT-006): new `GET /api/billing/status`, `GET /api/billing/invoices`, `POST /api/billing/refund`, `GET /api/marketplace/connect/status`, `POST /api/marketplace/purchases/{id}/refund`.
- `PlanQuotaService` plan-gated AI quota at `AiGateway`: exhausted plans return `402 { kind: "quota_exceeded", resetAt }`.
- `SuspensionGuardMiddleware` short-circuits mutating non-billing `/api/*` calls on suspended tenants with `402 { kind: "tenant_suspended", suspendedAt }`. `/api/billing/*` and `/api/auth/*` remain reachable.
- Stripe webhook idempotency: every event deduplicated through the new `StripeWebhookEvents` table (unique `EventId`); every Stripe API call (Checkout, Portal, invoices, refunds, Connect) carries a deterministic `Idempotency-Key`.
- `TenantSettings.TrialEndsAt`, `GracePeriodUntil`, `SuspendedAt`, `ChargesEnabled`, `PayoutsEnabled`; new `SubscriptionLifecycleService`; `AuditAction.BillingChanged = 13`.
- Frontend `/admin/billing` dashboard (plan / usage / invoices / feature-flag panels), topbar nav link, and global grace + suspended banners.
- Trial gating on Checkout sessions via `subscription_data.trial_period_days=14`; `automatic_tax.enabled=true` on every checkout.
- Marketplace buyer checkout returns `409 { kind: "connect_not_ready" }` when the publisher's Stripe Connect account has `charges_enabled=false`; webhook now handles `charge.dispute.created`.
- EF migration `BillingHardening`.
- ADR-0005 (Billing & subscription hardening).
- `IpAllowlistMiddleware` (PRD SEC-007), `GET /api/billing/features`, `GET /api/reports/{id}/compare-prior`, `radiopad eval` CLI.
- `radiopad mcp serve` JSON-RPC 2.0 MCP server with 4 read-only tools (PRD §17.4).
- `GET /api/audit/search` advanced filtering and `GET /api/reports/{id}/quality` heuristic score.
- `DictateButton` Web Speech API component; Whisper-local desktop wiring docs.
- Marketplace pipeline: submission → admin review → Stripe Connect purchase with revenue share (`MarketplaceController`, PRD §16).
- `mobile-bundle` GitHub Actions workflow: ubuntu Android APK + macos iOS xcarchive jobs (PRD §15).
- EF Core `InitialCreate` migration covering every entity; auto-applied on boot via `db.Database.MigrateAsync()`.
- External IdP OIDC bearer validation (`OidcBearerMiddleware`) with JWKS auto-refresh and optional MFA `amr` enforcement (PRD AUTH-002).
- TOTP MFA enrollment + verification under `/api/auth/mfa/*` (PRD AUTH-003).
- Magic-link sign-in under `/api/auth/magic-link/*` with MailKit SMTP and dev-link fallback (PRD AUTH-004).
- RFC 8628 OAuth 2.0 Device Authorization Grant under `/api/auth/device/*` for CLI + desktop pairing (PRD AUTH-007).
- `RetentionWorker` `BackgroundService` enforces `TenantSettings.RetentionDays` (PRD §13.3); purges stale `AiRequest` + `ReportVersion` rows, audits `RetentionPurge`. `LegalHold` short-circuits the sweep; `AuditEvents` are never deleted.
- Pluggable `IKmsProvider` abstraction (PRD SEC-003) with `EnvKmsProvider` + `LocalKmsProvider` real implementations and AWS KMS / Azure Key Vault / GCP KMS stubs.
- `POST /api/tenant/settings/kms/verify` endpoint with role-gating and `CmkLastVerifiedAt` stamp.
- Frontend `/prompts` Prompt Studio page (PRD §16.4) and topbar nav link.
- SCIM 2.0 user provisioning under `/scim/v2/` (PRD AUTH-005). Tenant-scoped bearer; supports list / get / create / replace / patch (`active`) / soft-delete plus `ServiceProviderConfig` and `ResourceTypes` discovery.
- `User.IsActive` flag with sign-in enforcement (deprovisioned users get 401).
- `TenantSettings.RetentionDays`, `HashOnlyAuditMode`, `LegalHold` (PRD §13.3) plus `ScimBearerSecret`. Surfaced through `GET/POST /api/tenant/settings`.
- Backend `GET /api/reports/{id}/export/hl7` returns an HL7 v2.5 `ORU^R01` message; subject to RPT-012 gating, audits `ReportExported` with `format:"hl7"`.
- Backend `GET /api/audit/siem` streams the tenant audit chain to a SIEM in NDJSON (`format=json`) or ArcSight CEF (`format=cef`). RBAC-gated; PHI minimisation enforced.
- Frontend `api.reports.exportHl7` blob client.
- Backend `POST /api/auth/signin` mints an HMAC-derived bearer for `(tenant, user)` and audits `UserLogin`.
- Backend `GET /api/usage/analytics` aggregates reporting + AI + governance KPIs (PRD §18).
- Frontend `/analytics` and `/validation` pages, plus topbar nav links.
- Login page now hits `/api/auth/signin`, stores the token in the OS secure store, and primes the API client cache so subsequent requests are bearer-authenticated.
- CI: `desktop-bundle.yml` publishes the .NET backend as a self-contained single-file binary and stages it under `desktop/src-tauri/binaries/` for the Tauri sidecar.
- Desktop: Tauri 2 sidecar wiring spawns the bundled `radiopad-api` binary at startup (PRD DESK-015) and copies `rulebooks/` + `templates/` as bundle resources.
- Frontend: `api.ts` now attaches an `Authorization: Bearer <token>` header from an in-memory cache populated at startup from the secure-auth store. `setActiveAuthToken` / `getActiveAuthToken` exported.
- CLI: `radiopad ingest fhir <file>` companion that POSTs a FHIR R4 ServiceRequest/Bundle JSON file to the ingest endpoint.
- Frontend `/offline` page (PRD MOB-005) for inspecting, editing, force-syncing, and discarding offline drafts.
- `frontend/lib/secureAuth.ts` (PRD MOB-006) — secure auth-token store backed by Keychain/Keystore via `@capacitor-community/secure-storage`, with `@capacitor/preferences` and `localStorage` fallbacks.
- `mobile/package.json` pulls `@capacitor-community/secure-storage`.
- CI: `.github/workflows/desktop-bundle.yml` — multi-OS Tauri 2 bundler with optional Authenticode / Apple Developer ID / Tauri updater signing gated on repository secrets.
- FHIR R4 ingest endpoint `POST /api/ingest/fhir/servicerequest` — accepts a `ServiceRequest` resource or a `Bundle` containing one, reuses the existing bearer/idempotency/audit pipeline, and maps standard FHIR fields onto the Draft report.
- CLI `radiopad ingest` and `radiopad dicom fetch <report-id>` companion commands.
- Mobile/desktop offline draft store at `frontend/lib/offlineDrafts.ts` (backed by `@capacitor/preferences` natively, `localStorage` on the web). Auto-syncs via `@capacitor/network` when connectivity returns.
- `frontend/app/ShellBridge.tsx` listens for the Tauri `radiopad://new-report` event and starts mobile auto-sync.
- `mobile/package.json` pulls `@capacitor/preferences` and `@capacitor/network`.
- HL7/FHIR ingest webhook (PRD INT-001..004): new `POST /api/ingest/order` with constant-time per-tenant bearer auth (`TenantSettings.IngestBearerSecret`), idempotent on `accessionNumber`, audited as `OrderIngested`. Bad bearers audit a `PolicyViolation`.
- DICOMweb study-context fetch (PRD DCM-001..006): new `IDicomWebClient` (vendor-neutral WADO-RS/QIDO-RS) and `GET /api/reports/{id}/dicom-context`. Returns `configured:false` when not set up; otherwise queries the tenant-configured PACS and audits `DicomContextFetched`.
- `TenantSettings.IngestBearerSecret`, `DicomWebBaseUrl`, `DicomWebBearerSecret`. The secrets are write-only via the API: `GET /api/tenant/settings` exposes `ingest.bearerConfigured` and `dicomWeb.bearerConfigured` flags, never the raw values.
- New `AuditAction.OrderIngested = 10` and `AuditAction.DicomContextFetched = 11`.
- Desktop (Tauri): second global shortcut `Ctrl/Cmd+Shift+N` emits `radiopad://new-report` on the AppHandle for an upcoming frontend listener. Existing `Ctrl/Cmd+Shift+R` window focus shortcut preserved.
- Frontend: admin Settings page gains an "Integrations" panel for the ingest bearer + DICOMweb base URL/bearer. Uses only locked design tokens.
- Iteration 14 integration tests under `tests/RadioPad.Api.Tests/Integration/Iteration14Tests.cs`: ingest webhook (503/401/200 + dedup) and DICOM context (`configured:false` when unset).
- Cost-aware provider routing (PRD AI-009 / BILL-003): new `IProviderRouter` + `EfProviderRouter`. `POST /api/reports/{id}/ai` with no `providerId` auto-routes to the cheapest enabled provider that satisfies tenant + PHI policy and echoes `routedBy:"auto"` + `selectedProviderId`; an explicit `providerId` echoes `routedBy:"manual"`. `ProviderConfig` gains `CostPerInputKToken`, `CostPerOutputKToken`, `MaxCostPerCallUsd`.
- Hallucination detector (PRD AI-007), 100% admin-managed: new `HallucinationDetector` runs on every `POST /api/reports/{id}/validate` and emits `RuleId="ai:unsupported_claim"` when an Impression sentence falls below the configured support fraction. Toggle, severity, threshold, and allow-list all live in the new `TenantSettings` row and are editable from `/admin/settings`.
- Admin tenant settings surface (PRD BILL-001 / AI-007): new `TenantSettings` entity + `GET/POST /api/tenant/settings` (RBAC `MedicalDirector`/`ReportingAdmin`/`ItAdmin`) + new Next.js page at `frontend/app/admin/settings/page.tsx`. Surfaces hallucination-detector controls, plan tier, feature flags, and Stripe subscription status.
- PDF + DOCX export (PRD RPT-011): `GET /api/reports/{id}/export/pdf` (QuestPDF) and `/export/docx` (DocumentFormat.OpenXml). Both subject to RPT-012 (`Status>=Validated`) and audit `ReportExported`.
- Stripe billing (PRD BILL-001 / BILL-006): new `BillingController` exposing `POST /api/billing/checkout`, `POST /api/billing/portal`, and a signature-validated `POST /api/billing/webhook`. Subscription state writes back to `TenantSettings` via webhook. API keys and webhook secret read from `STRIPE_SECRET_KEY` / `STRIPE_WEBHOOK_SECRET` env vars (never committed).
- Terminology adapter seam (PRD STD-001 / STD-002): new `ITerminologyAdapter` + `NoOpTerminologyAdapter` for licensed RadLex / ACR RADS plug-ins.
- ADR-0004 (Authentication & SSO pipeline) — proposed direction (OpenIddict + JWT + rotated refresh + per-tenant session revocation). Deferred to a dedicated iteration; documented so AUTH-001/004/006/007 do not silently slip.
- Iteration 13 integration tests under `tests/RadioPad.Api.Tests/Integration/Iteration13Tests.cs`: cost-aware routing (auto vs manual), hallucination detector unit + allow-list, tenant settings RBAC + roundtrip, PDF/DOCX export gating + magic-byte assertions, Stripe webhook (rejects bad signature, 503 when secret missing, 200 on valid HMAC).
- Frontend: report editor toolbar gains **Export PDF** and **Export DOCX** buttons next to the existing text/FHIR exports; new `requestBlob` helper in `frontend/lib/api.ts` for binary downloads.
- Audit-chain verification (PRD §13.2 / AUTH-006): `IAuditLog.VerifyChainAsync` re-computes the SHA-256 chain; new `GET /api/audit/verify` returns `200 { intact: true }` when clean and `422 { kind: "audit_chain_broken", firstBrokenEventId, … }` on tamper. Restricted to `ComplianceReviewer` / `ItAdmin` / `MedicalDirector`.
- Rulebook rollback (PRD RB-008): `POST /api/rulebooks/{id}/rollback { version }` materialises a new approved copy whose version carries a `+rollback-<timestamp>` suffix; existing rows are never mutated.
- Multi-mode AI generation (PRD AI-001, AI-002, RPT-006, RPT-007): `POST /api/reports/{id}/ai` now accepts `mode ∈ { impression | cleanup | draft | concise | formal | patient_friendly | referring_summary }`. Unknown modes return `400 { kind: "validation", supportedModes }`. Each mode resolves a per-mode prompt block from the active rulebook (with conservative defaults) and routes through the same `AiGateway` so PHI policy + usage ledger are unchanged.
- RBAC enforcement (PRD AUTH-002): new `TenantedController.RequireRole(...)` returns `403 { kind: "forbidden", requiredRoles }`. Applied to `POST /api/rulebooks/{id}/approve`, `/deprecate`, `/rollback` (allow `MedicalDirector`, `ReportingAdmin`, `ItAdmin`), `POST /api/providers`, and `POST/DELETE /api/lexicon`. `GET /api/audit/verify` allows `ComplianceReviewer` / `ItAdmin` / `MedicalDirector`.
- Tenant lexicon (PRD STD-006): new `TenantLexicon` entity + `LexiconController` exposing `GET /api/lexicon`, `POST /api/lexicon`, `DELETE /api/lexicon/{id}`. `ReportValidator.Validate(report, rulebook, lexicon)` overload emits `RuleId = "lexicon:<term>"` `Severity = Warning` for forbidden terms in any section.
- Prior-report comparison (PRD RPT-009): `GET /api/reports/{id}/prior` returns the most recent same-tenant report with `Status >= Acknowledged` and the same body part.
- `frontend/lib/api.ts`: `runAi` mode is now a typed union; new `api.reports.prior(id)` and `api.lexicon.{list,save,delete}` clients.
- AI usage ledger: `IAiUsageStore` + `EfAiUsageStore` persist one `AiRequest` row per `AiGateway.RouteAsync` call (`status` ok / blocked / error) for AI-012 / BILL-002 traceability.

### Changed
- Billing hardening follow-up: Stripe webhook dedupe is source-scoped, marketplace webhooks require a secret outside `Testing`, refund requests validate `amountCents`/`reason` and tenant PaymentIntent ownership, Connect status audits only readiness changes, expired grace periods are treated as suspended, and plan quotas now enforce token budgets as well as AI-call counts.
- Stripe environment variables: canonical `RADIOPAD_STRIPE_SECRET_KEY` / `RADIOPAD_STRIPE_WEBHOOK_SECRET` (read through the new `BillingEnv` helper). Legacy `STRIPE_SECRET_KEY` / `STRIPE_WEBHOOK_SECRET` are accepted as a fallback for one release; remove before v0.3.
- `Tenant.AllowSandboxRulebooks` (default `false`) gates AI runs against non-Approved rulebooks (PRD RB-010). Violations return `409 { kind: "rulebook_governance" }` via the new `RulebookGovernanceException`.
- `GET /api/usage/summary?from&to` — per-tenant rollup (total / ok / blocked / error counts, input/output tokens, avg latency, `byProvider`).
- `GET /api/reports/{id}/export/text?preview=true` — returns the narrative without auditing or mutating status, for in-editor preview.
- Server-side report list pagination (`skip`, `take`, `X-Total-Count`) and dashboard pager.
- `GET /api/health/ready` — DB connectivity readiness probe.
- `GET /api/reports/{id}/versions` returning the most-recent 50 `ReportVersion` snapshots.
- `radiopad provider test --id <guid>` CLI command performing a real round-trip against a mock provider.
- Validation panel grouped by severity (Blocker / Warning / Info) with per-bucket counts.
- Enterprise SaaS documentation baseline: root governance, `.github/` templates and instructions, `.cursor/rules/`, full `docs/00-product/` through `docs/08-user-docs/` hierarchy, `openapi/openapi.yaml` v0.2 surface, `docs/_reports/`, `docs/_archived_documentation/2026-05-04/ARCHIVE_INDEX.md`, refreshed master `docs/INDEX.md`.

### Changed
- `GET /api/reports/{id}/export/{text,fhir}` now require `Status >= Validated` (PRD RPT-012); otherwise return `409 { kind: "report_state" }`. Successful export writes `AuditAction.ReportExported` and bumps status to `Exported`.
- `AiGateway` constructor takes an optional `IAiUsageStore?`; existing callers/tests stay source-compatible.
- `PATCH /api/reports/{id}` now appends a `ReportVersion` snapshot on every edit.

### Security
- `AiGateway.RouteAsync` now writes an `AuditAction.ProviderBlocked` event before rethrowing `ProviderPolicyException`.
- Billing audit (`IBillingAudit`) hashes `email`, `stripeCustomerId`, `paymentIntentId`, and `subscriptionId` to `sha16:<hex>` before they land in `AuditEvents.DetailsJson`. Raw identifiers stay on `TenantSettings` and on Stripe's API; they never enter the audit chain.

## [0.1.0] — 2026-05-04 — Architecture baseline

### Added
- ASP.NET Core 8 backend (`Domain`, `Application`, `Validation`, `Infrastructure`, `Api`).
- Next.js 16 App Router frontend with locked Open Design system.
- Tauri 2 desktop shell with `Ctrl+Shift+R` global focus and clipboard TTL wipe.
- Capacitor 6 mobile shell.
- .NET 8 CLI (`radiopad`) with login, daemon, rulebook, report, audit, provider commands.
- Append-only `AuditEvents` with SHA-256 hash chain.
- Five seed rulebooks: chest CT, brain MRI, abdomen US, lumbar spine MRI, knee X-ray.
- Five report templates and matching golden test cases.
- AI gateway with Mock / Anthropic / Ollama adapters and PHI policy enforcement.
- FHIR `DiagnosticReport` text export.
