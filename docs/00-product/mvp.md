# MVP Definition

**Status:** Current  ·  **Owner:** Product  ·  **Last Updated:** 2026-05-04

## MVP goal

Demonstrate that an attending radiologist can sign a structured chest-CT report in RadioPad — including AI-assisted impression and rulebook validation — faster and more safely than in their current dictation-driven workflow.

## MVP feature set (v0.1.x)

- Locked Open Design UI on web + desktop.
- Five seed modalities with rulebooks + templates.
- AI gateway with Mock provider (default), Anthropic / Ollama optional, PHI policy enforced.
- Draft → Validated → Acknowledged → Exported state machine.
- FHIR text export.
- Append-only audit log + `radiopad audit verify`.
- Server-side report list pagination and search.
- Validation panel grouped by severity.
- Report version history (server snapshot + read API).
- CLI: login, daemon status, rulebook (validate/test/approve), report (list/get/validate/export), generate, audit (export/verify), provider (list/test).

## Excluded from MVP

- SSO / OIDC.
- Multi-rad sign-off and addendum workflow.
- Bidirectional FHIR.
- RAG over priors.
- Voice dictation.
- Mobile editing (mobile is read/acknowledge only).

## MVP success criteria

| Criterion | Target |
| --- | --- |
| Time-to-sign first draft (chest CT) | < 5 minutes for a trained radiologist on the test corpus. |
| Validation findings caught before sign-off | ≥ 95% of seeded defects in the golden suite. |
| Zero PHI leakage to non-compliant providers | Binary: pass. |
| Audit verification | 100% of MVP reports verifiable. |
| Pilot tenants | 3 tenants × 100 signed reports each. |

## Post-MVP backlog

See [../../ROADMAP.md](../../ROADMAP.md) Phases 2–4.
