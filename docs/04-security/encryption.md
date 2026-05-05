# Encryption

**Status:** Current (architecture) → Phase-2 (operational)  ·  **Owner:** Security  ·  **Last Updated:** 2026-05-04

## In transit

- TLS 1.2+ between client and reverse proxy.
- TLS recommended between proxy and backend; in-cluster mesh acceptable on hardened networks.
- HSTS planned (`max-age=31536000; includeSubDomains; preload`) at the reverse proxy.

## At rest

- **Database:** customer's choice (PostgreSQL TDE / cloud-managed encryption / file-system encryption). RadioPad does not double-encrypt at the application layer in v0.x.
- **Backups:** must be encrypted at rest by the storage provider; verified during DR drills.
- **Provider API keys:** stored as env vars only; never in the DB.

## Key management

- Application-layer keys are limited to the audit-chain SHA-256 (no secret material — the chain is integrity, not confidentiality).
- TLS certificates are managed by the reverse proxy / cluster ingress (e.g. cert-manager).
- IdP signing keys (Phase 3) come from the IdP's JWKS; we cache and rotate per JWKS.

## Password hashing

- Not applicable — RadioPad does not store user passwords in v0.x or post-Phase-3 (passwords belong to the IdP).

## Sensitive-field protection

- PHI fields are not encrypted at the application layer. Mitigation is:
  - Storage-layer encryption (DB + backup).
  - Tenant isolation.
  - Append-only audit log to detect unauthorised reads (Phase 3 read audits).
- Future option: column-level encryption for `Indication / Findings / Impression` using a tenant-scoped key envelope. Tracked as a Phase 3 ADR.

## Random number generation

- `Guid.NewGuid()` for ids (suitable; not used for security tokens).
- For tokens or nonces: `RandomNumberGenerator.GetBytes` from `System.Security.Cryptography`.
