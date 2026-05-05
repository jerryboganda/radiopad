# Agent Workflows

**Status:** Current  ·  **Owner:** Engineering  ·  **Last Updated:** 2026-05-04

Standard workflow for any AI coding agent in this repo.

## 1. Analyze before edit

- Read [../INDEX.md](../INDEX.md), [ai-context.md](ai-context.md), and the relevant area docs.
- Open the files to be changed; read at least 50 lines of context around the change site.
- Search the workspace for existing patterns — copy the existing pattern instead of inventing a new one.

## 2. Plan before implementation

- For non-trivial work, write a todo list (`manage_todo_list`) with concrete steps.
- Identify the files that will change, the tests that will be added, and the docs that need to update.
- Identify whether the change touches a *human-review-required* file (see [human-review-policy.md](human-review-policy.md)).

## 3. Implement incrementally

- One concept per edit.
- Use `multi_replace_string_in_file` for related edits.
- Re-read the file if the agent has been away from it for many tool calls.

## 4. Run validation

- Backend: `dotnet build && dotnet test`.
- Frontend: `pnpm typecheck` (and `pnpm build` if exporting).
- CI validates every rulebook YAML and runs every matching golden suite — if you touched a rulebook, run the affected suite locally first.
- Use `get_errors` on edited files before declaring success.

## 5. Update docs

- Canonical doc in `docs/`.
- `openapi/openapi.yaml` if API changed.
- `CHANGELOG.md` under `[Unreleased]`.
- `PROGRESS.md` iteration entry.

## 6. Final summary

- List files created / modified / archived.
- List validation steps run.
- Surface any open questions or assumptions.
- Recommend next work.

## Ralph-loop variant

For autonomous PRD-driven work the Ralph loop layers on top of this workflow:

1. Read `PRD.md` and `PROGRESS.md`.
2. Pick the next incomplete checklist item.
3. Run the standard workflow above.
4. Append a new iteration block in `PROGRESS.md` summarising what shipped.
5. Repeat until blocked or complete.
