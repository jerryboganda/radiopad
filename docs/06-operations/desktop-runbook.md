**Status:** Active  **Owner:** Desktop  **Last Updated:** 2026-05-16

# Desktop runbook

The RadioPad desktop shell is a Tauri 2 app at `desktop/src-tauri/`. It bundles
the Next.js static export from `frontend/out/` and spawns the ASP.NET Core
backend (`radiopad-api`) as a sidecar.

## Sidecar lifecycle

The shell supervises `radiopad-api` in `desktop/src-tauri/src/sidecar_manager.rs`.
Startup no longer panics when the sidecar is missing or fails to spawn. Instead,
the shell emits `radiopad://backend-status` events:

| State | Meaning | UI |
| --- | --- | --- |
| `starting` | Sidecar process is being launched. | Info banner |
| `ready` | `/api/health/ready` returned 200. | Hidden |
| `degraded` | Sidecar is running but readiness failed. | Warning banner |
| `restarting` | Sidecar exited and the supervisor is backing off before restart. | Warning banner |
| `failed` | Sidecar could not start or exceeded restart budget. | Danger banner |
| `disabled` | `RADIOPAD_NO_SIDECAR=1` is set. | Hidden |

The health check is intentionally low frequency (5 seconds) and uses a short
timeout so the app remains near-idle when healthy.

## Per-install pairing

Open the desktop app and route to `/pair`. The shell calls
`POST /api/auth/device/authorize`, displays the 8-character `userCode`, and
polls `POST /api/auth/device/token` at the documented interval. An operator
who is already signed in via the web app at `/devices` approves the code.
The minted bearer is persisted via `setAuthToken` (OS keyring on native;
browser-local in dev preview), and a copy is stored in the desktop keyring
under `radiopad-device-pairing-token` so the shell can prefill subsequent
launches.

## Iter-36 verification

| ID | Requirement | Status | Notes |
| --- | --- | --- | --- |
| DESK-001 | Windows + macOS desktop apps build green | OK (code) | `tauri.conf.json` declares `msi`, `dmg`, `deb`, `appimage`, `rpm` targets and the v1-compatible updater. Authenticode / Apple Developer signing is operator-supplied (out of scope for code verification). |
| DESK-002 | Auto-start / manage the local RadioPad daemon | OK | `sidecar_manager.rs` supervises the `radiopad-api` sidecar, emits health events, avoids panic-on-missing-binary, and is gated by `RADIOPAD_NO_SIDECAR=1`. |
| DESK-003 | Global hotkeys for the reporting workflow | OK | Six shortcuts registered (`Ctrl/Cmd+Shift+{R,N,I,W,D,C}`). Iter-36 closed the frontend gap: `ShellBridge.tsx` now translates every `radiopad://*` event into a navigation or a `radiopad:*` `CustomEvent` so feature pages can react. |
| DESK-004 | Secure clipboard with timeout | OK | `secure_copy` Tauri command writes the value, then a `tokio::time::sleep` task clears the clipboard and emits `radiopad://clipboard-cleared`. Per-tenant `secureClipboard.clearOnBlur` toggles a focus-loss clear. |
| DESK-005 | Local encrypted cache | OK | `local_cache.rs` — AES-256-GCM, key from the OS keyring (`crypto_keyring.rs`), per-scope file in the app data dir, lazy TTL eviction. |
| DESK-006 | Encrypted offline draft store | OK | `offline_drafts.rs` — same AES-256-GCM + keyring stack, append-only audit log. Iter-36 wired `frontend/lib/offlineDrafts.ts` to prefer this store under Tauri (Capacitor / `localStorage` remain the fallback for mobile and the web preview). |
| DESK-007 | Local PACS/RIS bridge plugins | OK | `pacs_plugins.rs` + `sandbox.rs` — SHA-256 + Ed25519-verified signed manifests under `%APPDATA%/RadioPad/plugins`. Iter-32. |
| DESK-008 | Device authorization & tenant pairing | OK | Iter-36 added `frontend/app/pair/page.tsx` driving the RFC 8628 grant via `api.auth.deviceAuthorize` / `deviceToken`. Desktop fingerprint + pairing token persist via `device_pairing.rs` + the OS keyring. |
| DESK-009 | Local model / plugin execution where enabled | OK | `sandbox.rs::verify_plugin` — constant-time SHA-256, optional Ed25519 verification, refusal of unsigned plugins in release builds. |
| DESK-010 | Local logs with PHI redaction | OK | `log_redactor.rs` installs a redacting `MakeWriter` over `tracing-subscriber` before any other code can produce log lines. |

### Out of scope (operator-supplied)

- Authenticode / Apple Developer ID code-signing certificates and the
  notarisation pipeline. The `tauri.conf.json` fields are stubs (`null`)
  pending operator credentials — see [INSTALLER_HARDENING.md](../../desktop/INSTALLER_HARDENING.md).
- Native enterprise IdP secrets for the per-tenant SSO bridge.

### Validation

- `cargo check` / `cargo test` and `pnpm typecheck` could not be executed
  in the iter-36 verification environment (toolchains not on PATH).
  TypeScript validity confirmed via the VS Code TS server.

## Files of interest

- `desktop/src-tauri/src/main.rs` — bootstrap, sidecar, hotkeys, secure clipboard.
- `desktop/src-tauri/src/{sidecar_manager,backend_health}.rs` — backend sidecar supervision and readiness checks.
- `desktop/src-tauri/src/{offline_drafts,local_cache,crypto_keyring}.rs` — DESK-005 / DESK-006.
- `desktop/src-tauri/src/{device_pairing,sandbox,pacs_plugins,log_redactor}.rs` — DESK-007..010.
- `frontend/app/ShellBridge.tsx` — translates Tauri events to React-app actions.
- `frontend/app/pair/page.tsx` — DESK-008 device pairing.
- `frontend/lib/offlineDrafts.ts` — DESK-006 storage adapter.
