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

const payload = await readHookPayload();
const command = commandFromPayload(payload);
const toolName = toolNameFromPayload(payload);
const match = command ? riskyCommands.find(({ pattern }) => pattern.test(command)) : null;

if (match) {
  writeHookResult({
    continue: true,
    systemMessage: `Open Design safety hook flagged ${toolName || 'a tool'}: ${match.reason}.`,
    hookSpecificOutput: {
      hookEventName: 'PreToolUse',
      permissionDecision: 'ask',
      permissionDecisionReason: `Potentially destructive command requires confirmation: ${match.reason}.`,
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