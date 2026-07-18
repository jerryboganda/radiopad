---
description: Watch the latest GitHub Actions run and surface failing-job logs.
argument-hint: "[workflow-file.yml]"
allowed-tools: Bash(gh run:*)
---

Watch CI. If a workflow file is given as `$ARGUMENTS`, target it; otherwise use the most recent run on the current branch.

1. Find the run: `gh run list --limit 5` (or `gh run list --workflow $ARGUMENTS --limit 5`).
2. Watch it to completion: `gh run watch <id>`.
3. On failure: `gh run view <id> --log-failed`, then summarize which job/step failed and why. On success: a one-line green summary.
