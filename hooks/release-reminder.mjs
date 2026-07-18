import { execSync } from 'node:child_process';
import { readHookPayload, writeHookResult } from './lib.mjs';

// Stop hook enforcing DESK-001 by reminder: if this session's changes touch anything that ships in
// the desktop bundle (frontend/ or desktop/) but no desktop version bump is present, nudge the
// operator to cut an auto-update release. Advisory only — never runs the release itself.

await readHookPayload(); // drain stdin

function gitFiles(cmd) {
  try {
    return execSync(cmd, { cwd: process.cwd(), stdio: ['ignore', 'pipe', 'ignore'] })
      .toString()
      .split('\n')
      .map((l) => l.trim())
      .filter(Boolean);
  } catch {
    return [];
  }
}

const changed = new Set([
  ...gitFiles('git diff --name-only HEAD'),
  ...gitFiles('git diff --name-only --cached'),
  ...gitFiles('git diff --name-only origin/main...HEAD'),
].map((f) => f.replace(/\\/g, '/')));

const files = [...changed];
const shipsDesktop = files.some((f) => /^(frontend|desktop)\//.test(f));
const versionTouched = files.some((f) =>
  /^desktop\/src-tauri\/(tauri\.conf\.json|Cargo\.toml)$/.test(f),
);

if (shipsDesktop && !versionTouched) {
  writeHookResult({
    continue: true,
    systemMessage:
      'DESK-001: files under frontend/ or desktop/ changed but no desktop version bump is present. ' +
      'After you commit + push, run `pnpm release:desktop` so the desktop auto-updater ships this change. ' +
      '(Backend-only / CLI-only / docs-only work does not need a release.)',
  });
} else {
  writeHookResult({ continue: true });
}
