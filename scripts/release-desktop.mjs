#!/usr/bin/env node
// RadioPad desktop release — one command to ship an auto-update.
//
// What it does (DESK-001 auto-update pipeline):
//   1. Bumps the desktop version (patch by default) in BOTH
//      desktop/src-tauri/tauri.conf.json and desktop/src-tauri/Cargo.toml
//      (they MUST stay in lock-step or the updater loops).
//   2. Commits just those two files and creates an annotated `vX.Y.Z` tag.
//   3. Pushes the branch + tag.
//
// Pushing the tag triggers GitHub Actions end-to-end with ZERO further steps:
//   desktop-bundle  → builds + signs the Windows .msi / Linux .AppImage,
//                     creates the GitHub Release, attaches the installers.
//   tauri-updater   → signs `latest.json` and uploads it to that release.
// The in-app "Check for updates" button reads
//   https://github.com/jerryboganda/radiopad/releases/latest/download/latest.json
// so every user gets the new build automatically. Do NOT build locally.
//
// Usage:
//   node scripts/release-desktop.mjs              # bump patch  (0.1.23 -> 0.1.24)
//   node scripts/release-desktop.mjs minor        # bump minor  (0.1.23 -> 0.2.0)
//   node scripts/release-desktop.mjs major        # bump major  (0.1.23 -> 1.0.0)
//   node scripts/release-desktop.mjs 0.5.0        # set an explicit version
//   pnpm release:desktop                          # same, via the package script

import { readFileSync, writeFileSync } from 'node:fs';
import { execSync } from 'node:child_process';
import { fileURLToPath } from 'node:url';
import { dirname, join } from 'node:path';

const repoRoot = join(dirname(fileURLToPath(import.meta.url)), '..');
const confPath = join(repoRoot, 'desktop', 'src-tauri', 'tauri.conf.json');
const cargoPath = join(repoRoot, 'desktop', 'src-tauri', 'Cargo.toml');

const run = (cmd) => execSync(cmd, { cwd: repoRoot, stdio: 'pipe' }).toString().trim();
const die = (msg) => { console.error(`\n✖ ${msg}\n`); process.exit(1); };

// --- resolve the new version -------------------------------------------------
const conf = readFileSync(confPath, 'utf8');
const curMatch = conf.match(/"version":\s*"(\d+)\.(\d+)\.(\d+)"/);
if (!curMatch) die(`Could not find a semver "version" in ${confPath}`);
const [maj, min, pat] = curMatch.slice(1).map(Number);
const current = `${maj}.${min}.${pat}`;

const arg = (process.argv[2] || 'patch').trim();
let next;
if (arg === 'patch') next = `${maj}.${min}.${pat + 1}`;
else if (arg === 'minor') next = `${maj}.${min + 1}.0`;
else if (arg === 'major') next = `${maj + 1}.0.0`;
else if (/^\d+\.\d+\.\d+$/.test(arg)) next = arg;
else die(`Invalid argument "${arg}". Use: patch | minor | major | X.Y.Z`);

const tag = `v${next}`;
console.log(`RadioPad desktop release: ${current} → ${next}  (tag ${tag})`);

// --- safety checks -----------------------------------------------------------
try { run('git rev-parse --is-inside-work-tree'); } catch { die('Not inside a git repository.'); }
const branch = run('git rev-parse --abbrev-ref HEAD');
const existingTags = run('git tag --list').split('\n');
if (existingTags.includes(tag)) die(`Tag ${tag} already exists. Pick a different version.`);

// Refuse to release a dirty tree EXCEPT the two version files we are about to
// write — the actual feature changes must already be committed/pushed first.
const dirty = run('git status --porcelain')
  .split('\n')
  .filter(Boolean)
  .map((l) => l.slice(3))
  .filter((p) => p && !p.endsWith('tauri.conf.json') && !p.endsWith('src-tauri/Cargo.toml'));
if (dirty.length) {
  die(
    `Working tree has uncommitted changes:\n  ${dirty.join('\n  ')}\n` +
    `Commit/push your feature changes first, then run the release.`,
  );
}

// --- bump both files (lock-step) --------------------------------------------
writeFileSync(confPath, conf.replace(/"version":\s*"\d+\.\d+\.\d+"/, `"version": "${next}"`));
const cargo = readFileSync(cargoPath, 'utf8');
if (!/^version = "\d+\.\d+\.\d+"/m.test(cargo)) die(`Could not find package version in ${cargoPath}`);
writeFileSync(cargoPath, cargo.replace(/^version = "\d+\.\d+\.\d+"/m, `version = "${next}"`));
console.log('✓ bumped tauri.conf.json + Cargo.toml');

// --- commit, tag, push -------------------------------------------------------
run(`git commit -m "chore(desktop): release ${tag}" -- "${confPath}" "${cargoPath}"`);
run(`git tag -a "${tag}" -m "RadioPad ${tag}"`);
run(`git push origin "${branch}"`);
run(`git push origin "${tag}"`);
console.log(`✓ committed, tagged, and pushed ${tag} (branch ${branch})`);

console.log(`
Release pipeline is now running on GitHub Actions — nothing else to do:
  • desktop-bundle  builds + signs the installers and creates the release
  • tauri-updater   signs + publishes latest.json
Watch it:  gh run watch $(gh run list --workflow desktop-bundle.yml --event push --limit 1 --json databaseId --jq '.[0].databaseId')
Release:   https://github.com/jerryboganda/radiopad/releases/tag/${tag}
`);
