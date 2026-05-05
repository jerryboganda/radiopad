# RadioPad Desktop (Tauri 2)

Native shell that loads the static export of the Next.js frontend and talks to
the local ASP.NET Core API on `http://127.0.0.1:7457`.

## Prerequisites

- Rust (stable) — install via [rustup.rs](https://rustup.rs)
- Node + pnpm
- Tauri prerequisites for your OS — see <https://v2.tauri.app/start/prerequisites/>
- The RadioPad backend running on port 7457

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
