# Authorization (RBAC)

**Status:** Planned (Phase 3)  ·  **Owner:** Engineering + Security  ·  **Last Updated:** 2026-05-04

## Roles

| Role | Description |
| --- | --- |
| `Owner` | Tenant owner; billing; can delete the tenant. |
| `Admin` | Manages users, providers, rulebooks, templates. |
| `Radiologist` | Drafts, validates, signs, exports. |
| `Resident` | Drafts and proposes; sign-off requires an attending. |
| `Auditor` | Read-only access to reports + audit. |

In v0.1 every authenticated user is treated as a Radiologist.

## Permissions

| Permission | Owner | Admin | Radiologist | Resident | Auditor |
| --- | --- | --- | --- | --- | --- |
| `tenant.delete` | ✅ | ❌ | ❌ | ❌ | ❌ |
| `billing.read` | ✅ | ❌ | ❌ | ❌ | ❌ |
| `users.manage` | ✅ | ✅ | ❌ | ❌ | ❌ |
| `providers.manage` | ✅ | ✅ | ❌ | ❌ | ❌ |
| `rulebooks.manage` | ✅ | ✅ | ❌ | ❌ | ❌ |
| `rulebooks.approve` | ✅ | ✅ | ❌ | ❌ | ❌ |
| `templates.manage` | ✅ | ✅ | ✅ (own) | ❌ | ❌ |
| `reports.draft` | ✅ | ✅ | ✅ | ✅ | ❌ |
| `reports.validate` | ✅ | ✅ | ✅ | ✅ | ❌ |
| `reports.sign` | ✅ | ✅ | ✅ | ❌ | ❌ |
| `reports.export` | ✅ | ✅ | ✅ | ❌ | ✅ |
| `audit.read` | ✅ | ✅ | ✅ (own) | ✅ (own) | ✅ |
| `audit.export` | ✅ | ✅ | ❌ | ❌ | ✅ |

## Policy matrix

- Every controller method is annotated with one or more permissions (Phase 3).
- The default deny applies — missing annotation means the role cannot use the endpoint.
- Object-level access control: a Resident can only read their own drafts; an Auditor can read all reports in their tenant.

## Admin overrides

- `Owner` and `Admin` roles can read but cannot sign reports they did not draft (clinical safety).
- Reading another user's draft is audited as `ReportEdited` viewer-style (Phase 3 `ReportRead` action).

## Object-level rules

- `Report.AuthorUserId` is the radiologist who drafted it.
- Sign-off may only be performed by the `AuthorUserId` (Phase 3 will allow attending sign-off for resident drafts via an explicit `co-sign` permission).
