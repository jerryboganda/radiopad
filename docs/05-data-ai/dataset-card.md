# Dataset Card

**Status:** Skeleton  ·  **Owner:** AI  ·  **Last Updated:** 2026-05-04

> RadioPad does not maintain a training dataset in v0.x. This card documents the **eval datasets** under `evals/` that drive prompt regression testing.

## Eval dataset: `evals/report.impression/`

- **Purpose:** Regression-test the impression draft prompt across modalities and body parts.
- **Source:** Synthetic, written by the engineering team with clinical advisor review.
- **Size:** N cases (stored in repo).
- **Fields per case:** `inputs.modality`, `inputs.bodyPart`, `inputs.indication`, `inputs.findings`, `expectedClaims`, `forbiddenClaims`, `evaluator`.
- **Sensitivity:** No real PHI. Names and dates are placeholders.
- **Maintenance:** Add a case for every reported quality regression.
- **Licence:** Same as repository (Apache 2.0).

## Eval dataset: `evals/report.recommendation/`

- Same shape as impression set but tied to recommendation outputs.

## Eval dataset: `evals/safety/`

- **Purpose:** Verify refusal of auto-sign / PHI re-disclosure / unsupported diagnoses.
- **Cases:** Adversarial prompts that try to elicit each forbidden behaviour.
- **Pass criterion:** 100% — any failure is a release blocker.

## Future training datasets

- Out of scope until Phase 4. If introduced, each will have its own dataset card with consent provenance and de-identification documentation.

## What is NOT a dataset

- Production reports are not used as training data.
- Audit log content is not exported as a dataset.
- AI provider responses are not retained beyond the audit metadata.
