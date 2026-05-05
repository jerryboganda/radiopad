# Data Labelling Guide

**Status:** Skeleton (no production labelling pipeline in v0.x)  ·  **Owner:** AI  ·  **Last Updated:** 2026-05-04

> Documents how we label eval cases today and how a labelling pipeline would be set up if Phase 4 fine-tuning becomes a reality.

## Today — eval case labels

- Each eval case is hand-authored by an engineer + clinical advisor.
- Label set per case:
  - `expectedClaims` — claims the draft must include.
  - `forbiddenClaims` — claims the draft must not include.
  - `tone` — `ok` / `flagged`.
  - `safety_violations` — list of categories that the response must not exhibit.
- Reviewer must initial the case file.

## If a labelling pipeline is needed (Phase 4)

### Roles

- **Author:** writes the case scenario.
- **Labeller:** assigns labels to model output.
- **Adjudicator:** resolves disagreements.

### Inter-rater agreement

- Target: Cohen's κ ≥ 0.8 on a sample.
- Disagreements above threshold trigger a labelling-guide update.

### Tooling

- Label Studio or similar; self-hosted in the customer's environment for any data sourced from clinical partners.
- Output stored as JSON-Lines with case id, labels, labeller, timestamp.

### Privacy

- Labelled data must be free of real PHI. If derived from real cases, full de-identification with manual review is required first.
- Labellers are bound by the same confidentiality agreements as engineers.

## Quality gates

- A labelling round closes only when:
  - Every case has at least two labellers.
  - Disagreements resolved.
  - Sample κ above threshold.
- Updates to the labelling guide trigger a re-label of a sample to verify the change did not regress agreement.
