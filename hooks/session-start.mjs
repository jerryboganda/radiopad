import { readHookPayload, writeHookResult } from './lib.mjs';

// SessionStart hook: surfaces the RadioPad guardrails that are easiest to forget, so every
// session opens grounded in them. Concise on purpose — the full rules live in CLAUDE.md.

await readHookPayload();

const additionalContext = [
  'RadioPad guardrails (full rules in CLAUDE.md / AGENTS.md):',
  '- Ralph-loop state lives in PROGRESS.md — update it when you finish a checklist item.',
  '- Design lock: tokens only (no hardcoded hex/rgb/hsl), documented .rp-* classes, and every UI change verified in BOTH light and dark themes before it ships.',
  '- DESK-001: any change under frontend/ or desktop/ requires `pnpm release:desktop` before it reaches users (the desktop app self-updates).',
  '- Heavy builds/tests run in CI, not locally — push and STOP. Never watch or poll a run (no `gh run watch`); the operator monitors CI.',
  '- Prefer Serena symbol tools + codegraph_explore over grep/whole-file reads.',
].join('\n');

writeHookResult({
  continue: true,
  hookSpecificOutput: { hookEventName: 'SessionStart', additionalContext },
});
