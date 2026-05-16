# RadioPad

> AI-assisted radiology reporting platform — Web · Desktop · Mobile · CLI.
> **Radiologist remains the final authority.** RadioPad drafts, validates, and standardizes; the radiologist signs.

[![Status](https://img.shields.io/badge/status-active%20development-orange)]() [![License](https://img.shields.io/badge/license-Apache--2.0-blue)]()

RadioPad turns radiologist intent — dictation, structured findings, prior reports, measurements, study metadata — into high-quality **draft** radiology reports, validated against institution-specific rulebooks and exported as FHIR `DiagnosticReport`, PDF, DOCX, or plain text.

The platform is delivered as four coordinated surfaces:

| Surface | Technology | Purpose |
| --- | --- | --- |
| **Web** | Next.js 16 (App Router) | Reporting workspace, admin, governance, analytics |
| **Backend** | ASP.NET Core 8 (C#) + EF Core | Domain logic, AI gateway, validation, audit, FHIR export |
| **Desktop** | Tauri 2 (Rust + bundled web) | Hotkeys, secure clipboard, PACS/RIS bridge, local daemon |
| **Mobile** | Capacitor 6 | iOS / Android wrapper of the web reporting workspace |
| **CLI** | `radiopad` (.NET 8 global tool) | Rulebook authoring, validation, batch generation, audit export |

See [PRD.md](./PRD.md) for the engineering PRD and the bundled enterprise PRD (`RadioPad — Enterprise PRD …md`) for the full spec.

---

## Quickstart

### Prerequisites

- **.NET SDK 8.0+**
- **Node.js 20+** and **pnpm 9+** (`corepack enable`)
- **Rust toolchain** (only for desktop builds: `rustup`, `cargo`)
- **Capacitor CLI** prerequisites (Android Studio / Xcode) for mobile builds

### Run web + backend locally

```powershell
# Backend (ASP.NET Core API on http://localhost:7457)
cd backend/RadioPad.Api
dotnet restore
dotnet run --project src/RadioPad.Api

# Frontend (Next.js dev server on http://localhost:3000)
cd ../../frontend
pnpm install
pnpm dev
```

### Run the CLI

```powershell
cd cli/RadioPad.Cli
dotnet run -- rulebook validate ../../rulebooks/chest_ct_v1.yaml
```

### Build the desktop app

```powershell
cd frontend; pnpm build       # produces frontend/out/
cd ../desktop; cargo tauri build
```

### Build the mobile app

```powershell
cd frontend; pnpm build
cd ../mobile; npx cap copy android; npx cap open android
```

---

## Architecture

```
Clients: Web (Next.js) · Desktop (Tauri) · Mobile (Capacitor) · CLI
                        │ HTTPS / JSON
                        ▼
        ASP.NET Core 8 API  ──►  EF Core  ──►  SQLite (dev) / PostgreSQL (prod)
              │
   ┌──────────┼──────────┐
   ▼          ▼          ▼
 Anthropic  Azure OpenAI  Local model (Ollama / vLLM)
 (BAA/BYOK) (BAA)
```

PHI flows are gated by a **Provider Compliance Class**. Requests carrying PHI are rejected unless the active tenant policy allows the destination provider — the gateway never silently downgrades.

## Repository layout

```
backend/RadioPad.Api/   ASP.NET Core solution
frontend/               Next.js 16 app
desktop/                Tauri 2 shell
mobile/                 Capacitor 6 project
cli/RadioPad.Cli/       .NET global tool
rulebooks/              YAML rulebooks
templates/              JSON report templates
docs/                   Product / architecture / security / devops / user docs
PRD.md  PROGRESS.md     Ralph-loop memory
```

## Safety

1. RadioPad never auto-signs reports.
2. AI-generated text is visually marked until reviewed.
3. PHI requests blocked unless the destination provider is `phi_approved` for the tenant.
4. Audit log is append-only.
5. Local deployments bind `127.0.0.1` by default.

## License

Apache-2.0 — see [LICENSE](./LICENSE).
