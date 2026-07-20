# Design: compute discipline — heavy work on CI, no local babysitting

**Date:** 2026-07-19
**Status:** Approved (design), pending implementation

Two related operator rules, both currently unenforced:

- **Rule 1 — heavy compute belongs on GitHub Actions**, never the laptop or the VPS.
- **Rule 2 — code, don't babysit.** Small quick checks only; the bulk of a session is
  editing code. Commit and push when the work is done, then stop. The operator reports
  errors.

They share one motive (the development laptop is low-spec and must not be saturated or
tied up) and one failure mode (the rule is written down in a file that loses to another
file), so they are specified and implemented together.

## Problem

### Rule 1 already exists and is still ignored

[AGENTS.md](../../../AGENTS.md) §0.5 states rule 1 clearly and completely. It is
nevertheless not followed, for three mechanical reasons:

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

### Rule 2 is written nowhere, and CLAUDE.md contradicts it too

No instruction file states rule 2. Meanwhile [CLAUDE.md](../../../CLAUDE.md) §DESK-001
ends with *"Confirm the run is green with `gh run watch`."* — a blocking wait of several
minutes that the operator has now explicitly ruled out. `gh run watch` also appears
throughout AGENTS.md §0.5 and in `subagents/*.md` as the prescribed follow-up to a push.

This is the same defect as rule 1, one layer along: the instruction files tell agents to
do the thing the operator does not want.

## Goals

- Make both rules authoritative and impossible to read past.
- Remove every contradicting instruction, including the `gh run watch` prescriptions.
- Add a mechanical prompt so an agent that ignores the prose still gets stopped.
- Preserve the fast local feedback loop the operator relies on.
- Keep completion claims honest once CI is no longer observed (see Decisions).

## Non-goals

- Changing any CI workflow. Coverage is already sufficient.
- Hard-blocking heavy commands. Advisory prompts only.
- Restricting the production VPS beyond what AGENTS.md §0.5 already says.
- Removing the `ci-watch` skill or the `release-reminder` Stop hook. Both remain
  available for the operator to invoke deliberately; they simply stop being routine
  agent behaviour.

## Decisions

| Decision | Choice | Rationale |
| --- | --- | --- |
| Enforcement strength | **Prompt for confirmation**, not hard deny | Matches the existing hook contract — `.claude/settings.json` states "All hooks are advisory: they ask/remind, never mutate files or block outright." Lets the operator approve a genuine one-off without disabling the hook. |
| Canonical location | **Full text in CLAUDE.md**; AGENTS.md and GEMINI.md become pointers | Consistent with how the RC design-system rule is already structured across the three files. Single source of truth, no drift. |
| `pnpm dev` / `dotnet run` | Allowed, no prompt | Dev servers are incremental and idle most of the time. The operator verifies changes by running the app. |
| Single targeted test | Allowed, no prompt | `dotnet test --filter <Name>` / one Vitest file. Seconds of cost, high feedback value. Already carved out by §0.5. |
| Full `pnpm typecheck` | **Prompts** | A whole-frontend `tsc` sweep is minutes and heavy RAM. `ci.yml` already runs it. |
| `git` / `gh` (non-blocking) | Allowed, no prompt | Near-zero cost, and they are the mechanism by which work reaches CI. |
| After a push | **Push and stop.** No `gh run watch`, no polling, no waiting — including for desktop releases | Operator's explicit instruction; they monitor CI themselves and report failures. |
| How completion is reported | State what changed and that CI will decide. **Do not claim the change works.** | Once CI is unobserved, "it passes" would be an unverified assertion. Honest reporting is what makes push-and-stop safe. |

The last row matters more than it looks. Dropping verification and *also* dropping the
habit of hedging would mean confidently reporting success nobody checked. The rule is
"don't wait for CI", not "assume CI is green."

## Design

### 1. CLAUDE.md — two new authoritative sections

Add two `## ⚠️ MISSION-CRITICAL` sections alongside the existing design-system and
DESK-001 sections.

**Rule 1 section** carries the full text adapted from AGENTS.md §0.5: the scope of
"heavy", the rationale (low-spec laptop; shared VPS hosts live tenants), the
allowed-locally carve-out, the VPS prohibition, and the requirement that uncovered heavy
tasks gain a workflow rather than be run by hand.

**Rule 2 section** states: the bulk of a session is coding; local checking is limited to
the allowed-locally set; when the work is done, commit and push and stop; do not watch,
poll, or wait on CI; the operator reports failures. It also carries the honest-reporting
requirement from the Decisions table.

### 2. CLAUDE.md — fix the contradicting `## Commands` block

Current content instructs full builds. Restructure into two explicitly labelled groups:

- **Allowed locally** — `dotnet run --project src/RadioPad.Api`, `pnpm dev`,
  `dotnet test --filter <Name>`, a single Vitest file, the CLI rulebook-validate command.
- **CI runs these — do not run locally** — `dotnet build`, full `dotnet test`,
  `pnpm typecheck`, `pnpm build`, naming `ci.yml` as the thing that covers them.

Note: the replacement instruction is *push*, not *push and watch*.

### 3. CLAUDE.md — amend DESK-001

Remove the trailing *"Confirm the run is green with `gh run watch`."* The release
procedure otherwise stands unchanged: `pnpm release:desktop` still runs automatically
after any `frontend/` or `desktop/` change, and the tag still drives the pipeline
end-to-end. Only the blocking confirmation step goes.

### 4. AGENTS.md — shrink §0.5 to a pointer, add §0.6-bis for rule 2

Replace the body of §0.5 with a short pointer to the CLAUDE.md section, keeping the
heading and a one-sentence summary so the rule stays visible when skimming. Mirrors how
§0.1 points at the design-system contract. Add a matching short pointer section for
rule 2. Strip the `gh run watch` prescriptions from both.

### 5. GEMINI.md — add pointers

Two short sections matching AGENTS.md's, so Gemini sessions see both rules.

### 6. `hooks/pretooluse.mjs` — heavy-command prompt

Add a `heavyCommands` array alongside the existing `riskyCommands`, evaluated the same
way and yielding `permissionDecision: 'ask'`.

**Rule 1 matches (prompt):** `dotnet build`, `dotnet test` *without* `--filter`,
`pnpm build`, `pnpm typecheck`, `pnpm lint`, `cargo build`, `cargo test`, `next build`,
`tauri build`, `docker build`, `docker compose build`.

**Rule 2 matches (prompt):** `gh run watch`, and `gh run view` with a `--log`/`--log-failed`
flag on a still-running job. These are the long blocking waits.

**Does not match (allowed):** `pnpm dev`, `dotnet run`, `dotnet test --filter …`,
`vitest run <file>`, `git` in all forms, and non-blocking `gh` (`gh run list`, `gh pr
create`).

Each prompt reason names the rule and the alternative, so a stopped agent knows the next
move rather than retrying a variant.

### 7. `hooks/pretooluse.ps1` — lockstep mirror

`pretooluse.ps1` is a parallel PowerShell implementation of the same logic, and
`.claude/settings.json` matches on both `Bash` and `PowerShell`. It must receive the
equivalent `$heavyCommands` list, or the guard applies inconsistently depending on which
shell the agent chose.

### 8. Drop the remaining watch prescriptions

Verified by grep — the prescription appears in more places than the CLAUDE.md/AGENTS.md
sections above:

| File | Where | Action |
| --- | --- | --- |
| `AGENTS.md` | lines 40, 62, 142 | Trim to "push". Line 62 is inside the release section and also says "verify the run is green". |
| `subagents/test-runner.md` | line 14 | Trim to "push". |
| `.claude/agents/test-runner.md` | line 15 | **Duplicate of the above, identical text.** Must be edited in lockstep or the rule half-applies. |
| `.claude/commands/release-desktop.md` | lines 12, 14 | Watches the release run to completion and reports the conclusion. Since DESK-001 has agents invoke the release automatically, this is the likeliest route to an accidental long wait. Trim the watch step; keep the version bump, tag, and push. |
| `.claude/commands/ci-watch.md` | lines 10–11 | **Leave unchanged.** Its entire purpose is watching, and it is operator-invoked. |
| `docs/_reports/ai-dev-env-audit-2026-07-18.md`, `docs/HANDOFF-desktop-first-surface-split.md` | — | **Leave unchanged.** Historical records of what was true when written; rewriting them would falsify the record. |

`feature-dev.md` and `explorer.md` do **not** mention watching — they only reference
§0.5, which stays valid since that heading survives. No change needed.

## Risks

| Risk | Mitigation |
| --- | --- |
| The `--filter` carve-out regex is wrong, blocking the operator's fast test loop | Negative lookahead on `dotnet test`; verify both `dotnet test` (prompts) and `dotnet test --filter Foo` (silent) before committing. |
| `.mjs` and `.ps1` drift | Both edited in the same change; patterns kept in the same order with identical reasons. |
| Prompt fatigue causes reflexive approval | Scope limited to genuinely expensive or genuinely blocking commands. Dev servers, targeted tests, and ordinary git/gh never prompt. |
| Push-and-stop degrades into unverified success claims | Encoded explicitly in the rule 2 section and the Decisions table: report what changed, state that CI decides, never assert it passes. |
| A broken desktop release ships to users unobserved | Accepted by the operator, who monitors CI. The release pipeline is unchanged and still fails loudly in Actions. |

## Verification

Hook behaviour is testable locally at negligible cost (piping a JSON payload to the hook
script), which is itself within the allowed-local set. Confirm:

1. `dotnet build` → prompts.
2. `dotnet test` → prompts.
3. `dotnet test --filter Retention_Worker_Skips_When_LegalHold` → silent.
4. `pnpm dev` → silent.
5. `pnpm typecheck` → prompts.
6. `git push` → silent.
7. `gh run watch` → prompts.
8. `gh run list -L 1` → silent.

Same eight against `pretooluse.ps1`.

Documentation changes are verified by reading: CLAUDE.md must contain no instruction to
run a full build and no instruction to watch CI, and the three instruction files must not
disagree.

## Out of scope / follow-ups

- `subagents/*.md` reference "AGENTS.md §0.5". Those references remain valid (the heading
  survives) but could later be repointed at CLAUDE.md.
- The `ci-watch` skill stays in the repo as an operator-invoked tool. If it proves to be a
  temptation for agents, a later change could mark it operator-only in its description.
