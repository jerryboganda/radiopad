# CLAUDE.md — RadioPad project memory for Claude Code

# CLAUDE.md — RadioPad project memory for Claude Code

## ⚠️ MISSION-CRITICAL: RC design system (dual-theme tokens), build-time Tailwind, sidebar shell

RadioPad's visual identity is the **RC design system** (PRD v3.0 §20; reference mockups at `UI UX SCREENS/Authentication/`, RC-01…RC-10): a light-first white/blue clinical-SaaS palette with a **first-class deep-navy dark theme**. The **canonical token source** is [frontend/app/tokens.css](frontend/app/tokens.css) (RC `--color-*` primitives — light in `:root`, dark overridden under `html[data-theme="dark"]` — **plus** the alias layers that re-point RadioPad's original 44 token names — `--bg`, `--accent`, semantic families — onto the RC primitives, joined by the new tokens `--accent-fg`/`--scrim`/`--link`/`--bg-selected` and the `--ai`/`--navy` families) and [frontend/tailwind.config.ts](frontend/tailwind.config.ts) (`var()`-based Tailwind scales, `darkMode: ['selector', '[data-theme="dark"]']`). Theme runtime: [frontend/lib/theme.ts](frontend/lib/theme.ts) (`rp-theme` localStorage, pre-paint bootstrap in `layout.tsx`) + `<ThemeToggle />`. [frontend/app/globals.css](frontend/app/globals.css) carries the `@tailwind` directives; [frontend/app/shell.css](frontend/app/shell.css) is the sidebar shell + page chrome. The **app shell** is the canonical left-sidebar SaaS shell. Read [docs/02-design/design.md](docs/02-design/design.md) before touching any UI.

Hard rules:

1. Write against the documented alias contract (`--bg`, `--accent`, `--accent-fg`, `--scrim`, semantic families: green/blue/red/amber/ai/purple/navy) — it resolves to the RC primitives via `tokens.css`. Never hardcode colours (no hex/rgb in feature CSS or TSX), do not invent new tokens inline, and do not write new code against the legacy Hallmark paper/saffron/marine alias names.
2. **BOTH themes are mandatory.** Light is the first-run default (THEME-001); dark is first-class (deep navy, never pure black). Every UI change must be checked in **both** themes before it ships. Print/exports always render the light document theme (THEME-015). Intentionally-dark exceptions in both themes: the `.op-bash` terminal block and present-mode surfaces.
3. Render every page inside `<AppShell>` (`frontend/components/shell/AppShell.tsx`). Use `<Container>` + `<PageHeader>` for the top of every page. Use the documented component classes only (`.rp-shell`, `.rp-sidebar`, `.rp-topbar`, `.rp-page-header`, `.rp-panel`, `.section-block`, `.composer`, `.msg`, `.finding`, `.ai-mark`, `.brand-mark`, `.badge`, `.status-badge`, button variants `.primary` / `.primary-ghost` / `.ghost` / `.subtle`, and the shared classes in `tokens.css`). The legacy `.app` / `.topbar` classes are reserved for in-page editor chrome inside `.split` two-pane surfaces and must not be the application root.
4. AI-generated text wears `.ai-mark` (blue "✨ generated" treatment — tinted field + label, paired with an amber "Requires review" flag; never hue-only) until acknowledged or edited.
5. Validation severities map: Blocker→red, Warning→amber, Info/Style→blue.
6. Data-driven pages use `<Skeleton />` / `<EmptyState />` / `<ErrorState onRetry />` for loading / empty / error states.
7. **Forbidden:** MUI/Ant/Chakra/Bootstrap, emoji as icons, additional accent colours, hardcoded colours, primary navigation patterns other than the canonical left-sidebar shell. The old "no dark mode / light-only" rule is **revoked** — dark mode is now required, not forbidden. (Build-time **Tailwind 3** is part of the stack — `@tailwind` directives in `globals.css`, config in `tailwind.config.ts`, PostCSS + Autoprefixer; it compiles to static CSS for `output: 'export'`. Utilities and the named RadioPad classes may be mixed; both resolve to the same variables, so both are theme-aware.)

If a token doesn't exist for what you need, extend the RC primitives in `tokens.css` (light **and** dark values, and mirror any new scale in `tailwind.config.ts`); for shell/chrome extend `shell.css`. Update `docs/02-design/design.md` in the same change — never inline.

## ⚠️ MISSION-CRITICAL: Desktop changes → cut a release (auto-update, DESK-001)

The desktop app self-updates. **Whenever you change anything that ships in the desktop build (anything under `frontend/` or `desktop/`), shipping a new desktop release is PART OF THE TASK — do it automatically, the operator should never have to ask.** Builds run on GitHub Actions only; never build the desktop app locally or on the VPS.

After your `frontend/`/`desktop/` changes are committed and pushed, run one command:

```bash
pnpm release:desktop      # patch bump (0.1.23 → 0.1.24); also accepts `minor` / `major` / `X.Y.Z`
```

It bumps `desktop/src-tauri/tauri.conf.json` **and** `desktop/src-tauri/Cargo.toml` in lock-step, commits, tags `vX.Y.Z`, and pushes. The tag then drives the pipeline end-to-end with no further steps: `desktop-bundle` builds + signs the Windows `.msi` / Linux `.AppImage` and creates the release; `tauri-updater` signs + publishes `latest.json`. The in-app "Check for updates" button reads the GitHub Releases `latest.json`, so every user auto-downloads the new build. Confirm the run is green with `gh run watch`.

- Backend-only / CLI-only / docs-only changes do **not** need a desktop release.
- Never bump only one version file or hand-edit a build (version mismatch → updater loop). Always use `pnpm release:desktop`.
- Signing secrets (`TAURI_PRIVATE_KEY`, `TAURI_KEY_PASSWORD`) are already set in GitHub; the public key is embedded in `tauri.conf.json`. macOS is excluded from the matrix until Apple signing is configured.

## ⚠️ MISSION-CRITICAL: three specialised surfaces from one frontend (RADIOPAD_SURFACE)

RadioPad is **desktop-first, surface-specialised**. The single `frontend/` codebase builds into three scoped bundles selected by the `RADIOPAD_SURFACE` build flag:

- **desktop** = the entire reporting product (worklist, editor, dictation, library authoring, personal settings, **companion host**). Clinical roles.
- **web** = master-admin / platform operations ONLY (`admin/*`, users, billing, SSO, providers, governance, usage). NO reporting. Clinical-only users get a "download the desktop app" interstitial ([WebAdminGate](frontend/components/shell/WebAdminGate.tsx)).
- **mobile** = a dictation **companion** that pairs to a live desktop session (pairing + voice dictation + remote only). NO standalone reporting.

How it works: routes live in App Router **route groups** `frontend/app/(desktop|web|mobile|shared)/`. [scripts/build-surface.mjs](frontend/scripts/build-surface.mjs) (`pnpm --filter @radiopad/frontend build:{desktop,web,mobile}`) sets the flag, stages non-target groups OUT of `app/` (and swaps the root `/` for a redirect on web/mobile), runs `next build`, and moves `out/` → `out-<surface>`. So each shell **physically** ships only its routes. [lib/surface.ts](frontend/lib/surface.ts) exposes `SURFACE`/`isWebSurface`/`surfaceAllows`; nav is surface-tagged in [nav.config.tsx](frontend/components/shell/nav.config.tsx). Tauri consumes `out-desktop` (`build:desktop`), Capacitor `out-mobile` (`build:mobile`), web deploy serves `out-web`. Plain `next dev` = full desktop app (all groups present).

The **companion** relay is a cloud subsystem (`/ws/companion` + `/api/companion/*`, [lib/companion.ts](frontend/lib/companion.ts), [CompanionHostPanel](frontend/components/companion/CompanionHostPanel.tsx)) — desktop advertises a code, phone pairs and streams dictation into the desktop's focused section via `getLastFocusedSectionEditor().insertAtCursor`.

## Project mission

AI-assisted radiology reporting platform. Radiologist drafts, validates, and signs; AI never auto-signs. See [PRD.md](PRD.md) and [PROGRESS.md](PROGRESS.md) (Ralph-loop memory).

## Strict tech stack

| Layer | Technology |
| --- | --- |
| Web | Next.js 16 (App Router) |
| Backend | ASP.NET Core 8 + EF Core |
| Desktop | Tauri 2 (Rust) |
| Mobile | Capacitor 6 |
| CLI | .NET 8 global tool |

No other frameworks. Do not propose Express/Fastify/NestJS/etc. for the backend.

## Repo map

- `backend/RadioPad.Api/` — ASP.NET Core solution (Domain, Application, Validation, Infrastructure, Api, tests)
- `frontend/` — Next.js app; routes grouped by surface under `app/(desktop|web|mobile|shared)/`, built per-surface via `build:{desktop,web,mobile}` (see surface-model note above)
- `desktop/` — Tauri shell (consumes `frontend/out-desktop`)
- `mobile/` — Capacitor project (companion; consumes `frontend/out-mobile`)
- `cli/RadioPad.Cli/` — .NET global tool
- `rulebooks/` — YAML rulebooks
- `templates/` — JSON report templates
- `docs/` — full documentation hierarchy
- `src/`, `daemon/` — LEGACY Open Design reference (read-only)
- `*.legacy.*` — archived original Open Design root files

## Commands

```powershell
# Backend
cd backend/RadioPad.Api && dotnet build && dotnet test
dotnet run --project src/RadioPad.Api    # → http://127.0.0.1:7457

# Frontend
cd frontend && pnpm install && pnpm dev   # → http://localhost:3000
pnpm typecheck && pnpm build

# CLI
dotnet run --project cli/RadioPad.Cli -- rulebook validate ../../rulebooks/chest_ct_v1.yaml
```

## Safety boundaries (non-negotiable)

1. RadioPad never auto-signs reports.
2. AI text wears `.ai-mark` until reviewed.
3. PHI requests blocked unless provider compliance is `PhiApproved` or `LocalOnly` (enforced in `AiGateway`; throws `ProviderPolicyException`).
4. Audit log is append-only; use `IAuditLog.AppendAsync` only.
5. Backend binds `127.0.0.1` by default.
6. Tenant isolation enforced via `TenantedController.ResolveContextAsync`.

## Memory checkpoints

- See [/memories/repo/radiopad-design-lock.md](/memories/repo/radiopad-design-lock.md) for the design-lock note.
- Update `PROGRESS.md` after completing a checklist item.
- Update `docs/` when changing behaviour.
