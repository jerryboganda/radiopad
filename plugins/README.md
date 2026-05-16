# Plugins Layer

Plugins are the distribution layer for agent capabilities. Open Design does not yet load plugins at runtime, but this folder defines the project manifest shape for bundling instructions, skills, hooks, subagents, and commands as a reviewable package.

## Current Package

`plugins/open-design-agent-kit/plugin.json` describes the five-layer kit checked into this repo:

- Layer 1 memory: `CLAUDE.md`
- Layer 2 skills: `skills/README.md` and `skills/*/SKILL.md`
- Layer 3 hooks: `.github/hooks/open-design-agent-kit.json` and `hooks/`
- Layer 4 subagents: `.github/agents/*.agent.md` and `subagents/*.md`
- Layer 5 distribution: the plugin manifest itself

## Manifest Rules

Plugin manifests must be plain JSON, versioned, and explicit about security boundaries. They should not contain credentials, local paths outside the repo, or generated runtime state.

Recommended top-level fields:

- `name`
- `version`
- `description`
- `publisher`
- `license`
- `layers`
- `install`
- `security`
- `compatibility`

## Security Review

Before promoting a plugin to a real install path, review:

1. Which files can execute code.
2. Which hooks can block or ask for permissions.
3. Which skills ask agents to use network or shell tools.
4. Which subagents have execute/edit tools.
5. Whether all referenced files are tracked and intentionally public.

Runtime plugin installation should require explicit user approval.