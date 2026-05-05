# Evals

**Status:** Draft  ·  **Owner:** Engineering + Clinical  ·  **Last Updated:** 2026-05-04

Evaluations come in three buckets: **functional**, **safety**, and **quality**.

## Functional evals (deterministic)

| Suite | Source | What it asserts |
| --- | --- | --- |
| Backend integration | `backend/RadioPad.Api/tests/RadioPad.Api.Tests/Integration/` | API contract, tenant isolation, PHI gate, version history. |
| Rulebook golden cases | `rulebooks/_tests/<id>/*.json` | Each rulebook flags / clears the expected findings. |
| Audit chain | `radiopad audit verify` against a seeded dataset | Recomputed SHA-256 chain matches stored values. |
| FHIR export | unit test in `RadioPad.Application.Tests` | Narrative + JSON exports contain all sections. |

CI runs these on every push.

## Safety evals (binary)

| Eval | What it asserts |
| --- | --- |
| **PHI block recall** | A request with `containsPhi: true` to a `Sandbox`/`DeIdentifiedOnly` provider always returns 403 and audits `ProviderBlocked`. Target 100% on the synthetic PHI corpus. |
| **PHI block precision** | A request with `containsPhi: false` to any compliant provider is never blocked. Target 100%. |
| **Audit completeness** | Every state transition writes the matching `AuditAction`. Target 100%. |
| **Tenant escape** | A request with tenant slug A cannot read entity owned by tenant B. Target 100%. |

## AI quality evals (graded)

Defined in [../05-data-ai/ai-quality-rubric.md](../05-data-ai/ai-quality-rubric.md). Run on a corpus of synthetic indications + findings; grade each model output on:

- Accuracy (no fabricated entities).
- Completeness (all required impression sentences).
- Tone (clinical declarative).
- Safety (no diagnostic claims beyond input).
- Format (matches template).

Hallucination rate target < 2%.

## Regression evals

- Re-run all of the above on every push to `main` and on every release tag.
- A failing safety eval blocks the release. A failing quality eval is a P1 ticket but does not block.

## Prompt evals

See [../05-data-ai/prompt-evals.md](../05-data-ai/prompt-evals.md). Each production prompt has at least one positive case and one safety negative case.
