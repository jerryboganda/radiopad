# RadioPad — VPS Deployment (Nginx Proxy Manager pattern)

**Status:** Current  ·  **Owner:** Operations  ·  **Last Updated:** 2026-11-21

This directory contains the image build recipes and reference manifests for the
production VPS (`185.252.233.186`), which uses **Nginx Proxy Manager** (NPM) for
TLS termination and routing, behind **Cloudflare**. This differs from the root
`deploy/` directory, which uses Caddy for TLS.

> **Layout changed.** Production is the compose project at `/opt/docker/radiopad`
> and runs **prebuilt images only** — there is no source checkout on the VPS and
> nothing is built there (CPU-heavy work runs on GitHub Actions, see `CLAUDE.md`).
> The old `/opt/radiopad` + `src/` + `--build` flow and `_deploy-images.sh` no
> longer exist.

## Architecture

```
Internet → Cloudflare (proxied, SSL Full strict)
         → NPM (host :443, container nginx-proxy-manager-app-1)
              proxy host 42: radiopadstudio.com, www  → radiopad-marketing:80 (nginx + Astro static)
                                                          ↓ /api /ws /saml /scim proxy (back-compat)
              proxy host 43: admin.radiopadstudio.com → radiopad-web:80 (nginx + Next.js static)
                                                          ↓ /api/* proxy
                                                    radiopad-api:7457 (ASP.NET Core 8)
                                                          ↓
                                                    SQLite: /opt/docker/radiopad/data/radiopad.db
```

NPM reaches the containers by name over the external docker network
`nginx-proxy-manager_default` (compose network alias `npm`); no host ports are
published. `radiopad-api` additionally joins the external `platform` network
for UBAG (see below).

The root domain serves the marketing site (`marketing/`, Astro); the admin app
(`frontend/`, `build:web` surface) lives on `admin.radiopadstudio.com`. The
marketing container ALSO proxies `/api`, `/ws`, `/saml`, `/scim` straight
through to `radiopad-api` — this is deliberate, not leftover: already-installed
desktop clients default to `RADIOPAD_BACKEND=https://radiopadstudio.com`
(baked in at build time) and must keep working indefinitely on the root
domain, even though new desktop builds default to the admin subdomain. Do not
remove those proxy blocks from `nginx-marketing.conf` without shipping and
fully rolling out a desktop release first.

## Files

| File | Purpose |
|---|---|
| `web.Dockerfile` | Multi-stage: pnpm 9.15.9 + Next.js build → nginx:alpine (admin app) |
| `marketing.Dockerfile` | Multi-stage: pnpm 9.15.9 + Astro build → nginx:alpine (marketing site) |
| `api.Dockerfile` | Multi-stage: .NET SDK 8 build → aspnet:8.0 runtime |
| `nginx.conf` | Nginx config for the admin app: static SPA + `/api/*` proxy to API container |
| `nginx-marketing.conf` | Nginx config for the marketing site: static multi-page site + the same `/api /ws /saml /scim` proxy blocks (back-compat, see above) |
| `docker-compose.yml` | **Build-from-source reference** (builds images, publishes loopback ports, defines an unused `radiopad-postgres`). Not what production runs — see below. |
| `docker-compose.ubag.yml` | Overlay that joins the shared UBAG network |
| `README-deploy-user.md` | One-time setup of the least-privilege `radiopad-deploy` user for CI deploys |

## Production layout (`/opt/docker/radiopad`)

| Path | Contents |
|---|---|
| `docker-compose.yml` | Image-only compose: `image: radiopad-{api,web,marketing}:latest` + `pull_policy: never`, env, networks |
| `.secrets.env` | Secrets (`chmod 600`) — loaded via `env_file`. **Must be readable by `radiopad-deploy`** (owner `radiopad-deploy`), or the CI deploy fails at `docker compose up` with `open .secrets.env: permission denied` after loading the new images |
| `data/` | SQLite database (`radiopad.db`) mounted at `/data` in `radiopad-api` |
| `backups/` | Ad-hoc DB backups |
| `docker-compose.yml.bak-*` | Previous compose revisions — take one before every edit |

The directory is owned by `radiopad-deploy` (in the `docker` group). Non-secret
settings live in the compose `environment:` block, e.g.
`ConnectionStrings__RadioPad: Data Source=/data/radiopad.db`,
`RADIOPAD_PUBLIC_WEB_URL: https://admin.radiopadstudio.com`,
`RADIOPAD_WEBAUTHN_RP_ID: radiopadstudio.com`, and
`Logging__LogLevel__Microsoft.EntityFrameworkCore.Database.Command: Warning`.

## Deploying

Every push to `main` touching `backend/**`, `frontend/**`, `marketing/**`,
`rulebooks/**`, `templates/**` or `deploy/vps/**` runs
[`web-deploy-images.yml`](../../.github/workflows/web-deploy-images.yml): it
builds the three images on GitHub Actions, uploads them as the
`radiopad-images` artifact, and its `deploy` job ships them over SSH as
`radiopad-deploy` and runs `docker compose up -d`. The deploy job is skipped
(not failed) until the `VPS_HOST` / `VPS_DEPLOY_USER` / `VPS_DEPLOY_SSH_KEY`
secrets exist — see `README-deploy-user.md`.

Manual fallback (on the VPS):

```bash
gh run download <run_id> --repo jerryboganda/radiopad -n radiopad-images -D /tmp/rpimg
gunzip -c /tmp/rpimg/radiopad-images.tar.gz | docker load
cd /opt/docker/radiopad && docker compose up -d
docker compose ps
```

Changing only env/config (no new image): back up `docker-compose.yml`, edit it,
then `docker compose config -q && docker compose up -d <service>` to recreate
just that service. The API runs EF migrations on startup.

## Secrets

`/opt/docker/radiopad/.secrets.env` (`chmod 600`). Production startup
intentionally fails unless these are present:

| Env var | Purpose |
|---|---|
| `RADIOPAD_AUTH_SECRET` | RadioPad bearer token signing secret |
| `RADIOPAD_COLUMN_KEK` | Env-backed wrapping key (compose sets `RADIOPAD_COLUMN_KEY_REF: env:RADIOPAD_COLUMN_KEK`) |
| `RADIOPAD_COLUMN_KEY_WRAPPED` | Base64 wrapped data encryption key for encrypted columns |

Also present in production: the Gmail API OAuth trio
(`RADIOPAD_GMAIL_CLIENT_ID` / `_CLIENT_SECRET` / `_REFRESH_TOKEN`, with
`RADIOPAD_EMAIL_PROVIDER: gmail` in compose), `RADIOPAD_BOOTSTRAP_SECRET`, and
`RADIOPAD_UBAG_TOKEN`. Generate new keys with `openssl rand -base64 48`
(auth secret) / `openssl rand -base64 32` (KEK). The deterministic development
column key fallback must not be used in Production. If OIDC is enabled, also
set `RADIOPAD_OIDC_AUTHORITY`, `RADIOPAD_OIDC_CLIENT_ID`, optional
`RADIOPAD_OIDC_CLIENT_SECRET`, `RADIOPAD_OIDC_AUDIENCE`, redirect URI, and claim
mapping env vars for the IdP.

## Cloudflare + NPM proxy hosts

DNS for `radiopadstudio.com` is on Cloudflare (Free plan; moved from Hostinger
nameservers 2026-11-21).

| Record | Cloudflare | Target |
|---|---|---|
| `radiopadstudio.com` (A), `www` (CNAME), `admin` (A) | Proxied | `185.252.233.186` |
| MX `mx1`/`mx2.hostinger.pk`, SPF TXT, `autoconfig`, `autodiscover` | DNS only | Hostinger mail — never proxy these |

- **SSL/TLS mode: Full (strict).** The origin keeps its own Let's Encrypt certs
  in NPM (cert 44 for root+www, 45 for admin), renewed by NPM over HTTP-01.
  Do not drop to Flexible — NPM force-redirects to HTTPS and Flexible loops.
- **Real client IPs:** proxy hosts 42 and 43 carry
  `real_ip_header CF-Connecting-IP;` in NPM's *Advanced* field (NPM already
  trusts Cloudflare ranges via its generated `ip_ranges.conf`). Without it the
  API's forwarded-headers handling resolves every request to a Cloudflare edge
  IP, which breaks the per-IP rate limiter and the audit log's IPs. Keep it on
  both hosts when editing them in the NPM UI.
- **100s limit:** Cloudflare Free cuts any single request at 100s (nginx allows
  300s). A non-streaming API call longer than that returns 524 to the client.
- Universal SSL covers `radiopadstudio.com` + `*.radiopadstudio.com`, so
  `admin` can stay proxied. A 2-level name (e.g. `x.admin.radiopadstudio.com`)
  would need DNS-only.

When the admin app's public URL changes, also update on the API container:
- `RADIOPAD_PUBLIC_WEB_URL` → the new admin origin
- `RADIOPAD_WEBAUTHN_ORIGINS` → add the new origin (keep the existing ones so
  passkeys/sessions keep working). `RADIOPAD_WEBAUTHN_RP_ID` stays the parent
  domain; WebAuthn accepts an RP ID that is a registrable-domain suffix of the
  calling origin.

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

## Health and readiness checks

Run on the VPS (goes through NPM locally, bypassing Cloudflare):

```bash
Rm="--resolve radiopadstudio.com:443:127.0.0.1"; Ra="--resolve admin.radiopadstudio.com:443:127.0.0.1"
curl -sk $Rm https://radiopadstudio.com/api/health
# Expected: {"status":"ok","service":"radiopad-api","time":"..."}
curl -sk $Ra -o /dev/null -w "%{http_code}\n" https://admin.radiopadstudio.com/
# Expected: 200
curl -sk $Rm -I https://radiopadstudio.com/saml/metadata
# Expected when SAML is configured: API response, not the static web index.
cd /opt/docker/radiopad && docker compose ps
```

## Backup and restore (SQLite)

Production data is the SQLite file `/opt/docker/radiopad/data/radiopad.db`
(WAL mode). Take a consistent copy before every deploy that changes schema, and
store it off-host after encrypting it with the operator-approved process:

```bash
cd /opt/docker/radiopad
docker compose stop radiopad-api
cp -p data/radiopad.db backups/radiopad-$(date +%Y%m%d-%H%M%S).db
docker compose start radiopad-api
```

Restore by stopping `radiopad-api`, copying the backup over `data/radiopad.db`
(and removing stale `radiopad.db-wal` / `-shm`), then starting it again.
Validate on a copy first unless downtime is approved.

## Moving to PostgreSQL (not done yet)

Production still runs on SQLite. The repo's build-from-source
`docker-compose.yml` defines a `radiopad-postgres` service, but its API still
points at SQLite. To migrate later: stop writes and take a cold SQLite backup,
start a Postgres container on `radiopad_network`, load the DB with
`dimitri/pgloader` (mount `/opt/docker/radiopad/data` writable — pgloader opens
SQLite read-write), normalize types to the EF/Npgsql natives (GUID → `uuid`,
timestamps → `timestamptz`, booleans → `boolean`, enum/int → `integer`), point
`ConnectionStrings__RadioPad` at Postgres, and keep the SQLite backup until
readiness, tenant sign-in, report creation and audit verification pass. Never
run both stacks with live traffic at once.

## Rollback

- **Bad image:** re-run the manual fallback with the last good
  `web-deploy-images` run id (`docker load` overwrites `:latest`), then
  `docker compose up -d`. Schema downgrades are not automatic — if a migration
  changed schema, restore the pre-deploy SQLite backup.
- **Bad config edit:** restore the matching `docker-compose.yml.bak-*` and
  `docker compose up -d`.
- **Bad NPM edit:** NPM keeps its DB at
  `/opt/docker/nginx-proxy-manager/data/database.sqlite`; take a copy before
  bulk edits. Restore it plus the `proxy_host/*.conf` file, then
  `docker exec nginx-proxy-manager-app-1 nginx -s reload`.
- **Traffic rollback:** in Cloudflare, switch the record to DNS only (bypasses
  Cloudflare), or disable the NPM proxy host.
