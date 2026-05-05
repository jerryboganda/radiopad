# Accessibility

**Status:** Current  ·  **Owner:** Design  ·  **Last Updated:** 2026-05-04

## Target

WCAG 2.1 Level **AA** for web and desktop. Mobile inherits the same UI but is explicitly read/acknowledge only in v0.x.

## Keyboard navigation

- Tab order matches reading order in the locked layout.
- All interactive elements are reachable without a mouse.
- Esc closes the active modal.
- `Ctrl+S` saves (no-op once auto-save is wired).
- `Ctrl+Enter` triggers the primary CTA on the active surface.
- Tauri shell adds `Ctrl+Shift+R` (global) to focus the RadioPad window.

## Focus management

- Focus is moved into the first input of an opening modal.
- On close, focus returns to the element that triggered the modal.
- Focus ring uses `--accent` and is never suppressed.

## Contrast

- Body text on `--bg`: contrast ratio ≥ 4.5:1 (`--text` `#1a1916` on `--bg` `#faf9f7` ≈ 12.7:1).
- Buttons: hover/active states maintain ≥ 3:1 against the surface.
- Findings/badges encode severity with colour **and** text — never colour alone.

## Screen reader behaviour

- Forms use `<label for>` and `aria-describedby` for help/error text.
- Validation findings use `role="alert"` when surfaced after a Validate run.
- Modals are `role="dialog"` with `aria-labelledby` pointing at their title.
- AI-generated regions are `aria-label="AI suggestion"` so a screen reader announces the provenance.

## Forms and errors

- Required fields are marked in the label, not asterisked alone.
- Error messages live under the field in `.finding.blocker` style.
- The first error receives focus on submit failure.

## Motion

- All non-essential animations honour `prefers-reduced-motion: reduce`.

## Testing

- axe-core run against the dev build; target zero critical violations.
- Manual keyboard run-through on every milestone.
- Screen-reader sanity check (NVDA + VoiceOver) before each MAJOR.
