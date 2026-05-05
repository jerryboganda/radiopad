# Archive Index — 2026-05-04

This folder catalogues legacy documentation files that were superseded by the canonical hierarchy under `docs/00-product/`, `docs/03-architecture/`, etc. We **kept** the originals in their original location for traceability rather than deleting them.

## Mapping

| Legacy path | Canonical replacement(s) | Notes |
| --- | --- | --- |
| `docs/agent-adapters.md` | `docs/01-ai-agent/model-policy.md`, `docs/05-data-ai/model-abstraction.md` | Original Open Design adapter docs; superseded by RadioPad provider model. |
| `docs/architecture.md` | `docs/03-architecture/architecture.md` (current) + `docs/03-architecture/system-design.md` | The current `architecture.md` was already the canonical RadioPad version; system-design adds the module table. |
| `docs/modes.md` | `docs/01-ai-agent/agent-workflows.md` + `docs/01-ai-agent/ai-rules.md` | Open Design playground mode descriptions; not used by RadioPad. |
| `docs/references.md` | `docs/03-architecture/api-reference.md` + `openapi/openapi.yaml` | Old reference index for the playground. |
| `docs/roadmap.md` | `ROADMAP.md` (root) + `docs/00-product/roadmap.md` | Both root and product roadmap supersede the Open Design timeline. |
| `docs/skills-protocol.md` | `docs/01-ai-agent/agent-permissions.md` + `docs/01-ai-agent/agent-safety.md` | Open Design "skills" protocol; not part of the RadioPad architecture. |
| `docs/spec.md` | `PRD.md` (root) + `docs/00-product/srs.md` + `docs/00-product/frd.md` | Original Open Design playground spec; superseded. |

## Rationale for keeping originals

- Several inbound links from the legacy `src/` and `daemon/` folders may still reference these files; preserving them avoids broken links until those folders are also fully retired.
- They form a historical record of the project's pivot from the Open Design playground to RadioPad.

## Removal criteria

A legacy file may be deleted in a future cleanup PR when **all** of the following are true:

1. No remaining references from `src/`, `daemon/`, or any active doc.
2. The replacement(s) above are confirmed to cover the content fully.
3. An ADR records the deletion with the date and rationale.

Until then, treat the legacy files as **read-only history**.
