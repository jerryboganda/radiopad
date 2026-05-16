# Open Design Agent Kit Plugin

This package manifest bundles the project-specific five-layer agent setup for Open Design.

It is a distribution document today, not a daemon runtime plugin. The daemon does not automatically load `plugins/*/plugin.json` yet. Treat it as the reviewable manifest that future install tooling can consume.

## Bundle Contents

- `.github/copilot-instructions.md`: automatic GitHub Copilot workspace entrypoint.
- `CLAUDE.md`: project constitution and repo memory.
- `skills/README.md` and `skills/*/SKILL.md`: capability layer.
- `.github/hooks/open-design-agent-kit.json` and `hooks/`: deterministic lifecycle hooks.
- `.github/agents/*.agent.md` and `subagents/*.md`: focused subagent roles.
- `plugins/README.md`: plugin authoring and security notes.

## Install Guidance

For shared repo work, use the tracked files directly. For agent runtimes that require local config folders, copy or symlink the relevant files into ignored runtime folders on your own machine. Do not commit local mirrors.

## Release Checklist

1. Run `pnpm test -- --run tests/agent-kit-docs.test.ts`.
2. Confirm every manifest `files` entry exists.
3. Review executable hook changes.
4. Confirm no secrets or `.od/` runtime files are referenced.