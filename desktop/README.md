# RadioPad Desktop (Tauri 2)

Native shell that loads the static export of the Next.js frontend. The app is a
**thin client over the hosted production API** (`https://radiopad.polytronx.com`)
for everything — auth, reports, AI, settings. The ONE exception is **on-device
dictation transcription**: the desktop bundles a loopback-only `radiopad-api`
sidecar that runs the on-device Parakeet CPU STT engine, so dictation audio
(PHI) is transcribed on the machine and never leaves it. The frontend routes
only `POST /api/stt/transcribe` to that sidecar (`http://127.0.0.1:7457`); see
`get_backend_url` / `get_local_stt_url` in `src-tauri/src/main.rs`.

Override the app API with `RADIOPAD_BACKEND`, and the STT sidecar bind with
`RADIOPAD_LOCAL_BIND`, for local development.

## Prerequisites

- Rust (stable) — install via [rustup.rs](https://rustup.rs)
- Node + pnpm
- Tauri prerequisites for your OS — see <https://v2.tauri.app/start/prerequisites/>
- For app data during dev: reachable RadioPad API (production by default, or set
  `RADIOPAD_BACKEND` to a local backend). The STT sidecar is built/bundled by CI.

## Dev

```powershell
# from repo root
pnpm install
pnpm --filter @radiopad/frontend dev   # starts Next.js on :3000

# in a second terminal
cd desktop
cargo tauri dev
```

## Build

```powershell
pnpm --filter @radiopad/frontend build   # produces frontend/out/
cd desktop
cargo tauri build
```

The bundled binary is found in `desktop/src-tauri/target/release/bundle/`.

## Design lock

The desktop shell renders the same Next.js frontend as the web — visuals are
fully governed by the locked Open Design tokens in `frontend/app/globals.css`
and `frontend/app/radiopad.css`. Do not add OS-native chrome that conflicts.
