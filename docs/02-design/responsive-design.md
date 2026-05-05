# Responsive Design

**Status:** Current  ·  **Owner:** Design  ·  **Last Updated:** 2026-05-04

## Breakpoints

| Name | Min width | Use |
| --- | --- | --- |
| `desk-l` | 1440px | Large reading rooms. Two-pane editor with comfortable margins. |
| `desk` | 1200px | Default desktop. Two-pane editor. |
| `tablet` | 900px | Single column with collapsible sidecar. |
| `mobile` | < 900px | Single column. Editor read-only on mobile shell. |

## Desktop behaviour

- Topbar + split shell.
- Composer pane min-width 720 px; sidecar pane min-width 360 px.
- Resizable splitter (Phase 2).

## Tablet behaviour

- Topbar collapses to a brand mark + "menu" disclosure.
- Sidecar becomes a tab in the composer pane (Validation | AI Assist).
- Modals expand to 80% width.

## Mobile behaviour

- Topbar is sticky; shows tenant + report status only.
- Editor is read-only; acknowledge button is the single primary action.
- Templates / Rulebooks / Providers screens are admin-only and not available on mobile in v0.x.

## Layout rules

- Never set fixed `px` widths on text containers; use `max-inline-size` and `var(--measure)` (planned token).
- Always preserve the warm-paper margin around the composer.
- Never let a `.primary` button collapse to `.subtle` on small screens — keep its visual weight; reduce padding instead.
