# Compute Discipline Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make two operator rules — heavy compute runs on GitHub Actions, and agents push then stop rather than watching CI — authoritative in CLAUDE.md, contradiction-free across every instruction file, and mechanically enforced by an advisory pre-tool-use guard.

**Architecture:** Documentation changes move the rules into CLAUDE.md (the file every agent is told wins) and reduce AGENTS.md/GEMINI.md to pointers, mirroring how the RC design-system rule is already structured. A `heavyCommands` array is added to the existing `pretooluse` hook — same shape, same advisory `ask` decision as the `riskyCommands` array already there — implemented twice (Node + PowerShell) because `.claude/settings.json` matches on both `Bash` and `PowerShell`. A new dependency-free test script pins the allow/ask boundary so the carve-outs can't silently regress.

**Tech Stack:** Node 20 ESM (`hooks/*.mjs`), Windows PowerShell 5.1 (`hooks/*.ps1`), Markdown. No new dependencies.

**Spec:** [docs/superpowers/specs/2026-07-19-ci-only-heavy-compute-design.md](../specs/2026-07-19-ci-only-heavy-compute-design.md)

## Global Constraints

- **Advisory only.** Every guard returns `permissionDecision: 'ask'`, never `'deny'`. `.claude/settings.json` documents the contract: "All hooks are advisory: they ask/remind, never mutate files or block outright."
- **`.mjs` and `.ps1` stay in lockstep.** Same patterns, same order, same reason strings. A rule present in one and absent from the other applies inconsistently depending on the agent's shell.
- **These must never prompt:** `pnpm dev`, `pnpm install`, `pnpm release:desktop`, `dotnet run`, `dotnet test --filter <Name>`, a single Vitest file, and every `git` command.
- **CLAUDE.md is authoritative.** AGENTS.md and GEMINI.md carry headings plus a one-sentence summary and a link — never a second copy of the rule body.
- **Do not rewrite historical documents.** `docs/_reports/ai-dev-env-audit-2026-07-18.md` and `docs/HANDOFF-desktop-first-surface-split.md` mention `gh run watch`; they record what was true when written and stay untouched.
- **Do not modify `.claude/commands/ci-watch.md`.** Watching is its entire purpose and it is operator-invoked.
- **This is a docs + hooks change.** Nothing ships in the desktop bundle, so DESK-001 does not apply — no `pnpm release:desktop` at the end.

## Deviation from the spec (accepted)

The spec's §6 asks the guard to also prompt on "`gh run view` with a `--log`/`--log-failed`
flag on a still-running job." A regex cannot know whether a run is still in progress, and
`gh run view --log-failed` against a *finished* run is fast and genuinely useful. This plan
therefore matches **only `gh run watch`** for rule 2. `gh run view` in all forms stays
allowed.

## File Structure

| File | Status | Responsibility |
| --- | --- | --- |
| `hooks/pretooluse.mjs` | Modify | Node guard; gains `heavyCommands` beside existing `riskyCommands` |
| `hooks/pretooluse.ps1` | Modify | PowerShell mirror of the same logic |
| `hooks/test-pretooluse.mjs` | Create | Dependency-free allow/ask assertions for the Node guard |
| `hooks/test-pretooluse.ps1` | Create | Same assertions against the PowerShell guard |
| `CLAUDE.md` | Modify | Authoritative text for both rules; `## Commands` rewrite; DESK-001 amend |
| `AGENTS.md` | Modify | §0.5 → pointer; new rule-2 pointer; strip watch at 3 sites |
| `GEMINI.md` | Modify | Two pointer sections |
| `subagents/test-runner.md` | Modify | Strip watch prescription |
| `.claude/agents/test-runner.md` | Modify | Duplicate of the above — strip in lockstep |
| `.claude/commands/release-desktop.md` | Modify | Drop the watch step; keep bump/tag/push |

---

### Task 1: Node guard + its test

The test script is the deliverable that makes every later claim checkable, so it comes
first and is written to fail.

**Files:**
- Create: `hooks/test-pretooluse.mjs`
- Modify: `hooks/pretooluse.mjs`

**Interfaces:**
- Consumes: `readHookPayload`, `writeHookResult`, `commandFromPayload`, `toolNameFromPayload` from `hooks/lib.mjs` (already imported at line 1; no change to `lib.mjs`).
- Produces: `hooks/pretooluse.mjs` emits `{ hookSpecificOutput: { permissionDecision: 'ask' | 'allow' } }` on stdout as JSON. `hooks/test-pretooluse.mjs` exits 0 when all cases pass, 1 otherwise.

- [ ] **Step 1: Write the failing test**

Create `hooks/test-pretooluse.mjs`:

```js
// Pins the allow/ask boundary of pretooluse.mjs. No dependencies; run with `node hooks/test-pretooluse.mjs`.
import { spawnSync } from 'node:child_process';
import { fileURLToPath } from 'node:url';
import path from 'node:path';

const here = path.dirname(fileURLToPath(import.meta.url));
const hook = path.join(here, 'pretooluse.mjs');

// 'ask' = guard prompts. 'allow' = runs silently.
const cases = [
  // Rule 1 — heavy compute belongs in CI.
  { command: 'dotnet build', expect: 'ask' },
  { command: 'dotnet build --configuration Release', expect: 'ask' },
  { command: 'dotnet test', expect: 'ask' },
  { command: 'pnpm build', expect: 'ask' },
  { command: 'pnpm typecheck', expect: 'ask' },
  { command: 'pnpm lint', expect: 'ask' },
  { command: 'pnpm --filter @radiopad/frontend build:desktop', expect: 'ask' },
  { command: 'npx next build', expect: 'ask' },
  { command: 'cargo build --release', expect: 'ask' },
  { command: 'cargo test', expect: 'ask' },
  { command: 'cargo tauri build', expect: 'ask' },
  { command: 'docker build -t radiopad .', expect: 'ask' },
  { command: 'docker compose build', expect: 'ask' },

  // Rule 2 — do not wait on CI.
  { command: 'gh run watch 12345', expect: 'ask' },

  // Carve-outs that must stay silent. A regression here breaks the operator's loop.
  { command: 'dotnet test --filter Retention_Worker_Skips_When_LegalHold', expect: 'allow' },
  { command: 'dotnet run --project src/RadioPad.Api', expect: 'allow' },
  { command: 'pnpm dev', expect: 'allow' },
  { command: 'pnpm install', expect: 'allow' },
  { command: 'pnpm release:desktop', expect: 'allow' },
  { command: 'pnpm vitest run frontend/lib/companion.test.ts', expect: 'allow' },
  { command: 'git push', expect: 'allow' },
  { command: 'git commit -m "wip"', expect: 'allow' },
  { command: 'gh run list -L 1', expect: 'allow' },
  { command: 'gh pr create --fill', expect: 'allow' },

  // Regression guard: the pre-existing riskyCommands must still fire.
  { command: 'git reset --hard', expect: 'ask' },
];

function decide(command) {
  const result = spawnSync(process.execPath, [hook], {
    input: JSON.stringify({ tool_name: 'Bash', tool_input: { command } }),
    encoding: 'utf8',
  });
  if (result.status !== 0) throw new Error(`hook exited ${result.status}: ${result.stderr}`);
  return JSON.parse(result.stdout).hookSpecificOutput.permissionDecision;
}

let failed = 0;
for (const { command, expect } of cases) {
  const actual = decide(command);
  const ok = actual === expect;
  if (!ok) failed += 1;
  console.log(`${ok ? 'PASS' : 'FAIL'}  ${expect.padEnd(5)}  ${command}${ok ? '' : `   → got '${actual}'`}`);
}
console.log(failed ? `\n${failed} of ${cases.length} failing` : `\nall ${cases.length} passing`);
process.exit(failed ? 1 : 0);
```

- [ ] **Step 2: Run it to verify it fails**

Run: `node hooks/test-pretooluse.mjs`

Expected: exit 1. Every `expect: 'ask'` case for a heavy command reports
`FAIL  ask  ...  → got 'allow'`, because `heavyCommands` does not exist yet. The
`allow` cases and the `git reset --hard` case already pass.

- [ ] **Step 3: Add the heavy-command patterns**

In `hooks/pretooluse.mjs`, insert this array immediately after the closing `];` of
`riskyCommands` (currently line 28):

```js
const heavyCommands = [
  {
    pattern: /\bdotnet\s+build\b/i,
    reason: 'a full dotnet build belongs in CI (ci.yml → backend job), not this laptop',
  },
  {
    pattern: /\bdotnet\s+test\b(?![^\n]*--filter)/i,
    reason: 'the full dotnet suite belongs in CI; locally run `dotnet test --filter <Name>`',
  },
  {
    pattern: /\bpnpm\s+(?:--filter\s+\S+\s+)?(?:run\s+)?(?:build|typecheck|lint)\b/i,
    reason: 'full frontend build/typecheck/lint belongs in CI (ci.yml → frontend job)',
  },
  {
    pattern: /\b(?:npx\s+)?next\s+build\b/i,
    reason: 'a Next.js production build belongs in CI',
  },
  {
    pattern: /\btauri\s+build\b/i,
    reason: 'desktop bundling runs on GitHub Actions only (desktop-bundle.yml)',
  },
  {
    pattern: /\bcargo\s+(?:build|test)\b/i,
    reason: 'cargo build/test is expensive; CI runs it',
  },
  {
    pattern: /\bdocker\s+(?:compose\s+)?build\b/i,
    reason: 'docker image builds run in CI, never on the laptop or the VPS',
  },
  {
    pattern: /\bgh\s+run\s+watch\b/i,
    reason: 'do not wait on CI — push and stop; the operator monitors runs and reports failures',
  },
];
```

Then replace the match line (currently line 33):

```js
const match = command ? riskyCommands.find(({ pattern }) => pattern.test(command)) : null;
```

with:

```js
const risky = command ? riskyCommands.find(({ pattern }) => pattern.test(command)) : null;
const heavy = !risky && command ? heavyCommands.find(({ pattern }) => pattern.test(command)) : null;
const match = risky || heavy;
```

And replace the `if (match)` body's two message strings so a heavy match reads as a
compute-discipline reminder rather than a destructive-command warning:

```js
if (match) {
  const label = risky ? 'safety guard' : 'compute-discipline guard';
  const detail = risky
    ? `Potentially destructive command requires confirmation: ${match.reason}.`
    : `RadioPad compute rule (CLAUDE.md): ${match.reason}.`;
  writeHookResult({
    continue: true,
    systemMessage: `RadioPad ${label} flagged ${toolName || 'a tool'}: ${match.reason}.`,
    hookSpecificOutput: {
      hookEventName: 'PreToolUse',
      permissionDecision: 'ask',
      permissionDecisionReason: detail,
    },
  });
} else {
```

Leave the `else` branch exactly as it is.

- [ ] **Step 4: Run the test to verify it passes**

Run: `node hooks/test-pretooluse.mjs`

Expected: exit 0, final line `all 25 passing`.

If `pnpm release:desktop` reports `ask`, the `pnpm` pattern is too greedy — it must
require `build`/`typecheck`/`lint` as a whole word, and `release:desktop` contains none.
If `dotnet test --filter …` reports `ask`, the negative lookahead is wrong.

- [ ] **Step 5: Commit**

```bash
git add hooks/pretooluse.mjs hooks/test-pretooluse.mjs
git commit -m "feat(hooks): prompt on heavy builds and CI waits (Node)"
```

---

### Task 2: PowerShell mirror + its test

**Files:**
- Create: `hooks/test-pretooluse.ps1`
- Modify: `hooks/pretooluse.ps1`

**Interfaces:**
- Consumes: `Read-HookPayload`, `Get-HookCommand`, `Get-HookToolName`, `Write-HookResult` from `hooks/lib.ps1` (already dot-sourced at line 1; no change to `lib.ps1`).
- Produces: identical JSON contract to Task 1 — `hookSpecificOutput.permissionDecision` of `'ask'` or `'allow'`.

- [ ] **Step 1: Write the failing test**

Create `hooks/test-pretooluse.ps1`:

```powershell
# Pins the allow/ask boundary of pretooluse.ps1. Run with: powershell -File hooks/test-pretooluse.ps1
$ErrorActionPreference = 'Stop'
$hook = Join-Path $PSScriptRoot 'pretooluse.ps1'

$cases = @(
    @{ Command = 'dotnet build'; Expect = 'ask' },
    @{ Command = 'dotnet build --configuration Release'; Expect = 'ask' },
    @{ Command = 'dotnet test'; Expect = 'ask' },
    @{ Command = 'pnpm build'; Expect = 'ask' },
    @{ Command = 'pnpm typecheck'; Expect = 'ask' },
    @{ Command = 'pnpm lint'; Expect = 'ask' },
    @{ Command = 'pnpm --filter @radiopad/frontend build:desktop'; Expect = 'ask' },
    @{ Command = 'npx next build'; Expect = 'ask' },
    @{ Command = 'cargo build --release'; Expect = 'ask' },
    @{ Command = 'cargo test'; Expect = 'ask' },
    @{ Command = 'cargo tauri build'; Expect = 'ask' },
    @{ Command = 'docker build -t radiopad .'; Expect = 'ask' },
    @{ Command = 'docker compose build'; Expect = 'ask' },
    @{ Command = 'gh run watch 12345'; Expect = 'ask' },
    @{ Command = 'dotnet test --filter Retention_Worker_Skips_When_LegalHold'; Expect = 'allow' },
    @{ Command = 'dotnet run --project src/RadioPad.Api'; Expect = 'allow' },
    @{ Command = 'pnpm dev'; Expect = 'allow' },
    @{ Command = 'pnpm install'; Expect = 'allow' },
    @{ Command = 'pnpm release:desktop'; Expect = 'allow' },
    @{ Command = 'pnpm vitest run frontend/lib/companion.test.ts'; Expect = 'allow' },
    @{ Command = 'git push'; Expect = 'allow' },
    @{ Command = 'git commit -m "wip"'; Expect = 'allow' },
    @{ Command = 'gh run list -L 1'; Expect = 'allow' },
    @{ Command = 'gh pr create --fill'; Expect = 'allow' },
    @{ Command = 'git reset --hard'; Expect = 'ask' }
)

$failed = 0
foreach ($case in $cases) {
    $payload = @{ tool_name = 'Bash'; tool_input = @{ command = $case.Command } } | ConvertTo-Json -Compress
    $stdout = $payload | & powershell -NoProfile -NonInteractive -File $hook
    $actual = ($stdout | ConvertFrom-Json).hookSpecificOutput.permissionDecision
    $ok = $actual -eq $case.Expect
    if (-not $ok) { $failed++ }
    $status = if ($ok) { 'PASS' } else { 'FAIL' }
    $suffix = if ($ok) { '' } else { "   -> got '$actual'" }
    Write-Output ("{0}  {1}  {2}{3}" -f $status, $case.Expect.PadRight(5), $case.Command, $suffix)
}

if ($failed -gt 0) {
    Write-Output ""
    Write-Output "$failed of $($cases.Count) failing"
    exit 1
}
Write-Output ""
Write-Output "all $($cases.Count) passing"
exit 0
```

- [ ] **Step 2: Run it to verify it fails**

Run: `powershell -NoProfile -File hooks/test-pretooluse.ps1`

Expected: exit 1, with the heavy-command cases reporting `FAIL  ask  ...  -> got 'allow'`.

- [ ] **Step 3: Add the heavy-command patterns**

In `hooks/pretooluse.ps1`, insert immediately after the closing `)` of `$riskyCommands`
(currently line 32):

```powershell
$heavyCommands = @(
    [pscustomobject]@{
        Pattern = '\bdotnet\s+build\b'
        Reason = 'a full dotnet build belongs in CI (ci.yml -> backend job), not this laptop'
    },
    [pscustomobject]@{
        Pattern = '\bdotnet\s+test\b(?![^\n]*--filter)'
        Reason = 'the full dotnet suite belongs in CI; locally run `dotnet test --filter <Name>`'
    },
    [pscustomobject]@{
        Pattern = '\bpnpm\s+(?:--filter\s+\S+\s+)?(?:run\s+)?(?:build|typecheck|lint)\b'
        Reason = 'full frontend build/typecheck/lint belongs in CI (ci.yml -> frontend job)'
    },
    [pscustomobject]@{
        Pattern = '\b(?:npx\s+)?next\s+build\b'
        Reason = 'a Next.js production build belongs in CI'
    },
    [pscustomobject]@{
        Pattern = '\btauri\s+build\b'
        Reason = 'desktop bundling runs on GitHub Actions only (desktop-bundle.yml)'
    },
    [pscustomobject]@{
        Pattern = '\bcargo\s+(?:build|test)\b'
        Reason = 'cargo build/test is expensive; CI runs it'
    },
    [pscustomobject]@{
        Pattern = '\bdocker\s+(?:compose\s+)?build\b'
        Reason = 'docker image builds run in CI, never on the laptop or the VPS'
    },
    [pscustomobject]@{
        Pattern = '\bgh\s+run\s+watch\b'
        Reason = 'do not wait on CI - push and stop; the operator monitors runs and reports failures'
    }
)
```

Then replace the match block (currently lines 34-37):

```powershell
$match = $null
if ($command) {
    $match = $riskyCommands | Where-Object { $command -match $_.Pattern } | Select-Object -First 1
}
```

with:

```powershell
$match = $null
$isRisky = $false
if ($command) {
    $match = $riskyCommands | Where-Object { $command -match $_.Pattern } | Select-Object -First 1
    if ($match) {
        $isRisky = $true
    }
    else {
        $match = $heavyCommands | Where-Object { $command -match $_.Pattern } | Select-Object -First 1
    }
}
```

Then replace the two message strings inside `if ($match) { ... }`:

```powershell
if ($match) {
    $label = if ($isRisky) { 'safety guard' } else { 'compute-discipline guard' }
    $detail = if ($isRisky) {
        "Potentially destructive command requires confirmation: $($match.Reason)."
    } else {
        "RadioPad compute rule (CLAUDE.md): $($match.Reason)."
    }
    Write-HookResult ([ordered]@{
        continue = $true
        systemMessage = "RadioPad $label flagged $(if ($toolName) { $toolName } else { 'a tool' }): $($match.Reason)."
        hookSpecificOutput = [ordered]@{
            hookEventName = 'PreToolUse'
            permissionDecision = 'ask'
            permissionDecisionReason = $detail
        }
    })
}
```

Leave the `else` branch exactly as it is.

- [ ] **Step 4: Run the test to verify it passes**

Run: `powershell -NoProfile -File hooks/test-pretooluse.ps1`

Expected: exit 0, final line `all 25 passing`.

Note: `-match` is case-insensitive by default in PowerShell, so the patterns carry no
`(?i)` prefix — unlike the `.mjs` versions, which need the `/i` flag.

- [ ] **Step 5: Commit**

```bash
git add hooks/pretooluse.ps1 hooks/test-pretooluse.ps1
git commit -m "feat(hooks): mirror the heavy-command guard in PowerShell"
```

---

### Task 3: CLAUDE.md — the authoritative rules

**Files:**
- Modify: `CLAUDE.md:31` (DESK-001 amend), after `CLAUDE.md:35` (new sections), `CLAUDE.md` `## Commands` block

- [ ] **Step 1: Amend DESK-001**

At line 31, the sentence currently ends:

> `...so every user auto-downloads the new build. Confirm the run is green with `gh run watch`.`

Delete the final sentence so it ends at `...auto-downloads the new build.` Nothing else
in that paragraph changes.

- [ ] **Step 2: Insert the two rule sections**

After the DESK-001 bullet list (currently ending at line 35, the `Signing secrets` bullet)
and before `## ⚠️ MISSION-CRITICAL: three specialised surfaces from one frontend`, insert:

```markdown
## ⚠️ MISSION-CRITICAL: CPU-intensive work runs on GitHub Actions, never locally

**All heavy, CPU/RAM-intensive work for this project — full builds, full test suites, lint and type-check sweeps, static analysis, bundling, desktop/mobile packaging, Docker image builds, and coverage — runs on GitHub Actions. Not on the development laptop. Not on the VPS.** This is a permanent project rule that binds every agent and contributor.

Why: the development machine is low-spec and saturating it stalls the whole session, and the production VPS (`/opt/radiopad`) hosts live tenants — a compiler or test run there risks the live site. CI runners are disposable, parallel, and free for this purpose.

1. **Do not run full builds, full test suites, or lint/type-check sweeps** locally or on the VPS. Commit and push; GitHub Actions runs them.
2. **Allowed locally** — focused, cheap feedback only: editing and reading code, one targeted unit test (`dotnet test --filter <Name>`, or a single Vitest file), and running the app to look at a change (`pnpm dev`, `dotnet run`). Anything that compiles the whole solution or the whole frontend, or runs a whole suite, belongs in CI.
3. **The production VPS runs the app only.** Never invoke `dotnet build/test`, `pnpm build/lint`, `cargo build`, or `docker compose build` there for development. Deploys pull pre-built images produced by CI.
4. **Every merge-gating task lives in `.github/workflows/`.** If a heavy task is not yet covered by a workflow, add the workflow in the same change rather than running it by hand.
5. **Green CI is the evidence, not local output.** "It builds on my machine" proves nothing.

A pre-tool-use guard (`hooks/pretooluse.mjs` / `.ps1`) prompts on these commands. If it fires, the answer is almost always to push instead — not to approve it.

## ⚠️ MISSION-CRITICAL: code, don't babysit

**Spend the session writing code, not waiting on machines. Do small quick checks only. When the work is done, commit, push, and stop.** The operator monitors CI and will report failures.

1. **Never watch or poll CI.** No `gh run watch`, no sleep-and-retry loops, no waiting for a run to finish — including after a desktop release. Push and end the turn.
2. **Checks stay small.** The allowed-locally set above is the ceiling: a targeted test, a look at the running app. Do not chain verification steps hunting for confidence.
3. **Report honestly.** Because CI is unobserved, say what you changed and that CI will decide it. **Never claim a change builds, passes, or works when you have not seen it do so.** Push-and-stop is only safe if the report doesn't overstate.
4. **The operator reports errors.** If something is broken, they will say so. Do not pre-emptively re-verify finished work.
```

- [ ] **Step 3: Rewrite the `## Commands` block**

Replace the whole existing block — currently:

````markdown
## Commands

```powershell
# Backend
cd backend/RadioPad.Api && dotnet build && dotnet test
dotnet run --project src/RadioPad.Api    # → http://127.0.0.1:7457

# Frontend
cd frontend && pnpm install && pnpm dev   # → http://localhost:3000
pnpm typecheck && pnpm build

# CLI
dotnet run --project cli/RadioPad.Cli -- rulebook validate ../../rulebooks/chest_ct_v1.yaml
```
````

with:

````markdown
## Commands

**Allowed locally** — cheap, focused feedback:

```powershell
# Run the app
dotnet run --project backend/RadioPad.Api/src/RadioPad.Api   # → http://127.0.0.1:7457
cd frontend && pnpm install && pnpm dev                      # → http://localhost:3000

# One targeted test — never the whole suite
dotnet test --filter <TestName>
pnpm vitest run <one-file>

# CLI
dotnet run --project cli/RadioPad.Cli -- rulebook validate ../../rulebooks/chest_ct_v1.yaml
```

**CI runs these — do not run them locally or on the VPS.** `.github/workflows/ci.yml`
covers all of them on every push (backend build+test, CLI, frontend lint+typecheck+test+build):

```powershell
dotnet build          # ci.yml → backend
dotnet test           # ci.yml → backend  (targeted --filter runs are fine locally)
pnpm typecheck        # ci.yml → frontend
pnpm build            # ci.yml → frontend
cargo tauri build     # desktop-bundle.yml
```

Push and stop — do not watch the run.
````

- [ ] **Step 4: Verify no contradiction survives**

Run: `grep -nE "gh run watch|dotnet build && dotnet test|pnpm typecheck && pnpm build" CLAUDE.md`

Expected: no output. Any hit is a leftover instruction that contradicts the new sections.

- [ ] **Step 5: Commit**

```bash
git add CLAUDE.md
git commit -m "docs(claude): make the two compute-discipline rules authoritative"
```

---

### Task 4: AGENTS.md — pointers, watch prescriptions removed

**Files:**
- Modify: `AGENTS.md:32-45` (§0.5 body), `AGENTS.md:62`, `AGENTS.md:135`, `AGENTS.md:142`

- [ ] **Step 1: Replace the §0.5 body with a pointer**

Keep the heading at line 32. Replace lines 34-44 (the blockquote, the "Why", and the
five numbered rules) with:

```markdown
> **All heavy, CPU/RAM-intensive work — full builds, full test suites, lint/type-check sweeps, bundling, packaging, Docker builds — runs on GitHub Actions, never on the development laptop or the VPS.**

The full contract lives in [CLAUDE.md](CLAUDE.md) §"CPU-intensive work runs on GitHub Actions". Read it there; it is authoritative. Locally you may edit code, run one targeted test (`dotnet test --filter <Name>` or a single Vitest file), and run the app (`pnpm dev`, `dotnet run`) — nothing that compiles the whole solution or frontend, or runs a whole suite.
```

- [ ] **Step 2: Add the rule-2 pointer**

Immediately after the §0.5 block and before `## 0.6.`, insert:

```markdown
---

## 0.5.1. MISSION-CRITICAL: CODE, DON'T BABYSIT

> **Spend the session writing code. Do small quick checks only. Commit, push, and stop — never watch or poll CI. The operator monitors runs and reports failures.**

Full contract: [CLAUDE.md](CLAUDE.md) §"code, don't babysit". Because CI is unobserved, report what changed and let CI decide — never claim a change builds or passes when you have not seen it do so.
```

- [ ] **Step 3: Strip the watch instruction at line 62**

Currently:

> `3. The app's button reads ...latest.json`, so every user auto-downloads the new build. Verify the run is green (`gh run watch …`); don't hand the operator manual steps.`

Replace the trailing clause so it reads:

> `3. The app's button reads ...latest.json`, so every user auto-downloads the new build. Push and stop — don't watch the run, and don't hand the operator manual steps.`

- [ ] **Step 4: Update lines 135 and 142**

Line 135 currently ends `— see §0.5. Do not run them locally or on the VPS.` Leave that
sentence; it is still correct.

Line 142 currently reads:

> `Locally, at most run a single targeted test for the thing you changed (`dotnet test --filter <Name>`), then push and watch CI: `gh run watch` / `gh run view --log-failed`.`

Replace with:

> `Locally, at most run a single targeted test for the thing you changed (`dotnet test --filter <Name>`), then commit and push. Do not watch the run — see §0.5.1.`

Also delete line 131 (`cargo tauri build          # in desktop/ after `pnpm build` in frontend/`)
from the local-commands block, since both halves of it are now CI-only. Leave line 132
(`npx cap copy android`) — it is a cheap file copy, not a build.

- [ ] **Step 5: Verify**

Run: `grep -n "gh run watch" AGENTS.md`

Expected: no output.

- [ ] **Step 6: Commit**

```bash
git add AGENTS.md
git commit -m "docs(agents): point at CLAUDE.md for both compute rules"
```

---

### Task 5: GEMINI.md — pointers

**Files:**
- Modify: `GEMINI.md` (insert before `## Strict tech stack`, currently line 13)

- [ ] **Step 1: Insert both pointer sections**

After the `## ⚠️ UI/UX is LOCKED` section and before `## Strict tech stack`, insert:

```markdown
## ⚠️ Compute runs on GitHub Actions — not this machine

**All heavy work — full builds, full test suites, lint/type-check sweeps, bundling, packaging, Docker builds — runs on GitHub Actions, never on the development laptop or the VPS.** Locally: edit code, run one targeted test (`dotnet test --filter <Name>` or a single Vitest file), run the app (`pnpm dev`, `dotnet run`). Nothing more. Full contract in [CLAUDE.md](CLAUDE.md).

## ⚠️ Code, don't babysit

**Write code; don't wait on machines.** Small quick checks only. When the work is done, commit, push, and stop — never watch or poll CI (`gh run watch` is out). The operator monitors runs and reports failures. Because CI is unobserved, report what you changed and let CI decide — never claim a change builds or passes when you have not seen it do so. Full contract in [CLAUDE.md](CLAUDE.md).
```

- [ ] **Step 2: Verify**

Run: `grep -c "CLAUDE.md" GEMINI.md`

Expected: a count at least 2 higher than before the edit — the pointers must link, not restate.

- [ ] **Step 3: Commit**

```bash
git add GEMINI.md
git commit -m "docs(gemini): add compute-discipline pointers"
```

---

### Task 6: Strip the remaining watch prescriptions

These three files each tell an agent to wait on CI. `subagents/test-runner.md` and
`.claude/agents/test-runner.md` are duplicates with identical text — editing one and not
the other leaves the rule half-applied, which is the exact defect this whole change exists
to fix.

**Files:**
- Modify: `subagents/test-runner.md:14`
- Modify: `.claude/agents/test-runner.md:15`
- Modify: `.claude/commands/release-desktop.md:2,11-14`

- [ ] **Step 1: Update both test-runner definitions**

In **both** files the line currently ends:

> `... — push and watch CI (`gh run watch` / `gh run view --log-failed`).`

Replace that trailing clause in both so the line ends:

> `... — commit and push. Do not watch the run; the operator monitors CI and reports failures.`

The rest of each line (the `dotnet test --filter` carve-out, the §0.5 reference) is
unchanged.

- [ ] **Step 2: Update the release-desktop command**

In `.claude/commands/release-desktop.md`:

Change the frontmatter `description` (line 2) from:

> `Cut a desktop auto-update release (DESK-001) and watch the pipeline to green.`

to:

> `Cut a desktop auto-update release (DESK-001): bump, tag, push, stop.`

Delete steps 3 and 4 entirely (lines 11-13 — the `gh run watch` invocation and the
`gh release view` confirmation), and replace step 5 (line 14) with:

```markdown
3. Report the new version and the tag that was pushed, then stop. Do not watch the run — `desktop-bundle` and `tauri-updater` take it from here, and the operator monitors CI.
```

Also trim the now-unused `Bash(gh run:*), Bash(gh release:*)` entries from the
`allowed-tools` line (line 4), leaving:

```markdown
allowed-tools: Bash(git status:*), Bash(git rev-parse:*), Bash(git log:*), Bash(pnpm release:desktop:*)
```

Leave line 16 (`Never hand-edit only one version file...`) exactly as it is.

- [ ] **Step 3: Verify the whole repo**

Run:

```bash
grep -rn "gh run watch" --include=*.md . | grep -v "docs/_reports/\|docs/HANDOFF-\|docs/superpowers/\|.claude/commands/ci-watch.md"
```

Expected: no output. The excluded paths are deliberate — two historical records, the
spec/plan themselves, and the operator-invoked `ci-watch` command.

- [ ] **Step 4: Commit**

```bash
git add subagents/test-runner.md .claude/agents/test-runner.md .claude/commands/release-desktop.md
git commit -m "docs(agents): drop CI-watch steps from subagents and release command"
```

---

### Task 7: Final consistency pass and push

**Files:**
- Modify: `docs/superpowers/specs/2026-07-19-ci-only-heavy-compute-design.md` (date correction)

- [ ] **Step 1: Fix the spec's date**

The spec header reads `**Date:** 2026-07-19`; it was written on 2026-07-20. Correct both
the `Date:` line and leave the filename as-is (renaming would break the link in this
plan's header and in git history).

Change `**Date:** 2026-07-19` to `**Date:** 2026-07-20`.

- [ ] **Step 2: Re-run both hook test suites**

```bash
node hooks/test-pretooluse.mjs
powershell -NoProfile -File hooks/test-pretooluse.ps1
```

Expected: both exit 0 with `all 25 passing`. This is the only verification step in the
plan that runs twice — the guards are the one piece of behaviour, and both shells must
agree.

- [ ] **Step 3: Confirm no instruction file contradicts another**

```bash
grep -rn "gh run watch\|dotnet build && dotnet test\|pnpm typecheck && pnpm build" CLAUDE.md AGENTS.md GEMINI.md
```

Expected: no output.

- [ ] **Step 4: Commit and push**

```bash
git add docs/superpowers/specs/
git commit -m "docs(spec): correct authoring date"
git push
```

- [ ] **Step 5: Stop**

Report which files changed and that CI will validate the push. **Do not run
`gh run watch`** — that is the rule this change exists to establish, and the guard added
in Task 1 will now prompt if you try. No desktop release is needed: nothing under
`frontend/` or `desktop/` was touched.

---

## Self-Review

**Spec coverage** — every spec section maps to a task: §1 → Task 3 Step 2; §2 → Task 3
Step 3; §3 → Task 3 Step 1; §4 → Task 4; §5 → Task 5; §6 → Task 1; §7 → Task 2; §8 table
→ Task 4 (AGENTS.md rows) and Task 6 (the four remaining rows); §8's "leave unchanged"
rows → Global Constraints. The spec's Verification list of 8 cases is covered by the 25
cases in Tasks 1-2, which is a superset.

**One accepted deviation**, flagged above rather than silently applied: the guard matches
only `gh run watch`, not `gh run view --log-*`, because "still-running" is not
expressible as a regex.

**Type consistency** — `heavyCommands` / `$heavyCommands` carry identical patterns in the
same order across both files; `risky`/`heavy`/`match` in the `.mjs` correspond to
`$match`/`$isRisky` in the `.ps1`; both emit the same
`hookSpecificOutput.permissionDecision` contract the two test scripts parse.
