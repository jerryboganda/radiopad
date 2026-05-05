# Design System

**Status:** Locked  ·  **Owner:** Design + Engineering  ·  **Last Updated:** 2026-05-04  ·  **Source of Truth:** [globals.css](../../frontend/app/globals.css) + [design.md](design.md)

> The Open Design (Claude.ai-inspired) warm-paper system is locked. No alternate palettes, no Tailwind utilities, no MUI/Ant/Chakra/Bootstrap. Adding a new token requires updating both `globals.css` and `design.md` in the same change.

## Colour

| Token | Value | Use |
| --- | --- | --- |
| `--bg` | `#faf9f7` | Page background. |
| `--bg-elevated` | `#ffffff` | Cards, modals. |
| `--text` | `#1a1916` | Primary text. |
| `--text-muted` | `#6e6c66` | Secondary / labels. |
| `--accent` | `#c96442` | Primary CTA, links. |
| `--accent-soft` | `#f0d9cc` | Accent backgrounds. |
| `--border` | `#e8e4dd` | Lines, separators. |
| Semantic green | family for `.badge.ok`, `.finding.info` validates | Affirmative. |
| Semantic blue | family for `.badge.info`, `.finding.info` | Informational. |
| Semantic purple | `.ai-mark`, `.banner.ai`, `.badge.ai` | AI-generated content. |
| Semantic red | `.finding.blocker`, `.badge.danger` | Blocker. |
| Semantic amber | `.finding.warning`, `.badge.warn` | Warning. |

## Typography

| Token | Stack | Use |
| --- | --- | --- |
| `--serif` | Source Serif 4, Iowan, Charter, … | Reports / AI prose. |
| `--sans` | Inter, Helvetica Neue, system-ui, … | UI chrome, buttons. |
| `--mono` | JetBrains Mono, Menlo, … | Rule ids, accession numbers, codes. |

Headings use the sans family by default; report bodies use serif. Do not introduce additional weights beyond 400/500/600.

## Spacing

`4 / 8 / 12 / 16 / 20 / 24 / 32 / 48` (px). No arbitrary values.

## Radius

- `4 / 8 / 12 / 16` px.
- Modals: 12. Cards: 8. Inputs: 8. Pills/badges: 999 (full).

## Shadows

- Card: `0 1px 2px rgba(26,25,22,0.04)`.
- Modal: `0 16px 48px rgba(26,25,22,0.18)`.

## Components

| Class | Purpose |
| --- | --- |
| `.app` | Top-level shell. |
| `.topbar` | Top navigation. |
| `.split` | Two-pane layout. |
| `.pane`, `.panel` | Panes within a split. |
| `.section-block` | A single report section. |
| `.composer`, `.composer-shell` | Editor input region. |
| `.msg`, `.finding`, `.ai-mark` | Inline message-style content. |
| `.brand-mark`, `.badge` | Brand and status pills. |
| `.rp-container`, `.rp-panel`, `.rp-workspace`, `.rp-narrative`, `.rp-table` | RadioPad-specific layout helpers. |
| `.rp-modal-backdrop`, `.rp-modal`, `.rp-field` | Modal shell + form rows. |
| Buttons: `.primary`, `.primary-ghost`, `.ghost`, `.subtle`, `.icon-btn` | Exactly one `.primary` per surface. |

## Icon rules

- Inline SVG only. No emoji as functional icons.
- Stroke icons preferred; 20 px default.

## Motion

- Easing: `cubic-bezier(0.2, 0.6, 0.2, 1)`.
- Durations: 120 ms (micro), 200 ms (panel), 320 ms (modal).
- Avoid spring/elastic curves; the warm-paper aesthetic is calm.

## Adding tokens

1. Add to `frontend/app/globals.css` under the existing `:root` block.
2. Document in this file and in [design.md](design.md).
3. Add usage examples to [ui-spec.md](ui-spec.md) if cross-screen.
