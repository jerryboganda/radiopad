# ADR-0003: Append-only audit log with SHA-256 hash chain

- **Status:** Accepted (Iteration 1, 2026-05-02)
- **Decision-makers:** Engineering + Compliance
- **Related code:** `backend/RadioPad.Api/src/RadioPad.Infrastructure/Repositories/Repositories.cs::EfAuditLog`

## Context

Radiology reporting is regulated. We need a tamper-evident record of every clinically meaningful event:

- AI requests and responses (with prompt-version + model + latency).
- Report edits, validation passes, acknowledgement, and exports.
- Provider-policy blocks (PHI rejection, disabled provider).
- Rulebook approval / deprecation.
- Auth events.

The log must withstand operator mistakes ("I just tweaked one row") and detect silent corruption (DB restore from a bad backup, malicious edit).

## Decision

- Single table `audit_events` per tenant. Mutation API: only `IAuditLog.AppendAsync(AuditEvent ev)`. Repository never exposes `Update`/`Delete`.
- Each row carries `IntegrityChain = SHA-256(prevHash || canonicalJson(row))`, where `prevHash` is the previous row's `IntegrityChain` for the same tenant (or empty string for the first event). Canonical JSON is `id|tenantId|(int)action|detailsJson|prevHash`.
- The verifier UI (`/audit/verify`) and external auditors recompute the chain client-side; a mismatch flags the offending event with `.badge.danger`.
- Backups must be Postgres point-in-time (WAL-aware). No `pg_dump --data-only` rewriting of `audit_events`.
- Operators cannot disable audit. The middleware/gateway boundaries throw rather than skip auditing.

## Consequences

- Catching tampering is mechanical, not heuristic.
- Migration of `AuditEvents` requires preserving `IntegrityChain` exactly — never recompute it during restore.
- Query patterns are append/read-only; large tenants must rely on indexed time ranges (`(tenantId, createdAt desc)`).
- A separate retention policy lives outside this ADR; deletion happens by cryptographic shredding of derived data, never by mutating the chain.
