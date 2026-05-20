# Deployment

**Status:** Current (basic)  ·  **Owner:** Ops  ·  **Last Updated:** 2026-05-20

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
      ConnectionStrings__RadioPad: Host=postgres;Database=radiopad;Username=radiopad;Password=...
      ASPNETCORE_ENVIRONMENT: Production
      RADIOPAD_AUTH_SECRET: <random-32-byte-secret>
      RADIOPAD_PUBLIC_WEB_URL: https://radiopad.example.com
      RADIOPAD_DEV_HEADERS: "0"
      RADIOPAD_ENABLE_SWAGGER: "0"
      RADIOPAD_COLUMN_KEY_REF: env:RADIOPAD_COLUMN_KEK
      RADIOPAD_COLUMN_KEK: <base64-32-byte-wrapping-key>
      RADIOPAD_COLUMN_KEY_WRAPPED: <base64-wrapped-32-byte-data-key>
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

## Frontend

- `pnpm build` → `frontend/out/` → upload to the proxy / CDN with cache headers per [caching.md](../03-architecture/caching.md).

## Desktop / mobile

- Desktop installer built via `cargo tauri build`; signed (planned) and shipped through an updater channel.
- Mobile apps built via `npx cap copy android|ios` and shipped through the relevant store (planned).

## Deploy verification

- `curl /api/health` → 200.
- `curl /api/health/ready` → 200.
- Sign in to staging tenant and run the full smoke flow.
- Request a magic link and confirm the response never includes `devLink` in Production.
- Confirm `/saml/metadata` and `/scim/v2/ServiceProviderConfig` are proxied to the API when those integrations are enabled.
- Verify audit chain: `radiopad audit verify --tenant <slug>`.
- Confirm production API calls without a valid bearer/OIDC identity return 401 unless `RADIOPAD_DEV_HEADERS=1` was explicitly set for a controlled test host.
- If `RADIOPAD_TRUST_FORWARDED_FOR=1`, confirm `RADIOPAD_TRUSTED_PROXY_CIDRS` covers only the immediate reverse proxy peer.
