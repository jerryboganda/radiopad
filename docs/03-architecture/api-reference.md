# RadioPad - HTTP API reference

**Status:** Current  ·  **Owner:** Engineering  ·  **Last Updated:** 2026-05-20
> Base URL: `http://127.0.0.1:7457` (development).
> Development endpoints accept the tenant headers below. Production requests require either a validated RadioPad bearer (`Authorization: Bearer rp_...`), the HttpOnly `radiopad_session` cookie issued by browser sign-in flows, or a validated OIDC bearer projected by `OidcBearerMiddleware`; raw dev headers are accepted in production only when `RADIOPAD_DEV_HEADERS=1`. Public production exceptions are limited to health checks, billing webhook, magic-link request/consume, logout, and RFC 8628 device authorize/token bootstrap endpoints.

## Auth and context headers

| Header | Required | Notes |
| --- | --- | --- |
| `Authorization: Bearer rp_<opaque>` | production path | 12-hour RadioPad bearer bound to tenant, user, session epoch, and issued-at time. Current middleware also requires tenant/user lookup hints. |
| `Authorization: Bearer <jwt>` | production path | OIDC JWT accepted when OIDC env configuration is enabled. |
| `X-RadioPad-Tenant` | dev/test or lookup hint | Tenant slug. Authoritative only when dev/test headers are explicitly enabled. |
| `X-RadioPad-User`   | dev/test or lookup hint | User email. Authoritative only when dev/test headers are explicitly enabled. |
| `X-RadioPad-RequestId` | optional | Echoed back; auto-generated when missing. Used for log correlation and the `requestId` field on error responses. |

Production login direction is generic OIDC Authorization Code + PKCE with
magic-link fallback. Web sessions use the current bearer-backed `rp_session`
HttpOnly/SameSite cookie where implemented, while native desktop/mobile store
session material in OS secure storage. The web OIDC entry points are
`GET /api/auth/oidc/authorize` and `GET /api/auth/oidc/callback`.

`POST /api/auth/signin` is **dev/test-only** and fails closed unless explicit
dev/test headers are enabled. Hosted deployments must use OIDC, magic link,
SAML/WebAuthn/device flow, or another proof-based flow instead of exchanging a
public tenant/user tuple.

The enterprise identity foundation is backend-only in this release. `GlobalUser`, `ExternalIdentity`, `TenantMembership`, and `AuthSession` rows do not add public endpoints or response fields; `/api/tenant/me` continues to return the resolved tenant-scoped user.

Errors are RFC 7807 `application/problem+json` for unhandled exceptions. PHI/policy failures from `POST /api/reports/{id}/ai` are converted to a `403 Forbidden` JSON body `{ error, kind: "provider_policy" }` by the controller - the global handler is the safety net for everything else:

```json
{
  "type": "internal/unhandled",
  "title": "Internal error",
  "status": 500,
  "detail": "(safe summary)",
  "requestId": "rq-9f2câ€¦"
}
```

Controller-handled provider policy failures return 403 `kind:"provider_policy"`. `ProviderPolicyException` raised outside those controller guards (or any unhandled propagation) is mapped by the global safety net to 409 with `type: policy/provider`. Unhandled exceptions â†’ 500 with `type: internal/unhandled`. The audit log is **never** mutated by error handling.

## Health

`GET /api/health` → `200 { "status": "ok", "service": "radiopad-api", "time": "..." }`.
`GET /api/health/ready` → `200` when the DB is reachable, `503` otherwise. Use as a Kubernetes / Compose readiness probe.

## Tenant

`GET /api/tenant/me` â†’ resolved tenant + user.

## Reports

| Method | Path | Description |
| --- | --- | --- |
| GET    | `/api/reports`                  | List reports. Query: `modality`, `status` (int), `q`, `skip`, `take` (â‰¤500). Response includes `X-Total-Count` header. |
| POST   | `/api/reports`                  | Create. Body `CreateReportDto { modality, bodyPart, indication, comparison?, accessionNumber, rulebookId?, templateId? }`. â†’ 201. |
| GET    | `/api/reports/{id}`             | Get one. |
| PATCH  | `/api/reports/{id}`             | Update sections. Each call also appends a `ReportVersion` snapshot. |
| GET    | `/api/reports/{id}/versions`    | Recent edit history (most-recent 50). |
| POST   | `/api/reports/{id}/validate`    | Run rulebook validation. Returns `ValidationFinding[]`. |
| POST   | `/api/reports/{id}/ai`          | Body `{ mode: "impression" \| "cleanup" \| "draft" \| "concise" \| "formal" \| "patient_friendly" \| "referring_summary", providerId? }`; omitted `providerId` uses auto-routing. **Rate-limited** (`ai` policy: 60/min/tenant). Synchronous — holds the connection for the whole provider call; kept for older desktop builds. New clients use the async job pair below. |
| POST   | `/api/reports/{id}/ai/jobs`     | Async variant of `/ai` (2026-07-12): same body/validation, returns `202 { jobId, status:"running" }` immediately. Generation runs detached from the connection — a dropped client no longer cancels it. **Rate-limited** (`ai`). |
| POST   | `/api/reports/{id}/generate/jobs` | Async variant of `/generate`: returns `202 { jobId }`; on completion the poll's `result` is the updated `Report` (sections adopted + version snapshot, like the sync endpoint). **Rate-limited** (`ai`). |
| GET    | `/api/reports/{id}/ai/jobs/{jobId}` | Poll a job (both kinds): `{ jobId, kind, mode, status: "running"\|"ok"\|"error", elapsedMs, result?, error?, errorKind? }`. Fast request; NOT `ai`-rate-limited. 404 `kind:"job_not_found"` after a server restart (jobs are in-memory; results retained ~15 min). |
| POST   | `/api/reports/{id}/acknowledge` | Mark as Acknowledged. Blocked with 409 `kind:"validation_blockers"` when strict validation has any `Blocker`. |
| GET    | `/api/reports/{id}/export/text` | Plain-text export. `preview=true` is draft-safe and does not audit/export; final export requires Acknowledged/Exported. |
| GET    | `/api/reports/{id}/export/json` | Structured JSON report export. Requires Acknowledged/Exported and audits `ReportExported`. |
| GET    | `/api/reports/{id}/export/fhir` | FHIR R4 `DiagnosticReport` JSON. Requires Acknowledged/Exported and audits `ReportExported`. |
| GET    | `/api/reports/{id}/export/pdf`  | PDF export. Requires Acknowledged/Exported and audits `ReportExported`. |
| GET    | `/api/reports/{id}/export/docx` | DOCX export. Requires Acknowledged/Exported and audits `ReportExported`. |
| GET    | `/api/reports/{id}/export/hl7`  | HL7 v2.5 ORU^R01 export. Requires Acknowledged/Exported and audits `ReportExported`. |
| POST   | `/api/reports/{id}/rewrite`     | Iter-30 RPT-007. Body `{ mode: "concise"\|"formal"\|"patient_friendly"\|"referring_summary", sections?: string[], providerId? }`. **Rate-limited** (`ai`). PHI policy enforced. |
| POST   | `/api/reports/{id}/sign`        | Iter-30. Body `{ role: "Primary"\|"CoSigner"\|"Addendum", note? }`. First signature must be `Primary`; non-Primary roles require an existing Primary. SHA-256 hashed. Audited as `ReportSigned`. |
| POST   | `/api/reports/{id}/addendum`    | Iter-30. Body `{ body }`. Requires existing Primary signature. Creates a new `ReportVersion` with `isAddendum=true`. Audited as `ReportAddendumAppended`. |
| GET    | `/api/reports/{id}/signatures`  | List signatures for a report (oldest first). |

## Rulebooks

| Method | Path | Description |
| --- | --- | --- |
| GET    | `/api/rulebooks`              | List. |
| GET    | `/api/rulebooks/{id}`         | Get with YAML body. |
| POST   | `/api/rulebooks`              | Save (create or version-bump). |
| POST   | `/api/rulebooks/validate`     | Validate raw YAML without persisting. |
| POST   | `/api/rulebooks/test`         | Run golden cases against raw YAML. |
| POST   | `/api/rulebooks/{id}/approve`    | Audit-logged. Records `RulebookApproved`. |
| POST   | `/api/rulebooks/{id}/deprecate`  | Audit-logged. Records `RulebookDeprecated`. |

## Validation packs (Iter-35)

Versioned, tenant-scoped bundles of golden test cases (`{report, expectFlagged}`) that a rulebook must pass before promotion. Lifecycle: `Draft` â†’ `Approved` (Medical Director / IT Admin) â†’ `Deprecated` (terminal - re-approval is rejected with `409 kind:"validation_packs"`).

| Method | Path | Role | Description |
| --- | --- | --- | --- |
| GET    | `/api/validation-packs` | any member | Optional `?rulebookId=`. Returns pack rows with `caseCount`. |
| POST   | `/api/validation-packs` | MedicalDirector / ItAdmin | Body `{ rulebookId, version, name?, goldenCases: [{name?, report, expectFlagged?}] }`. 409 with `kind:"validation_packs"` on duplicate `(rulebookId, version)`. |
| POST   | `/api/validation-packs/{id}/approve`   | MedicalDirector / ItAdmin | Promote `Draft â†’ Approved`. Audits `ValidationPackApproved`. |
| POST   | `/api/validation-packs/{id}/deprecate` | MedicalDirector / ItAdmin | Mark `Deprecated` (terminal). Audits `ValidationPackDeprecated`. |
| POST   | `/api/validation-packs/{id}/run`       | Radiologist+ | Execute the pack against the latest matching rulebook. Returns `{passed, failed, totalCases, failures: [{caseId, missing, unexpected}]}`. Audits `ValidationPackRun` with pass/fail counts. |
| GET    | `/api/validation-packs/{id}/export`    | MedicalDirector / ItAdmin | Canonical export `{id, rulebookId, version, name, status, createdAt, approvedAt, cases}`. |

## Templates

`GET /api/templates`, `GET /api/templates/{id}`.

## Billing endpoints

All billing endpoints are tenant-scoped via the standard headers. Stripe API calls carry deterministic `Idempotency-Key`s; webhook events are deduplicated against the `StripeWebhookEvents` table by `(Source, EventId)`. Audit rows write `AuditAction.BillingChanged` (= 13) with PII (`email`, `stripeCustomerId`, `paymentIntentId`, `subscriptionId`) hashed to `sha16:<hex>` in `DetailsJson`.

| Method | Path | Role | Behavior |
| --- | --- | --- | --- |
| POST   | `/api/billing/checkout` | `BillingAdmin` / `ItAdmin` / `MedicalDirector` | Start a Stripe Checkout session (PRD BILL-001). Trial gated via `subscription_data.trial_period_days=14`; `automatic_tax.enabled=true`; session metadata includes `radiopadFlow=billing`. |
| POST   | `/api/billing/portal`   | `BillingAdmin` / `ItAdmin` / `MedicalDirector` | Open the Stripe Billing Portal (PRD BILL-006). |
| POST   | `/api/billing/webhook`  | unauthenticated (signature) | Validates against `RADIOPAD_STRIPE_WEBHOOK_SECRET` (legacy `STRIPE_WEBHOOK_SECRET` accepted for one release); deduplicated by `(Source, EventId)`. Iter-36: handles `checkout.session.completed`, `customer.subscription.{created,updated,deleted}`, `invoice.payment_succeeded` (clears `gracePeriodUntil` + `suspendedAt`, sets `subscriptionStatus="active"`), and `invoice.payment_failed` (opens 7-day `gracePeriodUntil`; escalates to `suspendedAt` once that window has elapsed). See [docs/06-operations/billing-stripe.md](../06-operations/billing-stripe.md). |
| GET    | `/api/billing/status`   | any tenant member | Returns string `plan`, `subscriptionStatus`, `trialEndsAt`, `gracePeriodUntil`, `suspendedAt`, `currentPeriodEnd`, and `customerConfigured` (PRD BILL-002/004). |
| GET    | `/api/billing/credits`  | any tenant member | Month-to-date AI credit balance: `{ plan, periodStart, periodEnd, used: { calls, inputTokens, outputTokens }, limits, remaining, trialEndsAt }`; `trialEndsAt` is emitted as `null` outside Trial. Reuses `PlanQuotaService` so the values match the AI gateway's enforcement gate (PRD BILL-002 / BILL-007). |
| GET    | `/api/billing/invoices` | `BillingAdmin` / `ItAdmin` / `MedicalDirector` | Newest-first list of up to 20 Stripe invoices with `hostedInvoiceUrl` + `invoicePdf` (PRD BILL-005). |
| POST   | `/api/billing/refund`   | `BillingAdmin` / `ItAdmin` | Issue a refund against a tenant-owned `paymentIntentId`; optional partial `amountCents` and validated `reason`. Audited as `BillingChanged` after success or tenant-mismatch rejection (PRD BILL-007). |
| POST   | `/api/marketplace/webhook` | unauthenticated (signature) | Requires `RADIOPAD_STRIPE_WEBHOOK_SECRET` outside `Testing`; unsigned fixtures are allowed only in test hosts. Deduplicated by `(Source, EventId)`. |
| GET    | `/api/marketplace/connect/status` | `BillingAdmin` / `ItAdmin` | Stripe Connect onboarding state for the publisher (`chargesEnabled`, `payoutsEnabled`, requirements). Readiness changes are audit-logged only when those flags change. When `chargesEnabled=false`, buyer checkout returns 409 `kind:"connect_not_ready"` (PRD MKT-006). |
| POST   | `/api/marketplace/purchases/{id}/refund` | `BillingAdmin` / `ItAdmin` / `MedicalDirector` | Refund a marketplace purchase; reverses platform fee and publisher transfer; audited as `BillingChanged`. |

Public marketplace catalogue endpoints (`GET /api/marketplace/listings`, `GET /api/marketplace/listings/{id}`) return only listing metadata. `ArtifactBody` is not exposed from public listing reads, so paid artifacts cannot be fetched without a purchase-specific delivery path.

### Subscription lifecycle errors (402)

Mutating non-billing `/api/*` endpoints can now return `402 Payment Required` with an RFC-7807 problem body:

```json
// AI gateway plan-quota gate (PlanQuotaService â†’ QuotaExceededException)
{
  "type": "billing/quota-exceeded",
  "title": "Plan quota exceeded",
  "status": 402,
  "kind": "quota_exceeded",
  "resetAt": "2026-06-01T00:00:00Z"
}

// SuspensionGuardMiddleware on a tenant with TenantSettings.SuspendedAt != null
{
  "type": "billing/tenant-suspended",
  "title": "Tenant suspended",
  "status": 402,
  "kind": "tenant_suspended",
  "suspendedAt": "2026-04-29T14:11:02Z"
}
```

`SuspensionGuardMiddleware` exempts `/api/billing/*` and `/api/auth/*` so operators can pay an invoice or sign in. It also treats an elapsed `GracePeriodUntil` as suspended even before the next AI quota check. Connect onboarding gating from `/api/marketplace/checkout` returns 409 with `kind:"connect_not_ready"`.

The canonical `kind` enum surfaces in `openapi/openapi.yaml#/components/schemas/Problem` and now includes `quota_exceeded`, `tenant_suspended`, `connect_not_ready`, and `validation_blockers` alongside the existing `validation`, `forbidden`, `report_state`, `rulebook_governance`, `audit_chain_broken`, `provider_policy`, `provider_unavailable`, etc.

## Usage

| Method | Path | Description |
| --- | --- | --- |
| GET | `/api/usage/summary?from=&to=` | Per-tenant AI usage rollup over the optional `[from,to]` window (default = lifetime). Reads the `AiRequest` ledger written by `AiGateway`. Returns totals (`totalRequests`, `okCount`, `blockedCount`, `errorCount`, `inputTokens`, `outputTokens`, `avgLatencyMs`), `costTotalUsd` (iter-34 BILL-005), and a `byProvider[]` array. Each `byProvider` row carries `provider`, `adapter`, `requests`, `inputTokens`, `outputTokens`, plus iter-34 `costInputUsd` / `costOutputUsd` / `costTotalUsd` priced via the matching tenant `ProviderConfig.CostPerInputKToken` / `CostPerOutputKToken` (USD per 1K tokens). When no current `ProviderConfig` matches the historical row's name (retired provider) the cost columns stay `0` and `unpriced=true`. |
| GET | `/api/usage/analytics?from=&to=` | PRD §18.1 / §18.2 governance + product KPIs over the optional window (default = last 30 d). Embeds the same `ai` rollup shape (including `costTotalUsd` and the priced `byProvider[]`). |

## Providers

| Method | Path | Description |
| --- | --- | --- |
| GET    | `/api/providers`              | Returns providers without secrets (only `apiKeyConfigured: bool`, plus `quality` 0–1, the operator-supplied compliance class, and the iter-34 PROV-009 `retentionLabel` free-text label). |
| POST   | `/api/providers`              | Save (create when `id` missing). `apiKeySecretRef` accepts `env:NAME` only; empty keeps an existing secret ref or configures a no-key provider. OpenAI-compatible endpoints are validated for scheme/private-network safety before save. Provider config saves are audited. |
| POST   | `/api/providers/{id}/health`  | Adapter-specific health probe. Returns `{ ok, error?, note?, status?, runtime?, probedAt }` and writes an audit row. For `ollama-chat` calls `GET {base}/api/tags`; for `vllm` calls `GET {base}/v1/models`; for `llama-cpp` calls `GET {base}/health`; for `openai-compatible` calls `GET {base}/v1/models` without sending bearer auth and blocks unsafe endpoint targets; for CLI adapters validates the configured binary without sending a prompt. ItAdmin / ReportingAdmin / MedicalDirector only. |
| POST   | `/api/providers/{id}/oauth/refresh-token`        | **Iter-35 PROV-007.** Save (or replace) the per-provider OAuth refresh token in the encrypted vault. Body `{ refreshToken: string, expiresAt?: string \| null, rotationPolicy?: "never"\|"before_expiry"\|"every_24h" }`. Token is encrypted with AES-256-GCM under a per-token DEK wrapped by the tenant KMS KEK. Returns `204 No Content`. **ItAdmin / BillingAdmin only.** Audited as `OAuthRefreshRotated` with `kind:"saved"`; never logs the token bytes. Returns `503 kind:"kms_unavailable"` when no tenant KEK is configured. |
| DELETE | `/api/providers/{id}/oauth/refresh-token`        | **Iter-35 PROV-007.** Delete the stored refresh token (clears all four ciphertext columns + timestamps; rotation policy is preserved). `204 No Content`. ItAdmin / BillingAdmin only. Audited as `OAuthRefreshRotated` with `kind:"deleted"`. |
| GET    | `/api/providers/{id}/oauth/refresh-token/status` | **Iter-35 PROV-007.** Returns `{ hasToken: bool, updatedAt: string\|null, expiresAt: string\|null, rotationPolicy: string }`. **Never** returns the ciphertext, IV, tag, or wrapped DEK. ItAdmin / BillingAdmin only. |

### Adapter catalog

The provider rows reference an `adapter` id from this fixed catalog. Compliance class shown is the **default for catalog seeding**; the operator-supplied `Compliance` column on the `ProviderConfig` row is what `AiGateway.EnforcePhiPolicy` actually evaluates at runtime.

| Adapter id | Kind | Default compliance | Notes |
| --- | --- | --- | --- |
| `mock` / `anthropic` / `azure-openai` / `aws-bedrock` / `google-vertex` / `openai` | HTTP | `Sandbox` unless the tenant has a documented `PhiApproved` posture | Cloud-vendor adapters. |
| `openai-compatible` | HTTP | `Sandbox` (or `LocalOnly` for `127.0.0.1` / `localhost` / `*.local` hosts) | Generic OpenAI-API-compatible endpoint. **Iter-36:** when `apiKeySecretRef` is set but resolves empty, the adapter throws `ProviderPolicyException("api_key_missing")` instead of letting upstream return 401. Private-network endpoints require `LocalOnly`; PHI requires `LocalOnly` or the explicit `RADIOPAD_OPENAI_COMPATIBLE_ALLOW_PHI=1` review flag. |
| `ollama-chat` / `vllm` / `llama-cpp` | HTTP | `LocalOnly` | In-tenant local servers (iter-32 AI-011). |
| `gemini-cli` | CLI subprocess | `Sandbox` | **Iter-36 AI-012.** Shells out to `gemini` headless mode with `--output-format json`. Binary override env `RADIOPAD_GEMINI_BIN` (default `gemini`). PHI and secret-like prompts are refused by the adapter even if a row is misclassified. |
| `codex-cli` | CLI subprocess, fail-closed | `Sandbox` | **Iter-36 AI-012 / Iter-47 hardening.** Shells out to `codex exec --sandbox read-only -` only when `RADIOPAD_CODEX_CLI_ENABLED=1`. Binary override env `RADIOPAD_CODEX_BIN` (default `codex`). The adapter does not opt into `--full-auto`; PHI and secret-like prompts are refused. |
| `ubag` | HTTPS automation gateway | `PhiApproved` | **Iter-50; PHI gate removed 2026-06-27 (operator decision).** Routes report prompts to production UBAG through the RadioPad backend. `model` selects the UBAG target (`gemini_web`, `deepseek_web`, `chatgpt_web`, or `mock`); default UI preset uses `gemini_web`. Secret-shaped prompts are still refused; the PHI block was removed so cleanup/impression/rewrite/cross-check run on patient-linked reports. Ordered ChatGPT -> Gemini -> DeepSeek runs use `/api/ubag/workflows/ordered-web-chain`, not report drafting. |

CLI adapters share these guard-rails (`RadioPad.Infrastructure.Providers.Cli.CliProviderRunner`):

- The composed prompt is piped on **stdin**; arguments are passed via `ProcessStartInfo.ArgumentList` so a prompt can never cross a shell boundary.
- Per-process timeout defaults to **60s**; override with `RADIOPAD_CLI_PROVIDER_TIMEOUT_MS`. Timeout / missing-binary / non-zero exit all surface as `ProviderTransportException`.
- Binary allowlist via `RADIOPAD_CLI_PROVIDER_ALLOWED_PATHS` (semicolon-separated). Production API hosts require a non-empty allowlist before CLI providers can execute; missing production allowlists throw `ProviderPolicyException("cli_binary_allowlist_required")`. When set, the resolved binary path must be in the list or the adapter throws `ProviderPolicyException("cli_binary_not_allowed")`. Empty / unset allowlists are development-only and use PATH lookup.
- The subprocess environment is scrubbed to OS basics (`PATH`, home/config/temp locations). Add only the variables a reviewed CLI needs via `RADIOPAD_CLI_PROVIDER_ENV_ALLOWLIST`.
- Prompt sanitiser refuses NUL and other C0 control characters (tab / newline / CR are allowed).
- Adapter-level policy refuses `containsPhi:true` and secret-shaped prompts before process launch.

## UBAG Hub

UBAG is a governed browser-automation gateway for report AI work. RadioPad never
calls UBAG from the browser; the frontend calls RadioPad's `/api/ubag/*`
endpoints, and the backend calls production UBAG with server-only auth.

Required posture:

- UBAG provider rows are `PhiApproved` (operator decision 2026-06-27); the workflow sends only de-identified report text and the adapter's PHI gate was removed.
- Secret-shaped prompts are still rejected before dispatch.
- Generated output is draft material and is rendered with `.ai-mark` until reviewed.
- Provider login, CAPTCHA, 2FA, consent, cookies, and credentials remain manual in UBAG Browser Sessions.

Configuration:

| Variable | Default | Notes |
| --- | --- | --- |
| `RADIOPAD_UBAG_BASE_URL` | `https://ubag.polytronx.com` | Production UBAG base URL. |
| `RADIOPAD_UBAG_API_VERSION` | `2026-05-22` | Sent in UBAG job/workflow envelopes. |
| `RADIOPAD_UBAG_TIMEOUT_MS` | `120000` | HTTP timeout and adapter polling budget. |
| `RADIOPAD_UBAG_ALLOWED_TARGETS` | `chatgpt_web,gemini_web,deepseek_web,mock` | Comma-separated allowlist for single jobs/provider adapter use. |
| `RADIOPAD_UBAG_AUTH_SECRET_REF` / `RADIOPAD_UBAG_AUTH_SECRET` | empty | Optional server-only auth. Prefer `env:NAME` in `RADIOPAD_UBAG_AUTH_SECRET_REF`. |
| `RADIOPAD_UBAG_AUTH_SCHEME` | `Bearer` | `Bearer`, `Basic`, or `Raw`. |
| `RADIOPAD_UBAG_AUTH_HEADER` | `Authorization` | Optional custom header name. |

| Method | Path | Description |
| --- | --- | --- |
| GET | `/api/ubag/status` | Health, browser summary, target readiness, allowed targets, and ordered-chain targets. Roles: ItAdmin / ReportingAdmin / MedicalDirector / ComplianceReviewer. |
| POST | `/api/ubag/jobs` | Submit one non-PHI job. Body `{ target, prompt }`. Roles: ItAdmin / ReportingAdmin / MedicalDirector. Audits `AiResponse` metadata with hashes only. |
| GET | `/api/ubag/jobs/{id}` | Poll a UBAG job. |
| POST | `/api/ubag/workflows/ordered-web-chain` | Create and run the fixed `chatgpt_web -> gemini_web -> deepseek_web` workflow. Body `{ prompt, name? }`. |
| GET | `/api/ubag/workflows/runs/{id}` | Poll a UBAG workflow run. |

## AI routing preview

| Method | Path | Description |
| --- | --- | --- |
| GET | `/api/ai/routing/preview?phi=&modality=&input=&output=` | **Iter-32 AI-010.** Explains the gateway's routing decision for a hypothetical `(modality, phi, tokens)` tuple without performing a real AI call. Returns the chosen provider plus a per-candidate `costScore`, `qualityScore`, `latencyScore`, `compositeScore`, `eligible`, and `ineligibleReason`. Weights come from per-tenant `TenantSettings.RoutingWeightsJson` (default `{"cost":0.5,"quality":0.4,"latency":0.1}`). Tie-breaks use P95 24h latency from `AiRequest`. ItAdmin / MedicalDirector only. Audited as `RoutingPreviewQueried`. |
| POST | `/api/ai/sandbox/compare` | **Iter-34 PROV-005.** Runs the same prompt across up to four sandbox-class providers serially and returns each `output`, `latencyMs`, `inputTokens`, `outputTokens`, plus a generic `error` string when a single provider failed. Body `{ reportId, mode, providerIds[] }` (1â€“4). Auth: Radiologist / MedicalDirector / ReportingAdmin / ItAdmin. Refuses `409 { kind:"sandbox_required" }` unless `Tenant.AllowSandboxRulebooks=true`; refuses `400 { kind:"providers_not_sandbox" }` when any provider is disabled, cross-tenant, or `Compliance != Sandbox`. PHI policy is still enforced inside `AiGateway.EnforcePhiPolicy` for every dispatch. Audits one wrapper `AiResponse` row with `details: { kind:"sandbox_compare", mode, providerCount }` in addition to the per-call rows the gateway writes. |

## Prompt overrides

| Method | Path | Description |
| --- | --- | --- |
| GET    | `/api/prompts/overrides`              | List overrides for the current tenant. |
| POST   | `/api/prompts/overrides`              | Upsert by `(rulebookId, blockKey)`. **Iter-32 AI-009:** every save lands as `Draft`; the row does not affect AI runtime until approved. MedicalDirector / ReportingAdmin. |
| POST   | `/api/prompts/overrides/{id}/approve` | **Iter-32 AI-009.** Promote `Draft` â†’ `Approved`. **MedicalDirector only** (separation of duties). Audited as `PromptOverrideApproved` with `bodyHash = sha256(body)`. |
| DELETE | `/api/prompts/overrides/{id}`         | Delete override. MedicalDirector / ReportingAdmin. |

`EfPromptOverrideStore.LoadAsync` only returns `Status == Approved` rows, so a draft body never reaches the AI gateway.

## Audit

`GET /api/audit?from=â€¦&to=â€¦&take=200` â†’ audit events (append-only, SHA-256 hash chain). Each event:

```ts
{
  id: string;
  tenantId: string;
  action: number;          // RadioPad.Domain.Enums.AuditAction
  detailsJson: string;
  integrityChain: string;  // sha256("{id}|{tenantId}|{(int)action}|{detailsJson}|{prevHash}")
  createdAt: string;       // ISO-8601
}
```

The `/audit/verify` UI recomputes the chain client-side.

## Iter-30 - Terminology, Bidirectional FHIR, Bulk Invoice Export

### Terminology (STD-001 RadLex, STD-002 RADS)

| Method | Path | Description |
| --- | --- | --- |
| GET | `/api/terminology/radlex/search?q=&take=20` | Prefix search across the curated RadLexÂ® subset bundled with RadioPad. RadLexÂ® is a registered trademark of RSNA. |
| GET | `/api/terminology/radlex/CodeSystem` | Minimal FHIR R4 `CodeSystem` resource (`content: "fragment"`). |
| GET | `/api/terminology/rads?system=` | ACR RADS lookup. Without `system`, lists supported systems; with `system` (`bi_rads`, `li_rads`, `pi_rads`, `lung_rads`, `tirads`, `c_rads`) returns category codes + short labels only. No copyrighted prose; `publicGuidanceUrl` points to ACR. |

### Bidirectional FHIR ingest

| Method | Path | Description |
| --- | --- | --- |
| POST | `/api/ingest/fhir/servicerequest` | Existing endpoint - now also captures the originating `ServiceRequest.id` into `Report.serviceRequestRef` so the eventual DiagnosticReport can be correlated. |
| POST | `/api/ingest/fhir/diagnosticreport` | New (Iter-30). Accepts a FHIR R4 `DiagnosticReport` (or `Bundle` containing one). Maps `identifier[0].value`â†’`accessionNumber`, `code.coding[0].display`/`text`â†’`modality`, `category[0].text`â†’`bodyPart`, `conclusion`â†’`impression`, base64-decoded `presentedForm[0].data`â†’`findings`, `basedOn[0].reference`â†’`serviceRequestRef`. Creates a Draft report; audited as `ReportImported`. Same bearer-secret auth as `/order`. |

### Tenant settings — security, integrations, and PACS vendor selector

`GET /api/tenant/settings` returns tenant safety settings plus integration
status blocks for ingest, DICOMweb, PACS, retention, SCIM, CMK, and validation
strictness. Secret fields are never echoed; the response only includes
`*Configured` booleans. The response also includes `ipAllowlistJson`, a JSON
array of CIDR strings used by the per-tenant IP allowlist middleware.
Malformed allowlist JSON or malformed legacy CIDR values fail closed with
`503 { kind: "ip_allowlist_invalid" }` instead of disabling enforcement.

`POST /api/tenant/settings` is a partial update for `MedicalDirector`,
`ReportingAdmin`, and `ItAdmin`. Omitted fields are left unchanged. It accepts
`ipAllowlistJson`, optional write-only secret fields, and `pacsVendor`
(`"sectra" | "visage" | "carestream" | "" | null`). The empty string clears
the PACS vendor selection back to generic DICOMweb. Invalid severity, support
threshold, retention days, CIDR JSON, or PACS vendor values return
`400 { error, kind: "validation" }`. See
[integrations/pacs-vendor-adapters.md](integrations/pacs-vendor-adapters.md)
for the per-vendor endpoint matrix and credential conventions.

### Multi-radiologist sign-off + addendum

The legacy `acknowledge` endpoint stays in place. Iter-30 adds explicit signature
state separate from the report status:

```ts
type SignatureRole = "Primary" | "CoSigner" | "Addendum";
interface ReportSignature {
  id: string;
  userId: string;
  role: SignatureRole;
  signedAt: string;          // ISO-8601 UTC
  note?: string;
  hash: string;              // sha256("{id}|{reportId}|{userId}|{(int)role}|{signedAt:o}|{note}")
}
```

Sign-off ordering is enforced server-side: the first signature must be `Primary`,
duplicate `Primary` is rejected with 409, and `CoSigner` / `Addendum` require an
existing `Primary`. Addenda create a new `ReportVersion` with `isAddendum=true`
rather than mutating the report body, preserving the audit trail.

### Bulk invoice export (BILL-003)

```
GET /api/billing/invoices/export?from=YYYY-MM-DD&to=YYYY-MM-DD&format=csv|zip
```

| Format | Body | Notes |
| --- | --- | --- |
| `csv` (default) | `text/csv` with header `id,number,period,amountCents,currency,status,hostedInvoiceUrl` | Streamed inline. |
| `zip` | `application/zip` | Contains `invoices.csv`, one minimal-but-valid PDF per invoice under `invoices/<number-or-id>.pdf`, plus `manifest.txt` with one `path|sha256` line per file. |

RBAC: `BillingAdmin`, `ItAdmin`, or `MedicalDirector`. Audited as
`BillingChanged` with action `bulk_export`. Returns `503` when Stripe is not
configured or the tenant has no `StripeCustomerId`.

## Security, SIEM, and SCIM

| Method | Path | Role / Auth | Description |
| --- | --- | --- | --- |
| POST | `/api/admin/security/test-webhook` | `ItAdmin` / `MedicalDirector` / `ComplianceReviewer` | Sends a synthetic non-PHI `SecurityAlert` payload to `RADIOPAD_SECURITY_WEBHOOK_URL` (or legacy `RADIOPAD_ANOMALY_WEBHOOK_URL`) and signs it with `RADIOPAD_SECURITY_WEBHOOK_SECRET` when present. Returns `{ sent, configured, statusCode }`; returns `{ configured:false, sent:false }` when no webhook is configured. |
| POST | `/api/admin/observability/slo-alerts` | `ItAdmin` / `MedicalDirector` / `ComplianceReviewer` | Alertmanager (or compatible) webhook receiver. Appends one `SystemAlert` audit row summarising the payload (status, receiver, alert names, payload hash). Never stores PHI. |
| GET  | `/api/admin/observability/availability` | `ItAdmin` / `ComplianceReviewer` | Iter-35 PERF-004 - last computed snapshot of the in-process synthetic availability monitor: `{ windowSec, totalProbes, errorCount, errorRate, lastCheckedAt, targets[] }`. Burn-rate breaches against `RADIOPAD_AVAILABILITY_BURN_RATE_THRESHOLD` are recorded as append-only `SystemAlert` audit rows with `kind="availability_burn_rate"`. |
| GET | `/api/siem/status` | `ItAdmin` / `MedicalDirector` / `ComplianceReviewer` | Lists configured SIEM sinks and last-push status. |
| GET | `/api/audit/siem?format=json\|cef` | `ItAdmin` / `MedicalDirector` / `ComplianceReviewer` | Snapshot export of the append-only audit chain for SIEM ingestion. |
| GET/POST | `/scim/v2/Users` | `Authorization: Bearer <ScimBearerSecret>` + tenant header | SCIM 2.0 user list/create. Supports `userName eq "x"` filter. `ScimBearerSecret` may be stored as a literal test token or `env:NAME`; inactive users are omitted from list/search responses. |
| GET/PUT/PATCH/DELETE | `/scim/v2/Users/{id}` | SCIM bearer | SCIM 2.0 user get/replace/patch/soft-delete. |
| GET/POST | `/scim/v2/Groups` | SCIM bearer | SCIM 2.0 group list/create. Supports `displayName eq "x"` filter. Group membership projects user roles through `TenantSettings.ScimGroupRoleMapJson`. |
| GET/PUT/PATCH/DELETE | `/scim/v2/Groups/{id}` | SCIM bearer | SCIM 2.0 group get/replace/patch/delete. PATCH supports displayName/externalId plus `members` add/replace/remove and `members[value eq "<userId>"]` remove. Deleting a mapped group removes memberships and reprojects affected user roles. |
| GET | `/scim/v2/ServiceProviderConfig` | SCIM bearer | SCIM service provider metadata. |
| GET | `/scim/v2/ResourceTypes` | SCIM bearer | SCIM resource discovery for both `User` and `Group`. |

## Iter-32 Authentication / SSO / MFA

| Method | Path | Auth | Description |
| --- | --- | --- | --- |
| GET  | /saml/metadata | public | SAML 2.0 SP descriptor XML. |
| POST | /saml/acs | public IdP callback | SAML 2.0 Assertion Consumer Service. Verifies XML signature against `RADIOPAD_SAML_IDP_CERT_PEM`, maps NameID + tenant attribute, mints a bearer, audits `UserLogin{method:"saml"}`. |
| POST | /api/auth/webauthn/register-options | bearer/OIDC | Begin WebAuthn / passkey registration. |
| POST | /api/auth/webauthn/register | bearer/OIDC | Persist a passkey under the current `(tenant, user)`. |
| POST | /api/auth/webauthn/signin-options | bearer/OIDC | Begin WebAuthn assertion. |
| POST | /api/auth/webauthn/signin | bearer/OIDC | Complete WebAuthn assertion; mints a bearer; audits `UserLogin{method:"webauthn"}`. |
| POST | `/api/auth/magic-link/request` | public | Requests a single-use 15-minute sign-in link. Production requires `RADIOPAD_PUBLIC_WEB_URL` and SMTP configuration; raw dev links are returned only outside Production. Client-supplied callback origins are ignored in Production. |
| POST | `/api/auth/magic-link/consume` | public | Consumes the magic token, mints an `rp_` bearer, sets the HttpOnly `radiopad_session` cookie for browser sessions, and audits `UserLogin{method:"magic-link"}`. |
| POST | `/api/auth/logout` | public | Clears the browser `radiopad_session` cookie. Native shells should also clear their secure bearer store. |
| GET | `/api/auth/oidc/authorize-url` | public | Returns an operator-configured OIDC authorization URL for optional browser SSO bootstrap. Requires `RADIOPAD_OIDC_AUTHORIZE_URL`, `RADIOPAD_OIDC_CLIENT_ID`, and `RADIOPAD_PUBLIC_WEB_URL`; includes `RADIOPAD_OIDC_AUDIENCE` as `audience` when configured. |
| POST | /api/auth/device/authorize | public | RFC 8628 device flow: body `{ clientId, deviceFingerprint? }`; returns `{ deviceCode, userCode, verificationUri, verificationUriComplete, expiresIn, interval }` for desktop/CLI pairing. |
| POST | /api/auth/device/token | public | RFC 8628 polling: body `{ deviceCode, grantType:"urn:ietf:params:oauth:grant-type:device_code" }`; returns `authorization_pending`, `slow_down`, `access_denied`, `expired_token`, or `{ accessToken, tokenType:"Bearer", expiresIn, tenant, user }`. |
| POST | /api/users/{id}/unlock | Compliance / IT-Admin clears `LockedUntil` and resets the failure counter; audits `UserUnlocked`. |
| POST | /api/users/{id}/revoke-sessions | Compliance / IT-Admin increments `User.SessionEpoch`; every outstanding bearer fails HMAC validation; audits `SessionsRevoked`. |

OIDC presets (`RADIOPAD_OIDC_PRESET=keycloak|auth0|okta`) auto-fill `RADIOPAD_OIDC_TENANT_CLAIM`, `RADIOPAD_OIDC_EMAIL_CLAIM` and `RADIOPAD_OIDC_REQUIRE_MFA` for supported IdP profiles. Presets never overwrite explicit env values. Production policy is generic OIDC Authorization Code + PKCE; provider-specific presets are configuration shortcuts, not separate auth architectures.

OIDC bearer projection validates the external JWT and then re-checks the mapped RadioPad tenant/user row. Missing tenants, inactive users, and locked users are rejected before tenant headers are injected.

SAML ACS is fail-closed when `RADIOPAD_SAML_IDP_CERT_PEM` is not configured. The unsigned-assertion escape hatch `RADIOPAD_SAML_DEV_INSECURE=true` is ignored in Production and is only for local/test IdP harnesses.

Account-lockout policy: 5 failed sign-ins within a 15-minute sliding window flips `User.LockedUntil = now + 15 min` and `IsActive = false`; auto-unlocks once the timer expires or an admin calls `/api/users/{id}/unlock`. TOTP/password-style failures flow through `LockoutPolicy`; magic-link consume uses single-use hashed tokens with expiry and rejects inactive or locked users at session validation.


---

## Internationalization (Iter-35)

| Method | Path | Auth | Description |
| --- | --- | --- | --- |
| GET | `/api/tenant/settings/locale` | any tenant member | Returns `{ locale, supported }`. `locale` is the tenant default (`TenantSettings.Locale`); `supported` is the locked allow-list `["en","es","de","fr","pt","hi"]`. |
| PUT | `/api/tenant/settings/locale` | `ItAdmin` / `MedicalDirector` | Body: `{ locale }`. Validates against the supported set (case-insensitive); on mismatch returns `400 { error, kind: "validation" }`. |
| PUT | `/api/users/me/locale` | any tenant member | Body: `{ locale }` where `locale` is one of the supported tags or `null` to clear the per-user override (`User.PreferredLocale`). |

These endpoints affect chrome only. Rulebook YAML, finding text, and
`RadioPad.Validation` engine messages are never translated.
