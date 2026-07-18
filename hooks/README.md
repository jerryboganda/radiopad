# RadioPad agent hooks

Small, auditable hooks for local AI-agent sessions. Every hook is **advisory** — it asks
for confirmation or emits a reminder, and never mutates source files.

## Hooks

| Hook | Event | What it does |
|---|---|---|
| `pretooluse.mjs` / `.ps1` | PreToolUse (`Bash`\|`PowerShell`) | Asks before destructive shell commands — recursive force-deletes outside a scoped path, `git reset --hard`, `git clean -fdx`, `Remove-Item -Recurse -Force`, Windows `del /s`, and `curl … \| sh` remote-exec. |
| `designlock.mjs` | PreToolUse (`Edit`\|`Write`\|`MultiEdit`) | Asks before a frontend `.tsx`/`.css` edit that introduces a hardcoded colour (hex/rgb/hsl), a forbidden UI library (MUI/Ant/Chakra/Bootstrap), or a legacy Hallmark alias — the RC design lock (CLAUDE.md rules 1 & 7). `frontend/app/tokens.css` is exempt. |
| `dotnet-format.mjs` | PostToolUse (`Edit`\|`Write`\|`MultiEdit`) | **Opt-in** (`RADIOPAD_DOTNET_FORMAT_HOOK=1`): applies `.editorconfig` whitespace fixes to the single edited `backend/`/`cli/` `.cs` file. No-op by default so it never slows the edit loop. |
| `release-reminder.mjs` | Stop | If the session's changes touch `frontend/` or `desktop/` (ships in the desktop bundle) but no desktop version bump is present, reminds you to run `pnpm release:desktop` (DESK-001). |
| `session-start.mjs` | SessionStart | Injects the easiest-to-forget guardrails (design lock, both-themes, DESK-001, CI-not-local) into context. |

Shared helpers live in `lib.mjs` / `lib.ps1` (payload read/write, path + command extraction).

## Input / output contract

Hooks receive JSON on stdin and emit JSON on stdout. `PreToolUse` returns
`hookSpecificOutput.permissionDecision`: `allow` for normal operations, `ask` for ones a
human should confirm. `Stop` / `SessionStart` return `continue: true` plus a `systemMessage`
or `hookSpecificOutput.additionalContext`. Exit code `0` means success.

## Activation

Claude Code reads hooks from `.claude/settings.json`, which is now **committed** (only
`.claude/settings.local.json` is gitignored), so the whole team gets the same wiring. Run
`claude` from the repo root so the relative `node hooks/<name>.mjs` paths resolve.

## Safety rules

- Keep hooks short and auditable; do not read secrets or print env vars.
- No long network calls; never mutate source files.
- Keep the Node and PowerShell variants behaviourally aligned.
