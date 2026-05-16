# Open Design Automatic Agent Instructions

These instructions are the GitHub Copilot automatic entrypoint for this workspace. Apply them on every task without waiting for the user to ask.

## Always Apply The Five Layers

1. **Memory:** Treat `CLAUDE.md` as the project constitution. Use it for architecture facts, safety boundaries, commands, and conventions.
2. **Skills:** Use `skills/README.md`, `docs/skills-protocol.md`, and the relevant `skills/*/SKILL.md` whenever work touches artifact workflows, templates, examples, design systems, prompt routing, or generated output quality.
3. **Hooks:** Respect `.github/hooks/open-design-agent-kit.json` and the scripts in `hooks/` when the runtime supports hooks. Do not bypass safety prompts for destructive shell commands.
4. **Subagents:** Delegate automatically to `.github/agents/*.agent.md` roles when their description fits: explorer for mapping, code-reviewer for independent QA, test-runner for validation, and feature-dev for scoped implementation.
5. **Plugins:** Keep `plugins/open-design-agent-kit/plugin.json` synchronized whenever layer files are added, moved, or removed.

## Default Workflow

- Classify the request first: planning, research, implementation, review, frontend/UX, docs, security, or validation.
- For implementation requests, act end to end: inspect, plan briefly, edit, validate, review, and report results.
- Search and read existing docs/code before editing unfamiliar areas.
- Prefer root-cause fixes and existing repo patterns over new abstractions.
- Keep user/runtime state out of git: never commit `.od/`, `.claude/`, `.agents/`, `.opencode/`, `.codex/`, secrets, API keys, or generated databases.
- When code changes, run the narrowest relevant validation and prefer `pnpm typecheck` plus focused `pnpm test` runs. If local tools are missing, state that clearly and run any available static or manifest checks.

## Open Design Boundaries

- The daemon is a local-trust Express process bound to `127.0.0.1`; do not imply hosted multi-user auth exists.
- Browser BYOK uses `@anthropic-ai/sdk` with `dangerouslyAllowBrowser`; do not move that pattern into hosted/server contexts without a security review.
- Project file APIs must use `daemon/projects.js` validation helpers.
- Preview artifacts must remain sandboxed iframes; do not add `allow-same-origin` without documented security review.

## Completion Contract

Before finalizing, ensure the response names what changed, what was verified, and any honest limits. For non-trivial changes, use an independent review pass or the code-reviewer subagent before calling the work complete.