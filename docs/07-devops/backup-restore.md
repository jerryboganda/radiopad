# Backup & Restore

**Status:** Current (architecture)  ·  **Owner:** Ops  ·  **Last Updated:** 2026-05-04

## What we back up

| Item | Frequency | Retention | Encryption | Where |
| --- | --- | --- | --- | --- |
| Postgres base backup | daily | 35 days | yes (storage layer) | managed DB |
| Postgres WAL | continuous | 35 days | yes | managed DB |
| Configuration (Terraform) | per change | git history | n/a | source control |
| Secrets | n/a — managed by secret manager; rotation policy applies | — | yes | secret manager |
| Audit log (logical export) | weekly per tenant (planned) | 7 years | yes | object storage |
| Container images | per release | 90 days | n/a | registry |

## What we do **not** back up

- Local dev databases.
- CI ephemeral databases.
- AI provider responses (we audit metadata, not responses).

## Restore procedures

### Postgres point-in-time

1. Identify target timestamp `T`.
2. Provision a fresh DB instance.
3. Restore latest base backup before `T`; replay WAL up to `T`.
4. Smoke-check schema with EF migrations marker.
5. Run `radiopad audit verify` per tenant.
6. Repoint the API to the restored DB.

### Audit-only export restore

1. Locate the JSON-Lines export.
2. Reload into an isolated DB for analysis.
3. Verify chain hashes locally.
4. Reconcile against the live audit log.

## Verification

- Quarterly DR drill restores the staging DB into an isolated environment, runs smoke + audit verify, and records timing.
- Backup validation: nightly job (planned) ensures the latest base backup is restorable.

## Customer-visible export

- `radiopad tenant export --tenant <slug>` (Phase 3) produces a signed zip with reports, versions, audit log, providers, rulebooks, templates.
- Signed with a release key; verifiable offline.
