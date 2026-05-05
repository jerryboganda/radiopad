# Data Classification

**Status:** Current  ·  **Owner:** Security  ·  **Last Updated:** 2026-05-04

| Class | Examples | Storage rules | Logging rules |
| --- | --- | --- | --- |
| **Public** | Documentation, marketing copy, OpenAPI schema | No restriction. | OK to log. |
| **Internal** | Source code, infrastructure config sans secrets | Repo + private CI. | OK to log internally. |
| **Confidential** | Tenant configuration, rulebooks, templates, provider rows (without keys), audit metadata | Tenant-scoped DB rows; encryption at rest by storage layer. | Log identifiers only; never full payloads. |
| **Restricted (Sensitive Personal Data / PHI)** | Patient identifiers, accession numbers, indication / findings / impression text | Tenant-scoped DB rows; encryption at rest by storage layer; never in non-prod environments without de-identification. | **Never log.** Redact aggressively. |
| **Customer Data** | Anything inside a tenant's reports | Treated as Restricted by default. | Same as Restricted. |

## Where each class lives

- Public: `docs/`, `README.md`, `openapi/`.
- Internal: source code, `appsettings*.json` (sans secrets), CI workflows.
- Confidential: tenant rows in `Tenants`, `Providers`, `Rulebooks`, `ReportTemplates`, `AuditEvents`.
- Restricted: `Reports`, `ReportVersions` content, anything in section text.

## Handling rules

- **Restricted data must not appear in:** logs, JSON responses outside the tenant context, screenshots, support tickets, AI provider requests to non-compliant providers, or any external system not bound by a BAA.
- **De-identification before sharing:** required for any export to a non-`PhiApproved` / non-`LocalOnly` provider. The `containsPhi` flag is the contract.
- **Test data:** synthetic only. Real-looking patient names that a reasonable person could associate with a real patient are forbidden.

## Classification change

Reclassifying a column requires:

1. Document the new class here.
2. Update [privacy.md](privacy.md) and [data-retention.md](data-retention.md).
3. Update logging redaction rules.
4. ADR if the change affects architecture or storage.
