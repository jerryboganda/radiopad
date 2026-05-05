# Unit Test Plan

**Status:** Current  ·  **Owner:** Engineering  ·  **Last Updated:** 2026-05-04

## Scope

- Domain entities + enums.
- Application services (`AiGateway` policy logic, `FhirDiagnosticReportSerializer`).
- Validation engine + rulebook YAML parsing.
- Infrastructure helpers (`AuditLog` chain math, `EnvSecretResolver`).
- Frontend pure utilities.

## Tooling

- Backend: xUnit + plain `Assert`. No FluentAssertions or Moq.
- Frontend: Vitest for utilities (most logic lives behind the typed API client).

## Conventions

- Test classes named `<TypeUnderTest>Tests` and live in `tests/RadioPad.Api.Tests/<Project>/`.
- One test method per behaviour.
- Arrange/Act/Assert blocks separated by blank lines.
- No shared mutable fixtures; use local builders.

## Coverage targets

- Domain + Validation: ≥ 90% line coverage.
- Application (excluding adapters): ≥ 80%.
- Adapters: behaviour covered through integration tests; unit-level coverage not required.
- Frontend utilities: ≥ 80%.

## What we explicitly do NOT unit-test

- DB queries (covered by integration tests).
- HTTP handler wiring (covered by integration tests).
- React components (covered by `pnpm typecheck` + the design lock).

## Run

```powershell
cd backend/RadioPad.Api
dotnet test --filter Category!=Integration

cd ../../frontend
pnpm test
```
