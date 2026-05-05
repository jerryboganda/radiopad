# Prompt Versioning

**Status:** Draft  ·  **Owner:** Engineering + Clinical  ·  **Last Updated:** 2026-05-04

Production prompts are treated like code: versioned, reviewed, evaluated.

## Version format

`<surface>.<purpose>.v<MAJOR>.<MINOR>`

Examples:
- `report.impression.v1.0`
- `report.recommendation.v1.0`
- `report.technique.v1.0`

`MAJOR` bumps for any change that could alter clinical phrasing or safety; `MINOR` for cosmetic / typo changes.

## Storage

Prompts are committed under `docs/05-data-ai/prompt-library.md` and (when wired) loaded by `AiGateway` via a constant map. Inline string-in-code prompts are forbidden once the prompt library is wired.

## Change review

- A prompt PR must include:
  - The diff of the prompt.
  - Updated [prompt-evals.md](../05-data-ai/prompt-evals.md) cases.
  - A note on expected behaviour change.
- Reviewers: at least one Engineering and one Clinical reviewer for `MAJOR` bumps.

## Rollback

- Prompts are content-addressable in the audit log (`AiRequest.details.promptVersion`).
- Rolling back a prompt is a config flip — no code change required once the loader exists.
- Rollback decision documented in the next iteration entry of `PROGRESS.md`.

## Evaluation requirements

- Each `MAJOR` bump runs the full prompt eval suite.
- A `MAJOR` cannot ship while the safety eval (PHI block precision/recall) is below 100%.
- Hallucination-rate regressions of > 0.5% absolute block the bump.

## Telemetry

Every AI call records:
- `promptVersion`
- `model`
- `tokens.prompt`
- `tokens.completion`
- `latencyMs`
- `phiClass`

These fields enable post-hoc analysis without storing the prompt or output text in the audit log.
