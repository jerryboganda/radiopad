# Agent OS Integration Plan

## Goal

Install Agent OS v3 for RadioPad and make its standards, product context, and spec workflow
usable without overriding the repository's existing source-of-truth instructions.

## Tasks

1. Install the Agent OS base under the developer home directory.
2. Install the project command namespace under `.claude/commands/agent-os/`.
3. Discover and document RadioPad standards under `agent-os/standards/`.
4. Build the standards index for targeted injection.
5. Add product mission, roadmap, and tech-stack context.
6. Document the repeatable workflow and verify the installation.

## Completion criteria

- Agent OS project commands are present and readable by Claude Code.
- Every project standard has one index entry with a concise description.
- Product context is aligned with current RadioPad instructions and PRD.
- The integration preserves existing `.claude` commands, skills, hooks, and source-of-truth
  files.
