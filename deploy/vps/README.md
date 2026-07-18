# RadioPad — VPS Deployment (Nginx Proxy Manager pattern)

**Status:** Current  ·  **Owner:** Operations  ·  **Last Updated:** 2026-05-20

This directory contains the deployment manifests for a VPS that uses
**Nginx Proxy Manager** (NPM) for TLS termination and domain routing.
This differs from the root `deploy/` directory which uses Caddy for TLS.

## Architecture

```
Internet → NPM (port 80/443) → 127.0.0.1:8093 → radiopad-web (nginx + Next.js static)
                                     ↓ /api/* proxy
                               radiopad-api:7457 (ASP.NET Core 8)
                                     ↓
                                radiopad-postgres:5432 (PostgreSQL volume)
```

## Files

| File | Purpose |
|---|---|
| `web.Dockerfile` | Multi-stage: pnpm 9.15.9 + Next.js build → nginx:alpine |
| `api.Dockerfile` | Multi-stage: .NET SDK 8 build → aspnet:8.0 runtime |
| `nginx.conf` | Nginx config: static SPA + `/api/*` proxy to API container |
| `docker-compose.yml` | Compose manifest for VPS (no Caddy — NPM handles TLS) |

## Prerequisites on VPS

- Docker 26+ with Compose plugin
- Nginx Proxy Manager running on ports 80/443
- SSH access to VPS (SSH key at `~/.ssh/id_ed25519`)
- A protected secrets file at `/opt/radiopad/.secrets.env` (`chmod 600`)
- For legacy SQLite migration only: `pgloader` available as a one-shot
  container or installed on the host

## First-time setup

```bash
mkdir -p /opt/radiopad
cd /opt/radiopad
git clone https://github.com/Sub-organization-maternal-mind/radiopad.git src

# Create secrets file
cat > .secrets.env << 'EOF'
RADIOPAD_AUTH_SECRET=<generate-with: openssl rand -base64 48>
RADIOPAD_PUBLIC_WEB_URL=https://radiopad.your-domain.com
RADIOPAD_COLUMN_KEK=<generate-with: openssl rand -base64 32>
RADIOPAD_COLUMN_KEY_WRAPPED=<base64-wrapped-32-byte-data-key>
# Set these only if API IP allowlists should trust an upstream proxy header.
# RADIOPAD_TRUST_FORWARDED_FOR=1
# RADIOPAD_TRUSTED_PROXY_CIDRS=172.16.0.0/12
# Required for production magic-link login:
# RADIOPAD_SMTP_HOST=smtp.example.com
# RADIOPAD_SMTP_PORT=587
# RADIOPAD_SMTP_USER=<mailbox>
# RADIOPAD_SMTP_PASS=<app-password-or-secret>
# RADIOPAD_SMTP_FROM="RadioPad <no-reply@example.com>"
# Optional: ANTHROPIC_API_KEY=...
```

```bash
chmod 600 .secrets.env

# Build and start. The compose file keeps secrets and data outside the
# fresh-synced source tree at /opt/radiopad/.secrets.env and in Docker volumes.
cd src
docker compose -f deploy/vps/docker-compose.yml --env-file ../.secrets.env up -d --build
docker compose -f deploy/vps/docker-compose.yml --env-file ../.secrets.env ps
```

## Required production secrets

Production startup intentionally fails unless these are present:

| Env var | Purpose |
|---|---|
| `POSTGRES_PASSWORD` | Password for the compose-managed PostgreSQL role |
| `RADIOPAD_AUTH_SECRET` | RadioPad bearer token signing secret |
| `RADIOPAD_COLUMN_KEY_REF` | KMS/KEK reference or env-backed wrapping key reference |
| `RADIOPAD_COLUMN_KEY_WRAPPED` | Base64 wrapped data encryption key for encrypted columns |

The deterministic development column key fallback must not be used in
Production. If OIDC is enabled, also set `RADIOPAD_OIDC_AUTHORITY` and the
matching `RADIOPAD_OIDC_CLIENT_ID`, optional `RADIOPAD_OIDC_CLIENT_SECRET`,
`RADIOPAD_OIDC_AUDIENCE`, redirect URI, and claim mapping env vars for the IdP.

## SQLite → PostgreSQL migration

Use this only when moving an existing VPS from the legacy SQLite layout
(`/opt/radiopad/data/radiopad.db`) to the PostgreSQL compose layout.

1. Stop writes and take a cold SQLite backup.

   ```bash
   cd /opt/radiopad/src
   docker compose -f deploy/vps/docker-compose.yml --env-file ../.secrets.env down
   cp /opt/radiopad/data/radiopad.db /opt/radiopad/data/radiopad.db.pre-postgres
   ```

2. Start PostgreSQL only and wait for it to become healthy.

   ```bash
   docker compose -f deploy/vps/docker-compose.yml --env-file ../.secrets.env up -d radiopad-postgres
   docker compose -f deploy/vps/docker-compose.yml --env-file ../.secrets.env ps radiopad-postgres
   ```

3. Load the SQLite database into PostgreSQL from the compose network. Replace
   placeholders with the values from `/opt/radiopad/.secrets.env`. The mount is
   intentionally writable because pgloader's SQLite driver opens the source DB
   read-write even during a cold import.

   ```bash
   docker run --rm --network radiopad_network \
     -v /opt/radiopad/data:/legacy \
     dimitri/pgloader:latest \
     pgloader sqlite:////legacy/radiopad.db \
       postgresql://radiopad:<POSTGRES_PASSWORD>@radiopad-postgres:5432/radiopad
   ```

   After pgloader completes, normalize SQLite affinity columns to the EF/Npgsql
   native types before starting the API: GUID columns must be `uuid`, timestamps
   must be `timestamptz`, booleans must be `boolean`, and enum/int columns must
   be `integer`. Do this against a cold backup first and keep the original
   SQLite file until API readiness, tenant sign-in, report creation, and audit
   verification pass.

4. Start the API and web services, then run readiness checks.

   ```bash
   docker compose -f deploy/vps/docker-compose.yml --env-file ../.secrets.env up -d --build
   curl -fsS http://127.0.0.1:8093/api/health
   curl -fsS http://127.0.0.1:8093/api/health/ready
   ```

Keep the cold SQLite backup until smoke tests, tenant sign-in, report creation,
and audit verification have passed. Do not run the old SQLite stack and the new
PostgreSQL stack with live traffic at the same time.

## Updating

```bash
cd /opt/radiopad
# Backup VPS-specific config if you use src/.deploy
rm -rf deploy-bak fresh-src
cp -r src/.deploy deploy-bak

# Pull latest
git clone --depth=1 https://github.com/Sub-organization-maternal-mind/radiopad.git fresh-src
rsync -av --exclude='.deploy' fresh-src/ src/ --delete

# Restore VPS config (if using .deploy/ instead of deploy/vps/)
cp -r deploy-bak src/.deploy

# Rebuild
cd src
docker compose -f deploy/vps/docker-compose.yml --env-file ../.secrets.env down
docker compose -f deploy/vps/docker-compose.yml --env-file ../.secrets.env up -d --build --no-cache
docker compose -f deploy/vps/docker-compose.yml --env-file ../.secrets.env ps
```

## UBAG AI-orchestrator wiring (updated 2026-07-18)

RadioPad consumes the UBAG gateway (compose project at `/opt/docker/ubag`,
gateway container `ubag-vps-gateway-1`) as its browser-driving AI provider
layer. RadioPad reaches it **internally** over the shared external docker
network `platform` — never via the public `ubag.polytronx.com` host, which
sits behind operator Basic-auth.

1. Add to `/opt/docker/radiopad/.secrets.env` (chmod 600):

   ```bash
   # Preferred: secret-ref indirection — only the env NAME is ever stored elsewhere.
   RADIOPAD_UBAG_TOKEN=<UBAG_APP_SECRET from the ubag gateway's env>
   ```

   and in the compose `environment:` block (non-secret values):

   ```yaml
   RADIOPAD_UBAG_BASE_URL: http://ubag-vps-gateway-1:8080
   RADIOPAD_UBAG_AUTH_SECRET_REF: env:RADIOPAD_UBAG_TOKEN
   RADIOPAD_UBAG_ALLOWED_TARGETS: gemini_web,deepseek_web,chatgpt_web,mock
   # Operator inbox for login-lost / gateway-down alert emails (throttled 1/provider/day):
   RADIOPAD_OPERATOR_ALERT_EMAIL: ops@example.com
   ```

2. Give `radiopad-api` the `platform` network (external) in addition to
   `radiopad_network`, then `docker compose up -d`. On hosts without UBAG,
   omit the network + env — RadioPad logs a startup ERROR in Production when
   the base URL/secret are missing and fails over to its non-UBAG providers.

3. Verify: `GET https://<domain>/api/ubag/status` (authenticated, admin role)
   shows `health.ok: true`, per-target readiness (tri-state — `ready: null`
   means the gateway reports no login signal, which is normal for vps-local
   executors and does NOT disable providers), and any operator alerts
   (`alerts[]`, `gatewayUnreachableSince`).

## NPM Proxy Host

Add a proxy host in Nginx Proxy Manager:
- **Domain**: `radiopad.your-domain.com`
- **Scheme**: `http`
- **Forward Hostname/IP**: `127.0.0.1`
- **Forward Port**: `8093`
- **SSL**: Let's Encrypt (recommended)

The compose file binds the web port to `127.0.0.1` by default:
`127.0.0.1:${RADIOPAD_WEB_PORT:-8093}:80`. Do not change this to `0.0.0.0`
unless a separate firewall and TLS exposure review has approved direct access.

## Health and readiness checks

```bash
curl http://127.0.0.1:8093/api/health
# Expected: {"status":"ok","service":"radiopad-api","time":"..."}

curl http://127.0.0.1:8093/saml/metadata -I
# Expected when SAML is configured: API response, not the static web index.
```

## PostgreSQL backup and restore

Create backups before every rebuild or migration, and store them off-host after
encrypting them with the operator-approved backup process.

```bash
cd /opt/radiopad/src
docker compose -f deploy/vps/docker-compose.yml --env-file ../.secrets.env exec radiopad-postgres \
  pg_dump -U radiopad -d radiopad -Fc -f /var/lib/postgresql/data/radiopad-predeploy.dump
```

Restore into an isolated PostgreSQL volume first, run smoke checks, then repoint
the API only after validation. Avoid restoring over the live volume unless the
incident commander has approved downtime.

## Rollback notes

- **Before SQLite migration cutover:** keep the legacy SQLite DB backup at
  `/opt/radiopad/data/radiopad.db.pre-postgres`; roll back by restoring the
  previous git revision/compose file and the SQLite DB while the new stack is
  stopped.
- **After PostgreSQL cutover:** prefer rolling back application images/source
  while keeping the PostgreSQL volume. Schema downgrades are not automatic; if a
  migration changed schema, restore the predeploy Postgres backup into an
  isolated volume and validate before repointing traffic.
- **Traffic rollback:** disable the NPM proxy host or point it back to the last
  healthy loopback port. The web port should remain loopback-only.
