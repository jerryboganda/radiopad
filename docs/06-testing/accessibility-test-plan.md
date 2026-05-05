# Accessibility Test Plan

**Status:** Draft  ·  **Owner:** Engineering + Design  ·  **Last Updated:** 2026-05-04  ·  **Source of Truth:** [../02-design/accessibility.md](../02-design/accessibility.md)

## Targets

- WCAG 2.1 AA across the web surface.
- Same standards for the desktop and mobile shells (they are the same UI).

## Automated checks

- `axe-core` via Playwright (planned) — runs on the dashboard, report editor, audit, providers, rulebooks, and templates pages.
- `eslint-plugin-jsx-a11y` rules in the frontend lint config.

## Manual checks per release

- Tab order across each page; no traps.
- Focus rings visible against `--bg`.
- Screen reader announcement of `.ai-mark`, validation severities, and status changes.
- Severity colour pairs (`green/blue/purple/red/amber`) verified for contrast against `--bg` and `--text`.
- High-contrast mode renders without losing tokens.
- Reduced-motion preference disables non-essential transitions.

## Specific RadioPad rules

- `.ai-mark` is announced as "AI suggestion" so the audio context remains clear when the visual cue is unavailable.
- Validation severity (Blocker / Warning / Info) is conveyed by **text + icon shape + colour** — never colour alone.
- Status badges (Draft / Validated / Acknowledged / Exported) use a text label; colour is a secondary cue.

## Issues triage

- Critical (cannot complete a clinical task with assistive tech) → SEV-1.
- Major (significant friction) → fix in next minor.
- Minor (cosmetic) → fix in next minor.

## Pen-test parallel

- A combined accessibility + usability review with a clinical user is required before any v1.0.0 candidate.
