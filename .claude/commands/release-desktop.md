---
description: "Cut a desktop auto-update release (DESK-001): bump, tag, push, stop."
argument-hint: "[patch|minor|major|X.Y.Z]"
allowed-tools: Bash(git status:*), Bash(git rev-parse:*), Bash(git log:*), Bash(pnpm release:desktop:*)
---

Run the RadioPad desktop release ritual exactly (CLAUDE.md DESK-001). Do NOT skip a step.

1. Verify the working tree is clean and the branch's feature changes are committed and pushed. If the tree is dirty, stop and report — the release must not include uncommitted work.
2. Run `pnpm release:desktop $ARGUMENTS` (defaults to a patch bump when no argument is given). This bumps `desktop/src-tauri/tauri.conf.json` **and** `desktop/src-tauri/Cargo.toml` in lock-step, commits, tags `vX.Y.Z`, and pushes.
3. Report the new version and the tag that was pushed, then stop. Do not watch the run — `desktop-bundle` and `tauri-updater` take it from here, and the operator monitors CI.

Never hand-edit only one version file — the Tauri updater loops on a version mismatch. `pnpm release:desktop` is the only correct way to bump.
