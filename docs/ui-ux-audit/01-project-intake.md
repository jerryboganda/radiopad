# 01 — Project Intake

**Audit target:** `frontend/` (RadioPad web client)
**Audit date:** 2026-05-16
**Audit scope:** All routes in `frontend/app/**/page.tsx`, all shared
components in `frontend/components/**`, plus the canonical stylesheets
(`globals.css`, `shell.css`, `radiopad.css`).
**Audit boundary:** Audit-only — no production UI files were modified.

## Framework

- **Next.js 16.2.4** with the App Router (file-system routing under
  `frontend/app/**`).
- **React 18.3.1** with TypeScript 5.9.3 (strict, `noEmit` build).
- **Static export:** `next.config.ts` sets `output: 'export'` and
  `trailingSlash: true` so the same bundle ships into Tauri (desktop) and
  Capacitor (mobile). **Implication for this audit:** the production
  artifact has no Node server; route handlers / middleware are dev-only.
- **Internationalization:** `next-intl@3.26.5` with locale negotiation in
  `frontend/middleware.ts` and client fallback in `frontend/lib/i18n.ts`.

## Styling system

- **Hand-rolled CSS, no utility framework.** Three stylesheets are imported
  by `frontend/app/layout.tsx` in this order:
  1. `frontend/app/globals.css` (~116 KB) — token layer + component classes
     (`.panel`, `.section-block`, `.composer`, `.msg`, `.finding`,
     `.ai-mark`, `.primary`, `.ghost`, …).
  2. `frontend/app/radiopad.css` (~19 KB) — page-specific helpers.
  3. `frontend/app/shell.css` (~13 KB) — sidebar/topbar/page-header shell.
- **Locked design tokens:** warm cream `--bg #faf9f7`, accent
  `--accent #c96442`, semantic families (green/blue/purple/red/amber),
  radii (6/10/14/pill), shadows (xs/sm/md/lg), serif/sans/mono.
- **No Tailwind, no MUI, no Ant, no Chakra, no Bootstrap.** No dark mode.

## Component library

- **App shell** (`frontend/components/shell/`):
  `AppShell`, `Sidebar`, `Topbar`, `PageHeader`, `Breadcrumbs`,
  `Container`, `ProfileMenu`, `MobileDrawerBackdrop`, `PageActionsSlot`,
  `ShellContext`, `nav.config`.
- **UI primitives** (`frontend/components/ui/`):
  `StatusBadge`, `Skeleton` (+ `TableSkeleton`), `EmptyState`,
  `ErrorState`.
- **Feature components** (`frontend/components/`):
  `LocalePicker`, `IntlBoundary`, `DictateButton`,
  `DesktopStatusBanner`, `BillingStatusBanner`.

There is no generic `<Button>`, `<Input>`, `<Card>`, `<Modal>`, `<Table>`
or `<Tabs>` primitive — pages compose the locked CSS classes directly.

## Routing system

File-system App Router; **37 pages** discovered (see
`03-route-inventory.md`):

| Group | Count |
|---|---|
| Top-level / public | 12 |
| Reports | 2 |
| Rulebooks | 3 |
| Audit | 2 |
| Analytics | 2 |
| Mobile | 3 |
| Admin | 14 |

There is a single `app/layout.tsx`; **no per-group `layout.tsx` files**.
There are **no dynamic route segments** (`[id]`); detail pages take their
ids via query strings (e.g. `/reports/view?id=...`). There is no `not-found.tsx`,
`error.tsx`, `loading.tsx`, or `global-error.tsx` at the app root.

## State management

- React local state + `next-intl` translations.
- Shell state via `ShellContext` (sidebar collapsed + mobile drawer).
- Topbar action slot via `PageActionsSlot` context.
- All data flows through the typed `api` client in `frontend/lib/api.ts`
  (CLAUDE.md mandate: no direct `fetch` from pages).

## Forms

No forms library. Forms are hand-rolled with the locked `input`,
`textarea`, `select` styles in `globals.css` (lines 138-156).

## Build & validation scripts

`frontend/package.json`:

| Script | Command |
|---|---|
| `pnpm dev` | `next dev -p 3000` |
| `pnpm build` | `next build` |
| `pnpm start` | `next start` |
| `pnpm lint` | `next lint` |
| `pnpm typecheck` | `tsc -b --noEmit` |
| `pnpm test` | `vitest run` |

## Environment

- Backend expected at `http://127.0.0.1:7457` (proxied via
  `next.config.ts#rewrites` during `next dev`; static export uses
  `NEXT_PUBLIC_API_BASE`).
- No `.env.example` shipped in `frontend/`; the only env var the static
  bundle reads is `NEXT_PUBLIC_API_BASE`.

## Run instructions (this audit)

```powershell
cd frontend
pnpm install   # 304 packages added; benign "ignored build scripts" warning
.\node_modules\.bin\tsc -b --noEmit  # passes clean
```

See `02-run-and-validation-log.md` for full output.

## Blockers

- **None** for static evidence. Every page file was readable.
- `pnpm typecheck` and `pnpm build` (run from the workspace root) fail
  because `pnpm` runs an automatic `runDepsStatusCheck` that re-installs and
  exits non-zero on the `ERR_PNPM_IGNORED_BUILDS` warning. Calling the
  underlying `tsc` / `next build` binaries directly works. This is a
  workspace-config issue, not a code defect (filed as finding
  `UIUX-STR-???` for triage).
- No live screenshots were captured: the static-export bundle requires a
  running ASP.NET backend for `api.me()`, `api.reports()`, etc. — every
  page is data-driven and would render its `ErrorState` or hang on
  `Skeleton` without it. Screenshot capture is deferred to a follow-up
  iteration; see `11-screenshot-index.md`.
