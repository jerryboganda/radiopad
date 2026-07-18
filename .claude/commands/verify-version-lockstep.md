---
description: Assert desktop version parity between tauri.conf.json and Cargo.toml.
allowed-tools: Bash(node scripts/verify-version-lockstep.mjs)
---

Run `node scripts/verify-version-lockstep.mjs`.

It reads the semver from `desktop/src-tauri/tauri.conf.json` and `desktop/src-tauri/Cargo.toml` and exits non-zero if they differ (a mismatch makes the Tauri updater loop). Report both versions and PASS/FAIL.

If they differ, do **not** hand-fix one file — the correct fix is `pnpm release:desktop`, which sets both in lock-step.
