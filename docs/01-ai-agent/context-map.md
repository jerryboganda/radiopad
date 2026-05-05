# Context Map

**Status:** Current  ·  **Owner:** Engineering  ·  **Last Updated:** 2026-05-04

Quick map for AI agents and humans alike.

## Product docs

- [../00-product/vision.md](../00-product/vision.md), [problem-statement.md](../00-product/problem-statement.md), [prd.md](../00-product/prd.md), [brd.md](../00-product/brd.md), [srs.md](../00-product/srs.md), [frd.md](../00-product/frd.md), [nfr.md](../00-product/nfr.md), [scope.md](../00-product/scope.md), [mvp.md](../00-product/mvp.md), [personas.md](../00-product/personas.md), [user-stories.md](../00-product/user-stories.md), [use-cases.md](../00-product/use-cases.md), [acceptance-criteria.md](../00-product/acceptance-criteria.md), [kpi-metrics.md](../00-product/kpi-metrics.md), [pricing-billing.md](../00-product/pricing-billing.md), [tenant-model.md](../00-product/tenant-model.md), [roadmap.md](../00-product/roadmap.md), [release-scope.md](../00-product/release-scope.md).

## Architecture docs

- [../03-architecture/architecture.md](../03-architecture/architecture.md) — system overview.
- [../03-architecture/api-reference.md](../03-architecture/api-reference.md) — HTTP API.
- [../03-architecture/database-design.md](../03-architecture/database-design.md) — entities + indexes.
- [../03-architecture/adr/](../03-architecture/adr/) — architectural decision records.

## Source folders

- `backend/RadioPad.Api/src/` — Domain · Application · Validation · Infrastructure · Api.
- `frontend/` — Next.js App Router; `frontend/app/globals.css` is the canonical stylesheet.
- `desktop/` — Tauri 2 shell.
- `mobile/` — Capacitor 6 shell.
- `cli/RadioPad.Cli/` — .NET 8 global tool.

## Test folders

- `backend/RadioPad.Api/tests/RadioPad.Api.Tests/` — unit + integration.
- `rulebooks/_tests/<rulebook_id>/` — clinical golden cases.
- `frontend/__tests__/` — frontend tests (when present).

## Infra folders

- `deploy/` — Dockerfile.api, docker-compose.yml.
- `.github/workflows/` — CI.
- `desktop/src-tauri/capabilities/default.json` — Tauri capabilities.

## Configuration files

- `backend/RadioPad.Api/Directory.Build.props` — common .NET props.
- `backend/RadioPad.Api/src/RadioPad.Api/appsettings*.json` — runtime config.
- `frontend/next.config.ts` — App Router config + dev rewrites.
- `frontend/package.json`, `pnpm-lock.yaml`.
- `.env.example` — reference env vars.

## Entry points

- Backend: `backend/RadioPad.Api/src/RadioPad.Api/Program.cs`.
- Frontend: `frontend/app/layout.tsx`, `frontend/app/page.tsx`.
- Desktop: `desktop/src-tauri/src/main.rs`.
- Mobile: `mobile/capacitor.config.ts`.
- CLI: `cli/RadioPad.Cli/Program.cs`.
