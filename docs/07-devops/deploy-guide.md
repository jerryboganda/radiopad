# RadioPad — deployment guide

This guide covers a self-hosted deployment of the backend behind a reverse proxy with PostgreSQL and operator-managed secrets. The desktop and mobile clients are bundled as static exports of the frontend and ship through their respective stores / installers.

## 1. Topology

```
[Browser / Desktop / Mobile]
            │ HTTPS
            ▼
   [Reverse proxy (nginx / Caddy / Traefik)]
       - terminates TLS
       - injects X-RadioPad-Tenant / X-RadioPad-User
            │
            ▼
   [RadioPad.Api]  (binds 127.0.0.1:7457 by default;
                    set RADIOPAD_BIND=http://0.0.0.0:7457
                    only when the proxy is on a different host)
            │
            ▼
   [PostgreSQL 16+]    [object storage for exports — optional]
```

> Hard rule: **never expose `RadioPad.Api` directly to the internet.** The API trusts the tenant/user headers it receives, so the reverse proxy must terminate auth first.

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
| `ANTHROPIC_API_KEY` (or any other key referenced by `ApiKeySecretRef`) | when using that provider | The provider config stores `env:ANTHROPIC_API_KEY`; the runtime resolves it. |

Secrets are **never** stored in the database — only the `env:NAME` reference is.

## 4. Database

```bash
# In production, run migrations explicitly:
dotnet ef database update \
  --project backend/RadioPad.Api/src/RadioPad.Infrastructure \
  --startup-project backend/RadioPad.Api/src/RadioPad.Api
```

The dev-friendly `EnsureCreated` fallback in `DevSeed` triggers only when no migrations are applied. Once an `InitialCreate` migration is committed, every deploy uses `MigrateAsync`.

## 5. Reverse proxy notes

- Forward `X-RadioPad-Tenant`, `X-RadioPad-User`, and `X-RadioPad-RequestId` from your auth layer.
- Strip any inbound `X-RadioPad-*` headers from the public client — only the proxy may set them.
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
