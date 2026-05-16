# Hooks Layer

Hooks are deterministic lifecycle automation for agent sessions. They enforce simple project policy and inject short runtime context without relying on model obedience.

The tracked workspace manifest is `.github/hooks/open-design-agent-kit.json`. It points at Node scripts for general runtimes and PowerShell `windows` overrides for Windows runtimes where `node` may not be on PATH. Shell wrappers are included for agents that expect the Claude-style names shown in the five-layer diagrams.

## Registered Events

| Event | Script | Purpose |
|---|---|---|
| `SessionStart` | `session-start.mjs` | Inject concise project architecture and safety context. |
| `PreToolUse` | `pretooluse.mjs` | Ask for confirmation before potentially destructive shell commands. |
| `PostToolUse` | `posttooluse.mjs` | Remind agents to validate when source or test files were touched. |
| `SubagentStop` | `subagent-stop.mjs` | Ask parent agents to summarize subagent results instead of pasting logs. |
| `Stop` | `stop.mjs` | Remind agents to report changed files, checks, and limitations. |

## Input And Output Contract

Hooks receive JSON on stdin and emit JSON on stdout. The scripts are permissive about input shape because different agent runtimes name fields differently.

`PreToolUse` returns `hookSpecificOutput.permissionDecision`:

- `allow` for normal commands
- `ask` for destructive operations such as recursive deletes, hard resets, forced cleans, or piped remote execution

Exit code `0` means success. These hooks do not intentionally use exit code `2`; they prefer `ask` so a human can approve truly intended dangerous work.

## Local Runtime Notes

Do not commit `.claude/settings.json`, `.claude/settings.local.json`, `.opencode/`, `.codex/`, or `.agents/`. If an agent runtime needs a local hook manifest, copy or mirror `.github/hooks/open-design-agent-kit.json` into that runtime's expected ignored location.

## Safety Rules

- Hooks must stay short and auditable.
- Hooks must not read secrets or print environment variables.
- Hooks must not perform long network calls.
- Hooks must not mutate source files unless a future feature explicitly documents that behavior.

## Review Checklist

Before changing a hook script or manifest:

1. Confirm commands are relative repo paths, not absolute paths or remote shell snippets.
2. Confirm both Node and PowerShell variants stay behaviorally aligned.
3. Confirm scripts only read stdin and write JSON to stdout unless a future feature documents another side effect.
4. Confirm destructive commands return `permissionDecision: ask` or a stricter decision.
5. Run `pnpm test -- --run tests/agent-kit-docs.test.ts` when Node and pnpm are available.