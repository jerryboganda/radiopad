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
