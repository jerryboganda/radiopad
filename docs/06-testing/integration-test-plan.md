# Integration Test Plan

**Status:** Current  ·  **Owner:** Engineering  ·  **Last Updated:** 2026-05-04

## Scope

End-to-end through the HTTP API using `Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program>` against an in-memory SQLite database.

## What is covered

- Tenant resolution (`X-RadioPad-Tenant`, `X-RadioPad-User`).
- Cross-tenant isolation (tenant A cannot read tenant B).
- Reports lifecycle: create → patch → validate → ai → acknowledge → export → versions.
- Rulebook lifecycle: save → validate-yaml → approve → deprecate.
- Provider PHI policy: `containsPhi: true` to a Sandbox provider returns 403 with `kind: provider_policy` and writes a `ProviderBlocked` audit event.
- Audit chain: every transition adds an event; `IntegrityChain` matches the formula.
- Pagination headers (`X-Total-Count`).
- Health (`/api/health`, `/api/health/ready`).

## Test tenant

- Slug: `it`.
- Default user: `it-radiologist@radiopad.local`.
- Provider: Mock with compliance `LocalOnly`.

## Conventions

- Tests under `tests/RadioPad.Api.Tests/Integration/`.
- File-scoped namespaces match folder structure.
- Each test acquires its own `WebApplicationFactory` via a fixture; no shared state across tests.
- Use `Category=Integration` trait for filtering.

## Run

```powershell
cd backend/RadioPad.Api
dotnet test --filter Category=Integration
```

## CI

- Same suite runs in CI (`.github/workflows/ci.yml`) on every PR.
- Failure is a merge blocker.
