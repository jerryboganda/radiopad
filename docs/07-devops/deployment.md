# Deployment

**Status:** Current (basic)  ·  **Owner:** Ops  ·  **Last Updated:** 2026-05-04

## Targets

- **Local:** `dotnet run` + `pnpm dev`. See [dev-setup.md](dev-setup.md).
- **Self-hosted (v0.x):** Docker Compose under `deploy/`. See [deploy-guide.md](deploy-guide.md).
- **Hosted (planned, Phase 2/3):** Kubernetes via Helm.

## Components

- **API** — single container per replica.
- **Frontend** — static files served by a reverse proxy or CDN.
- **Reverse proxy** — terminates TLS, sets HSTS, forwards `/api/*` to the API.
- **Database** — Postgres for production deployments.

## Compose layout (v0.x)

```yaml
services:
  api:
    image: radiopad/api:<tag>
    environment:
      RADIOPAD_BIND: 0.0.0.0:7457
      ConnectionStrings__RadioPad: Host=postgres;Port=5432;Database=radiopad;Username=radiopad;Password=...
      RADIOPAD_AUTH_SECRET: ...
      RADIOPAD_COLUMN_KEY_REF: ...
      RADIOPAD_COLUMN_KEY_WRAPPED: ...
      ASPNETCORE_ENVIRONMENT: Production
    depends_on: [postgres]
  postgres:
    image: postgres:16
    environment: { POSTGRES_DB: radiopad, POSTGRES_USER: radiopad, POSTGRES_PASSWORD: ... }
    volumes: ["pgdata:/var/lib/postgresql/data"]
  proxy:
    image: caddy:2
    volumes: ["./Caddyfile:/etc/caddy/Caddyfile", "./certs:/data"]
    ports: ["443:443"]
volumes:
  pgdata: {}
```

## Migration apply

- The API image runs `dotnet ef database update` on startup if `RADIOPAD_RUN_MIGRATIONS=true`.
- For controlled rollouts, run migrations as a separate one-shot job before the API rolls out.

## Production environment requirements

- Production uses Postgres via `ConnectionStrings__RadioPad`; SQLite is for local
  development and legacy data migration only.
- `RADIOPAD_AUTH_SECRET`, `RADIOPAD_COLUMN_KEY_REF`, and
  `RADIOPAD_COLUMN_KEY_WRAPPED` must be set before starting a Production API.
- Optional OIDC validation is enabled with `RADIOPAD_OIDC_AUTHORITY`; set
  `RADIOPAD_OIDC_CLIENT_ID`, optional `RADIOPAD_OIDC_CLIENT_SECRET`,
  `RADIOPAD_OIDC_REDIRECT_URI`, `RADIOPAD_OIDC_AUDIENCE`, tenant/email claim
  env vars, and `RADIOPAD_OIDC_REQUIRE_MFA=1` to match the IdP policy.
- The VPS manifest in `deploy/vps/` binds the web container to loopback by
  default (`127.0.0.1:8093`) so Nginx Proxy Manager or another TLS proxy is the
  only public entry point.

## Frontend

- `pnpm build` → `frontend/out/` → upload to the proxy / CDN with cache headers per [caching.md](../03-architecture/caching.md).

## Desktop / mobile

- Desktop installer built via `cargo tauri build`; signed (planned) and shipped through an updater channel.
- Mobile apps built via `npx cap copy android|ios` and shipped through the relevant store (planned).

## Deploy verification

- `curl /api/health` → 200.
- `curl /api/health/ready` → 200.
- Sign in to staging tenant and run the full smoke flow.
- Verify audit chain: `radiopad audit verify --tenant <slug>`.
