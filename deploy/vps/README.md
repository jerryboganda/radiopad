# RadioPad — VPS Deployment (API only, Nginx Proxy Manager)

**Status:** Current  ·  **Owner:** Operations  ·  **Last Updated:** 2026-11-21

The production VPS (`185.252.233.186`, shared multi-app box) runs **only the
RadioPad API**, published as **`https://api.radiopadstudio.com`**. The website
(`radiopadstudio.com`, `www`) and the admin web app
(`admin.radiopadstudio.com`) are static builds on Hostinger — see
[`deploy/hostinger/README.md`](../hostinger/README.md).

> Production is the compose project `/opt/docker/radiopad`, prebuilt images
> only — no source checkout and nothing built on the VPS (CLAUDE.md: heavy work
> runs on GitHub Actions). The old `/opt/radiopad` layout, `_deploy-images.sh`,
> the `radiopad-web` / `radiopad-marketing` containers and NPM proxy hosts
> 42/43 are gone (removed 2026-11-21; NPM backup in
> `/opt/docker/nginx-proxy-manager/removed-radiopad-hosts-202611211611/`).

## Architecture

```
Desktop app / phone companion / admin web app (Hostinger)
   → Cloudflare (proxied, SSL Full strict)
   → NPM (host :443, container nginx-proxy-manager-app-1)
        proxy_host/radiopad-api-manual.conf  (hand-maintained, NOT in NPM's DB)
   → radiopad-api:7457 (ASP.NET Core 8, container on networks radiopad + npm + platform)
   → SQLite: /opt/docker/radiopad/data/radiopad.db
   → UBAG gateway over the `platform` network (see below)
```

## Files in this directory

| File | Purpose |
|---|---|
| `api.Dockerfile` | Multi-stage: .NET SDK 8 build → aspnet:8.0 runtime (+ Gemini CLI). **The only image production uses.** |
| `README-deploy-user.md` | One-time setup of the least-privilege `radiopad-deploy` user for CI deploys |
| `docker-compose.yml` | Build-from-source reference (all services, loopback ports, unused `radiopad-postgres`). Not what production runs. |
| `docker-compose.ubag.yml` | Overlay that joins the shared UBAG network |
| `web.Dockerfile`, `marketing.Dockerfile`, `nginx.conf`, `nginx-marketing.conf` | Legacy container builds of the two static surfaces — no longer deployed (Hostinger serves them). |

## Production layout (`/opt/docker/radiopad`)

| Path | Contents |
|---|---|
| `docker-compose.yml` | Image-only compose: one service, `radiopad-api` (`image: radiopad-api:latest`, `pull_policy: never`), env, networks `radiopad`, `npm` (external `nginx-proxy-manager_default`), `platform` (external) |
| `.secrets.env` | Secrets, `chmod 600`, **owned by `radiopad-deploy`** — if it is not readable by that user the CI deploy fails at `docker compose up` with `open .secrets.env: permission denied` |
| `data/` | SQLite database `radiopad.db`, mounted at `/data` |
| `backups/` | DB copies (take one before schema changes) |
| `docker-compose.yml.bak-*` | Previous compose revisions — take one before every edit |

Notable non-secret env in compose: `ConnectionStrings__RadioPad: Data Source=/data/radiopad.db`,
`RADIOPAD_PUBLIC_WEB_URL: https://admin.radiopadstudio.com` (login/magic-link pages),
`RADIOPAD_CORS_ORIGINS: https://admin.radiopadstudio.com,https://radiopadstudio.com,https://www.radiopadstudio.com`,
`RADIOPAD_WEBAUTHN_RP_ID: radiopadstudio.com` + `RADIOPAD_WEBAUTHN_ORIGINS`,
`Logging__LogLevel__Microsoft.EntityFrameworkCore.Database.Command: Warning`.

## Deploying

Pushes to `main` touching `backend/**`, `rulebooks/**`, `templates/**`,
`deploy/vps/api.Dockerfile` or `deploy/vps/docker-compose.yml` run
[`web-deploy-images.yml`](../../.github/workflows/web-deploy-images.yml): it
builds `radiopad-api:latest` on GitHub Actions, ships it over SSH as
`radiopad-deploy` (secrets `VPS_HOST`, `VPS_DEPLOY_USER`, `VPS_DEPLOY_SSH_KEY`),
`docker load`s it and runs `docker compose up -d` in `/opt/docker/radiopad`.
The API runs EF migrations on startup.

Manual fallback (on the VPS):

```bash
gh run download <run_id> --repo jerryboganda/radiopad -n radiopad-images -D /tmp/rpimg
gunzip -c /tmp/rpimg/radiopad-images.tar.gz | docker load
cd /opt/docker/radiopad && docker compose up -d && docker compose ps
```

Env/config-only change: back up `docker-compose.yml`, edit, then
`docker compose config -q && docker compose up -d radiopad-api`.

## `api.radiopadstudio.com` proxy + certificate

- Vhost: `/opt/docker/nginx-proxy-manager/data/nginx/proxy_host/radiopad-api-manual.conf`
  (hand-maintained, like the other `*-manual.conf` files on this box; it does
  not appear in the NPM UI). Port 80 serves ACME + redirects; port 443 proxies
  to `radiopad-api:7457` with `real_ip_header CF-Connecting-IP`, `/ws/` upgrade
  (3600s, access log off — the bearer rides in `?access_token=`), 300s read
  timeout and `proxy_buffering off` for AI jobs / SSE.
- Cert: Let's Encrypt via certbot inside the NPM container (webroot
  `/data/letsencrypt-acme-challenge`), path
  `/etc/letsencrypt/live/api.radiopadstudio.com/`. Renewed weekly by
  `/etc/cron.d/radiopad-api-cert-renew` (Mon 03:17, then `nginx -s reload`).
- After editing: `docker exec nginx-proxy-manager-app-1 nginx -t && docker exec nginx-proxy-manager-app-1 nginx -s reload`.
- Real client IPs: NPM trusts Cloudflare ranges (`ip_ranges.conf`) and the vhost
  reads `CF-Connecting-IP`; the API's forwarded-headers handling then sees the
  visitor IP (per-IP rate limiter + audit log depend on it — keep it).
- Cloudflare Free cuts any single request at **100s** (nginx allows 300s); a
  non-streaming call longer than that returns 524 to the client.

## Secrets (`/opt/docker/radiopad/.secrets.env`)

Production startup fails unless these are present:

| Env var | Purpose |
|---|---|
| `RADIOPAD_AUTH_SECRET` | Bearer token signing secret |
| `RADIOPAD_COLUMN_KEK` | Env-backed wrapping key (compose sets `RADIOPAD_COLUMN_KEY_REF: env:RADIOPAD_COLUMN_KEK`) |
| `RADIOPAD_COLUMN_KEY_WRAPPED` | Base64 wrapped data key for encrypted columns |

Also present: Gmail API OAuth trio (`RADIOPAD_GMAIL_CLIENT_ID` /
`_CLIENT_SECRET` / `_REFRESH_TOKEN`, with `RADIOPAD_EMAIL_PROVIDER: gmail`),
`RADIOPAD_BOOTSTRAP_SECRET`, `RADIOPAD_UBAG_TOKEN`. New keys:
`openssl rand -base64 48` (auth secret), `openssl rand -base64 32` (KEK). The
development column-key fallback must never be used in Production. OIDC, if
enabled: `RADIOPAD_OIDC_AUTHORITY`, `RADIOPAD_OIDC_CLIENT_ID`, optional
`RADIOPAD_OIDC_CLIENT_SECRET`, `RADIOPAD_OIDC_AUDIENCE`, redirect URI, claim maps.

## UBAG AI-orchestrator wiring

RadioPad consumes the UBAG gateway (compose project `/opt/docker/ubag`,
container `ubag-vps-gateway-1`) over the shared external docker network
`platform` — never via the public `ubag.polytronx.com` host.

- `.secrets.env`: `RADIOPAD_UBAG_TOKEN=<UBAG_APP_SECRET>`.
- compose env: `RADIOPAD_UBAG_BASE_URL: http://ubag-vps-gateway-1:8080`,
  `RADIOPAD_UBAG_AUTH_SECRET_REF: env:RADIOPAD_UBAG_TOKEN`,
  `RADIOPAD_UBAG_ALLOWED_TARGETS: gemini_web,deepseek_web,chatgpt_web,mock`,
  `RADIOPAD_OPERATOR_ALERT_EMAIL`.
- Verify: `GET https://api.radiopadstudio.com/api/ubag/status` (admin) →
  `health.ok: true`; `ready: null` per target is normal for vps-local executors.

## Health checks

```bash
# From anywhere:
curl -s https://api.radiopadstudio.com/api/health            # {"status":"ok",...}
curl -s https://api.radiopadstudio.com/api/mobile/latest     # current desktop/APK version
# On the VPS (through NPM, bypassing Cloudflare):
R="--resolve api.radiopadstudio.com:443:127.0.0.1"
curl -s $R https://api.radiopadstudio.com/api/health
curl -s $R -o /dev/null -w "ws %{http_code} (401=ok)\n" --http1.1 \
  -H Connection:Upgrade -H Upgrade:websocket -H Sec-WebSocket-Version:13 \
  -H Sec-WebSocket-Key:dGhlIHNhbXBsZSBub25jZQ== https://api.radiopadstudio.com/ws/companion
cd /opt/docker/radiopad && docker compose ps
```

## Backup and restore (SQLite)

**Automatic (since 2026-11-23):** `/usr/local/sbin/radiopad-backup.sh`, run from `/etc/cron.d/radiopad-backup` at 02:30 UTC,
takes a consistent online copy (`sqlite3 .backup`, safe while the API runs), checks `PRAGMA integrity_check`, gzips and
AES-256 encrypts it to `/opt/docker/radiopad/backups/nightly/radiopad-<ts>.db.gz.enc` (plus `uploads-<ts>.tar.gz.enc` for dictation audio).
It keeps **only the newest** backup of each kind (the new one is verified before older ones are removed), so it never grows.
The key is `/opt/docker/radiopad/.backup.key` (root, 600): **copy it somewhere off the server** or the backups cannot be read after a disk loss.
Restore: `openssl enc -d -aes-256-cbc -pbkdf2 -pass file:/opt/docker/radiopad/.backup.key -in <file>.db.gz.enc | gunzip > radiopad.db`
(stop `radiopad-api`, replace `data/radiopad.db`, remove stale `-wal`/`-shm`, start). Log: `/var/log/radiopad-backup.log`.
The backups live on the same disk as the database; they do not cover loss of the server.

**Manual copy:**

```bash
cd /opt/docker/radiopad
docker compose stop radiopad-api
cp -p data/radiopad.db backups/radiopad-$(date +%Y%m%d-%H%M%S).db
docker compose start radiopad-api
```

Restore: stop `radiopad-api`, copy the backup over `data/radiopad.db` (remove
stale `radiopad.db-wal` / `-shm`), start it. Validate on a copy first unless
downtime is approved. Store encrypted copies off-host.

## Moving to PostgreSQL (not done)

Production still runs on SQLite. To migrate later: cold SQLite backup → start a
Postgres container on `radiopad_network` → load with `dimitri/pgloader` (mount
`/opt/docker/radiopad/data` writable) → normalise types (GUID → `uuid`,
timestamps → `timestamptz`, booleans → `boolean`, enum/int → `integer`) → point
`ConnectionStrings__RadioPad` at Postgres → keep the SQLite backup until
sign-in, report creation and audit verification pass. Never run both stacks
with live traffic.

## Rollback

- **Bad image:** re-run the manual fallback with the last good
  `web-deploy-images` run id, then `docker compose up -d`. Schema changes are
  not auto-reverted — restore the pre-deploy SQLite backup if needed.
- **Bad compose edit:** restore the matching `docker-compose.yml.bak-*`, `docker compose up -d`.
- **Bad vhost edit:** fix/restore `radiopad-api-manual.conf`, `nginx -t`, reload.
  NPM's DB is `/opt/docker/nginx-proxy-manager/data/database.sqlite` — copy it
  before bulk NPM edits.
- **Traffic:** in Cloudflare, set the `api` record to DNS only to bypass Cloudflare.
