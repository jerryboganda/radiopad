---
description: Cut a desktop auto-update release (DESK-001) and watch the pipeline to green.
argument-hint: "[patch|minor|major|X.Y.Z]"
allowed-tools: Bash(git status:*), Bash(git rev-parse:*), Bash(git log:*), Bash(pnpm release:desktop:*), Bash(gh run:*), Bash(gh release:*)
---

Run the RadioPad desktop release ritual exactly (CLAUDE.md DESK-001). Do NOT skip a step.

1. Verify the working tree is clean and the branch's feature changes are committed and pushed. If the tree is dirty, stop and report — the release must not include uncommitted work.
2. Run `pnpm release:desktop $ARGUMENTS` (defaults to a patch bump when no argument is given). This bumps `desktop/src-tauri/tauri.conf.json` **and** `desktop/src-tauri/Cargo.toml` in lock-step, commits, tags `vX.Y.Z`, and pushes.
3. Watch the build:
   `gh run watch $(gh run list --workflow desktop-bundle.yml --event push --limit 1 --json databaseId --jq '.[0].databaseId')`
4. Confirm `tauri-updater` published `latest.json` and the GitHub Release is no longer a draft: `gh release view v<version>`.
5. Report the new version, the run conclusion, and the release URL. If any job failed, show which one and `gh run view --log-failed`.

Never hand-edit only one version file — the Tauri updater loops on a version mismatch. `pnpm release:desktop` is the only correct way to bump.
