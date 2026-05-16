**Status:** Audit deliverable  **Owner:** UI/UX Audit  **Last Updated:** 2026-05-17

# Project Intake

## Framework

RadioPad's frontend is a Next.js 16 App Router application under `frontend/`, using React 18 and TypeScript. It is configured as a static export so the same bundle can ship into Tauri desktop and Capacitor mobile shells.

Evidence:
- `frontend/package.json` uses `next: ^16.2.4`, `react: ^18.3.1`, and `typescript: ^5.6.3`.
- `frontend/next.config.ts` sets `output: 'export'`, `trailingSlash: true`, and unoptimized images.

## Styling System

The UI uses the locked RadioPad/Open Design CSS token system:

- Token layer: `frontend/app/globals.css`
- Domain patterns: `frontend/app/radiopad.css`
- Sidebar shell/chrome: `frontend/app/shell.css`
- Canonical design documentation: `docs/02-design/design.md`

Key locked tokens include `--bg`, `--accent`, `--text`, `--border`, semantic green/blue/purple/red/amber families, `--serif`, `--sans`, and `--mono`.

## Component Library

No external UI component framework was identified. Shared UI is custom React + CSS:

- Shell: `frontend/components/shell/*`
- UI states: `frontend/components/ui/EmptyState.tsx`, `ErrorState.tsx`, `Skeleton.tsx`, `StatusBadge.tsx`
- Page-local components: report editor, rulebook editor panels, mobile report clients, provider OAuth admin client

## Routing System

Routing is file-system based through `frontend/app/**/page.tsx`. Several detail pages are query-param wrappers around client components stored in dynamic-looking folders.

Examples:
- `/reports/view?id=...` -> `frontend/app/reports/view/page.tsx` -> `frontend/app/reports/[id]/ReportClient.tsx`
- `/rulebooks/view?id=...` -> `frontend/app/rulebooks/view/page.tsx` -> `frontend/app/rulebooks/[id]/RulebookDetailClient.tsx`
- `/mobile/reports/edit?reportId=...` -> `frontend/app/mobile/reports/edit/page.tsx`

Route helpers live in `frontend/lib/routes.ts`.

## State Management

State is local React component state plus typed API calls. No global state library was identified. Cross-cutting state and native shell state include:

- `ShellContext` for sidebar collapse/drawer behavior
- `PageActionsProvider` for topbar/page actions
- `ShellBridge` for Tauri events, biometric token hydration, and offline sync
- Local storage for tenant/user headers and fallback auth/offline state

## Forms

Forms are mostly page-local controlled inputs using base `input`, `textarea`, and `select` styles from `globals.css` and `.section-block` from `radiopad.css`. Several forms use visible labels without `htmlFor`/`id` association; see `07-accessibility-audit.md`.

## Testing Tools

Configured tools:

- Vitest
- jsdom
- React Testing Library
- TypeScript build mode via `tsc -b --noEmit`

No Playwright, axe, browser screenshot, or visual regression tooling is configured in `frontend/package.json`.

## Build Scripts

Root scripts:

| Script | Command |
|---|---|
| `dev` | `pnpm --filter @radiopad/frontend dev` |
| `build` | `pnpm --filter @radiopad/frontend build` |
| `lint` | `pnpm --filter @radiopad/frontend lint` |
| `typecheck` | `pnpm --filter @radiopad/frontend typecheck` |
| `test` | `pnpm --filter @radiopad/frontend test` |

Frontend scripts:

| Script | Command |
|---|---|
| `dev` | `next dev -p 3000` |
| `build` | `next build` |
| `start` | `next start` |
| `lint` | `next lint` |
| `typecheck` | `tsc -b --noEmit` |
| `test` | `vitest run` |

## Run Instructions

Expected local frontend run path:

```powershell
pnpm --filter @radiopad/frontend dev
```

Expected backend for API-backed UI:

```powershell
dotnet run --project backend\RadioPad.Api\src\RadioPad.Api
```

Desktop shell uses `desktop/src-tauri/tauri.conf.json` with `beforeDevCommand: pnpm --filter @radiopad/frontend dev`.

Mobile shell uses `mobile/capacitor.config.ts` with `webDir: ../frontend/out`.

## Blockers

Rendered browser inspection and screenshots were not available in this CLI environment. Existing pnpm script attempts were blocked before script execution by pnpm's ignored-builds policy:

```text
[ERR_PNPM_IGNORED_BUILDS] Ignored build scripts: esbuild@0.21.5, sharp@0.34.5
Run "pnpm approve-builds" to pick which dependencies should be allowed to run scripts.
```

The audit therefore combines source-level review, configuration review, route/component discovery, and evidence-backed risk assessment. Screenshot evidence could not be captured.
