# RadioPad — Desktop-First Surface Split & Companion — SESSION HANDOFF

> **Status: SHIPPED & LIVE IN PRODUCTION (2026-07-12).** Everything below is done and
> verified unless it's under "Outstanding". A fresh session can start from this file.
> Persistent memories also cover this: `radiopad-surface-specialization`,
> `radiopad-web-topology`, `radiopad-npm-vhost-clobber` (auto-load next session).

> **Update 2026-11-21 (ops):** the website (`radiopadstudio.com`) and admin web app
> (`admin.radiopadstudio.com`) are static builds on **Hostinger**; the VPS runs only the API at
> **`api.radiopadstudio.com`** (compose project `/opt/docker/radiopad`), all behind Cloudflare.
> `/opt/radiopad`, `_deploy-images.sh`, the compose override and the `radiopad-web` /
> `radiopad-marketing` / `radiopad-admin` containers are gone. §4, §6 and §10 are corrected;
> the `radiopad.polytronx.com` notes in §4 are history. Current runbooks:
> `deploy/vps/README.md`, `deploy/hostinger/README.md`.

Repo root: `E:\RadioPad MEGA Folder\RADIOPAD`  ·  GitHub: `jerryboganda/radiopad` (branch `main`)
Approved plan: `C:\Users\Admin\.claude\plans\please-i-want-to-lexical-sunset.md`

---

## 1. What this was

Re-scope RadioPad from ONE shared Next.js frontend into **three build-time surfaces**
selected by a `RADIOPAD_SURFACE` flag, so each shell physically ships only its own routes:

- **Desktop** = the entire reporting product (worklist, editor, dictation, library
  authoring, personal settings, **phone-companion host**). Clinical roles.
- **Web** = master-admin / platform operations ONLY. No reporting, no clinical login;
  clinical users get a "download the desktop app" interstitial.
- **Mobile** = a dictation **companion** that pairs to a LIVE desktop session and streams
  voice into the report open on that desktop. No standalone reporting.

---

## 2. Architecture / mechanism (the important part)

- Routes live in App Router route groups: `frontend/app/(desktop|web|mobile|shared)/`.
- `frontend/scripts/build-surface.mjs` runs `next build` with `RADIOPAD_SURFACE` set,
  **stages non-target groups OUT of `app/`** (and swaps the root `/` for a redirect on
  web/mobile), then moves `out/` → `out-<surface>`. Crash-safe (restores on next run +
  SIGINT handler). Commands: `pnpm --filter @radiopad/frontend build:{desktop,web,mobile}`.
- `frontend/lib/surface.ts` — `SURFACE`, `isWebSurface`, `surfaceAllows()`. Booleans
  compare directly to the inlined env so off-surface code is dead-code-eliminated.
- `frontend/components/shell/nav.config.tsx` — every nav item is `surfaces`-tagged;
  `Sidebar.tsx` filters by surface.
- `frontend/components/shell/WebAdminGate.tsx` — clinical-only users → desktop-app notice
  (only when authenticated; defers to AuthGate when signed-out). Gate perms in
  `frontend/lib/permissions.ts` `WEB_ADMIN_PERMISSIONS` (manage/approve/verify/export ONLY
  — a plain Radiologist holds users.read/billing.read/mcp_tools.invoke, so those are OUT).
- Shells: Tauri `frontendDist`→`out-desktop` + `beforeBuildCommand build:desktop` + CSP
  `wss:` (`desktop/src-tauri/tauri.conf.json`); Capacitor `webDir`→`out-mobile`
  (`mobile/capacitor.config.ts`).

### Companion relay (desktop ↔ phone)
Raw WebSocket (NOT SignalR — pnpm store blocked the client dep). Meets at the CLOUD.
- Backend: `CompanionSession` entity + `AddCompanionSession` migration; `CompanionController`
  (`/api/companion/*`); `CompanionRelayEndpoint` (`/ws/companion`) + `CompanionRelayRegistry`
  (in-memory host/companion slots). Files under
  `backend/RadioPad.Api/src/RadioPad.Api/{Controllers,Services}/Companion*.cs`.
  WS auth mirrors `RadioPadBearerMiddleware` incl. the AuthSession revocation/expiry check;
  a `RevalidateLoopAsync` watchdog + 12h max-lifetime cap close revoked/expired sockets.
  QR advertisements last 15 minutes, pair retries are idempotent for the same bearer,
  and unlinking revokes only the token bound to that session. The relay sends a
  20-second keepalive and clients reconnect with bounded backoff after transient drops.
- Frontend: `frontend/lib/companion.ts` (WS client, `companionBase()` → cloud),
  `frontend/components/companion/CompanionHostPanel.tsx` (desktop "Pair phone", inserts
  dictation via `sectionEditorRegistry.getLastFocusedSectionEditor().insertAtCursor`),
  `frontend/app/(mobile)/companion/page.tsx` (mobile pair → dictate → remote).
- Standalone `mobile/reports/*` were removed.

---

## 3. What's LIVE in production (all verified)

| Surface | URL / artifact | Container |
|---|---|---|
| Desktop | GitHub release **v0.1.63** (MSI/AppImage/deb + signed `latest.json`) | n/a (Tauri app) |
| Web admin | **https://admin.radiopad.polytronx.com** (out-web, admin-only) | `radiopad-admin` :8094 |
| Marketing | https://radiopad.polytronx.com (Astro, unchanged) | `radiopad-web` :8093 |
| API + companion relay | https://radiopad.polytronx.com/api + /ws/companion | `radiopad-api` :7457 |
| Mobile companion | CI artifact APK (see §7) | n/a |

Health verified: API `{"status":"ok"}`; migration applied; admin serves out-web (has
`admin/`, `_next/`, NO `reports/`); `/ws/companion` → **401** over HTTP/1.1 through the full
Cloudflare→NPM→web→api chain (401 = auth reached; would be 101 with a valid token).

---

## 4. Prod infrastructure facts (VPS)

- **VPS:** `root@185.252.233.186` (SSH key already works: `ssh root@185.252.233.186`). Shared
  multi-app box. App dir `/opt/docker/radiopad` (compose + `.secrets.env` + `data/`; images
  only, no source checkout).
- **Current topology (2026-11-21):** Cloudflare (Full strict) →
  `radiopadstudio.com`/`www`/`admin` → Hostinger web hosting (static); `api.radiopadstudio.com`
  → VPS NPM hand-maintained `proxy_host/radiopad-api-manual.conf` → `radiopad-api:7457`
  (`real_ip_header CF-Connecting-IP;` — keep it, or the API sees Cloudflare IPs). The companion
  `/ws/` relay lives on the api host. No compose override file.
- **Historical (July, `radiopad.polytronx.com`) — superseded, kept for reference:**
- **Topology:** Cloudflare → NPM (`nginx-proxy-manager-app-1`, host :443) → containers.
  `radiopad.polytronx.com` is a **Cloudflare origin**; its NPM vhost is a HAND-MAINTAINED
  custom file: inside the NPM container `/data/nginx/custom/radiopad-origin.conf`
  (host path `/opt/docker/nginx-proxy-manager/data/nginx/custom/radiopad-origin.conf`).
- **Marketing-at-root is BY DESIGN:** `/opt/radiopad/docker-compose.override.yml` bind-mounts
  `marketing-dist` over `radiopad-web`'s html. The app's out-web in that image is shadowed —
  do NOT "fix" it; the admin app is the separate `radiopad-admin` container.
- **admin.radiopad.polytronx.com:** standalone certbot LE cert INSIDE the NPM container at
  `/etc/letsencrypt/live/admin.radiopad.polytronx.com/`; renewed by host cron
  `radiopad-admin-cert-renew` (Mon 03:17). DNS record must stay **DNS-only / grey cloud**
  (CF free Universal SSL doesn't cover 2-level subdomains).
- **Companion /ws requires** a `location /ws/` upgrade block BEFORE the catch-all `location /`
  in `radiopad-origin.conf` (both 80+443 server blocks). I added it (backup `.bak-wsfix`).
  If the vhost is ever clobbered/recreated, RE-ADD /ws/. WS test must use HTTP/1.1
  (`curl --http1.1`) — the 443 vhost has `http2 on` and WS-over-h2 spuriously 400s.

---

## 5. How to build / test / run locally

```bash
# Frontend (from RADIOPAD/frontend)
pnpm typecheck && pnpm test                     # 227 tests
pnpm build:desktop | build:web | build:mobile   # → out-<surface>
pnpm dev                                         # full desktop app on :3000 (all groups)

# Backend (from RADIOPAD)
dotnet build backend/RadioPad.Api/RadioPad.Api.sln
dotnet test  backend/RadioPad.Api/RadioPad.Api.sln   # 799 tests
```

Gotcha: per-surface builds clear `.next` (different route trees leave stale typed-routes).
If a typecheck fails with `Cannot find module '../../app/.../page.js'`, run `rm -rf frontend/.next`.

---

## 6. How to deploy (the playbook I used)

1. **Commit + push to `main`.** `backend/** rulebooks/** templates/**` (+ `deploy/vps/api.Dockerfile`,
   compose) → **`web-deploy-images`** builds the `radiopad-api` image and its `deploy` job ships it
   to `/opt/docker/radiopad` + `docker compose up -d`. Manual fallback, on the VPS:
   `gh run download <run_id> --repo jerryboganda/radiopad -n radiopad-images -D /tmp/rpimg &&
   gunzip -c /tmp/rpimg/radiopad-images.tar.gz | docker load &&
   cd /opt/docker/radiopad && docker compose up -d`. Backend runs EF migrations on startup.
2. **Web app + website:** `frontend/** marketing/**` → **`hostinger-deploy`** builds `out-web`
   (API base `https://api.radiopadstudio.com`) + marketing and rsyncs them to Hostinger.
3. **Desktop release:** `pnpm release:desktop` (bumps `tauri.conf.json`+`Cargo.toml`, commits,
   tags `vX.Y.Z`, pushes → `desktop-bundle` builds+signs the Windows MSI + creates the release,
   `tauri-updater` publishes `latest.json`). Needs a CLEAN tree first (untracked files block
   it — there are 2 stray PNGs in `UI UX SCREENS/`; move them aside or they'll block it).
   Watch: `gh run watch <desktop-bundle run id>`.

CLAUDE.md rule DESK-001: any `frontend/`/`desktop/` change → cut a desktop release.

---

## 7. The mobile APK (already downloaded)

- **`E:\RadioPad MEGA Folder\radiopad-mobile-apk\RadioPad-companion-v0.1.63-debug.apk`** (7.9 MB,
  validated). Sideload on Android (allow "install unknown apps"). Debug build (not a Play
  Store signed release). Re-fetch latest anytime:
  `gh run download $(gh run list --workflow mobile-bundle.yml --branch main --status success --limit 1 --json databaseId --jq '.[0].databaseId') -n radiopad-android-debug -D <dir>`

---

## 8. Adversarial review — 13 findings, ALL fixed (for the record)

Highlights: WebAdminGate fail-open (clinical perms) + fail-showing-notice-to-signed-out;
WS auth missing AuthSession revocation check; `/admin/fhir-import` was in (web) but is a
desktop reporting feature (moved to (desktop)); mobile mic left live on involuntary end;
**deploy gap: nginx/Caddy had no `/ws/` proxy** (companion unreachable) — added; watchdog +
max-lifetime for long WS sockets; registry superseded-peer / TOCTOU. Backend re-verified:
build + 799 tests green.

---

## 9. OUTSTANDING (only external things I can't do from here)

1. **Mobile app-store submission** (Play Store / App Store) — needs your store accounts +
   signing keys + their review. The installable debug APK exists (§7). iOS produced an
   `radiopad-ios-xcarchive` artifact (needs Xcode + Apple Developer signing to install).
2. **2-device live dictation smoke test** — needs two physical devices on the live cloud.
   The whole pair→relay→auth chain is verified (401 handshake); only the human
   "speak on phone → text on desktop" gesture is untested.
3. (Optional) The prod `radiopad-admin` service + admin vhost live on the VPS (added by the
   operator/parallel session), intentionally NOT mirrored into the repo's
   `deploy/vps/docker-compose.yml`. If you want infra-as-code parity, sync them — but that's
   a deliberate operator topology choice today.

---

## 10. Quick health check (paste to verify prod anytime)

```bash
WS="-H Connection:Upgrade -H Upgrade:websocket -H Sec-WebSocket-Version:13 -H Sec-WebSocket-Key:dGhlIHNhbXBsZSBub25jZQ=="
curl -s -o /dev/null -w "marketing/ %{http_code}\n" https://radiopadstudio.com/
curl -s -o /dev/null -w "api %{http_code}\n" https://api.radiopadstudio.com/api/health
curl -s --http1.1 $WS -o /dev/null -w "ws %{http_code}(401=ok)\n" https://api.radiopadstudio.com/ws/companion
curl -s -o /dev/null -w "admin/ %{http_code}\n" https://admin.radiopadstudio.com/
curl -s -o /dev/null -w "admin/admin/users %{http_code}\n" https://admin.radiopadstudio.com/admin/users/
ssh root@185.252.233.186 'cd /opt/docker/radiopad && docker compose ps --format "{{.Name}}: {{.State}}"'
```

Expected: 200 / 200 / 401 / 200 / 200, one container (`radiopad-api`) running.
