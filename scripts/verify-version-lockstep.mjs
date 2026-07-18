#!/usr/bin/env node
// Assert the desktop version is identical in tauri.conf.json and Cargo.toml.
// A mismatch makes the Tauri auto-updater loop, so this guards CLAUDE.md's
// "never bump only one version file" rule. Exit 0 = match, 1 = mismatch, 2 = read error.
import { readFileSync } from 'node:fs';

function read(path, extract) {
  try {
    return extract(readFileSync(path, 'utf8'));
  } catch (e) {
    console.error(`Cannot read ${path}: ${e.message}`);
    process.exit(2);
  }
}

const tauri = read('desktop/src-tauri/tauri.conf.json', (t) => JSON.parse(t).version);
const cargo = read('desktop/src-tauri/Cargo.toml', (t) => {
  const m = t.match(/^\s*version\s*=\s*"([^"]+)"/m);
  return m ? m[1] : null;
});

if (!tauri || !cargo) {
  console.error(`Missing version (tauri.conf.json=${tauri}, Cargo.toml=${cargo}).`);
  process.exit(2);
}

if (tauri !== cargo) {
  console.error(
    `Desktop version MISMATCH: tauri.conf.json=${tauri} vs Cargo.toml=${cargo}.\n` +
      'Do not hand-edit one file — run `pnpm release:desktop`, which sets both (a mismatch loops the updater).',
  );
  process.exit(1);
}

console.log(`Desktop version lock-step OK: ${tauri}`);
