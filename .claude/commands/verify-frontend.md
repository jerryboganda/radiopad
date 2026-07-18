---
description: Full local frontend gate — typecheck, unit tests, and all three surface builds.
allowed-tools: Bash(pnpm --filter @radiopad/frontend:*)
---

Run the full frontend verification the way CI does — **plus** the two surfaces CI omits. This is a heavier run; only do it when explicitly asked.

1. `pnpm --filter @radiopad/frontend typecheck`
2. `pnpm --filter @radiopad/frontend test`
3. `pnpm --filter @radiopad/frontend build:desktop`
4. `pnpm --filter @radiopad/frontend build:web`
5. `pnpm --filter @radiopad/frontend build:mobile`

Stop at the first failure and report it with the smallest useful excerpt. If everything passes, report a one-line green summary. Remember: if any `frontend/` file that ships in the bundle changed, DESK-001 still requires `pnpm release:desktop` afterwards.
