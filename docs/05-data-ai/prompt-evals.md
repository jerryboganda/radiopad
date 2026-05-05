# Prompt Evaluations

**Status:** Draft skeleton  ·  **Owner:** AI  ·  **Last Updated:** 2026-05-04

## Goals

- Catch quality regressions before users see them.
- Enforce the safety contract (no auto-sign, no PHI re-disclosure, no unsupported diagnoses).
- Provide objective evidence behind release sign-off.

## Eval categories

| Category | What it checks | Pass threshold |
| --- | --- | --- |
| **Accuracy** | Draft matches expected impression on golden cases. | ≥ 70% acceptance score on the evaluator rubric. |
| **Completeness** | All findings of significance reflected. | ≥ 80% recall on tagged findings. |
| **Tone** | Concise, professional, no slang. | ≥ 95% pass on rubric. |
| **Safety — refusal** | Refuses to sign / auto-finalize. | 100%. |
| **Safety — PHI** | Does not re-disclose PHI verbatim that was redacted upstream. | 100%. |
| **Format** | Matches the documented output shape. | 100%. |
| **Latency** | p95 within budget per provider. | per [non-functional requirements](../00-product/nfr.md). |

## Golden cases

- Stored under `evals/<prompt-id>/<case-id>.json`.
- Each case: `{ inputs, expectedShape, expectedClaims, forbiddenClaims, evaluator }`.
- Synthetic only — no real PHI.

## Evaluators

- **Rule-based** — JSON-schema validation, presence of forbidden / required tokens.
- **LLM-as-judge** — for accuracy & tone, using a separate model with a strict rubric.
- **Human review** — for ambiguous cases; sampled per release.

## Cadence

- Run on every PR that touches a prompt or a provider adapter.
- Run nightly against each provider with a smaller smoke set.
- Full sweep on release branches.

## Reporting

- Eval results stored as a CI artifact with pass/fail per case.
- Trend tracked in a planned dashboard.
- Regression on a Safety category is a release blocker.
