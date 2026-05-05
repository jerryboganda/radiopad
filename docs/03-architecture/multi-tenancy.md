# Multi-Tenancy

**Status:** Current  ·  **Owner:** Engineering + Security  ·  **Last Updated:** 2026-05-04

## Strategy

**Logical isolation in a shared database.** Every tenanted entity carries `TenantId Guid`. Every query filters by it. No row-level security in the DB layer in v0.x — isolation is enforced in application code via `TenantedController.ResolveContextAsync(_db, ct)`.

## Tenant-aware queries

```csharp
var (tenant, user) = await ResolveContextAsync(_db, ct);
var report = await _db.Reports.FirstOrDefaultAsync(
    r => r.Id == id && r.TenantId == tenant.Id, ct);
```

Bypassing `ResolveContextAsync` is a review-blocker.

## Shared vs dedicated resources

| Resource | Shared | Dedicated |
| --- | --- | --- |
| Compute (API pod) | ✅ | Optional Enterprise SKU |
| Database | ✅ | Optional Enterprise SKU |
| Provider API keys | ❌ | ✅ — per-tenant `ApiKeySecretRef` |
| Audit log | Logically per tenant | — |
| Static frontend assets | ✅ | — |

## Adding a new tenanted entity

1. Add `TenantId Guid` column with FK to `Tenants`.
2. Index leading with `TenantId`.
3. Filter every query by tenant id.
4. Add an integration test asserting cross-tenant isolation (request with tenant slug A cannot read entity B).

## Data export / deletion

- Tenant export (Phase 3): `radiopad tenant export --tenant <slug>` — produces a signed zip containing reports, versions, audit log, providers, rulebooks, templates.
- Tenant deletion: requires a 30-day grace period, an explicit `--force-delete` flag, and an Owner-role human approval. After deletion, audit events for the tenant remain in append-only form for compliance retention.

## Billing linkage

- `Tenant.BillingOwnerUserId` (Phase 3) links a tenant to its Stripe customer.
- On-prem deployments have no billing linkage.

## Cross-tenant operations

- There are none in v0.x. A future Enterprise feature ("organisation" rolling up multiple tenants) is out of scope until Phase 4.
