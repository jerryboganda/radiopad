# design-sync NOTES — RadioPad

Repo-specific gotchas for syncing the RC design system to claude.ai/design.

- **This is a Next.js app, not a component-library package.** `@radiopad/frontend` (in `frontend/`)
  has no dist and no build that emits one — the converter runs in synth-entry mode from
  `componentSrcMap` pins. `.d.ts` contracts come from source extraction, not shipped types.
- **Package root is `frontend/`**; all config paths are relative to it. Path alias `@/*` → `./*`
  via `tsconfig.json` (the `tsconfig` config key is required for imports to resolve).
- **Install**: pnpm workspace (`pnpm-lock.yaml` at repo root). `frontend/node_modules` is the
  `--node-modules` target (pnpm symlink layout; real files under the repo-root `.pnpm` store —
  still inside the repo, so workspace-bounding is satisfied).
- **Styling idiom**: NO Tailwind utilities in `components/` (verified by grep 2026-07-21, zero
  matches). Everything is named CSS classes (`.rp-*`, button variants `.primary`/`.ghost`/etc.)
  resolving to `var(--*)` tokens. The `@tailwind` directives at the top of `app/globals.css` are
  build-time only; in a raw stylesheet browsers ignore them (unknown at-rules) and nothing in the
  component set depends on Tailwind output.
- **CSS load order in the app** (`app/layout.tsx`): `@fontsource-variable/inter` → `globals.css`
  → `tokens.css` → `motion.css` → `radiopad.css` → `shell.css`. cssEntry = globals.css; the other
  four ship via `tokensGlob`.
- **Both themes**: light is default; dark = `html[data-theme="dark"]` (tokens.css overrides).
  Theme runtime is `lib/theme.ts` (`rp-theme` localStorage key). ThemeToggle flips the attribute.
- **Font**: Inter Variable only (`@fontsource-variable/inter`), `--font-mono` is a system mono
  stack (no shipped file). `--serif` is aliased to the sans stack (legacy alias, kept).
- **Excluded components (deliberate, 2026-07-21)**: router/auth/data-coupled — AppShell, Sidebar,
  Topbar, AuthGate, WebAdminGate, CommandPalette, ProfileMenu, NotificationsBell, TenantSwitcher,
  Breadcrumbs + SignInRequired (`next/link` needs app-router context), PageTransition
  (`usePathname`), PermissionGate (auth session), CaseQueue/StudyContextPanel/CriticalResultPanel/
  CatalogManager/OnDeviceModels (data fetching), SectionEditor/RichTextEditor (tiptap + editor
  registry coupling), companion/dictation overlays, LocalePicker/IntlBoundary, status banners.
  The sidebar-shell LOOK still ships via `shell.css` classes; the conventions header teaches the
  class vocabulary so the design agent builds the shell from plain markup.
- **Playwright**: machine has cached chromium builds 1194/1217/1223/1228 under
  `%LOCALAPPDATA%\ms-playwright` — pin the playwright version to a cached build via its
  `browsers.json`, don't download a new browser.

## Risks / to watch

- `AiActionsBar` + `AiActivityPanel` import `COMPLIANCE_LABELS` from `@/lib/api`, which reads
  `NEXT_PUBLIC_API_BASE` from `process.env` at module level — may throw `process is not defined`
  in the browser preview if the converter doesn't define it. Surfaced by the render check.
- `AuthScaffold` → `CheckUpdatesButton` uses `next-intl` `useTranslations` (needs
  `NextIntlClientProvider`) and Tauri updater dynamic imports. May need `cfg.provider` or
  exclusion — decide in the verify loop.
- `ThemeToggle` touches `localStorage`/`documentElement` — should be fine headless, but watch it.
- pnpm symlinks: font files resolve through `node_modules/.pnpm` — if `extraFonts` copying
  fails on symlinks, point at the resolved store path instead.
