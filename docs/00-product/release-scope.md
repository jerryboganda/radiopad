# Release Scope

**Status:** Current  ·  **Owner:** Product  ·  **Last Updated:** 2026-05-04

## v0.1.0 — Architecture baseline (shipped 2026-05-04)

**Included**
- Locked Open Design UI, App Router frontend, ASP.NET Core 8 backend.
- Tauri 2 desktop shell with `Ctrl+Shift+R` global focus.
- Capacitor 6 mobile (read/acknowledge only).
- .NET 8 CLI with login / daemon / rulebook / report / audit / provider commands.
- Five seed rulebooks + templates + golden suites.
- AI gateway (Mock + Anthropic + Ollama), PHI policy.
- Append-only audit log, SHA-256 chain, `audit verify`.
- FHIR `DiagnosticReport` text export.

**Excluded**
- SSO, RBAC enforcement, multi-rad sign-off, RAG, voice.

**Quality gates**
- ✅ `dotnet build && dotnet test` green on `main`.
- ✅ `pnpm typecheck && pnpm build` green.
- ✅ Every matching rulebook golden suite green in CI.

## v0.2.0 — Operational hardening (in progress)

**Included**
- Server-side report list pagination + search.
- `/api/health/ready` readiness probe.
- `ReportVersion` snapshot on every PATCH + `GET /api/reports/{id}/versions`.
- `radiopad provider test --id <guid>`.
- Validation panel grouped by severity.

**Quality gates**
- All v0.1.0 gates plus integration test for the `ProviderBlocked` audit on a disabled or `Blocked` provider. (The PHI gate this originally covered was removed on 2026-07-20 by operator decision.)
- Updated CHANGELOG entry under `[Unreleased]`.

## v0.3.0 — Clinical breadth (planned)

**Included (target)**
- Two new modalities (cardiac MRI, mammography).
- Addendum / amend-after-sign workflow.
- Read-only peer-review screen share.

## v1.0.0 — GA target (planned)

**Included (target)**
- SSO/OIDC, RBAC matrix, signed audit bundles.
- Hosted BAA-ready offering.
- SOC 2 Type I letter of attestation.

**Rollout plan**
- Pilot tenants on `release/0.x` → promote to `release/1.x` LTS.
- Database migration plan reviewed by ops and security.
- 30-day customer change-management window.
