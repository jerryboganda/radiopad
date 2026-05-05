# Component Spec

**Status:** Current  ·  **Owner:** Design + Engineering  ·  **Last Updated:** 2026-05-04

> Components are CSS classes from the locked Open Design system, not React abstractions. Pages compose them directly. New variants require updating [design.md](design.md) and [globals.css](../../frontend/app/globals.css).

## Buttons

| Class | Variants | States |
| --- | --- | --- |
| `.primary` | default | hover, active, disabled |
| `.primary-ghost` | default | hover, active, disabled |
| `.ghost` | default | hover, active, disabled |
| `.subtle` | default | hover |
| `.icon-btn` | default | hover, active, disabled |

Accessibility: focus ring uses `--accent`; disabled state never relies on colour alone (the cursor + `aria-disabled` make it obvious).

Test expectations: snapshot of computed styles is **not** required (the design lock is the test). Behaviour tests assert click handlers and `aria-disabled`.

## Badges

| Class | Use |
| --- | --- |
| `.badge.ok` | Green family — affirmative status. |
| `.badge.info` | Blue family — neutral info. |
| `.badge.ai` | Purple family — AI provenance. |
| `.badge.warn` | Amber family — Warning. |
| `.badge.danger` | Red family — Blocker. |

## Findings

| Class | Use |
| --- | --- |
| `.finding.blocker` | Red family — Blocker. |
| `.finding.warning` | Amber family — Warning. |
| `.finding.info` | Blue family — Informational. |

Each finding shows: short message, then `<code>{ruleId}</code>` and optional section suffix.

## AI mark

`.ai-mark` wraps any AI-generated text. It produces a purple background tint and a small "AI suggestion" tag in the corner. It must persist until the radiologist explicitly accepts the text (which removes the wrapper).

## Section block

`.section-block` is a labelled text region with:

- Top label in `var(--text-muted)`.
- Body using `var(--serif)` for narrative or a `<textarea>` styled to match.
- Optional placeholder in `var(--text-muted)`.

## Banner

| Class | Use |
| --- | --- |
| `.banner.info` | Loading, neutral system status. |
| `.banner.warn` | Recoverable issue. |
| `.banner.ai` | AI context (model, prompt version). |

Banners are dismissible only when explicitly fitted with an `.icon-btn` × button.

## Modal

`.rp-modal-backdrop` + `.rp-modal` provide a centered modal with locked tokens. Forms inside use `.rp-field` rows. Esc closes; the close button is an `.icon-btn`.

## Accessibility expectations

- Focus order: top-down, left-right.
- Keyboard reachable: every interactive element has a `tabindex` ≤ 0.
- Contrast: text/background contrast ≥ 4.5:1 for body, ≥ 3:1 for large text.
- Screen reader: badges announce both the colour-family meaning and the textual label (e.g. "Blocker, missing impression").
