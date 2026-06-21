# Desktop Architecture

**Status:** Current  ·  **Owner:** Engineering  ·  **Last Updated:** 2026-06-22

## Framework

Tauri 2 (Rust core + WebView2/WKWebView). Source under `desktop/src-tauri/`.
Pinned to the Tauri 2.11.x baseline (`tauri` 2.11.3, `tauri-build` 2.6.3, plugins
at their latest 2.x) with a committed `desktop/src-tauri/Cargo.lock` for
reproducible builds.

## Process model

- **Tauri main process (Rust):** owns the OS window, capability set, global
  shortcuts, clipboard helpers, encrypted local stores, device-keyring
  commands, backend sidecar supervision, and backend health events.
- **Renderer:** the static export from `frontend/out/` running in the system
  WebView. It uses the same Open Design CSS as the web app.
- **Backend sidecar:** the bundled ASP.NET Core `radiopad-api` binary. It binds
  `http://127.0.0.1:7457` by default and is supervised by
  `desktop/src-tauri/src/sidecar_manager.rs`.
- IPC is `invoke()`-style for narrow native capabilities plus one event stream
  (`radiopad://backend-status`) for sidecar health.

## Local storage

- The backend sidecar uses SQLite by default when no hosted database connection
  string is configured.
- The desktop shell stores long-lived native secrets in the OS credential store
  through `desktop/src-tauri/src/crypto_keyring.rs`.
- Offline drafts use `offline_drafts.rs`: AES-256-GCM values in
  `offline-drafts.enc.json`, with an append-only `offline_drafts_audit.log`.
- Non-draft cache entries use `local_cache.rs`: AES-256-GCM values with per-entry
  TTL and lazy eviction.
- The renderer still uses `localStorage` only for non-secret UI state and web
  preview fallbacks.

## Auto-update

- Tauri updater is configured but production release must inject a real
  ed25519 public key into `desktop/src-tauri/tauri.conf.json ->
  plugins.updater.pubkey`.
- Local/unsigned test builds may disable updater artifacts; production builds
  must not ship with an empty updater key.
- Channel: `stable` (default), `beta` (opt-in), `canary` (internal).

## OS integration

- Global shortcuts `Ctrl/Cmd+Shift+{R,N,I,W,D,C}` focus the app, start a report,
  generate impression, open rewrite mode, start dictation, and secure-copy the
  focused section.
- Clipboard write goes through `secure_copy`, which schedules a TTL wipe so
  accidentally-copied PHI does not linger on the clipboard.
- `radiopad://backend-status` events drive a locked-token desktop banner for
  starting, degraded, restarting, and failed backend states.
- Capability set is committed to `desktop/src-tauri/capabilities/default.json`
  and remains minimal by design.

## Security boundaries

- The desktop bundle is signed by release engineering (operator-supplied
  certificates/secrets). Unsigned internal builds must remain clearly marked in
  release notes/artifacts.
- The renderer talks to the configured API base URL and to the narrow Tauri
  command set only.
- No broad file-system or shell access is exposed to the renderer.
- Device pairing tokens are stored in the Tauri keyring path before mobile/web
  fallbacks.
- Local PACS plugin enable/disable state is stored outside signed manifests in
  `.enabled`, preserving manifest signature integrity.

## Build

- `pnpm --filter frontend build` produces `frontend/out/`.
- `cargo tauri build` consumes `frontend/out/` (configured in `tauri.conf.json` `frontendDist`).
- Output: `.msi` (Windows), `.app` / `.dmg` (macOS), `.AppImage` / `.deb` (Linux).

## Testing

- Smoke test: open the app, see the topbar, confirm the local service reaches
  ready, navigate to a report, verify global shortcuts, secure-copy timeout, and
  offline draft save/read.
- Kill or withhold the sidecar and confirm the desktop status banner appears
  without crashing the app.
- Frontend component tests cover the desktop backend status banner and
  Tauri-first secure auth storage.
 
