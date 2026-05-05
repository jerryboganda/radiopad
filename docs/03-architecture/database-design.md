# Database Design

**Status:** Current  ·  **Owner:** Engineering  ·  **Last Updated:** 2026-05-04

## Engines

- **Dev:** SQLite (`Data Source=radiopad.dev.db`).
- **Prod:** PostgreSQL 14+ via Npgsql `8.0.8`.
- Selection: connection-string sniff in `Program.cs` (`Data Source=` → SQLite; otherwise Npgsql).

## Entities (high level)

| Entity | PK | Tenant scoped? | Notes |
| --- | --- | --- | --- |
| `Tenant` | `Id Guid` | n/a | Slug + display name. |
| `User` | `Id Guid` | yes (membership) | Email + role; v0.1 minimal. |
| `Report` | `Id Guid` | yes | Status enum, modality, body part, accession, sections (`Indication`, `Technique`, `Comparison`, `Findings`, `Impression`, `Recommendations`), `AiHighlightsJson`, `RulebookId`. |
| `ReportVersion` | `Id Guid` | yes (via Report) | `Sequence`, `AuthorUserId`, `Action`, `RulebookId`, `SnapshotJson`. |
| `Rulebook` | `Id Guid` | yes | `RulebookId` (snake_case stable), `Version` semver, `Status` (draft/approved/deprecated), YAML. |
| `ReportTemplate` | `Id Guid` | yes | Modality + body part + sections JSON. |
| `Provider` | `Id Guid` | yes | Adapter, compliance class, `ApiKeySecretRef`. |
| `AuditEvent` | `Id Guid` | yes | `Action` enum, `DetailsJson`, `IntegrityChain` SHA-256, `PrevHash`. **Append-only.** |

## Indexes

- `Reports (TenantId, UpdatedAt DESC)`.
- `Reports (TenantId, AccessionNumber)` unique.
- `ReportVersions (ReportId, Sequence)` unique.
- `Rulebooks (TenantId, RulebookId, Version)` unique.
- `AuditEvents (TenantId, CreatedAt)`; never DELETE/UPDATE rows.

## Constraints

- `Reports.Status` ∈ `Draft | Validated | Acknowledged | Exported`.
- `Providers.ComplianceClass` ∈ `Blocked | Sandbox | DeIdentifiedOnly | PhiApproved | LocalOnly`.
- `AuditEvents.IntegrityChain` is required and never nullable; computed as `sha256("{id}|{tenantId}|{(int)action}|{detailsJson}|{prevHash}")`.

## Multi-tenancy

- Logical isolation; every tenanted query filters by `tenant.Id` resolved from `ResolveContextAsync`.
- Adding a new tenanted table requires:
  1. `TenantId Guid` column with FK to `Tenants`.
  2. Index leading with `TenantId`.
  3. An integration test asserting cross-tenant isolation.

## Data lifecycle

- Reports retained for the customer-defined retention window (default ∞ until billing decisions; see [../04-security/data-retention.md](../04-security/data-retention.md)).
- Audit events retained for the lifetime of the tenant + 7 years for compliance.
- Soft-delete is **not** used today; deletion would require an audit event and an ADR.
