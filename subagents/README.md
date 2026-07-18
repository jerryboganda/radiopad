# Subagents Layer

Subagents are focused helper roles with their own context window. Use them to keep research, review, test execution, and implementation work scoped instead of filling the main session with every intermediate detail.

This folder contains **portable** role definitions (the source of truth, runtime-agnostic).
They are promoted into runnable Claude Code subagents at `.claude/agents/*.md` (committed), where
each gets a real `tools:` allowlist and a `model:`. Edit a role here, then mirror the change into
`.claude/agents/` so both stay in sync.

## Available Subagents

| Subagent | Use For | Must Return |
|---|---|---|
| `explorer` | Read-only codebase mapping, file discovery, pattern finding. | Relevant paths, architecture facts, gaps, and risks. |
| `code-reviewer` | Independent diff review and regression risk analysis. | Findings first, ordered by severity, with file paths. |
| `test-runner` | Running focused validation commands and explaining failures. | Commands run, pass/fail status, failure excerpts, next action. |
| `feature-dev` | Scoped end-to-end implementation after requirements are clear. | Changed files, verification, and remaining risks. |

## Boundaries

- Use `explorer` before broad edits in unfamiliar areas.
- Use `code-reviewer` before completion for non-trivial changes.
- Use `test-runner` when validation is more than a single obvious command.
- Use `feature-dev` only when the user wants implementation, not just planning.
- Keep subagent outputs summarized. The main agent owns final user-facing decisions.

## Runtime Notes

These four roles are promoted to committed Claude Code subagents in `.claude/agents/`
(`explorer`, `code-reviewer`, `test-runner`, `feature-dev`) — dispatch them with `@explorer`,
`@code-reviewer`, etc., or let Claude auto-delegate. For other runtimes (`.agents/`, `.opencode/`,
`.codex/` — still gitignored) copy or symlink these definitions in locally.