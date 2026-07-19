# Design: CPU-intensive work runs on GitHub Actions, never locally

**Date:** 2026-07-19
**Status:** Approved (design), pending implementation

## Problem

The operator's development laptop is low-spec. Agentic AI sessions routinely run full
builds, full test suites, and whole-project typechecks on it, saturating CPU and RAM.

The rule against this **already exists** — [AGENTS.md](../../../AGENTS.md) §0.5 states it
clearly and completely. It is nevertheless not followed, for three concrete reasons:

1. **It lives in the file that loses.** Both CLAUDE.md and AGENTS.md declare CLAUDE.md the
   authoritative instruction file ("if they ever disagree, this file wins"). CLAUDE.md has
   no CI-only section, so the rule is advisory at best.
2. **CLAUDE.md actively contradicts it.** Its `## Commands` block instructs agents to run
   `dotnet build && dotnet test` and `pnpm typecheck && pnpm build` — precisely the
   commands §0.5 forbids. An agent following the authoritative file is *told* to hammer
   the laptop.
3. **Nothing enforces it.** `hooks/pretooluse.mjs` gates destructive commands
   (`rm -rf`, `git reset --hard`, curl-pipe-sh) but knows nothing about compute cost.

CI coverage is not the gap: `.github/workflows/ci.yml` already runs backend build+test,
CLI build+validate, and frontend lint+typecheck+test+build on every push. Redirecting
work to CI is therefore always actionable, never a dead end.

## Goals

- Make the CI-only rule authoritative and impossible to read past.
- Remove the contradicting instructions.
- Add a mechanical prompt so an agent that ignores the prose still gets stopped.
- Preserve the fast local feedback loop the operator relies on.

## Non-goals

- Changing any CI workflow. Coverage is already sufficient.
- Hard-blocking heavy commands. Advisory prompts only (see Decisions).
- Restricting the production VPS beyond what AGENTS.md §0.5 already says.

## Decisions

| Decision | Choice | Rationale |
| --- | --- | --- |
| Enforcement strength | **Prompt for confirmation**, not hard deny | Matches the existing hook contract — `.claude/settings.json` states "All hooks are advisory: they ask/remind, never mutate files or block outright." Lets the operator approve a genuine one-off local build without disabling the hook. |
| Canonical location | **Full text in CLAUDE.md**; AGENTS.md and GEMINI.md become pointers | Consistent with how the RC design-system rule is already structured across the three files. Single source of truth, no drift. |
| `pnpm dev` / `dotnet run` | Allowed, no prompt | Dev servers are incremental and idle most of the time. The operator verifies changes by running the app. |
| Single targeted test | Allowed, no prompt | `dotnet test --filter <Name>` / one Vitest file. Seconds of cost, high feedback value. Already carved out by §0.5. |
| Full `pnpm typecheck` | **Prompts** | A whole-frontend `tsc` sweep is minutes and heavy RAM. `ci.yml` already runs it. |
| `git` / `gh` | Allowed, no prompt | Near-zero cost, and they are the mechanism by which work reaches CI. |

## Design

### 1. CLAUDE.md — new authoritative section

Add a third `## ⚠️ MISSION-CRITICAL` section, placed alongside the existing design-system
and DESK-001 sections. It carries the **full text** of the rule, adapted from AGENTS.md
§0.5, including: the scope of "heavy", the rationale (low-spec laptop; shared VPS hosts
live tenants), the allowed-locally carve-out, the VPS prohibition, the requirement that
uncovered heavy tasks gain a workflow rather than be run by hand, and the statement that
green CI — not local output — satisfies the PR checklist.

### 2. CLAUDE.md — fix the contradicting `## Commands` block

Current content instructs full builds. Restructure into two explicitly labelled groups:

- **Allowed locally** — `dotnet run --project src/RadioPad.Api`, `pnpm dev`,
  `dotnet test --filter <Name>`, a single Vitest file, the CLI rulebook-validate command.
- **CI runs these — do not run locally** — `dotnet build`, full `dotnet test`,
  `pnpm typecheck`, `pnpm build`, naming `ci.yml` and `gh run watch` as the replacement.

This is the highest-value change in the spec: it removes the instruction that causes the
behaviour.

### 3. AGENTS.md — shrink §0.5 to a pointer

Replace the body of §0.5 with a short pointer to the CLAUDE.md section, keeping the
heading and the one-sentence summary so the rule is still visible when skimming. Mirrors
how §0.1 points at the design-system contract.

### 4. GEMINI.md — add a pointer

Short section matching AGENTS.md's, so Gemini sessions see the rule.

### 5. `hooks/pretooluse.mjs` — heavy-command prompt

Add a `heavyCommands` array alongside the existing `riskyCommands`, evaluated the same
way and yielding `permissionDecision: 'ask'`.

**Matches (prompt):** `dotnet build`, `dotnet test` *without* `--filter`, `pnpm build`,
`pnpm typecheck`, `pnpm lint`, `cargo build`, `cargo test`, `next build`, `tauri build`,
`docker build`, `docker compose build`.

**Does not match (allowed):** `pnpm dev`, `dotnet run`, `dotnet test --filter …`,
`vitest run <file>`, all `git` and `gh` commands.

The prompt reason names the replacement — that `ci.yml` covers the command, and that the
next action is push + `gh run watch` — so a blocked agent knows how to proceed rather
than retrying a variant.

### 6. `hooks/pretooluse.ps1` — lockstep mirror

`pretooluse.ps1` is a parallel PowerShell implementation of the same logic, and
`.claude/settings.json` matches on both `Bash` and `PowerShell`. It must receive the
equivalent `$heavyCommands` list, or the guard applies inconsistently depending on which
shell the agent chose.

## Risks

| Risk | Mitigation |
| --- | --- |
| The `--filter` carve-out regex is wrong, blocking the operator's fast test loop | Negative lookahead on `dotnet test`; verify both `dotnet test` (prompts) and `dotnet test --filter Foo` (silent) before committing. |
| `.mjs` and `.ps1` drift | Both edited in the same change; patterns kept in the same order with identical reasons. |
| Prompt fatigue causes reflexive approval | Scope limited to genuinely expensive commands. Dev servers and targeted tests — the common cases — never prompt. |

## Verification

Hook behaviour is testable locally at negligible cost (piping a JSON payload to the hook
script), which is itself within the allowed-local set. Confirm:

1. `dotnet build` → prompts.
2. `dotnet test` → prompts.
3. `dotnet test --filter Retention_Worker_Skips_When_LegalHold` → silent.
4. `pnpm dev` → silent.
5. `pnpm typecheck` → prompts.
6. `git push` → silent.

Same six against `pretooluse.ps1`.

Documentation changes are verified by reading: CLAUDE.md must contain no instruction to
run a full build, and the three instruction files must not disagree.

## Out of scope / follow-ups

- `subagents/*.md` already reference "AGENTS.md §0.5". Those references remain valid
  (the heading survives) but could later be repointed at CLAUDE.md.
