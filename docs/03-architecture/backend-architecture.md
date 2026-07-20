# Backend Architecture

**Status:** Current  ·  **Owner:** Engineering  ·  **Last Updated:** 2026-05-04

## Layers

```
Api          ← Controllers, middleware, host
Infrastructure ← EF Core, audit chain, secret resolver
Validation   ← Rulebook engine, YAML schema
Application  ← Services (AiGateway, FhirSerializer), DTOs, adapters
Domain       ← Entities + enums (no dependencies)
```

Dependency direction is one-way (downward in the diagram). Reversing it (e.g. Domain referencing Infrastructure) is forbidden.

## Service responsibilities

- **`AiGateway`** — Single entrypoint for AI. Rejects disabled and `Blocked` providers → audits `ProviderBlocked` on rejection → routes to provider adapter → audits `AiRequest` + `AiResponse`.
- **`ReportValidator`** — Loads the rulebook by id+version, applies rules to a report, returns findings.
- **`FhirDiagnosticReportSerializer`** — Builds narrative + FHIR JSON.
- **`IAuditLog`** — Append-only writer; computes SHA-256 chain.
- **`RadioPadDbContext`** — EF Core context; SQLite or Npgsql via connection-string sniff.

## Controllers

- `ReportsController` — list/get/post/patch/validate/ai/acknowledge/export/versions.
- `RulebooksController` — list/get/save/validate-yaml/approve/deprecate.
- `ReportTemplatesController` — list/save.
- `ProvidersController` — list/save.
- `AuditController` — stream tenant-scoped events.
- Health: `/api/health`, `/api/health/ready`.

All inherit `TenantedController` and call `ResolveContextAsync(_db, ct)` first.

## Middleware pipeline

```
Request
  → RequestCorrelationMiddleware (stamps X-Request-Id)
  → GlobalExceptionMiddleware (RFC-7807 problem details)
  → Authentication (dev header / OIDC Phase 3)
  → Authorization (tenant resolved later inside controllers)
  → MVC controller
  → Response
```

## Persistence

- EF Core. Migrations via `dotnet ef migrations add <name>`.
- SQLite `Data Source=radiopad.dev.db` for dev.
- PostgreSQL via `Host=...;Database=...;Username=...;Password=...` for prod (sniffed in `Program.cs`).
- Every tenanted table indexes `TenantId` first.

## Background jobs

- None in v0.x. Future jobs (audit export, billing reconciliation) will use `IHostedService` + a queue of choice (Phase 3).

## Auth handling

- v0.1: header-based dev tenant; integration test tenant slug `it`.
- Phase 3: OIDC + JWT bearer; tenant id from claim, user id from claim.

## Error handling

- Throw typed exceptions (`ProviderPolicyException`, validation, not-found, conflict).
- `GlobalExceptionMiddleware` maps to RFC-7807 problem details with appropriate HTTP code.
- Provider policy block → 403 with `{ error, kind: "provider_policy" }`.

## Testing strategy

- xUnit + plain `Assert`.
- Unit tests next to each library project.
- Integration tests under `tests/.../Integration/` using `WebApplicationFactory<Program>` against in-memory SQLite.
- Tests must respect tenant isolation (slug `it`).
