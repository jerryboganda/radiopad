# Agent OS in RadioPad

Agent OS v3 is installed for this repository to keep project standards discoverable and
injectable without replacing RadioPad's existing `CLAUDE.md`, `AGENTS.md`, or `.claude`
configuration.

## Locations

- Base installation: `$HOME/agent-os` (developer-local; Agent OS `main` commit
  `cae8e664fb59a01869718c3151e0f45b7a06a2fb` installed on 2026-08-11).
- Project standards: `agent-os/standards/`.
- Product context: `agent-os/product/`.
- Feature specs: `agent-os/specs/`.
- Claude commands: `.claude/commands/agent-os/`.

`CLAUDE.md` remains the authoritative RadioPad instruction file. Agent OS standards
summarize and organize that contract; they do not override it.

## Working workflow

1. Before implementation, run `/agent-os/inject-standards` or specify the relevant
   standards explicitly.
2. For new patterns, run `/agent-os/discover-standards <area>` and then
   `/agent-os/index-standards`.
3. For a substantial feature, enter plan mode and run `/agent-os/shape-spec`.
4. Use `/agent-os/plan-product` when mission, roadmap, or stack context changes.
5. After project standards mature, sync reusable standards to the local base profile:

   ```bash
   cd /path/to/radiopad
   $HOME/agent-os/scripts/sync-to-profile.sh --new-profile radiopad --all --overwrite
   ```

6. Reinstall from that profile only when intentionally refreshing the project copy:

   ```bash
   $HOME/agent-os/scripts/project-install.sh --profile radiopad
   ```

Do not use the default sample profile for RadioPad. Keep this project's standards as the
reviewed source of truth and update the index whenever files are added or removed.
