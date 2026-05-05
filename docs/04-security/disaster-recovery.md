# Disaster Recovery

**Status:** Draft  ·  **Owner:** Ops  ·  **Last Updated:** 2026-05-04

## Targets

| Metric | Hosted SKU | On-prem |
| --- | --- | --- |
| **RTO** (recovery time objective) | 4 hours | Customer-defined |
| **RPO** (recovery point objective) | 15 minutes | Customer-defined |

## Backups

- **Database:** continuous WAL archiving + daily base backup. Retention 35 days, encrypted at rest, geo-redundant for hosted.
- **Configuration:** infrastructure-as-code (Terraform planned) committed to source control.
- **Audit log:** part of the database backup; verified by `radiopad audit verify` after restore.
- **Provider configurations:** part of the database backup; provider keys are env-var only and re-injected at deploy time.

## Restore process

1. Provision a new environment from infrastructure-as-code.
2. Restore the latest base backup.
3. Replay WAL up to the desired recovery point.
4. Re-inject secrets from the secret manager.
5. Run `radiopad audit verify --tenant <slug>` for each tenant.
6. Run smoke tests (login, list reports, validate, ask AI mock, export).
7. Repoint DNS / load balancer to the new environment.

## DR test

- Cadence: quarterly for hosted SKU; annual for on-prem deployments.
- Test scope: full restore from backup into an isolated environment; data integrity check; latency check.
- Outcome captured in [business-continuity.md](business-continuity.md).

## Failure modes considered

- DB primary loss → restore from backup; replica promotion if replicas exist.
- Region outage → DNS failover to a secondary region (Phase 3 hosted feature).
- Cloud account compromise → key rotation + re-provision from IaC.
- Audit chain mismatch → SEV-1 path; restore previous good snapshot under legal hold; investigate.

## Roles

- Primary IC: on-call engineer.
- Secondary: Ops Lead.
- Decision authority for "promote from backup": Engineering Lead.
