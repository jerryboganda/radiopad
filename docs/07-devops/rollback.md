# Rollback

**Status:** Current  ·  **Owner:** Ops  ·  **Last Updated:** 2026-05-04

## Application rollback

1. Decide rollback target tag (the previous green `vX.Y.Z`).
2. Pull the corresponding API container image.
3. `docker compose up -d` with the previous tag (or Helm rollback in Phase 2).
4. Verify `/api/health/ready`, run smoke tests.
5. Communicate to customers.

## Database rollback

> **Forward-only.** We do not write `Down()` migrations.

If a migration causes harm:

1. Identify the offending change.
2. Author a **forward-compatible** migration that restores the prior shape.
3. Deploy the new migration.
4. Roll the application back to the prior tag.
5. Schedule a follow-up migration once the application is stable.

## Audit chain after rollback

- An application rollback does not affect the audit chain — events remain.
- A database rollback (e.g. restoring a backup) would discard events; this is a SEV-1 path requiring legal-hold review.

## Feature-flag rollback

- Disable the flag via the configured store (env var / DB).
- Audit `PolicyViolation` if the flag governed a clinical safety boundary.

## Rollback drill

- Quarterly: roll the staging environment back one tag and forward; confirm the audit chain is intact.
