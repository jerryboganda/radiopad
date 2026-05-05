# RadioPad — product vision

> Source of truth for requirements is the
> [Enterprise PRD](../../RadioPad%20%E2%80%94%20Enterprise%20PRD%20_%20Project%20Requirement%20Detail%20Document.md)
> at the repo root. This page is the short version.

## Why

Radiology reporting is high-volume, high-stakes, and increasingly augmented by
AI — but most AI tooling assumes the radiologist trusts a black box and ships
prompts/PHI to vendors with no audit trail. RadioPad inverts that:

- **Local-first.** The reporting workspace runs on the radiologist's
  workstation; PHI never leaves without an explicit, governed decision.
- **Audit-first.** Every AI prompt, response, edit, export, and acknowledgement
  is recorded in a tamper-evident chain.
- **Rulebook-first.** Institutional reporting standards are encoded as
  versioned, testable YAML rulebooks — not opinions buried in prompt text.

## Who

| Persona | Primary surface | What they need |
| ------- | --------------- | -------------- |
| Attending radiologist | Web / desktop reporting workspace | Fast keyboard-driven editing, AI assist they can trust, blockers that prevent unsafe sign-off |
| Resident / fellow | Same workspace | Templates, in-line teaching prompts (planned), no surprise PHI egress |
| Department admin | Governance + Providers + Audit | Policy enforcement, provider routing, access reviews |
| Compliance / governance | Audit + Rulebooks | Immutable evidence; versioned rulebooks with golden tests |
| Integration engineer | CLI + REST API | Scriptable rulebook validation, FHIR export, audit dump |

## Outcomes

- Reduce time to draft a structured report by ≥ 30% vs free-text dictation.
- Zero unaudited AI calls — 100% pass through `AiGateway`.
- Zero blocker-class findings on signed reports.
- Rulebook regression coverage for every approved rulebook.

## Non-goals (for v1)

- Image viewing / PACS integration (PRD §15 — phase 2+).
- Real-time multi-user co-editing.
- Speech recognition (third-party integration only, not built in-house).
- Dark mode and theming (the locked Open Design palette is the only theme).
