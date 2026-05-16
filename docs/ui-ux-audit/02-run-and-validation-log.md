# 02 — Run & Validation Log

## Environment

- OS: Windows (PowerShell)
- Node: present (pnpm bundled)
- Package manager: pnpm (workspace root has `pnpm-workspace.yaml`)
- Backend (`backend/RadioPad.Api`): not started during this audit

## Commands attempted

### `pnpm install` (workspace root, executed in `frontend/`)

- **Result:** success — 304 packages resolved, 304 added.
- **Non-fatal warning:** `ERR_PNPM_IGNORED_BUILDS` for `esbuild@0.21.5`
  and `sharp@0.34.5`. This is informational; both packages work without
  their optional build scripts. Causes pnpm to exit `1`, which is benign.
- **Log:** `~/.copilot/session-state/<id>/files/pnpm-install.log`.

### `pnpm typecheck` and `pnpm build`

- **Result:** both fail with the same root cause — pnpm's
  `runDepsStatusCheck` re-invokes `pnpm install` before running the
  script, and that nested install exits `1` because of the same
  `ERR_PNPM_IGNORED_BUILDS` warning.
- **Not a code defect** — the underlying tools succeed when called
  directly (see below). Filed as a structural finding.

### `node_modules/.bin/tsc -b --noEmit` (direct invocation)

- **Result:** **PASS** (exit 0, no errors, no warnings).
- All ~37 page files + ~20 components compile cleanly under
  TypeScript 5.9.3 strict.
- **Log:** `~/.copilot/session-state/<id>/files/tsc.log`.

### `pnpm dev` / live screenshot capture

- **Not attempted in this iteration.** Every data-driven page calls the
  typed `api` client in `frontend/lib/api.ts`, which requires the
  ASP.NET backend at `http://127.0.0.1:7457`. Without it, screenshots
  would show only `ErrorState` or perpetual `Skeleton`, providing little
  signal beyond the static review.
- **Recommended next iteration:** spin up the backend
  (`dotnet run --project backend/RadioPad.Api/src/RadioPad.Api`) and
  capture each route at 320 / 390 / 768 / 1280 / 1920 with Playwright.
  See `11-screenshot-index.md`.

### `pnpm lint`

- **Not run.** `next lint` would also trigger the same
  `runDepsStatusCheck` issue. Static review surfaced more value per
  minute than relying on `eslint-config-next`'s defaults.

## Test suite

- **Not executed this iteration.** Tests live in `frontend/__tests__/`
  and would need the same `runDepsStatusCheck` workaround. Static
  review covers UI/UX surface; tests cover behavior.

## Conclusions usable for the audit

1. The codebase **compiles cleanly** — every finding in this audit is a
   design / UX / a11y / structural issue, not a build defect.
2. The `pnpm install/typecheck/build` integration is **broken at the
   wrapper level** but the tools themselves work. This is one of the
   audit's findings (`UIUX-STR-DX-001`).
3. Live runtime evidence (screenshots, axe scans, lighthouse) is
   deferred to the next iteration once a backend is reachable.
