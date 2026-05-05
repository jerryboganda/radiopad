# Tenant Model

**Status:** Current  ·  **Owner:** Engineering + Product  ·  **Last Updated:** 2026-05-04

## Concept

A **Tenant** is the unit of isolation. Every domain entity (`Report`, `Rulebook`, `ReportTemplate`, `Provider`, `AuditEvent`, `User` membership) carries a `TenantId Guid` and is filtered through `TenantedController.ResolveContextAsync(_db, ct)`.

## Hierarchy

```
Tenant ── 1..n Users (memberships)
       ── 1..n Providers
       ── 1..n Rulebooks (multiple versions)
       ── 1..n ReportTemplates
       ── 1..n Reports
              ── 1..n ReportVersions
              ── 1..n AuditEvents (also tenant-rooted)
```

## Identification

- Header: `X-RadioPad-Tenant: <slug>`. Dev tenant slug = `dev`. Integration test slug = `it`.
- User identification: `X-RadioPad-User: <email>` (v0.1 dev mode). SSO will replace this in Phase 3.
- The resolver creates the tenant + user lazily in dev mode; production binds the tenant from a JWT claim.

## Roles (planned hierarchy)

| Role | Capabilities |
| --- | --- |
| `Owner` | All admin operations; billing; tenant deletion. |
| `Admin` | Manage users, providers, rulebooks, templates. |
| `Radiologist` | Draft, validate, sign, export. |
| `Resident` | Draft and propose; sign-off requires an attending. (Phase 2.) |
| `Auditor` | Read-only access to reports + audit log. |

In v0.1 every authenticated user is treated as a Radiologist; the role enum exists but is not yet enforced.

## Data isolation strategy

- **Logical isolation in shared database**, with `TenantId` on every row.
- Every query passes through `ResolveContextAsync` and filters by tenant.
- Index strategy: composite indexes lead with `TenantId`.
- Per-tenant providers means each tenant's API keys live behind `ApiKeySecretRef` env vars; no shared secret.

## Tenant-aware queries

Every controller method uses:

```csharp
var (tenant, user) = await ResolveContextAsync(_db, ct);
var report = await _db.Reports.FirstOrDefaultAsync(
    r => r.Id == id && r.TenantId == tenant.Id, ct);
```

Bypassing `ResolveContextAsync` is forbidden and is a review-blocker.

## Billing ownership

- Tenant `Owner` role owns the Stripe subscription (Phase 2+).
- On-prem deployments do not have a billing record — the operator is the implicit owner.
