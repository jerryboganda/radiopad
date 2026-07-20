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

## Preview-authoring learnings (folded from wave 1, 2026-07-21)

- Previews import **named** exports from `'@radiopad/frontend'` — default exports are
  re-exported as named by the forked synth entry. Hooks (`useToast`) also resolve.
- `duration: 0` on pushed toasts — capture fixes `Date` but not timers; a default-duration
  toast can auto-dismiss before the screenshot.
- `position: fixed` UI (toast region, xc-badge) anchors to the story root, not the viewport
  (`.ds-single`/`.ds-cell` have `transform: translateZ(0)`) — give such stories a real
  backdrop with `minHeight` ≈300px.
- Per-story (`?story=`) captures don't clip overflow; only grid cells do. Popovers are safe
  in graded shots.
- No `open` prop on a popover component? Click its trigger in a mount `useEffect`
  (`ref.querySelector('.rp-combobox-trigger').click()`) — React's delegated onClick fires.
- AnimatedNumber shows its FINAL value on mount (count-up only runs on value *change*).
- Reveal's IntersectionObserver fires reliably at top-of-page in capture; no workaround.
- ThemeToggle: only the light state is capturable — seeding `rp-theme` localStorage would
  flip the whole sheet. A dark-state cell would need a capture-level theme-seeding override.
- `.rp-patientbar` has `overflow-x:auto` — over-wide compositions silently scroll-clip the
  right side. Budget segments; short accession codes.
- Useful extra classes verified in the bundle: `.metric-card`/`.metric-card-value`
  (data-tone accents), `.status-badge` with `data-tone="stat|ready|review|draft|info|ai|
  blocked|muted"` attributes (not classes).
- PageHeader renders `secondaryActions` then `primaryAction` (primary rightmost); Container's
  `fluid` is invisible at card width — show `className="narrow"` instead.
- `_ds_bundle.css` carries only shell.css; the bulk of classes arrive via
  `tokens/{globals,tokens,motion,radiopad}.css`. A class "missing" from _ds_bundle.css is fine.

## Preview-authoring learnings (folded from wave 2, 2026-07-21)

- Controlled popovers (AiActionsBar `rewriteOpen`) render statically — pass the prop `true`
  with a no-op change handler; no click choreography needed.
- Full `Report`/`AiActivityEntry` literals compose as plain untyped objects (previews are
  transpiled, not type-checked); `Report.status` accepts the string form (`'Draft'`).
- Epoch-ms timestamps render in the capture machine's local timezone — fine, but don't
  assert clock text in grades.
- The review sheet is ONE shared document: a `<style>` tag in any story applies to every
  cell. Per-story CSS glue must be byte-identical across a component's stories.
- `position: fixed; inset: 0` overlays fill the story cell under translateZ containment;
  give them a backdrop with minHeight ≈540–600 in a 900x640 single card.
- GenerationOverlay's error branch still requires `active: true` (else renders null).
- ProvenanceModal preview carries `<style>.rp-provenance{max-height:640px}</style>` glue —
  the product's own cap — because at viewport 900x640 the `min(80vh, 640px)` clamp = 512px
  and scroll-clips the chain. A future full re-verify can set viewport `900x800` instead
  and drop the glue (deliberately not changed mid-run: grade-hash scope).
- CrossCheckBadge product stage strings (from ReportClient.tsx): running
  `re-running engines`/`medical review`; completed `N suggestions`/`no changes`; failed
  `cross-check failed`.
- Panel widths that photograph well: 380px (ChecklistPanel, AiActivityPanel), 400px
  (ExportPanel), 760px (AiActionsBar), ~860px shell pieces.
- **RESOLVED risk:** the `lib/api` module-level `process.env.NEXT_PUBLIC_*` reads do NOT
  break previews in practice (AiActivityPanel rendered COMPLIANCE_LABELS fine with only the
  NODE_ENV define). The fork's env shim stays as belt-and-braces, but this is no longer a
  watch item.
- Repo finding (component source, not sync): ChecklistPanel pluralizes the noun but not the
  verb — singular renders "1 item require review" (ChecklistPanel.tsx ~line 93). Cosmetic.

## Re-sync risks (what can silently go stale — read me first on re-sync)

- **`frontend/types/` drives the `.d.ts` contracts.** If component APIs changed and the
  emit (cfg.buildCmd) isn't re-run, the shipped props interfaces silently describe the OLD
  API. When in doubt, re-run the emit — deterministic, cheap.
- **Previews + conventions.md name product CSS classes** (`.rp-*`, `.badge`, buttons,
  `.metric-card`, `.status-badge[data-tone]`). A rename in `app/{globals,tokens,motion,
  radiopad,shell}.css` invalidates them — the render check and conventions validation pass
  catch it, but only if they run.
- **ProvenanceModal's max-height glue** mirrors the product's own `min(80vh,640px)` clamp;
  if the product clamp changes, the glue could misrepresent — prefer flipping the viewport
  override to `900x800` and deleting the glue.
- **`.design-sync/fonts/inter-alias.css` duplicates fontsource woff2 paths.** A
  `@fontsource-variable/inter` version bump can rename `files/*.woff2` → regenerate the
  alias (see the generation one-liner in git history) or `[FONT_DANGLING]` fires.
- **Toolchain pins:** playwright in `.ds-sync` must match a cached chromium build
  (1.61.1 ↔ 1228 today); `typescript` in `.ds-sync` must stay on 5.x (7.x has a different
  API and silently skips validate's `.d.ts` parse check).
- **Verified-state lives in the uploaded `_ds_sync.json`**, not git — grades in
  `.design-sync/.cache/` are machine-local and disposable.
- **Partially verified:** ThemeToggle's dark state and any dark-theme rendering of cards
  were never captured (light-only sheets); dark theme correctness rides on the token
  system, not on per-component verification.

## AuthScaffold exclusion (2026-07-21)

AuthScaffold is EXCLUDED from the sync (componentSrcMap null): it renders
CheckUpdatesButton whose `useTranslations` needs NextIntlClientProvider. Shipping that
provider would require `cfg.provider` + an extraEntries intl module — both live in the
GLOBAL grade hash (sync-hashes configSlicesFor), so adding them mid-run invalidates every
verified grade; and shipping AuthScaffold WITHOUT it is a trap (crashes when the design
agent renders it). It's auth chrome, not reporting UI; the `.rp-auth-*` classes still ship
in the CSS. To re-add on a future full re-verify: extraEntries module wrapping
NextIntlClientProvider with `frontend/messages/en.json` + `cfg.provider`.

## Known render warns (triaged as legitimate — re-syncs: don't chase these)

- `[FONT_MISSING] "Cascadia Mono", "JetBrains Mono"` — the RC `--font-mono` stack is a
  system-mono fallback stack BY DESIGN; the app ships no mono font either. **User explicitly
  accepted system-monospace fallback (2026-07-21).** Inter/Inter Variable ship fully (fontsource
  woff2s + an authored `Inter` alias face in `.design-sync/fonts/inter-alias.css`).
- `[NO_DIST] --entry frontend/.synth-entry-placeholder.mjs doesn't exist` — deliberate synth-mode
  trigger, see the cfg.entry note above.

## Risks / to watch

- pnpm symlinks: font files resolve through `node_modules/.pnpm` — worked fine on Windows
  (junctions); if `extraFonts` copying ever fails on symlinks, point at the resolved store
  path instead.
- (2026-07-21: the original `process.env`, AuthScaffold-intl, and ThemeToggle-headless risk
  bullets are resolved — see the wave-2 learnings and AuthScaffold sections above.)
