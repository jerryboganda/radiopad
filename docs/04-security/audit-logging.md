# Audit Logging

**Status:** Current  ·  **Owner:** Engineering + Security  ·  **Last Updated:** 2026-05-04  ·  **Source of Truth:** [adr/ADR-0003-audit-chain.md](../03-architecture/adr/ADR-0003-audit-chain.md)

## Goals

- Every clinically or operationally significant action is recorded.
- The log is **append-only** and **integrity-chained** (SHA-256).
- The log is **verifiable offline** by an operator (`radiopad audit verify`).
- The log **never contains PHI or secrets**.

## Schema

| Field | Type | Notes |
| --- | --- | --- |
| `Id` | `Guid` | Primary key. |
| `TenantId` | `Guid` | Indexed. |
| `UserId` | `Guid?` | Author when applicable. |
| `Action` | `int` | `AuditAction` enum (see below). |
| `DetailsJson` | `string` | Action-specific payload; never PHI. |
| `CreatedAt` | `DateTimeOffset` | UTC. |
| `PrevHash` | `string` | Previous row's `IntegrityChain`. |
| `IntegrityChain` | `string` | `sha256("{id}|{tenantId}|{(int)action}|{detailsJson}|{prevHash}")`. |

## `AuditAction` enum

```
AiRequest = 0
AiResponse = 1
ReportEdited = 2
ReportExported = 3
ReportAcknowledged = 4
ProviderBlocked = 5
RulebookApproved = 6
RulebookDeprecated = 7
UserLogin = 8
PolicyViolation = 9
```

Actions are stable — once allocated a value never changes. New actions append.

## Writing

Only `IAuditLog.AppendAsync` writes. The implementation:

1. Reads the previous row's `IntegrityChain` for the tenant (or empty for the first event).
2. Stamps `Id`, `CreatedAt`, `PrevHash`.
3. Computes `IntegrityChain`.
4. Inserts.

Concurrency is handled by a per-tenant lock during write. There is no UPDATE or DELETE path.

## Reading

- `GET /api/audit` streams events for the active tenant in chronological order.
- The CLI `radiopad audit export` writes JSON-Lines to disk.

## Verifying

`radiopad audit verify` recomputes the chain locally and exits non-zero on the first mismatch, printing the offending event id. This is **the** authoritative integrity check.

## Detail payload conventions

- Use stable, machine-readable keys (`reportId`, `provider`, `phi`, `findingCounts`).
- Never include section text, prompts, or completions.
- Hashes (SHA-256 hex prefix) are acceptable for de-duplication / correlation.

## What we do **not** audit

- Read access to reports (Phase 3 will add `ReportRead`).
- AI provider response text (we audit metadata only).
- Search queries (we audit query hashes once search lands in Phase 2).
