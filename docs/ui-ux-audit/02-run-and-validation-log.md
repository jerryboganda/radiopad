**Status:** Audit deliverable  **Owner:** UI/UX Audit  **Last Updated:** 2026-05-17

# Run and Validation Log

## Commands Attempted

| Command | Result | Notes |
|---|---:|---|
| `pnpm --filter @radiopad/frontend typecheck` | Failed before script execution | pnpm attempted dependency status/install check and stopped on ignored build scripts. |
| `pnpm --filter @radiopad/frontend test` | Failed before script execution | Same pnpm ignored-builds blocker. |
| `pnpm --filter @radiopad/frontend build` | Failed before script execution | Same pnpm ignored-builds blocker. |
| `pnpm --filter @radiopad/frontend lint` | Failed before script execution | Same pnpm ignored-builds blocker. |

Combined exit line:

```text
VALIDATION_EXIT_CODES typecheck=1 test=1 build=1 lint=1
```

## Exact Blocker

```text
Scope: all 3 workspace projects
Lockfile is up to date, resolution step is skipped
Already up to date
[ERR_PNPM_IGNORED_BUILDS] Ignored build scripts: esbuild@0.21.5, sharp@0.34.5
Run "pnpm approve-builds" to pick which dependencies should be allowed to run scripts.
```

pnpm temporarily added an `allowBuilds` placeholder to `pnpm-workspace.yaml`. That accidental generated change was removed before these audit deliverables were written.

## Rendered Preview

No rendered browser preview was captured.

Blockers:

- No browser automation or screenshot capture tool was available in this environment.
- The frontend script runner was blocked by pnpm before `next dev` or `next build` could reliably run.
- Starting a dev server without a browser inspection path would not satisfy visual evidence requirements.

## Fallback Method Used

The audit used:

- Next.js route discovery from `frontend/app/**/page.tsx`
- Navigation discovery from `frontend/components/shell/nav.config.tsx`
- Route helper review from `frontend/lib/routes.ts`
- CSS/token review from `frontend/app/globals.css`, `frontend/app/radiopad.css`, and `frontend/app/shell.css`
- Source-level component and flow review across `frontend/app`, `frontend/components`, `frontend/lib`
- Native shell configuration review across `desktop/` and `mobile/`

## Validation Status

| Area | Status | Reason |
|---|---|---|
| TypeScript | Blocked | pnpm ignored-builds policy stopped script launch. |
| Unit/component tests | Blocked | pnpm ignored-builds policy stopped script launch. |
| Build/static export | Blocked | pnpm ignored-builds policy stopped script launch. |
| Lint | Blocked | pnpm ignored-builds policy stopped script launch. |
| Browser screenshots | Blocked | No browser/screenshot tool available. |
| Native Tauri/Capacitor preview | Not run | Native builds are long and were not needed for read-only source/config audit; native projects are also incomplete for mobile verification. |
