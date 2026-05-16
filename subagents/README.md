# Subagents Layer

Subagents are focused helper roles with their own context window. Use them to keep research, review, test execution, and implementation work scoped instead of filling the main session with every intermediate detail.

This folder contains portable role definitions. GitHub Copilot-native versions live in `.github/agents/*.agent.md` and should stay aligned with these files.

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

If another agent runtime expects subagents in an ignored local directory, copy or symlink these definitions into that runtime locally. Do not commit `.claude/`, `.agents/`, `.opencode/`, or `.codex/` mirrors.