# AI Context

**Status:** Current  ·  **Owner:** Engineering  ·  **Last Updated:** 2026-05-04

> One-page primer for any AI coding agent that joins the project. Read this and `AGENTS.md` before editing.

## Product

RadioPad — AI-assisted radiology reporting. Radiologist drafts → rulebook validates → AI suggests → radiologist signs → FHIR export. **AI never auto-signs.**

## Tech stack

Next.js 16 App Router · ASP.NET Core 8 + EF Core · Tauri 2 · Capacitor 6 · .NET 8 CLI. SQLite dev, PostgreSQL prod. Strict — no other frameworks/ORMs.

## Architecture (one paragraph)

`RadioPad.Api` exposes REST endpoints behind tenant-aware middleware. Controllers resolve `(tenant, user)` via `ResolveContextAsync`, then go through `RadioPad.Application` services (`AiGateway`, `ReportValidator`, `FhirDiagnosticReportSerializer`) and `RadioPad.Infrastructure` (`RadioPadDbContext`, `IAuditLog`). The frontend is a static export served by Next.js / Tauri / Capacitor and talks to the backend via the typed `frontend/lib/api.ts`.

## Folder map

```
backend/RadioPad.Api/src/    # Domain · Application · Validation · Infrastructure · Api
backend/RadioPad.Api/tests/  # Unit + integration (WebApplicationFactory<Program>)
frontend/                    # Next.js App Router; locked Open Design CSS
desktop/                     # Tauri 2
mobile/                      # Capacitor 6
cli/RadioPad.Cli/            # .NET 8 global tool
rulebooks/                   # YAML + golden cases under _tests/<id>/
templates/                   # Report templates (JSON)
docs/                        # Living documentation
src/, daemon/, *.legacy.*    # Read-only Open Design reference
```

## Key domain terms

- **Rulebook** — YAML, semver, owns clinical validation rules. Approved rulebooks need golden cases.
- **Template** — JSON, scaffolds report sections (id/label/placeholder/required).
- **Report** — `Draft → Validated → Acknowledged → Exported`.
- **ReportVersion** — append-only edit snapshot per PATCH.
- **Provider** — AI provider with `ProviderComplianceClass` (`Blocked / Sandbox / DeIdentifiedOnly / PhiApproved / LocalOnly`). Informational metadata; only `Blocked` affects routing.
- **AuditEvent** — append-only with SHA-256 chain.

## Important flows

- **Create + sign** → see [../00-product/use-cases.md UC-01](../00-product/use-cases.md).
- **Provider-availability gate** → `AiGateway.EnforcePhiPolicy`; rejects only disabled providers and `Compliance = Blocked`, auditing `ProviderBlocked` before throwing. The PHI gate it is named after was removed on 2026-07-20 by operator decision, so PHI routes to any enabled provider; `ContainsPhi` is still computed and recorded on the audit and usage rows.
- **Audit verify** → `radiopad audit verify` recomputes the chain locally.

## Current limitations

- No SSO/RBAC enforcement yet (header-based dev tenant).
- No image (DICOM) handling — RadioPad is a *reporting* layer.
- Mobile is read/acknowledge only.

## Source-of-truth docs

- [../INDEX.md](../INDEX.md) — full map.
- [../03-architecture/architecture.md](../03-architecture/architecture.md).
- [../04-security/security-architecture.md](../04-security/security-architecture.md).
- [../02-design/design.md](../02-design/design.md).
- [../../PRD.md](../../PRD.md), [../../PROGRESS.md](../../PROGRESS.md).
