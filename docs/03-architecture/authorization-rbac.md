# Authorization (RBAC)

**Status:** Current foundation  Â·  **Owner:** Engineering + Security  Â·  **Last Updated:** 2026-05-17

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

## Role-to-permission matrix

The matrix below is the current code-backed `RolePermissionMap`. It is the
source for endpoint migration planning; where an endpoint has not yet moved to
`RequirePermission(...)`, keep the documented current role allow-list in sync.

| Permission key | Radiologist | ReportingAdmin | MedicalDirector | ComplianceReviewer | ItAdmin | BillingAdmin |
| --- | :---: | :---: | :---: | :---: | :---: | :---: |
| `reports.read` | Y | Y | Y | Y | Y |  |
| `reports.draft` | Y | Y | Y |  | Y |  |
| `reports.edit` | Y | Y | Y |  | Y |  |
| `reports.validate` | Y | Y | Y |  | Y |  |
| `reports.sign` | Y |  | Y |  |  |  |
| `reports.export` | Y | Y | Y |  | Y |  |
| `rulebooks.read` | Y | Y | Y | Y | Y |  |
| `rulebooks.manage` |  | Y | Y |  | Y |  |
| `rulebooks.approve` |  | Y | Y |  | Y |  |
| `templates.read` | Y | Y | Y | Y | Y |  |
| `templates.manage` |  | Y | Y |  | Y |  |
| `templates.approve` |  | Y | Y |  | Y |  |
| `providers.read` |  | Y | Y |  | Y | Y |
| `providers.manage` |  | Y |  |  | Y |  |
| `audit.read` | Y | Y | Y | Y | Y | Y |
| `audit.verify` |  |  | Y | Y | Y |  |
| `audit.export` |  |  | Y | Y | Y |  |
| `users.read` | Y | Y | Y | Y | Y | Y |
| `users.manage` |  |  | Y |  | Y |  |
| `users.revoke_sessions` |  |  | Y | Y | Y |  |
| `billing.read` | Y | Y | Y | Y | Y | Y |
| `billing.manage` |  |  | Y |  | Y | Y |
| `security.manage` |  |  | Y | Y | Y |  |
| `validation_packs.read` | Y | Y | Y | Y | Y |  |
| `validation_packs.manage` |  |  | Y |  | Y |  |
| `validation_packs.run` | Y | Y | Y |  | Y |  |
| `mcp_tools.invoke` | Y | Y | Y |  | Y |  |
| `mcp_tools.manage` |  | Y | Y |  | Y |  |

## Endpoint permission matrix (migration target)

| Endpoint family / action | Permission to enforce | Roles from current map | Step-up MFA? |
| --- | --- | --- | --- |
| `GET /api/reports*` | `reports.read` | Radiologist, ReportingAdmin, MedicalDirector, ComplianceReviewer, ItAdmin | No |
| `POST /api/reports`, draft creation | `reports.draft` | Radiologist, ReportingAdmin, MedicalDirector, ItAdmin | No |
| `PATCH /api/reports/{id}`, addendum | `reports.edit` | Radiologist, ReportingAdmin, MedicalDirector, ItAdmin | Addendum: Yes |
| `POST /api/reports/{id}/validate` | `reports.validate` | Radiologist, ReportingAdmin, MedicalDirector, ItAdmin | No |
| `POST /api/reports/{id}/sign` | `reports.sign` | Radiologist, MedicalDirector | Yes |
| Final report export endpoints | `reports.export` | Radiologist, ReportingAdmin, MedicalDirector, ItAdmin | Yes for PHI export |
| Rulebook/template create/update | `rulebooks.manage` / `templates.manage` | ReportingAdmin, MedicalDirector, ItAdmin | Yes |
| Rulebook/template approve/deprecate | `rulebooks.approve` / `templates.approve` | ReportingAdmin, MedicalDirector, ItAdmin | Yes |
| Provider configuration and OAuth refresh-token vault | `providers.manage` | ReportingAdmin, ItAdmin | Yes |
| Audit read | `audit.read` | All current roles | No |
| Audit verify/export, SIEM export | `audit.verify` / `audit.export` | MedicalDirector, ComplianceReviewer, ItAdmin | Yes |
| User lock/unlock/manage | `users.manage` | MedicalDirector, ItAdmin | Yes |
| Session revocation | `users.revoke_sessions` | MedicalDirector, ComplianceReviewer, ItAdmin | Yes |
| Billing checkout/portal/refunds/marketplace payment ops | `billing.manage` | MedicalDirector, ItAdmin, BillingAdmin | Yes |
| Tenant security settings, KMS verify, security webhooks | `security.manage` | MedicalDirector, ComplianceReviewer, ItAdmin | Yes |
| Validation pack manage/approve | `validation_packs.manage` | MedicalDirector, ItAdmin | Yes |
| Validation pack run | `validation_packs.run` | Radiologist, ReportingAdmin, MedicalDirector, ItAdmin | No |
| MCP invoke/manage | `mcp_tools.invoke` / `mcp_tools.manage` | Invoke: Radiologist, ReportingAdmin, MedicalDirector, ItAdmin. Manage: ReportingAdmin, MedicalDirector, ItAdmin. | Manage: Yes |

Step-up MFA is a production requirement for the rows marked **Yes**. It is not
fully enforced by all current endpoints yet; until implemented, treat those
entries as required migration work, not evidence that the backend already
checks MFA freshness.

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
