# Project Analysis Report

**Status:** Final  ·  **Owner:** Engineering  ·  **Last Updated:** 2026-05-04

## Detected stack

- **Web:** Next.js 16 App Router, React 18, TypeScript, static export. Locked Open Design CSS in `frontend/app/globals.css` + `radiopad.css`.
- **Backend:** ASP.NET Core 8 + EF Core 8.0.10. SQLite (dev) / PostgreSQL (Npgsql 8.0.8) prod. YamlDotNet 15.1.6. Swashbuckle 6.7.0. Microsoft.AspNetCore.Mvc.Testing 8.0.10. Bind `127.0.0.1:7457` by default.
- **Desktop:** Tauri 2 (Rust + tokio); global shortcut `Ctrl+Shift+R`; clipboard TTL via `secure_copy`.
- **Mobile:** Capacitor 6 wrapping the static export.
- **CLI:** .NET 8 global tool `radiopad` (System.CommandLine).
- **Tests:** xUnit (backend), Vitest (frontend), `WebApplicationFactory<Program>` for integration.

## Detected architecture

- Layered backend: `Domain → Application → Validation → Infrastructure → Api`.
- Tenant isolation via `TenantedController.ResolveContextAsync(_db, ct)`.
- AI gateway with PHI policy (`ProviderComplianceClass` enum routing).
- Append-only audit chain (SHA-256) via `IAuditLog.AppendAsync`.
- Pagination via `skip` / `take` with `X-Total-Count`.
- Errors: RFC-7807 problem details with stable `kind` field.

## Detected security posture

- 127.0.0.1 default bind.
- PHI policy gating + `ProviderBlocked` audit on policy denial.
- Append-only audit + chain verifier (`radiopad audit verify`).
- Provider keys via `ApiKeySecretRef = "env:NAME"`.

Gaps:
- No central log aggregation, metrics, or tracing yet.
- No SBOM automation.
- No container vulnerability scanning automation.
- No DAST / pen-test result on file.
- No RBAC (planned Phase 3).

## Detected tests

- Unit + integration suites under `tests/RadioPad.Api.Tests/`.
- Rulebook golden tests under `rulebooks/_tests/<id>/`.
- Frontend `pnpm typecheck` is gating; minimal Vitest utility coverage.
- E2E plan documented; Playwright not yet implemented.

## Detected deployment

- Local: `dotnet run` + `pnpm dev`.
- Self-hosted: Docker Compose under `deploy/`.
- Hosted SKU and Kubernetes Helm chart planned for Phase 2.

## Detected risks

- Header-based dev auth in v0.1 is unsuitable for hosted multi-tenant without an upstream identity gateway.
- AI provider compliance drift: misconfigured rows could permit PHI to a non-compliant provider; mitigated by `EnforcePhiPolicy` + integration tests.
- Audit chain corruption: SEV-1 path; offline verifier is the canonical detector.
- Single-region hosted deployment risks region-wide outages until Phase 3.
- Performance with `EF.Functions.Like` `q` search will degrade beyond ≈ 100k reports / tenant.

## Detected legacy

- `src/`, `daemon/`, `*.legacy.*` files retained read-only as Open Design reference.
- Several superseded `docs/` root files archived via `_archived_documentation/`.

## Recommendations (priority order)

1. Land OpenTelemetry traces + Prometheus metrics (Phase 2).
2. Automate SBOM + container scanning in CI.
3. Schedule the first independent pen-test ahead of v1.0.0.
4. Implement webhooks + tenant export for hosted SKU.
5. Plan multi-region hosted deployment + Helm chart.
6. Replace `LIKE %q%` search with Postgres `tsvector` once volume warrants.
7. Add per-IP and per-tenant token-bucket rate limiting.
8. Roll out OIDC + RBAC (Phase 3) and retire header-based dev auth in production.
