# Tech Stack

**Status:** Current  ·  **Owner:** Engineering  ·  **Last Updated:** 2026-05-04

| Concern | Choice | Notes |
| --- | --- | --- |
| Language (backend) | C# 12 / .NET 8 | LTS through Nov 2026; matches CLI runtime. |
| Web framework | ASP.NET Core 8 | Minimal hosting model + MVC controllers. |
| ORM | EF Core 8.0.10 | SQLite provider for dev, Npgsql `8.0.8` for Postgres. |
| Migration tool | EF Core CLI (`dotnet ef`) | Migrations require human review. |
| API style | REST + JSON, RFC-7807 problems | OpenAPI in [openapi/openapi.yaml](../../openapi/openapi.yaml). |
| Auth | Header-based dev tenant (v0.1) → OIDC (Phase 3) | `X-RadioPad-Tenant`, `X-RadioPad-User`. |
| Authorization | Tenant isolation via `ResolveContextAsync` | RBAC enforcement Phase 3. |
| Web framework (frontend) | Next.js 16 App Router | Static export (`output: 'export'`). |
| UI runtime | React 18 + TypeScript | Strict TS. |
| Styling | Locked Open Design tokens in `globals.css` | No Tailwind utilities; no MUI/Ant. |
| Desktop | Tauri 2 (Rust) | Tokio async; `Ctrl+Shift+R` global shortcut; clipboard TTL. |
| Mobile | Capacitor 6 | Wraps `frontend/out`; read/acknowledge only. |
| CLI | .NET 8 + System.CommandLine | Global tool `radiopad`. |
| Cloud / hosting | Self-hosted on prem; Docker Compose for v0.x; Kubernetes-ready manifests planned | Hosted SKU planned. |
| Testing | xUnit (backend), Vitest (frontend), `WebApplicationFactory<Program>` (integration) | Plain `Assert`; no FluentAssertions/Moq. |
| Observability | `Microsoft.Extensions.Logging` + `AddSimpleConsole`; correlation header | OpenTelemetry planned. |
| Build | `dotnet`, `pnpm`, `cargo`, `npx cap` | See [../07-devops/dev-setup.md](../07-devops/dev-setup.md). |
| CI | GitHub Actions | `.github/workflows/ci.yml` runs build/test/golden suites. |

## Pinning

- .NET 8 SDK pinned via `global.json` (planned) and Directory.Build.props.
- Node engines pinned via `.nvmrc` (`v20.x`).
- pnpm version pinned via `packageManager` field in `package.json`.

## Forbidden additions

Any of the following requires explicit human approval:

- New backend frameworks (Express, NestJS, Fastify).
- New ORMs (Dapper).
- Additional UI frameworks (MUI, Ant, Chakra, Bootstrap).
- Tailwind utilities for styling.
- Dark-mode variants.
