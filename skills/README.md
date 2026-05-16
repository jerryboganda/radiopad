# Skills Layer

Skills are Open Design's project-specific capability layer. They are committed, reviewable folders that tell an agent how to produce a specific artifact type without redesigning the workflow from scratch.

## Where Skills Live

Committed skills live here:

```text
skills/<skill-name>/
├── SKILL.md
├── assets/
├── references/
└── example.html
```

The daemon also recognizes local, ignored overrides in `./.claude/skills/` and user-global skills in `~/.claude/skills/`. Do not commit those local folders.

## Discovery Contract

`daemon/skills.js` scans `skills/*/SKILL.md`, parses frontmatter, and exposes the result through `/api/skills`. Keep the frontmatter compatible with Claude Code's base shape plus Open Design's optional `od:` metadata.

```yaml
---
name: dashboard
description: |
  Admin / analytics dashboard in a single HTML file. Use for operations,
  business intelligence, metrics views, and internal tools.
triggers:
  - dashboard
  - analytics
od:
  mode: prototype
  platform: desktop
  scenario: operations
  preview:
    type: html
    entry: index.html
  design_system:
    requires: true
    sections: [color, typography, layout, components]
---
```

Required base fields:

- `name`: stable lowercase identifier; prefer matching the folder name.
- `description`: concrete discovery text that names surfaces, audiences, and trigger phrases.
- `triggers`: common user phrases that should route to the skill.

Recommended `od:` fields:

- `mode`: `prototype`, `deck`, `template`, or `design-system`.
- `preview.type`: normally `html`, with `entry: index.html`.
- `design_system.requires`: whether the active `DESIGN.md` must be injected.
- `platform`, `scenario`, `featured`, `default_for`, and `example_prompt` when the UI benefits from them.

## Skill Body Contract

Each `SKILL.md` body should include:

1. What the skill produces.
2. When to use it and when not to use it.
3. A pre-flight step that reads required side files.
4. A workflow that copies a seed template before filling content.
5. A P0/P1/P2 checklist or a link to `references/checklist.md`.
6. The output contract for `<artifact>` wrapping.

Keep `SKILL.md` concise. Put large layout catalogs, component inventories, and quality gates in `references/`.

## Assets And References

- `assets/template.html` should be a real runnable seed when the skill outputs HTML.
- `references/layouts.md` should contain paste-ready section, screen, or slide skeletons.
- `references/checklist.md` should make P0 failures unambiguous.
- `example.html` should open from disk and show the target quality floor.

## Security Rules

- Do not ask agents to fetch untrusted scripts into generated artifacts unless the dependency is already used elsewhere in the repo and has a clear reason.
- Do not invent metrics, customer names, credentials, or API keys.
- Do not write outside the project working directory. Project file paths must be relative.
- Skills may mention local agent tools, but they must still work through prompt injection for weaker adapters.

## Adding A Skill

1. Fork the closest existing skill.
2. Add or update `SKILL.md` with concrete frontmatter and a deterministic workflow.
3. Add `assets/` and `references/` only when the workflow actually reads them.
4. Add a real `example.html` for user-facing skills.
5. Run `pnpm test` and `pnpm typecheck` if code or shared schemas changed.
6. Update screenshots and docs when the skill is featured.

See `docs/skills-protocol.md` and `CONTRIBUTING.md` for the full protocol.