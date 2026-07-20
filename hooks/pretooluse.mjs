import { commandFromPayload, readHookPayload, toolNameFromPayload, writeHookResult } from './lib.mjs';

const riskyCommands = [
  {
    pattern: /\brm\s+-(?:[A-Za-z]*r[A-Za-z]*f|[A-Za-z]*f[A-Za-z]*r)[^\n]*(?:\s\/|\s~|\$HOME|%USERPROFILE%|[A-Za-z]:\\)/i,
    reason: 'recursive forced deletion outside a clearly scoped project path',
  },
  {
    pattern: /\bgit\s+reset\s+--hard\b/i,
    reason: 'hard reset can discard user work',
  },
  {
    pattern: /\bgit\s+clean\s+-[^\n]*[fdx][^\n]*\b/i,
    reason: 'git clean can delete untracked user files',
  },
  {
    pattern: /\bRemove-Item\b[^\n]*(?:-Recurse|-r)\b[^\n]*(?:-Force|-fo)\b/i,
    reason: 'forced recursive deletion can discard user work',
  },
  {
    pattern: /\b(?:del|erase|rd|rmdir)\b[^\n]*(?:\/s|-s)\b[^\n]*(?:[A-Za-z]:\\|%USERPROFILE%|%HOMEPATH%|\\)/i,
    reason: 'Windows recursive deletion can discard user work',
  },
  {
    pattern: /(?:curl|Invoke-WebRequest|iwr)\b[^\n|]*\|\s*(?:sh|bash|powershell|pwsh|iex)\b/i,
    reason: 'remote script execution needs explicit human review',
  },
];

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

const payload = await readHookPayload();
const command = commandFromPayload(payload);
const toolName = toolNameFromPayload(payload);
const risky = command ? riskyCommands.find(({ pattern }) => pattern.test(command)) : null;
const heavy = !risky && command ? heavyCommands.find(({ pattern }) => pattern.test(command)) : null;
const match = risky || heavy;

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
  writeHookResult({
    continue: true,
    hookSpecificOutput: {
      hookEventName: 'PreToolUse',
      permissionDecision: 'allow',
    },
  });
}