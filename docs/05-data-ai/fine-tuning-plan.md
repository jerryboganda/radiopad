# Fine-Tuning Plan

**Status:** Planned (Phase 4)  ·  **Owner:** AI  ·  **Last Updated:** 2026-05-04

## Stance

RadioPad does **not** fine-tune models on PHI in v0.x or Phase 2/3. We rely on prompt engineering + RAG. Fine-tuning is considered only for Phase 4 and only on synthetic / consented data.

## When fine-tuning would be considered

- A consistent, measurable quality gap that prompt engineering cannot close.
- Customer demand for a specialised modality model (e.g. mammography).
- Availability of consented or fully synthetic training data.

## Data requirements

- **No real PHI.** Period.
- Consented case banks from clinical partners under a documented research agreement, OR
- Fully synthetic data generated and reviewed.
- Per-source provenance recorded.

## Process

1. Define the quality gap with eval data.
2. Acquire training set with documented consent / provenance.
3. Build de-identification pipeline + audit.
4. Train on the chosen base (likely an open weights model running on infrastructure we control).
5. Evaluate against the safety + accuracy + tone rubric.
6. Compare against the baseline (prompt + RAG) — only ship if the win is meaningful.

## Safety review

- Independent review of the fine-tuned weights against the same safety eval set as production.
- Documented fail-closed behaviours (refusal patterns, format).
- Model card published per [model-card.md](model-card.md).

## Hosting

- Self-hosted on infrastructure we control.
- Provider compliance class assigned: `LocalOnly` if private to the tenant; `PhiApproved` if hosted with a BAA in place.

## Out of scope

- Fine-tuning a third-party hosted model on RadioPad data.
- Storing patient data outside the customer's environment for training purposes.
