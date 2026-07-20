# design-sync NOTES — RadioPad

Repo-specific gotchas for syncing the RC design system to claude.ai/design.

- **This is a Next.js app, not a component-library package.** `@radiopad/frontend` (in `frontend/`)
  has no dist and no build that emits one — the converter runs in synth-entry mode from
  `componentSrcMap` pins. `.d.ts` contracts come from source extraction, not shipped types.
- **`cfg.entry` points at a file that deliberately does not exist**
  (`frontend/.synth-entry-placeholder.mjs`). This is load-bearing, not a mistake: without an
  entry override the converter looks for the package at `node_modules/@radiopad/frontend`
  (absent — a repo never self-installs), and with a *nonexistent* override it (a) derives
  PKG_DIR=`frontend/` by walking up to the named package.json and (b) falls back to
  synthesizing the bundle entry from the `componentSrcMap` pins (soft-resolve). The
  `[NO_DIST] --entry ... doesn't exist` warning on every build is expected noise — do not
  "fix" it by creating the file (a real file would be used as the actual bundle entry).
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
- **`.d.ts` props extraction needs the generated types tree.** The app ships no dist, so
  without help the extractor picks `frontend/lib` as the types root, parses 0 files, and emits
  `[key: string]: unknown` stubs for every component (this happened — check a
  `ds-bundle/components/**/<Name>.d.ts` after building). Fix: **run
  `node ..\.ds-sync\node_modules\typescript\bin\tsc -p tsconfig.design-sync.json` from
  `frontend/` BEFORE the converter build whenever component APIs changed** — it emits a real
  `.d.ts` tree into `frontend/types/` (gitignored), which `findTypesRoot` prefers over `lib/`.
  `frontend/tsconfig.design-sync.json` is the committed emit config (`cfg.buildCmd` mirrors it).
  Do NOT use bare `npx tsc` (resolves a dummy package, prints "not the tsc you are looking for").
- **Playwright**: machine has cached chromium builds 1194/1217/1223/1228 under
  `%LOCALAPPDATA%\ms-playwright` — pin the playwright version to a cached build via its
  `browsers.json`, don't download a new browser.

## Known render warns (triaged as legitimate — re-syncs: don't chase these)

- `[FONT_MISSING] "Cascadia Mono", "JetBrains Mono"` — the RC `--font-mono` stack is a
  system-mono fallback stack BY DESIGN; the app ships no mono font either. **User explicitly
  accepted system-monospace fallback (2026-07-21).** Inter/Inter Variable ship fully (fontsource
  woff2s + an authored `Inter` alias face in `.design-sync/fonts/inter-alias.css`).
- `[NO_DIST] --entry frontend/.synth-entry-placeholder.mjs doesn't exist` — deliberate synth-mode
  trigger, see the cfg.entry note above.

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
