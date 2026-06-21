# RadioPad — deployment guide

**Status:** Current  ·  **Owner:** Engineering + Operations  ·  **Last Updated:** 2026-05-20

This guide covers a self-hosted deployment of the backend behind a reverse proxy with PostgreSQL and operator-managed secrets. The desktop and mobile clients are bundled as static exports of the frontend and ship through their respective stores / installers.

## 1. Topology

```
[Browser / Desktop / Mobile]
            │ HTTPS
            ▼
   [Reverse proxy (nginx / Caddy / Traefik)]
       - terminates TLS
       - forwards validated Authorization / OIDC identity
            │
            ▼
   [RadioPad.Api]  (binds 127.0.0.1:7457 by default;
                    set RADIOPAD_BIND=http://0.0.0.0:7457
                    only when the proxy is on a different host)
            │
            ▼
   [PostgreSQL 16+]    [object storage for exports — optional]
```

> Hard rule: **never expose `RadioPad.Api` directly to the internet.** Production API requests require a validated RadioPad bearer or OIDC bearer. Raw dev headers are rejected unless `RADIOPAD_DEV_HEADERS=1` is explicitly set for a controlled test host.

## 2. Build

```bash
# Backend
dotnet publish backend/RadioPad.Api/src/RadioPad.Api -c Release -o out/api

# Frontend (static)
cd frontend && pnpm install && pnpm build   # → frontend/out/
```

Or via Docker:

```bash
docker compose -f deploy/docker-compose.yml build
```

## 3. Configuration

All knobs are environment variables:

| Variable | Required | Purpose |
| --- | --- | --- |
| `RADIOPAD_BIND` | when exposing remotely | Kestrel bind URL. Default `http://127.0.0.1:7457`. |
| `ConnectionStrings__RadioPad` | yes (Postgres) | Standard Npgsql connection string. SQLite (`Data Source=…`) is **dev-only**. |
| `ASPNETCORE_ENVIRONMENT` | yes | Set to `Production`. |
| `RADIOPAD_AUTH_SECRET` | yes | Random bearer-token signing secret, at least 32 characters in Production. Rotate to invalidate all RadioPad-issued `rp_` tokens. |
| `RADIOPAD_PUBLIC_WEB_URL` | yes | Public browser origin, for example `https://radiopad.example.com`. Production magic-link callbacks are built from this value; client-supplied callback origins are ignored. |
| `RADIOPAD_DEV_HEADERS` | no | Defaults `0`; set to `1` only for controlled production-like tests that intentionally use dev tenant headers. |
| `RADIOPAD_ENABLE_SWAGGER` | no | Defaults `0`. Set to `1` only on a protected admin/test host when production Swagger UI is explicitly needed. |
| `RADIOPAD_COLUMN_KEY_REF` + `RADIOPAD_COLUMN_KEY_WRAPPED` | yes | Production at-rest column encryption key material. `RADIOPAD_COLUMN_KEY_REF` names the KMS/`env:` wrapping key; `RADIOPAD_COLUMN_KEY_WRAPPED` is the base64 wrapped 32-byte data key. The API fails startup in Production if either is missing. |
| `RADIOPAD_TRUST_FORWARDED_FOR` + `RADIOPAD_TRUSTED_PROXY_CIDRS` | when behind a proxy and enforcing IP allowlists | Set `RADIOPAD_TRUST_FORWARDED_FOR=1` only with trusted proxy CIDRs covering the immediate reverse proxy. Production ignores spoofable `X-Forwarded-For` when the peer is not trusted. |
| `RADIOPAD_SMTP_HOST` / `RADIOPAD_SMTP_PORT` / `RADIOPAD_SMTP_USER` / `RADIOPAD_SMTP_PASS` / `RADIOPAD_SMTP_FROM` | required for production magic links | SMTP settings for passwordless browser sign-in. Production never returns raw magic-link dev URLs when email delivery is unavailable. |
| `ANTHROPIC_API_KEY` (or any other key referenced by `ApiKeySecretRef`) | when using that provider | The provider config stores `env:ANTHROPIC_API_KEY`; the runtime resolves it. |
| `OPENAI_COMPATIBLE_API_KEY` | optional | Example env var for `openai-compatible` providers. Tenants may choose any `env:NAME` reference. |
| `RADIOPAD_COPILOT_BIN` / `RADIOPAD_GEMINI_BIN` / `RADIOPAD_CODEX_BIN` | optional | Override local CLI binaries for sandbox CLI providers. |
| `RADIOPAD_CLI_PROVIDER_TIMEOUT_MS` | optional | Per-process timeout for CLI providers. Default `60000`. |
| `RADIOPAD_CLI_PROVIDER_ALLOWED_PATHS` | required for production CLI providers | Semicolon-separated allowlist for CLI binaries. Missing production allowlists and unlisted binaries are blocked. |
| `RADIOPAD_CLI_PROVIDER_ENV_ALLOWLIST` | optional | Extra env vars passed to CLI subprocesses beyond OS basics. Keep minimal and provider-specific. |
| `RADIOPAD_COPILOT_SERVER_CLI_ENABLED` | optional | Defaults `0`; must be `1` before a production API host may execute `github-copilot-cli`. |
| `RADIOPAD_CODEX_CLI_ENABLED` | optional | Defaults `0`; must be `1` before `codex-cli` executes. |
| `RADIOPAD_OPENAI_COMPATIBLE_ALLOW_PHI` | optional | Defaults `0`; set only after reviewed approval for a remote OpenAI-compatible PHI endpoint. |
| `RADIOPAD_GITHUB_COPILOT_SDK_ENABLED` | optional | Defaults `false`. The SDK provider remains fail-closed until a reviewed official backend-safe transport is installed. |
| `RADIOPAD_UBAG_BASE_URL` | optional | Defaults to production UBAG `https://ubag.polytronx.com`. RadioPad backend only; never expose this as a frontend public env var. |
| `RADIOPAD_UBAG_API_VERSION` | optional | Defaults `2026-05-22`; sent in UBAG job/workflow envelopes. |
| `RADIOPAD_UBAG_TIMEOUT_MS` | optional | Defaults `120000`; HTTP timeout and adapter polling budget. |
| `RADIOPAD_UBAG_ALLOWED_TARGETS` | optional | Defaults `chatgpt_web,gemini_web,deepseek_web,mock`; comma-separated allowlist for UBAG provider jobs. |
| `RADIOPAD_UBAG_AUTH_SECRET_REF` | optional | Preferred UBAG auth reference, e.g. `env:RADIOPAD_UBAG_TOKEN`; resolved server-side only. |
| `RADIOPAD_UBAG_AUTH_SECRET` | optional | Direct server-side UBAG auth fallback when no secret ref is used. Do not commit it or return it in JSON. |
| `RADIOPAD_UBAG_AUTH_SCHEME` / `RADIOPAD_UBAG_AUTH_HEADER` | optional | Defaults `Bearer` / `Authorization`; use only when UBAG production auth requires a different scheme/header. |

Secrets are **never** stored in the database — only the `env:NAME` reference is.

Browser sign-in sets an HttpOnly `radiopad_session` cookie in addition to returning the `rp_` bearer for native shells. `POST /api/auth/logout` clears that cookie; native shells must clear their secure token store as well.

Public magic-link requests still participate in per-tenant IP allowlists: the API resolves the tenant slug from the JSON request body before the controller runs. Keep helper scripts environment-driven; never commit real mailbox credentials, app passwords, or personal account addresses.

CLI providers default to `Sandbox` because the local binary may call a vendor cloud. They refuse PHI and secret-shaped prompts before launch; do not rely on provider-row promotion to bypass that boundary.

UBAG also defaults to `Sandbox`. It is approved only for de-identified,
non-secret prompts in this release. ChatGPT, Gemini, and DeepSeek provider
logins remain manual through UBAG Browser Sessions; RadioPad must not automate
login, CAPTCHA, 2FA, consent, cookie extraction, or credential collection.

## 4. Database

```bash
# In production, run migrations explicitly:
dotnet ef database update \
  --project backend/RadioPad.Api/src/RadioPad.Infrastructure \
  --startup-project backend/RadioPad.Api/src/RadioPad.Api
```

The dev-friendly `EnsureCreated` fallback in `DevSeed` triggers only when no migrations are applied. Once an `InitialCreate` migration is committed, every deploy uses `MigrateAsync`.

## 5. Reverse proxy notes

- Forward `Authorization`, `X-RadioPad-Tenant`, `X-RadioPad-User`, and `X-RadioPad-RequestId`. In Production, RadioPad validates tenant/user context against the bearer or overwrites it from OIDC; do not synthesize a default tenant/user in the proxy.
- Limit `POST /api/reports/{id}/ai` at the proxy as well; the per-tenant fixed-window limiter inside the API is a safety net, not a quota system.

## 6. Backups & audit

- Back up `audit_events` with `WAL`-aware tooling (e.g. `pg_basebackup`). The integrity chain is verified by the `/audit/verify` UI and by re-running the SHA-256 chain externally.
- **Never** `UPDATE` or `DELETE` rows in `audit_events`. Use `IAuditLog.AppendAsync` only.

## 7. Observability

- Logs use ASP.NET Core's built-in console provider with timestamped output and a `RequestId/Tenant` scope from `RequestCorrelationMiddleware`.
- Recommended: ship logs to your aggregator and alert on `ProviderPolicyException` and `policy/provider` problem responses.

## 8. Disaster recovery

- Postgres point-in-time restore is sufficient — RadioPad has no other stateful store.
- Rulebooks, templates, and provider configs are tenant-scoped DB rows; restore the database and the application is whole.
