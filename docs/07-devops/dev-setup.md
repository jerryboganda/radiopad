# Local development setup

## Required toolchains

| Tool | Version | Purpose |
| ---- | ------- | ------- |
| .NET SDK | 8.0+ | Backend, CLI, tests |
| Node.js | 20 LTS | Frontend |
| pnpm | 9+ | Workspace package manager |
| Rust + Cargo (stable) | latest | Tauri desktop |
| Android Studio / Xcode | latest | Capacitor mobile |

> **Status:** the toolchains are not currently on the dev workstation's PATH.
> Install them before running the commands below — see PROGRESS.md for the
> tracking item.

## First-time bootstrap

```powershell
# 1. Frontend deps
pnpm install

# 2. Backend restore + build
dotnet restore backend/RadioPad.Api/RadioPad.Api.sln
dotnet build   backend/RadioPad.Api/RadioPad.Api.sln -c Debug

# 3. Run backend tests
dotnet test    backend/RadioPad.Api/RadioPad.Api.sln

# 4. Frontend typecheck
pnpm --filter @radiopad/frontend typecheck
```

## Running the stack

In separate terminals:

```powershell
# Backend (binds 127.0.0.1:7457; SQLite db at backend/RadioPad.Api/src/RadioPad.Api/radiopad-dev.db)
dotnet run --project backend/RadioPad.Api/src/RadioPad.Api

# Frontend (Next.js dev server on :3000, with /api proxied to :7457)
pnpm --filter @radiopad/frontend dev
```

Open <http://localhost:3000>. The dev seeder creates a `dev` tenant and a
`radiologist@radiopad.local` user; both are pre-populated in the API client.

## Desktop (Tauri)

```powershell
cd desktop
cargo tauri dev
```

## Mobile (Capacitor)

```powershell
cd mobile
pnpm install
pnpm exec cap add android   # first time
pnpm sync
pnpm android
```

## CLI

```powershell
dotnet run --project cli/RadioPad.Cli -- rulebook validate rulebooks/chest_ct_v1.yaml
```

## Environment variables

| Variable | Default | Notes |
| -------- | ------- | ----- |
| `RADIOPAD_BIND` | `http://127.0.0.1:7457` | Backend listen URL |
| `ConnectionStrings__Default` | (SQLite file) | Override for PostgreSQL |
| `Anthropic__ApiKey` | unset | If set, the Anthropic provider becomes usable |

## Troubleshooting

- **Frontend can't reach backend:** the dashboard shows a `.banner.warn` with
  the precise error. Confirm the backend is on 7457 and CORS allows
  `http://localhost:3000`.
- **PHI policy errors:** expected — the AI gateway will refuse to send PHI to
  a provider whose `compliance` is not `PhiApproved` or `LocalOnly`. Toggle
  the provider to a permitted class or remove PHI from the prompt.
