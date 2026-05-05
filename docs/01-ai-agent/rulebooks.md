# Rulebooks (AI / Domain)

**Status:** Current  ·  **Owner:** Clinical + Engineering  ·  **Last Updated:** 2026-05-04

> Clinical rulebook authoring (YAML schema, golden cases) lives in [../05-clinical/rulebook-authoring.md](../05-clinical/rulebook-authoring.md). This file documents *agent* rules — the policies AI agents must respect when generating, suggesting, or validating output.

## Business rules

- Every report belongs to exactly one tenant.
- A report transitions `Draft → Validated → Acknowledged → Exported`. No skipping; no reverse without an addendum (Phase 2).
- Validation is sourced from a versioned rulebook; the rulebook id is captured on the report.
- `Acknowledged` requires `Validated`. `Validated` requires no Blocker findings.

## Validation rules (engine-level)

- Findings have severity `Blocker | Warning | Info` and a stable `ruleId` (kebab-case-ish, e.g. `chest_ct.imp.001`).
- Rules can target any section (`indication`, `technique`, `comparison`, `findings`, `impression`, `recommendations`).
- Rules must be deterministic given the same report content + rulebook version.

## Domain-specific rules

- Laterality conflicts (left/right disagreements) are Blockers.
- Missing impression on a `Validated` candidate is a Blocker.
- Modality / body-part mismatch is a Warning at minimum.
- New domain rules require a clinical review and a golden case.

## Prompting rules (when AI assists)

- The system prompt MUST instruct the model to write neutral, declarative radiology language and to never assert findings beyond the structured input.
- The user prompt MUST include the report's `indication`, `technique`, and `findings`. It MUST NOT include the patient's name, MRN, or any direct identifier.
- The model MUST be told the output will be wrapped in `.ai-mark` and reviewed by a human.

## AI output rules

- All AI text wears `.ai-mark` (purple family) until the radiologist accepts.
- AI text MUST NOT include hedging like "It looks like" — use clinical declarative voice.
- If the model refuses or returns a safety message, surface it as `Info` not `Blocker`.
- Cost / token tracking MUST be recorded via `AuditAction.AiResponse` details.

## Safety rules

- PHI requests (`containsPhi: true`) are blocked unless `ProviderComplianceClass ∈ {PhiApproved, LocalOnly}`.
- Blocks audit `ProviderBlocked` *before* rethrow.
- If a model hallucinates an entity not in the input, the radiologist must edit it out before sign-off; the AI quality rubric ([../05-data-ai/ai-quality-rubric.md](../05-data-ai/ai-quality-rubric.md)) tracks this.
