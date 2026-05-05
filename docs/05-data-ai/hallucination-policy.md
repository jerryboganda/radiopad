# Hallucination Policy

**Status:** Current  ·  **Owner:** AI + Clinical  ·  **Last Updated:** 2026-05-04

## Definition

A **hallucination** is any AI-generated statement that:

- Asserts a clinical fact not supported by the input findings, OR
- Invents a measurement, side, or laterality not present in the input, OR
- Cites a guideline or value that does not exist.

## Mitigations in product

- System prompt explicitly forbids unsupported claims.
- AI text wears `.ai-mark` (purple family) until the radiologist acknowledges.
- Validation findings flag missing-but-required content; conversely, the rulebook does **not** flag presence of extra content (radiologist must catch hallucinations during review).
- Acknowledge requires `Validated` status; the radiologist physically passes through review.

## Detection

- Pre-release: golden eval cases with known-tagged "do not invent" claims.
- Post-release: customer feedback channel + sampled review.
- Phase 3: an automated post-hoc checker that compares draft claims to findings using rule-based extractors.

## Response when a hallucination is reported

1. Acknowledge to the customer within 1 business day.
2. Reproduce in our eval harness.
3. Add the case to the regression suite.
4. Patch via prompt change OR provider switch (per the model policy).
5. Communicate the fix in the next CHANGELOG entry under `### Fixed`.

## Hard rules

- No silent recovery: if the AI emits a hallucination and the user signs the report, the **user owns the report** but RadioPad still records the AI suggestion in the audit log.
- No automated correction without sign-off.
- No "AI confidence score" rendered as if it were a clinical confidence — that conflation is forbidden in the UI copy.
