# AI Product Spec

**Status:** Current  ·  **Owner:** Product + AI  ·  **Last Updated:** 2026-05-04

## Goal

Help radiologists draft accurate, complete, well-structured reports faster, while keeping human sign-off explicit and auditable.

## Capabilities (v0.x)

- **Impression draft** from `findings` + `indication` + `bodyPart` + `modality`.
- **Recommendation draft** from `findings` + `impression`.
- **Technique boilerplate** from `modality` + `bodyPart` (template-driven; AI optional).
- **Validation explanation** — turn a rulebook finding into a one-line plain-language explanation (Phase 2).

## Non-goals

- AI never auto-signs.
- AI does not classify diseases or render diagnoses on its own.
- AI does not select rulebooks or templates — those are human decisions.
- No image-based AI in v0.x.

## Inputs

| Field | Type | Sensitivity |
| --- | --- | --- |
| `modality` | string | Internal |
| `bodyPart` | string | Internal |
| `indication` | string | **Restricted (PHI)** |
| `findings` | string | **Restricted (PHI)** |
| `impression` (when generating recommendations) | string | **Restricted (PHI)** |
| `containsPhi` flag | bool | Routing decision |

## Outputs

- A single string (the draft).
- A list of caveats (e.g. "uncertain about size estimate").
- Wrapped in `.ai-mark` in the UI until acknowledged.

## Quality bar

- Acceptance rate (clinician keeps the draft mostly intact): ≥ 70%.
- Hallucination rate (verifiable factual errors): < 2%.
- Refusal accuracy: 100% on prompts that try to elicit PHI re-disclosure or sign-off behaviour.

See [ai-quality-rubric.md](ai-quality-rubric.md) and [prompt-evals.md](prompt-evals.md).

## Provider matrix

See [model-policy.md](../01-ai-agent/model-policy.md). Providers: Mock, Anthropic, Ollama. Provider compliance class is the routing decision.

## Roll-out

- v0.1: Mock + manual provider config.
- v0.2: Anthropic remote (de-identified only); Ollama local (PHI ok).
- v0.3+: streaming responses; multi-step reasoning gated by safety review.
