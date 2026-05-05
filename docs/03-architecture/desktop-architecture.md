# Desktop Architecture

**Status:** Current  ·  **Owner:** Engineering  ·  **Last Updated:** 2026-05-04

## Framework

Tauri 2 (Rust core + WebView2/WKWebView). Source under `desktop/src-tauri/`.

## Process model

- **Tauri main process (Rust):** owns the OS window, capability set, global shortcuts, and clipboard helpers.
- **Renderer:** the static export from `frontend/out/` running in the system WebView.
- IPC is `invoke()`-style; we keep the surface tiny (focus the window, secure clipboard write).

## Local storage

- No bespoke local DB in v0.x. The renderer uses standard `localStorage` for ephemeral UI state (last-used tenant slug, panel widths).
- Phase 4 may add a SQLite-backed offline cache for read-only reports — gated by an explicit on-prem feature flag.

## Auto-update

- Tauri updater pointed at a signed manifest (planned).
- Channel: `stable` (default), `beta` (opt-in).

## OS integration

- Global shortcut `Ctrl+Shift+R` (Windows/Linux) / `⌘⇧R` (macOS) brings RadioPad to the front.
- Clipboard write goes through `secure_copy` which schedules a 30-second TTL wipe so accidentally-copied PHI does not linger on the clipboard.
- Capability set is committed to `desktop/src-tauri/capabilities/default.json` — minimal by design (window, clipboard, http).

## Security boundaries

- The desktop bundle is signed (planned). Unsigned builds carry an unmistakable "Dev" badge in the Tauri title bar.
- The renderer only talks to the configured API base URL — there is no `tauri://` privileged API exposed to it beyond clipboard + focus.
- No file-system access from the renderer — exports go through OS file-save dialogs only.

## Build

- `pnpm --filter frontend build` produces `frontend/out/`.
- `cargo tauri build` consumes `frontend/out/` (configured in `tauri.conf.json` `frontendDist`).
- Output: `.msi` (Windows), `.app` / `.dmg` (macOS), `.AppImage` / `.deb` (Linux).

## Testing

- Smoke test: open the app, see the topbar, navigate to a report, verify the global shortcut focuses the window.
- The locked design is exactly the web design — no extra UI tests.
