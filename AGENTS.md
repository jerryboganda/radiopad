# RadioPad — instructions for AI coding agents

This file is the entry point for every AI coding agent (Claude Code, Codex, Cursor, Gemini, etc.) working in this repository. Read it before making any change.

---

## 0. Source of truth & precedence (read first)

> **[CLAUDE.md](CLAUDE.md) is the authoritative project instruction file.** When anything here disagrees with CLAUDE.md, CLAUDE.md wins. This file and [GEMINI.md](GEMINI.md) are deliberately thin, platform-neutral pointers so every agent (Codex, Cursor, Copilot, Gemini, …) reads the same rules. Do not restate the full contract here — read CLAUDE.md.

## 0.1. MISSION-CRITICAL UI/UX RULE

> **RadioPad's visual identity is the "RC" design system: a light-first white/blue clinical-SaaS palette with a first-class deep-navy dark theme. BOTH themes are mandatory** (light is the first-run default; dark is first-class, never pure black). The APP SHELL is the canonical left-sidebar SaaS layout. You MUST NOT introduce a different design system, colour palette, or component/theme library (MUI / Ant / Chakra / Bootstrap). Build-time **Tailwind 3** IS part of the stack and allowed.

The full contract lives in [CLAUDE.md](CLAUDE.md) (§"RC design system") and [docs/02-design/design.md](docs/02-design/design.md). The **canonical token source** is [frontend/app/tokens.css](frontend/app/tokens.css) (RC `--color-*` primitives — light in `:root`, dark under `html[data-theme="dark"]` — plus the alias layer that re-points RadioPad's original token names) with `var()`-based scales in [frontend/tailwind.config.ts](frontend/tailwind.config.ts) (`darkMode: ['selector', '[data-theme="dark"]']`); [frontend/app/globals.css](frontend/app/globals.css) carries the `@tailwind` directives and imports the token layers, and [frontend/app/shell.css](frontend/app/shell.css) is the sidebar shell + page chrome. When in doubt, read the design doc and copy the existing pattern.

Concretely:

- Write against the documented alias tokens (`--bg`, `--accent`, `--accent-fg`, `--scrim`, semantic families green/blue/red/amber/ai/purple/navy). They resolve to the RC primitives via `tokens.css`. **Do not hardcode colours (no hex/rgb in feature CSS/TSX), do not invent inline tokens, and do not write new code against the retired Hallmark paper/saffron/marine alias names.**
- **Both themes are mandatory** and every UI change must be checked in both before it ships. Print/exports always render the light document theme.
- Render every page inside `<AppShell>` (`frontend/components/shell/AppShell.tsx`). Use `<Container>` + `<PageHeader>` for the top of every page; do not re-implement chrome.
- Use the documented `.rp-*` component classes (`.rp-shell`, `.rp-sidebar`, `.rp-topbar`, `.rp-page-header`, `.rp-panel`, `.section-block`, `.composer`, button variants `.primary` / `.primary-ghost` / `.ghost` / `.subtle`, etc.). The legacy `.app` / `.topbar` classes survive only as in-page editor chrome inside `.split` two-pane surfaces — they must not be used as the application root.
- Validation severities map to the semantic families: Blocker → red, Warning → amber, Info/Style → blue.
- AI-generated text **must** be wrapped in `.ai-mark` (blue "✨ generated" treatment, paired with an amber "Requires review" flag) until reviewed.
- Data-driven pages render `<Skeleton />` while loading, `<EmptyState />` for zero rows, and `<ErrorState onRetry />` on fetch failure.
- **Forbidden:** Material UI / Ant / Chakra / Bootstrap, emoji-as-icons, additional accent colours, hardcoded colours, primary navigation patterns other than the canonical left-sidebar shell. (The old "light-only / no dark mode" rule is **revoked** — dark mode is now required. Build-time **Tailwind 3** is allowed — it compiles to static CSS for `output: 'export'`; utilities and named RC/RadioPad classes may be mixed.)

If a UI requirement cannot be met with the existing tokens/components, stop and extend the RC primitives in `tokens.css` (light **and** dark values, mirrored in `tailwind.config.ts`), or add the new class to `shell.css`, + `docs/02-design/design.md` in the same PR — never ship a one-off style.

---

## 0.5. MISSION-CRITICAL: CPU-INTENSIVE WORK RUNS ON GITHUB ACTIONS (not locally)

> **All heavy, CPU/RAM-intensive tasks for this project — full builds, full test suites, linting, type-checking, static analysis/security scans, bundling, desktop/mobile packaging, Docker image builds, and coverage — are performed by GitHub Actions CI, NOT on a developer's or agent's local/VPS machine.** This is a permanent project rule.

Why: the production server (`/opt/radiopad`) hosts many tenants and the local dev box is shared; saturating either with compiler/test/lint jobs risks the live site and wastes time. CI runners are disposable, parallel, and free for this purpose.

Rules for every AI agent and contributor:

1. **Do not run full builds, full test suites, or lint/type-check sweeps locally or on the VPS** as part of normal work. Push your branch and let GitHub Actions do it. Read the run result with `gh run watch` / `gh run view --log-failed`.
2. **Allowed locally:** focused, cheap feedback only — editing files, reading code, a single targeted unit test (`dotnet test --filter <Name>`), a quick type-check of one changed file, running the app to manually verify a change. Anything that compiles the whole solution, the whole frontend, or runs the whole suite belongs in CI.
3. **Production VPS is for running the app only.** Never invoke `dotnet build/test`, `pnpm build/lint`, `cargo build`, or `docker compose build` on the VPS for development. Deploys pull pre-built images produced by CI (or, when a manual rebuild is unavoidable, it is an explicit operator action — not routine agent behaviour).
4. **Every workflow that gates merge must live in `.github/workflows/`** — backend build+test, frontend typecheck+lint+build, CLI, and the desktop/mobile packaging jobs. If a heavy task is not yet covered by a workflow, add the workflow in the same PR rather than running it by hand.
5. **The PR checklist (§7) is satisfied by green CI, not by local output.** "It builds on my machine" is not evidence; a passing Actions run is.

---

## 0.6. MISSION-CRITICAL: SHIP A DESKTOP RELEASE AFTER EVERY DESKTOP-AFFECTING CHANGE (auto-update)

> **The desktop app self-updates (DESK-001). When you change anything that ends up in the desktop build — anything under `frontend/` or `desktop/` — you MUST cut a new desktop release so users receive it via the in-app "Check for updates" button. This is part of "done", not a separate request the operator has to make.** The operator should never have to ask for the build.

How releases work (fully automated by GitHub Actions — do NOT build locally):

1. After your `frontend/`/`desktop/` changes are committed and pushed, run **one command** from the repo root:
   ```bash
   pnpm release:desktop          # patch bump (e.g. 0.1.23 → 0.1.24); use `minor` / `major` / `X.Y.Z` to override
   ```
   It bumps the version in `desktop/src-tauri/tauri.conf.json` **and** `desktop/src-tauri/Cargo.toml` (they must stay in lock-step), commits, tags `vX.Y.Z`, and pushes the tag.
2. The pushed tag triggers the pipeline with no further action:
   - **`desktop-bundle`** → builds + code-signs the Windows `.msi` and Linux `.AppImage`, creates the GitHub Release, attaches the installers.
   - **`tauri-updater`** → signs `latest.json` and uploads it to that release.
3. The app's button reads `https://github.com/jerryboganda/radiopad/releases/latest/download/latest.json`, so every user auto-downloads the new build. Verify the run is green (`gh run watch …`); don't hand the operator manual steps.

Rules:

- **Backend-only / CLI-only / docs-only changes do NOT need a desktop release** (they don't ship in the desktop bundle). Frontend or `desktop/` changes DO.
- **Never bump only one of the two version files**, and never hand-edit a build — a version mismatch makes the updater loop. Always use `pnpm release:desktop`.
- **Signing keys are operator secrets** already configured in GitHub (`TAURI_PRIVATE_KEY`, `TAURI_KEY_PASSWORD`). The updater public key is embedded in `tauri.conf.json`. Don't regenerate or commit private keys.
- **macOS is intentionally excluded** from the release matrix until Apple Developer signing is set up (see the `os:` list in `.github/workflows/desktop-bundle.yml`). Windows + Linux ship.

---

## 1. Project at a glance

RadioPad is an AI-assisted radiology reporting platform delivered as Web (Next.js), Backend (ASP.NET Core 8), Desktop (Tauri), Mobile (Capacitor), and CLI (.NET global tool). See [PRD.md](PRD.md) for the engineering PRD and [PROGRESS.md](PROGRESS.md) for the Ralph-loop build log.

### Strict tech stack (do not change)

| Layer | Technology |
| --- | --- |
| Web | Next.js 16 (App Router, TypeScript, React 18) |
| Backend | ASP.NET Core 8 + EF Core |
| Desktop | Tauri 2 (Rust + bundled web export) |
| Mobile | Capacitor 6 |
| CLI | .NET 8 global tool (`radiopad`) |

Adding any other backend framework, ORM, or UI framework requires explicit human approval.

---

## 2. Repository map

```
backend/RadioPad.Api/   ASP.NET Core solution (Domain, Application, Validation, Infrastructure, Api, tests)
frontend/               Next.js app (App Router) — UI/UX uses the RC design system (light + first-class dark) + build-time Tailwind 3
desktop/                Tauri 2 shell (consumes frontend/out-desktop)
mobile/                 Capacitor 6 companion (consumes frontend/out-mobile)
cli/RadioPad.Cli/       .NET 8 global tool
rulebooks/              YAML rulebooks (chest_ct_v1, brain_mri_v1, …)
templates/              JSON report templates
subagents/              Portable AI subagent roles (explorer, code-reviewer, test-runner, feature-dev)
mcp-connectors/         Signed clinical data connectors (DICOM/FHIR/PACS) — a PRODUCT feature, not developer MCP
docs/                   Documentation (00-product, 02-design, 03-architecture, 04-security, …)
```

You may freely edit anything under `frontend/`, `backend/`, `desktop/`, `mobile/`, `cli/`, `docs/`, `rulebooks/`, `templates/`. (The former Open Design legacy tree — `src/`, `daemon/`, `app/`, `design-systems/`, and the `*.legacy.*` files — has been removed from the repo.)

---

## 3. Setup & validation commands

```powershell
# Backend
cd backend/RadioPad.Api
dotnet restore
dotnet build
dotnet test
dotnet run --project src/RadioPad.Api    # http://127.0.0.1:7457

# Frontend
cd frontend
pnpm install
pnpm dev          # http://localhost:3000 (proxies /api → backend)
pnpm typecheck
pnpm build        # static export → frontend/out/

# CLI
dotnet run --project cli/RadioPad.Cli -- rulebook validate ../../rulebooks/chest_ct_v1.yaml

# Desktop / mobile
cargo tauri build          # in desktop/ after `pnpm build` in frontend/
npx cap copy android       # in mobile/
```

> **The commands above are for local *focused* feedback only (one targeted test, a quick run). Full builds, full test runs, and lint/type-check sweeps are CI's job — see §0.5. Do not run them locally or on the VPS.**

After any code change, validation is done by **GitHub Actions** on push/PR:
1. Backend build + `dotnet test` (Validation / Application / Domain).
2. Frontend `pnpm typecheck` + lint + `pnpm build`.
3. CLI + desktop/mobile packaging jobs.

Locally, at most run a single targeted test for the thing you changed (`dotnet test --filter <Name>`), then push and watch CI: `gh run watch` / `gh run view --log-failed`.

---

## 4. Architectural & safety boundaries

These are non-negotiable. Violations must be reverted in review.

1. **Radiologist owns the final report.** RadioPad never auto-signs.
2. **AI-drafted text is visually marked** (`.ai-mark`) until reviewed.
3. **PHI policy is enforced in the AI gateway.** A request with `containsPhi: true` may only route to a provider with compliance class `PhiApproved` or `LocalOnly`. The gateway throws `ProviderPolicyException`; never swallow this exception silently.
4. **Audit log is append-only.** Use `IAuditLog.AppendAsync`. Never `UPDATE` or `DELETE` rows in `AuditEvents`.
5. **Backend binds 127.0.0.1 by default.** Remote exposure requires the operator to set `RADIOPAD_BIND` explicitly.
6. **Secrets never appear in logs, JSON responses, or test fixtures.** Provider API keys live in env vars referenced by `ApiKeySecretRef` (`env:NAME`).
7. **Tenant isolation:** every query must filter by the current tenant id resolved through `TenantedController.ResolveContextAsync`.

---

## 4.5. UBAG integration

- Adapter id `ubag` routes report AI work through the UBAG browser-automation gateway. The adapter is `PhiApproved` (operator decision 2026-06-27): only de-identified report text is routed, gated by AiGateway's compliance class; the Hub endpoints (`/api/ubag/*`) still apply PHI/secret heuristics to raw operator prompts.
- The env contract (base URL, auth secret-ref, allowed/ordered targets, alert email) is documented in [docs/03-architecture/provider-catalog.md](docs/03-architecture/provider-catalog.md) and [docs/07-devops/deploy-guide.md](docs/07-devops/deploy-guide.md).
- Login signal is **tri-state**: a gateway that registers no browser context is "no signal" — never treat missing contexts as logged-out or disable a provider on it; only an explicit `login_state` context row flips `Enabled`.
- UBAG failures must throw `ProviderTransportException` so gateway failover works.
- Provider logins (ChatGPT/Gemini/DeepSeek) remain manual through UBAG Browser Sessions; RadioPad never automates login, CAPTCHA, 2FA, consent, cookies, or credentials.
- As everywhere, [CLAUDE.md](CLAUDE.md) remains authoritative if anything here disagrees.

---

## 5. Files requiring human review before merge

- `backend/RadioPad.Api/src/RadioPad.Application/Services/AiGateway.cs` (PHI policy)
- `backend/RadioPad.Api/src/RadioPad.Validation/Engine/ReportValidator.cs` (clinical rules)
- `backend/RadioPad.Api/src/RadioPad.Application/Services/FhirDiagnosticReportSerializer.cs` (interop contract)
- `backend/RadioPad.Api/src/RadioPad.Infrastructure/Persistence/RadioPadDbContext.cs` (migrations / data model)
- Anything in `rulebooks/` whose `status: approved` line changes
- `docs/02-design/design.md` and `frontend/app/globals.css` (design system)

---

## 6. Coding conventions

### C# (backend, CLI)

- File-scoped namespaces.
- `Nullable enable` everywhere; no `!` operator unless justified by a comment.
- Records for DTOs; classes for entities.
- Async methods end in `Async` and accept `CancellationToken ct` as the last argument.
- Tests use xUnit + plain `Assert`. No FluentAssertions / Moq unless added with approval.

### TypeScript / React (frontend)

- App Router only. No legacy `pages/` directory.
- `'use client'` only when the component needs state or browser APIs.
- Reuse the typed `api` client in `frontend/lib/api.ts` — never call `fetch` directly from a page.
- Style with the locked design tokens via the existing class names. **No inline styles for colours/borders/radii** — only for layout-specific positions when no class fits.
- Keep components small; co-locate page-specific helpers next to the page.

### Rulebooks (YAML)

- `rulebook_id` is `snake_case` and stable across versions.
- Increment `version` (semver) on every published change.
- A rulebook with `status: approved` must have at least one passing golden-case test under `rulebooks/_tests/<rulebook_id>/`.

---

## 7. PR checklist

- [ ] Tech stack respected (Next.js / ASP.NET Core / Tauri / Capacitor only).
- [ ] **UI work uses only locked design tokens & classes** (see §0).
- [ ] `dotnet build` and `dotnet test` pass for backend changes.
- [ ] `pnpm typecheck` passes for frontend changes.
- [ ] No PHI, secrets, or real patient data in fixtures, logs, or screenshots.
- [ ] Audit log untouched in append-only semantics.
- [ ] `PROGRESS.md` updated when a checklist item is finished.
- [ ] `docs/` updated when behaviour changes.

---

## 8. When you're stuck

- Search `docs/` first.
- Check the reference mockups under `UI UX SCREENS/` and `docs/02-design/design.md` for UX patterns; reimplement on the strict stack.
- Open an explicit "open question" in `PROGRESS.md` rather than guessing.
