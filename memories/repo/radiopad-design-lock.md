# RadioPad design lock — RC design system (2026-07-13)

**Locked:** RadioPad's visual identity is the **RC design system** (PRD v3.0 §20). This supersedes and retires the Hallmark "paper & ink" system (warm paper, terracotta, OKLCH, light-only) in full.

## The lock

1. **Canonical token source = `frontend/app/tokens.css`** — RC `--color-*` primitives (light in `:root`, dark under `html[data-theme="dark"]`) + alias layers (Layer A legacy Hallmark names — do not use in new code; Layer B the 44-name semantic contract `--bg`/`--accent`/`--green`… plus new `--accent-fg`, `--scrim`, `--link`, `--bg-selected`, `--ai` family, `--navy` family) + shared component classes. Tailwind scales in `frontend/tailwind.config.ts` point at the same variables. Never hardcode colours; extend `tokens.css` (light AND dark values) when a token is missing.
2. **RC palette** — light-first white/blue clinical SaaS: canvas `#F5F8FB`, white surfaces, ink `#0F1F38`, accent blue `#2F88D8`/hover `#1F6FB8`; dark theme is deep navy (canvas `#0B1422`, surfaces `#111D2D`, accent `#3B82F6`), never pure black (THEME-014). One accent; status via green/blue/red/amber/ai/purple/navy families only.
3. **Dual themes are MANDATORY** — light is the first-run default (THEME-001); dark is first-class. Toggle on topbar + sign-in (THEME-002), `localStorage['rp-theme']`, pre-paint bootstrap (THEME-006, `lib/theme.ts` + `layout.tsx`), instant switch (THEME-005), print/export always light (THEME-015). Every UI change must be checked in BOTH themes. The old "no dark mode / light-only" rule is revoked. Intentionally dark in both themes: `.op-bash`, present-mode.
4. **Inter everywhere** — Inter Variable self-hosted via `@fontsource-variable/inter`; serif report prose retired (`--serif` is a legacy alias to the sans stack); `--mono` for accessions/IDs/hashes.
5. **Blue AI treatment** — AI-generated text wears `.ai-mark`: `--ai` blue tinted field + "✨ generated — review required" label, paired with amber "Requires review" until accepted/edited; never hue-only. Purple is now provenance/custom only. Severity map unchanged: Blocker red / Warning amber / Info+Style blue / success green.
6. **Mockups = source of truth for looks** — `UI UX SCREENS/Authentication/` (RC-01…RC-10, light+dark+state frames), implemented as-is; every other screen follows the same system. The written contract is `docs/02-design/design.md` (it wins over any other doc).

## Unchanged invariants

No component libraries (MUI/Ant/Chakra/Bootstrap); left-sidebar shell is the only primary nav; `<Skeleton/>`/`<EmptyState/>`/`<ErrorState/>` state trio; no emoji as icons; no additional accents; SearchableSelect for long option lists; RadioPad never auto-signs.
