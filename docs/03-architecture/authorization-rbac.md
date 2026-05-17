# Authorization (RBAC)

**Status:** Current foundation  ·  **Owner:** Engineering + Security  ·  **Last Updated:** 2026-05-17

## Current model

RadioPad currently authorizes tenant actions from the resolved tenant-scoped `User.Role`. The enterprise identity foundation adds `GlobalUser` and `TenantMembership`, but `GlobalUser` is not an authorization principal and `TenantMembership` does not replace the tenant-local `User` row yet.

The active backend roles are the stable `UserRole` enum values:

| Role | Value | Current purpose |
| --- | ---: | --- |
| `Radiologist` | 0 | Clinical reporting workflow: draft, validate, sign, export, and run approved validation packs. |
| `ReportingAdmin` | 1 | Reporting operations: providers, rulebook/template governance, lexicon, prompts, and MCP operations where allowed. |
| `MedicalDirector` | 2 | Clinical governance: approvals, validation packs, report sign/addendum, audit/security review, and user lockout workflows. |
| `ComplianceReviewer` | 3 | Audit/security review, SIEM/security checks, and session revocation workflows. |
| `ItAdmin` | 4 | Operational administration: providers, users/devices, security, validation packs, MCP, Copilot, and billing where allowed. |
| `BillingAdmin` | 5 | Billing, marketplace payment operations, provider OAuth token vault, and Copilot billing/admin operations where allowed. |

Do not reorder these enum values; frontend role helpers and persisted data rely on them.

## Permission catalog foundation

The backend now has a code-only permission catalog in `RadioPad.Application.Security`:

- `RbacPermission` enumerates stable permission concepts.
- `PermissionCatalog` maps each permission to a stable dot-form key such as `reports.sign`, `providers.manage`, or `audit.verify`.
- `RolePermissionMap` maps current `UserRole` values to permissions.
- `IPermissionService` / `RolePermissionService` provide default-deny permission decisions from the current tenant-scoped `User.Role`.
- `TenantedController.RequirePermission(...)` is available for endpoint migrations while `RequireRole(...)` remains for compatibility.

This foundation does not add DB-backed custom roles, tenant role assignments, or public API response fields. Existing endpoints keep their current behavior until each controller action is intentionally migrated from role allow-lists to permission checks.

## Current high-level permission families

| Permission family | Examples |
| --- | --- |
| Reports | `reports.read`, `reports.draft`, `reports.edit`, `reports.validate`, `reports.sign`, `reports.export` |
| Rulebooks/templates | `rulebooks.manage`, `rulebooks.approve`, `templates.manage`, `templates.approve` |
| Providers | `providers.read`, `providers.manage` |
| Audit/security | `audit.read`, `audit.verify`, `audit.export`, `security.manage` |
| Users/sessions | `users.read`, `users.manage`, `users.revoke_sessions` |
| Billing | `billing.read`, `billing.manage` |
| Validation packs | `validation_packs.read`, `validation_packs.manage`, `validation_packs.run` |
| MCP tools | `mcp_tools.invoke`, `mcp_tools.manage` |

## Migration rules

- Default-deny applies in the permission service: a role has only permissions explicitly mapped in `RolePermissionMap`.
- `User.Role` remains authoritative for this phase.
- Endpoint migrations must preserve existing `403` response compatibility unless the behavior change is documented and tested.
- Object-level ABAC checks are separate from RBAC and remain a later phase.
- Tenant membership, global identity, MFA freshness, device trust, workflow state, and PHI/provider compliance must be layered as ABAC checks before enterprise GA.

## Deferred custom RBAC

DB-backed custom roles, role assignments, tenant-specific permission overrides, and scoped role assignments remain deferred until the permission service is proven across existing endpoints. Those changes require a separate data model/API design pass and migration strategy.
