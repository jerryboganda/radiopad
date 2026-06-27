# Open Design Agent Constitution

This file is the project-level memory layer for agents working on Open Design. Keep it short, current, and factual. Long design rationale belongs in `docs/`; runtime state belongs in `.od/`; local agent state belongs in ignored folders such as `.claude/`, `.agents/`, `.opencode/`, and `.codex/`.

## Product Shape

Open Design is a local-first design generation product. The web app lets users turn natural-language briefs into editable design artifacts by pairing:

- a Next.js 16 App Router client in `app/` and `src/`
- a local Express daemon in `daemon/`
- file-based skills in `skills/`
- portable design systems in `design-systems/`
- project/runtime files under `.od/`, which must stay untracked

The product does not ship its own model runtime. It detects and delegates to installed agent CLIs such as Claude Code, Codex, Cursor Agent, Gemini CLI, OpenCode, Qwen, or falls back to browser-side Anthropic BYOK.

## Architecture Facts

- `next.config.ts` exports static production output to `out/` and rewrites `/api`, `/artifacts`, and `/frames` to the daemon during development.
- `daemon/server.js` owns the HTTP API, project file APIs, uploads, artifact saving, agent spawning, and static serving in production.
- `daemon/agents.js` is the adapter catalog and CLI argument builder. Preserve stdin prompt delivery for CLIs that need it on Windows.
- `daemon/db.js` stores metadata in SQLite at `.od/app.sqlite`; generated and uploaded project files live under `.od/projects/<projectId>/`.
- `daemon/projects.js` is the path traversal boundary for project files. Use its helpers instead of hand-resolving user paths.
- `src/prompts/system.ts` composes the prompt stack from discovery rules, base designer identity, active `DESIGN.md`, active `SKILL.md`, project metadata, and deck framework rules.

## Security And Privacy

- Treat the daemon as a local-trust process bound to `127.0.0.1`. Do not claim hosted multi-user auth exists.
- Browser BYOK uses `@anthropic-ai/sdk` with `dangerouslyAllowBrowser`; warn before moving that path into hosted/server contexts.
- Never commit API keys, local agent configs, `.od/`, `.claude/`, `.agents/`, `.opencode/`, `.codex/`, or generated runtime databases.
- Keep preview artifacts inside sandboxed iframes. Do not add `allow-same-origin` without a documented security review.
- When adding file APIs, route all paths through `daemon/projects.js` validation helpers.

## Development Commands

- Install and run locally: `corepack enable`, `pnpm install`, `pnpm dev:all`.
- Daemon only: `pnpm daemon`.
- Frontend only: `pnpm dev`.
- Type check: `pnpm typecheck`.
- Unit tests: `pnpm test`.
- Production build/static export: `pnpm build`.
- Live runtime adapter smoke test: `pnpm test:e2e:live` only when the required local agent credentials are available.

## Coding Conventions

- Use TypeScript in `src/`; keep `daemon/` as plain ESM JavaScript unless the repo is intentionally migrated.
- Use single quotes in JS/TS.
- Keep comments in English and only where intent or constraints are not obvious.
- Prefer existing helpers and local patterns over new abstractions.
- Do not add top-level dependencies without a clear product/security rationale.

## Skill And Design-System Rules

- Committed skills live in `skills/<name>/SKILL.md`; local/private skill overrides live in ignored `.claude/skills/`.
- Skill frontmatter must keep Claude-compatible keys (`name`, `description`, `triggers`) and optional OD metadata under `od:`.
- Skills with side files should reference `assets/` and `references/` clearly so `daemon/skills.js` can add the skill-root preamble.
- Every featured skill should include a real `example.html` and, when relevant, a screenshot under `docs/screenshots/skills/`.
- Design systems live in `design-systems/<slug>/DESIGN.md` using the 9-section schema described in `docs/skills-protocol.md`.

## Automatic Utilization

- Agents should apply all five layers without waiting for the user to name them: memory for repo facts, skills for artifact workflows, hooks for deterministic safety, subagents for focused delegation, and plugins for distribution metadata.
- When a layer file is added, moved, or removed, update `plugins/open-design-agent-kit/plugin.json` and `tests/agent-kit-docs.test.ts` in the same change.

## Agent Layer Map

- Layer 1 memory: this `CLAUDE.md`.
- Layer 2 skills: `skills/README.md` plus each `skills/*/SKILL.md`.
- Layer 3 hooks: `.github/hooks/open-design-agent-kit.json` and `hooks/` scripts.
- Layer 4 subagents: `subagents/*.md` portable definitions.
- Layer 5 plugins: `plugins/open-design-agent-kit/plugin.json`.

## Review Checklist

Before finishing a change, report what changed and what was verified. For code changes, prefer `pnpm typecheck` and the narrowest relevant `pnpm test` run. If a check cannot run, say why. Do not hide failing tests or runtime limitations.