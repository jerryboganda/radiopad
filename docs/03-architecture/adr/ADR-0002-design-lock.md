# ADR-0002: Lock the UI/UX to the Open Design (Claude.ai-inspired) visual language

- **Status:** Accepted (Iteration 2, 2026-05-02)
- **Decision-makers:** Product + Design + Engineering
- **Related:** [docs/02-design/design.md](../../02-design/design.md), [frontend/app/globals.css](../../../frontend/app/globals.css)

## Context

Radiology workflows demand a calm, low-glare, paper-like surface where AI suggestions are unmistakeably distinguishable from the radiologist's own writing. The Open Design system shipped by the legacy reference app already provides this: warm beige paper background, single accent (`#c96442`), serif body for prose, and a purple `.ai-mark` band reserved for AI-generated text.

The team explored Tailwind/utility-first and headless component libraries (Radix + Tailwind, MUI, Chakra). All would force colour and typography decisions that conflict with the established design language and would obscure the AI-vs-human visual contract.

## Decision

The frontend is **locked** to the Open Design visual language:

- Tokens (`--bg`, `--accent: #c96442`, `--text`, `--border`, semantic families green/blue/purple/red/amber, fonts `--serif` / `--sans` / `--mono`) are the only colours and fonts permitted.
- Component classes (`.app`, `.topbar`, `.split`, `.rp-panel`, `.rp-table`, `.section-block`, `.composer`, `.msg`, `.finding`, `.ai-mark`, `.brand-mark`, `.badge`, button variants `.primary` / `.primary-ghost` / `.ghost` / `.subtle`) are the only building blocks.
- AI-generated text **must** wear `.ai-mark` until acknowledged.
- Validation severities are encoded by the locked semantic families: blocker → red, warning → amber, info → blue.
- **Forbidden:** Tailwind utility-only styling, MUI / Ant / Chakra / Bootstrap, dark mode, emoji as functional icons, additional accent colours, replacing the topbar+split shell.

If a new requirement cannot be satisfied with the existing tokens/components, the change must extend `globals.css` *and* `docs/02-design/design.md` in the same PR — never inline a one-off style.

## Consequences

- Designers and engineers ship faster because every page looks immediately consistent.
- AI safety is partially enforced visually: a missing `.ai-mark` is a code-review blocker, not a stylistic preference.
- Adopting popular component kits is gated behind a future ADR.
- The lock is encoded in `AGENTS.md`, `CLAUDE.md`, and `/memories/repo/radiopad-design-lock.md` so AI coding agents inherit the constraint.
