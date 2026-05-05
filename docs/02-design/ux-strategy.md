# UX Strategy

**Status:** Current  ·  **Owner:** Design  ·  **Last Updated:** 2026-05-04

## User mental model

A radiologist's mental model is **"I'm typing a structured report; I want help that disappears when not needed and is unmistakable when present."** RadioPad models this directly:

- The composer is always front and centre, with the same warm-paper feel across surfaces.
- Validation findings appear as quiet notes, never modal interruptions.
- AI suggestions appear in a clearly purple-marked block (`.ai-mark`) that the radiologist can edit, accept, or dismiss in one keystroke.

## UX goals

1. **Velocity** — every interaction is one click, one keystroke, or one shortcut away.
2. **Trust** — every AI insertion is obviously AI; every validation finding cites its `ruleId`.
3. **Calm** — colour is reserved for severity; chrome stays muted.
4. **Familiarity** — the visual language matches Claude.ai so the cognitive cost of using AI is low.

## Friction points (current)

- Switching between Web and Desktop loses dictation cursor (Phase 4 voice work).
- Rulebook authoring is YAML-only (Phase 2: visual editor).
- Mobile is read/acknowledge only; some users want to triage on the go.

## Onboarding strategy

- Ship a tour-less onboarding: on first run, the editor is pre-populated with a sample chest-CT report so the radiologist immediately sees the validate / AI / acknowledge cycle.
- "What's locked" tooltip on the design system explains the warm-paper palette and `.ai-mark` semantics.

## Retention loops

- Daily: report list with quick filters (modality, status, search).
- Weekly: tenant admin reviews validation finding counts; tunes rulebook.
- Monthly: usage / quality metrics summary (planned, Phase 2).
