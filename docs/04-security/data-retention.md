# Data Retention

**Status:** Draft  ·  **Owner:** Security + Legal  ·  **Last Updated:** 2026-05-04

| Data | Default retention | Configurable | Deletion behaviour |
| --- | --- | --- | --- |
| Active reports | Indefinite (tenant decides) | Yes (per-tenant policy) | Soft-archive after policy period; hard-delete only on tenant request. |
| `ReportVersion` snapshots | Same as parent report | Tied to report | Deleted with report. |
| Audit log | Lifetime of tenant + 7 years (compliance default) | Yes (≥ 7 years) | Append-only; only physical purge after retention. |
| Provider configurations | Lifetime of tenant | n/a | Deleted on tenant deletion. |
| Rulebooks & templates | Lifetime of tenant | n/a | Deleted on tenant deletion. |
| Logs | 30 days (hosted) | Yes | Auto-rotated; never contains PHI. |
| Backups | 35 days (rolling) | Yes | Encrypted at rest; geo-redundant for hosted SKU. |
| Telemetry (planned) | 13 months | Yes (or off) | Aggregated; no individual identifiers after 30 days. |

## Tenant deletion

1. Owner initiates deletion via `radiopad tenant delete --tenant <slug>`.
2. Tenant enters a 30-day grace period; status is read-only.
3. Owner can re-activate within the grace period.
4. After grace period, hard-delete:
   - Reports / versions / rulebooks / templates / providers → deleted.
   - Audit log → retained per the configured retention; tenant marked `deleted_at`.

## Legal hold

- A legal hold suspends deletion for the affected tenant.
- Implemented by setting `Tenant.LegalHold = true`; deletion paths refuse to proceed.
- Documented per case in the security tracker.

## Customer-defined policies

- Each tenant can override defaults (within the floor: audit ≥ 7 years).
- Policy changes take effect on the next nightly retention sweep.
