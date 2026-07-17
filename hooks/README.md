# RadioPad agent guardrail

A single defensive hook for local AI-agent sessions: a **PreToolUse guard** that
asks for confirmation before potentially destructive shell commands — recursive
force-deletes outside a clearly scoped path, `git reset --hard`, `git clean -fdx`,
`Remove-Item -Recurse -Force`, Windows `del /s`, and `curl … | sh` remote-exec.
It only ever asks for confirmation; it never mutates files.

## Files

- `pretooluse.mjs` / `pretooluse.ps1` — the guard (Node + PowerShell mirrors).
- `lib.mjs` / `lib.ps1` — hook payload read/write helpers.

## Input / output contract

Hooks receive JSON on stdin and emit JSON on stdout. `PreToolUse` returns
`hookSpecificOutput.permissionDecision`: `allow` for normal commands, `ask` for
destructive operations. Exit code `0` means success; the guard prefers `ask` so a
human approves genuinely intended dangerous work.

## Activating it in Claude Code

Claude Code reads hooks from `.claude/settings.json`. In this repo `.claude/` is
**gitignored**, so the wiring is local per machine (a ready-to-use
`.claude/settings.json` is provided locally). Run `claude` from the repo root so
the relative `node hooks/pretooluse.mjs` path resolves. To share the guard with
the whole team, track a `settings.json` under a committed path instead.

## Safety rules

- Keep the guard short and auditable; do not read secrets or print env vars.
- No long network calls; never mutate source files.
- Keep the Node and PowerShell variants behaviourally aligned.
