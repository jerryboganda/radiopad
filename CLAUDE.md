# CLAUDE.md — RadioPad project memory for Claude Code

> **This file is the authoritative project instruction file.** [AGENTS.md](AGENTS.md) and [GEMINI.md](GEMINI.md) are thin pointers to it; if they ever disagree, this file wins.

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

## ⚠️ MISSION-CRITICAL: shipped platforms — Windows desktop + mobile companion ONLY

**Operator decision (2026-07-20), permanent until they say otherwise.** RadioPad ships:

| Product | Platforms |
| --- | --- |
| Desktop app | **Windows only** (`.msi`) |
| Mobile companion (pairing + voice dictation) | **Android + iOS** |
| Web | admin/platform surface, browser (unchanged) |

**macOS and Linux DESKTOP builds are out of scope.** They roughly doubled every release's
wall-clock and produced installers nobody was going to run. Do not add them back, do not "helpfully"
restore a `.dmg`/`.AppImage`/`.deb` target, and do not add a macOS or Linux entry to a desktop build
matrix. This is enforced in three places, all of which must change together if the decision ever
reverses:

1. [.github/workflows/desktop-bundle.yml](.github/workflows/desktop-bundle.yml) — matrix is
   `[windows-latest]`, and the sidecar triple resolver **hard-fails** on any other target rather
   than silently publishing a sidecar for a platform we do not ship.
2. [desktop/src-tauri/tauri.conf.json](desktop/src-tauri/tauri.conf.json) — `bundle.targets` is
   `["msi"]`; the `macOS` / `linux` bundle blocks are gone.
3. [.github/workflows/tauri-updater.yml](.github/workflows/tauri-updater.yml) — `latest.json`
   advertises `windows-x86_64` only. An entry for a platform we no longer build would point
   installed clients at a 404 and surface as "Update failed".

Also narrowed: `desktop-release.yml` (signed path) and `desktop-installer-verify.yml`
(`verify-windows` only). The mobile companion is **unaffected** — `mobile-bundle.yml` keeps both its
`android` and `ios` jobs, and the iOS job legitimately needs a macOS runner. Ubuntu runners used for
*backend/CI* jobs (ci, sidecar-reachability, flaky-hunt, on-device-latency) are also unaffected:
they test server code and cost nothing in release wall-clock.

## ⚠️ MISSION-CRITICAL: Desktop changes → cut a release (auto-update, DESK-001)

The desktop app self-updates. **Whenever you change anything that ships in the desktop build (anything under `frontend/` or `desktop/`), shipping a new desktop release is PART OF THE TASK — do it automatically, the operator should never have to ask.** Builds run on GitHub Actions only; never build the desktop app locally or on the VPS.

After your `frontend/`/`desktop/` changes are committed and pushed, run `pnpm release:desktop`
(patch bump by default; also accepts `minor` / `major` / `X.Y.Z`). The full ritual and the pipeline
it triggers are in [/release-desktop](.claude/commands/release-desktop.md).

- Backend-only / CLI-only / docs-only changes do **not** need a desktop release.
- Never bump only one version file or hand-edit a build (version mismatch → updater loop). Always use `pnpm release:desktop`.
- macOS and Linux desktop builds are **out of scope**, not pending — see the platforms section above. Nothing is blocked on Apple signing.

## ⚠️ MISSION-CRITICAL: CPU-intensive work runs on GitHub Actions, never locally

**All heavy, CPU/RAM-intensive work for this project — full builds, full test suites, lint and type-check sweeps, static analysis, bundling, desktop/mobile packaging, Docker image builds, and coverage — runs on GitHub Actions. Not on the development laptop. Not on the VPS.** This is a permanent project rule that binds every agent and contributor.

Why: the development machine is low-spec and saturating it stalls the whole session, and the production VPS (`/opt/radiopad`) hosts live tenants — a compiler or test run there risks the live site. CI runners are disposable, parallel, and free for this purpose.

1. **Do not run full builds, full test suites, or lint/type-check sweeps** locally or on the VPS. Commit and push; GitHub Actions runs them.
2. **Allowed locally** — focused, cheap feedback only: editing and reading code, one targeted unit test (`dotnet test --filter <Name>`, or a single Vitest file), and running the app to look at a change (`pnpm dev`, `dotnet run`). Anything that compiles the whole solution or the whole frontend, or runs a whole suite, belongs in CI.
3. **The production VPS runs the app only.** Never invoke `dotnet build/test`, `pnpm build/lint`, `cargo build`, or `docker compose build` there for development. Deploys pull pre-built images produced by CI.
4. **Every merge-gating task lives in `.github/workflows/`.** If a heavy task is not yet covered by a workflow, add the workflow in the same change rather than running it by hand.
5. **Green CI is the evidence, not local output.** "It builds on my machine" proves nothing.

A pre-tool-use guard (`hooks/pretooluse.mjs` / `.ps1`) prompts on these commands. If it fires, the answer is almost always to push instead — not to approve it.

## ⚠️ MISSION-CRITICAL: code, don't babysit

**Spend the session writing code, not waiting on machines. Do small quick checks only. When the work is done, commit, push, and stop.** The operator monitors CI and will report failures.

1. **Never watch or poll CI.** No `gh run watch`, no sleep-and-retry loops, no waiting for a run to finish — including after a desktop release. Push and end the turn.
2. **Checks stay small.** The allowed-locally set above is the ceiling: a targeted test, a look at the running app. Do not chain verification steps hunting for confidence.
3. **Report honestly.** Because CI is unobserved, say what you changed and that CI will decide it. **Never claim a change builds, passes, or works when you have not seen it do so.** Push-and-stop is only safe if the report doesn't overstate.
4. **The operator reports errors.** If something is broken, they will say so. Do not pre-emptively re-verify finished work.

## ⚠️ MISSION-CRITICAL: three specialised surfaces from one frontend (RADIOPAD_SURFACE)

RadioPad is **desktop-first, surface-specialised**. One `frontend/` codebase builds three scoped
bundles — **desktop** (the whole reporting product), **web** (master-admin ONLY, no reporting), and
**mobile** (a dictation companion that pairs to a live desktop session, no standalone reporting).
Routes are physically staged in or out per surface, so a route in the wrong group ships to the wrong
product or vanishes from all three.

**Before adding or moving any route, page, or nav entry, read [frontend/CLAUDE.md](frontend/CLAUDE.md)** —
it carries the route-group layout, the build-flag mechanics, and the companion relay.

## Project mission

AI-assisted radiology reporting platform. Radiologist drafts, validates, and signs; AI never auto-signs. See [PRD.md](PRD.md) and [PROGRESS.md](PROGRESS.md) (Ralph-loop memory).

## Strict tech stack

The stack is locked to what the manifests already pin. **No other frameworks — do not propose
Express/Fastify/NestJS/etc. for the backend.**

## Repo map

The tree is self-describing; one non-obvious entry is worth stating:

- `mcp-connectors/` — signed clinical data connectors (DICOM/FHIR/PACS) — a **product** feature, not developer MCP

## Commands

**Allowed locally** — cheap, focused feedback:

```powershell
# Run the app
dotnet run --project backend/RadioPad.Api/src/RadioPad.Api   # → http://127.0.0.1:7457
cd frontend && pnpm install && pnpm dev                      # → http://localhost:3000

# One targeted test — never the whole suite
dotnet test --filter <TestName>
pnpm vitest run <one-file>

# CLI
dotnet run --project cli/RadioPad.Cli -- rulebook validate ../../rulebooks/chest_ct_v1.yaml
```

Everything heavier than the above — full builds, full suites, typecheck/lint sweeps, `cargo tauri
build` — is CI's job, per the GitHub Actions rule above. Push and stop.

## Safety boundaries (non-negotiable)

1. RadioPad never auto-signs reports.
2. AI text wears `.ai-mark` until reviewed.
3. ~~PHI gate~~ **Removed by explicit operator instruction (2026-07-20).** Every enabled provider may receive any request regardless of `ProviderComplianceClass` or PHI content — including third-party/browser-automation providers with no BAA. Compliance classes are informational metadata only. **Do not reintroduce it, and do not "fix" it as a bug** — it is deliberate, and `AiPolicyHttpTests` / `RewriteModeTests` assert the current behaviour. (This is worth stating twice because it already happened once: an agent read the removal as an accident and restored the gate.) Two non-PHI checks deliberately remain in `AiGateway.EnforcePhiPolicy`: `Enabled == false` and `Compliance == Blocked` are explicit operator switches. **What replaces the gate is the audit trail** — `ContainsPhi` is still computed and recorded on every audit + usage row, so PHI routing is unrestricted but reconstructable. Keep it that way: unrestricted *and* invisible is the state to avoid.
4. Audit log is append-only; use `IAuditLog.AppendAsync` only.
5. Backend binds `127.0.0.1` by default.
6. Tenant isolation enforced via `TenantedController.ResolveContextAsync`.

## Memory checkpoints

- See [/memories/repo/radiopad-design-lock.md](/memories/repo/radiopad-design-lock.md) for the design-lock note.
- Update `PROGRESS.md` after completing a checklist item.
- Update `docs/` when changing behaviour.

## Code navigation

Prefer semantic tools over grepping or reading whole files: Serena's symbol tools (`find_symbol`, `find_referencing_symbols`, `get_symbols_overview`, `search_for_pattern`) for navigation/edits, and CodeGraph queries (`codegraph_explore` or `codegraph explore "<question>"`) for dependency/impact questions. Fall back to Grep/Read only when the semantic tools can't answer.
