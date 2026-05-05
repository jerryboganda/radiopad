# UI Spec

**Status:** Current  ·  **Owner:** Design  ·  **Last Updated:** 2026-05-04

> All screens use only the locked tokens & component classes from [design.md](design.md). This file documents per-screen state behaviour.

## Dashboard

| State | Behaviour |
| --- | --- |
| Default | Fetches first page (`skip=0, take=25`) for the active tenant. |
| Loading | Show a single `.banner.info` "Loading…" line; do **not** flash content. |
| Empty | "No reports yet" message + `.primary` "New report" button. |
| Error | `.banner.warn` with the `X-Request-Id` for support. |
| Filters changed | Debounced 200 ms; resets `page` to 0. |

## Report editor

| State | Behaviour |
| --- | --- |
| Loading | Skeleton lines using `.section-block` placeholders; no spinner. |
| Saving | "Saved" inline confirmation under the composer (debounced). |
| Validation Blocker | `Acknowledge` button disabled with tooltip "Resolve Blockers first". |
| AI in progress | `.banner.info` "Asking <provider>…"; cancel-on-unmount. |
| AI success | Insert `.ai-mark` block with [Accept] / [Dismiss]. |
| Provider blocked | `.banner.warn` "Provider not allowed for PHI" + link. |
| Acknowledged | Composer becomes read-only; status badge `.badge.ok`. |
| Exported | "Download" toast; status badge `.badge.ok`. |

## Validation panel

- Counts grouped by severity at the top.
- Per-bucket section: header in `var(--text-muted)` uppercase; findings as `.finding.<severity>`.
- Empty state: "Click *Validate* to run rulebook checks."

## Forms (modals)

- Use `.rp-modal-backdrop` + `.rp-modal`.
- Field rows use `.rp-field` (label above) or `.rp-field.rp-row` (inline).
- Required asterisk in `.text-muted` colour, placed after the label text.
- Errors render under the field in `.finding.blocker` style.

## Buttons

- Primary CTA: `.primary` — exactly one per surface.
- Secondary: `.primary-ghost`.
- Subtle: `.subtle` for "Cancel".
- Icon-only: `.icon-btn` with an SVG (never an emoji).

## Notifications / toasts

- Use a top-of-screen `.banner.<info|warn|ai>` row.
- AI-related banners use `.banner.ai`.
- Persist until the user dismisses or 8 s for non-blocking info.

## Empty / loading / error states (cross-screen)

| Class | Use |
| --- | --- |
| `.banner.info` | Loading, neutral status. |
| `.banner.warn` | Recoverable issue (network, validation). |
| `.banner.ai` | AI-only context (model, prompt version). |
| `.finding.blocker / .warning / .info` | Validation severities. |
| `.badge.ok / info / ai / warn / danger` | Status badges. |
