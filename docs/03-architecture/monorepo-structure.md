# Monorepo Structure

**Status:** Current  ·  **Owner:** Engineering  ·  **Last Updated:** 2026-05-04

This is a **polyglot monorepo** glued together by `pnpm-workspace.yaml` (for the JS/TS surfaces) and the .NET solution under `backend/RadioPad.Api/`.

```
.
├── backend/RadioPad.Api/               # .NET solution
│   ├── src/
│   │   ├── RadioPad.Domain/            # Entities + enums
│   │   ├── RadioPad.Application/       # Services + DTOs + provider adapters
│   │   ├── RadioPad.Validation/        # Rulebook engine
│   │   ├── RadioPad.Infrastructure/    # EF Core + audit chain
│   │   └── RadioPad.Api/               # Web host (Program.cs, controllers, middleware)
│   ├── tests/
│   │   └── RadioPad.Api.Tests/         # xUnit unit + integration
│   └── Directory.Build.props
├── frontend/                           # Next.js 16 App Router
│   ├── app/                            # routes, layout, globals.css
│   ├── lib/api.ts                      # typed HTTP client
│   ├── public/
│   ├── out/                            # static export (build output)
│   └── package.json
├── desktop/                            # Tauri 2
│   └── src-tauri/                      # Rust + capabilities
├── mobile/                             # Capacitor 6
│   ├── android/   ios/                 # added on demand
│   └── capacitor.config.ts
├── cli/RadioPad.Cli/                   # .NET 8 global tool
├── rulebooks/                          # YAML + golden cases
│   └── _tests/<rulebook_id>/           # JSON fixtures
├── templates/                          # JSON report templates
├── deploy/                             # Dockerfile.api, docker-compose.yml
├── .github/                            # CI workflows + agent instructions
├── docs/                               # Living documentation
├── openapi/openapi.yaml                # API contract
├── PRD.md  PROGRESS.md                 # engineering PRD + Ralph log
├── AGENTS.md  CLAUDE.md  GEMINI.md     # AI agent entry points
├── README.md  SECURITY.md  …           # governance
└── src/  daemon/  *.legacy.*           # READ-ONLY Open Design history
```

## Package boundaries

- The frontend never reaches into `backend/` source; all interaction is through the HTTP API.
- The desktop / mobile shells consume `frontend/out/` only; they do not import frontend source.
- The CLI talks to the API over HTTP; it imports `RadioPad.Domain` and `RadioPad.Validation` for local rulebook checks (no DB access).
- Backend layers obey `Domain → Application → Validation → Infrastructure → Api` strictly.

## Read-only history

- `src/`, `daemon/`, and `*.legacy.*` files are the original Open Design playground. They are kept for visual reference but **not** for runtime use. Do not edit; do not import.
