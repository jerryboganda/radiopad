# Prompt Architecture

**Status:** Current  ·  **Owner:** AI  ·  **Last Updated:** 2026-05-04

## Components of a RadioPad prompt

1. **System message** — defines the role (radiology drafting assistant), constraints (no diagnoses on its own, no PHI re-disclosure, no auto-sign), and output format.
2. **Context block** — `modality`, `bodyPart`, `indication`, optional `comparison`.
3. **User message** — the radiologist's findings text plus the explicit task (e.g. "draft an impression").
4. **Output schema** — plain text in v0.x; JSON envelope planned in Phase 2.

## Versioning

Each prompt has an id and a version: `<surface>.<purpose>.v<MAJOR>.<MINOR>`. Examples:

- `report.impression.v1.0`
- `report.recommendation.v1.0`
- `report.technique.v1.0`

Bump MINOR for additive changes, MAJOR for breaking changes. See [prompt-versioning.md](../01-ai-agent/prompt-versioning.md).

## Storage

- Prompts live as constants in `RadioPad.Application` so they ship with the build.
- Tenants cannot override the system prompt in v0.x. Phase 3 adds tenant-overridable suffix sections (with safety re-evaluation).

## Safety patterns

- Refusal patterns embedded in the system prompt for: producing diagnoses unsupported by findings, "sign" requests, PHI re-disclosure, requests to summarise prior unrelated cases.
- Output is bounded (token limit) to prevent runaway generation.

## Output shape (v0.x)

```
Impression: <one or more sentences>
Caveats: <newline-separated list, optional>
```

The frontend parses these two sections and renders them inside `.ai-mark`.

## Phase 2 — JSON envelope

```json
{
  "promptId": "report.impression.v1.1",
  "draft": "...",
  "caveats": ["...", "..."],
  "tokensIn": 450,
  "tokensOut": 110
}
```

Adapters will adapt provider responses to this shape.
