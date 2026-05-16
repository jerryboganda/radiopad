---
applyTo: "**"
---

# Architecture instructions

- Strict tech stack: Next.js 16 / ASP.NET Core 8 / Tauri 2 / Capacitor 6 / .NET 8 CLI. Do not introduce other frameworks or ORMs.
- Backend layers: `RadioPad.Domain` (entities, enums) → `RadioPad.Application` (services, DTOs) → `RadioPad.Validation` (rule engine) → `RadioPad.Infrastructure` (EF Core, audit chain) → `RadioPad.Api` (controllers, middleware). Never reverse the dependency direction.
- All tenanted queries must filter through the `(tenant, user)` tuple returned by `TenantedController.ResolveContextAsync(_db, ct)`.
- Audit log is **append-only**. Use `IAuditLog.AppendAsync`. Never UPDATE/DELETE rows in `AuditEvents`. The SHA-256 chain is `sha256("{id}|{tenantId}|{(int)action}|{detailsJson}|{prevHash}")`.
- AI gateway is the only place that may talk to external model providers. PHI requests must be blocked unless `ProviderComplianceClass` is `PhiApproved` or `LocalOnly`. The gateway audits `AuditAction.ProviderBlocked` before rethrowing `ProviderPolicyException`.
- HTTP API conventions: REST + JSON, camelCase, `JsonIgnoreCondition.WhenWritingNull`, RFC-7807 problem details for errors, `X-Total-Count` for paginated lists, `X-Request-Id` correlation header.
- Frontend goes through `frontend/lib/api.ts` only — never call `fetch` from a page.
- New design tokens or component classes require an update to both `frontend/app/globals.css` and `docs/02-design/design.md` in the same change.
