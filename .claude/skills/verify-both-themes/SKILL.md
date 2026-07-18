---
name: verify-both-themes
description: Use after any RadioPad UI change to satisfy the mandatory "verify in both light and dark" rule. Drives the running app in a browser, screenshots the route in light, flips the theme to dark, and screenshots again so the pair can be compared.
---

# Verify both themes

RadioPad's design lock (`docs/02-design/design.md`, THEME rules) makes it mandatory: **a UI change is not done until it has been eyeballed in BOTH light and dark.** Light is the first-run default; dark is first-class deep navy. This skill automates the toggle.

## How the theme works

- Theme is driven by `html[data-theme="light"|"dark"]` on the document root.
- Persistence key is `rp-theme` in `localStorage`; `frontend/lib/theme.ts` bootstraps it pre-paint; `<ThemeToggle />` flips it.
- Tailwind darkMode is `['selector', '[data-theme="dark"]']`.

## Steps

1. **Serve the app.** Either `pnpm dev` (full desktop app at http://localhost:3000) or serve a built surface (`out-desktop/` etc.). Navigate to the route you changed.
2. **Light shot.** Ensure light: run `document.documentElement.dataset.theme = 'light'` (and `localStorage.setItem('rp-theme','light')`), then screenshot.
3. **Dark shot.** Flip: `document.documentElement.dataset.theme = 'dark'; localStorage.setItem('rp-theme','dark')`, then screenshot the same viewport.
4. **Compare.** Check both shots for: contrast/legibility, no hue-only status signalling (severity chips must keep their labels), `.ai-mark` visible in both, no pure-black surfaces in dark, no hardcoded colour that failed to adapt.

Use the browser driver your environment provides (the global `agent-browser` CLI, or the Browser MCP). Re-snapshot after each theme flip.

## Caveats

- **Print / export always render the light document theme** (THEME-015) — verify export surfaces in light regardless of the current UI theme.
- Intentionally-dark-in-both exceptions: the `.op-bash` terminal block and present-mode surfaces.
