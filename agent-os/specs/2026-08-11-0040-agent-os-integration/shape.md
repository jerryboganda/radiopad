# Agent OS Integration - Shaping Notes

## Scope

Add Agent OS v3 standards injection and product-planning context to the existing RadioPad
repository. This is tooling and documentation only; it does not change application runtime
behavior.

## Decisions

- Use the Agent OS `main` source because the v3.0.0 release tag contains a test-only
  default-profile configuration.
- Use `project-install.sh --commands-only` so Agent OS's sample profile cannot overwrite
  RadioPad standards.
- Keep `CLAUDE.md` authoritative and treat Agent OS standards as concise indexed guidance.
- Record the current operator decision that the retired PHI compliance routing gate must not
  be restored accidentally.
- Keep full builds and test suites in GitHub Actions per RadioPad project policy.

## Context

- Visuals: none; this change does not alter the product UI.
- References: `CLAUDE.md`, `AGENTS.md`, `frontend/CLAUDE.md`, `PRD.md`,
  `docs/02-design/design.md`, and `docs/03-architecture/architecture.md`.
- Product alignment: standards reinforce local-first reporting, clinical review, audit
  evidence, and the three-surface frontend model.
