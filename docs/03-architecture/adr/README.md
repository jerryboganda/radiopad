# Architectural Decision Records

**Status:** Current  ·  **Owner:** Engineering  ·  **Last Updated:** 2026-05-04

ADRs capture significant decisions and their context. We follow [adr.github.io](https://adr.github.io/) conventions.

## Index

- [ADR-0001 — Initial architecture baseline](ADR-0001-stack.md)
- [ADR-0002 — Design system lock](ADR-0002-design-lock.md)
- [ADR-0003 — Audit chain (SHA-256, append-only)](ADR-0003-audit-chain.md)
- [0001-initial-architecture-baseline.md](0001-initial-architecture-baseline.md) — generated baseline (mirrors ADR-0001 with the canonical numbering format from the doc generator).

## When to write an ADR

- Cross-layer architectural change.
- Adding or removing a service / library / framework.
- Changing a security boundary (PHI policy, audit chain, tenant isolation).
- Approving a new AI provider class.

## How to write an ADR

1. Copy the template below into a new file: `NNNN-<short-slug>.md`. NNNN is the next free number.
2. Fill in **Context**, **Decision**, **Consequences**.
3. Submit a PR with the `human-review-required` label.
4. Once merged the ADR is **immutable** — supersede with a new ADR rather than edit.

## Status lifecycle

- `Proposed` → `Accepted` → optionally `Deprecated` (superseded by ADR-X) or `Rejected`.
- A PR can land an ADR at `Proposed`; promotion to `Accepted` requires the human-review checklist.

## Template

```md
# ADR NNNN: <Title>

## Status
Proposed | Accepted | Deprecated by ADR-XYZ | Rejected

## Context

## Decision

## Consequences
- Positive
- Negative
- Open questions

## Alternatives Considered

## Follow-up Actions
```
