# References for Agent OS Integration

## Agent OS v3

- **Location:** `C:\Users\Admin\agent-os`
- **Relevance:** Base scripts and the official project command templates.
- **Key patterns:** Standards live in the project, `index.yml` drives injection, and
  commands are installed under `.claude/commands/agent-os/`.

## RadioPad source of truth

- **Location:** `CLAUDE.md`, `AGENTS.md`, and `frontend/CLAUDE.md`
- **Relevance:** Defines precedence, architecture, UI, surface, safety, and workflow rules.
- **Key patterns:** Existing instructions remain authoritative; Agent OS adds concise indexed
  summaries rather than a competing instruction layer.

## Product and architecture

- **Location:** `PRD.md`, `docs/03-architecture/architecture.md`,
  `docs/02-design/design.md`
- **Relevance:** Supplies product mission, roadmap, stack, and design constraints.
- **Key patterns:** Local-first/audit-first reporting, one AI gateway, RC dual-theme shell,
  and separate desktop/web/mobile bundles.
