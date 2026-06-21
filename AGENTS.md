# RadioPad — instructions for AI coding agents

This file is the entry point for every AI coding agent (Copilot, Claude Code, Codex, Cursor, Gemini, etc.) working in this repository. Read it before making any change.

---

## 0. MISSION-CRITICAL UI/UX RULE (read first)

> **RadioPad's visual TOKENS (palette, typography, accent `#c96442`, semantic families, `.ai-mark`, radii, shadows) are LOCKED. The APP SHELL has been modernized to a left-sidebar SaaS layout — the sidebar shell is now canonical.** You MUST NOT introduce a different design system, colour palette, dark-mode variant, or component library (Tailwind / MUI / Ant / Chakra / Bootstrap).

The full spec lives in [docs/02-design/design.md](docs/02-design/design.md). The canonical stylesheet is [frontend/app/globals.css](frontend/app/globals.css) (token layer) plus [frontend/app/shell.css](frontend/app/shell.css) (sidebar shell + page chrome). When in doubt, read the design doc and copy the existing pattern.

Concretely:

- Use the documented design tokens (`--bg`, `--accent: #c96442`, `--text`, etc.). **Do not invent new ones.**
- Render every page inside `<AppShell>` (`frontend/components/shell/AppShell.tsx`). Use `<Container>` + `<PageHeader>` for the top of every page; do not re-implement chrome.
- Use the documented component classes (`.rp-shell`, `.rp-sidebar`, `.rp-topbar`, `.rp-page-header`, `.rp-panel`, `.section-block`, `.composer`, `.primary`, `.ghost`, `.subtle`, etc.). The legacy `.app` / `.topbar` classes survive only as in-page editor chrome inside `.split` two-pane surfaces — they must not be used as the application root.
- Reports / AI prose render in the serif stack (`var(--serif)`); chrome in sans; codes in mono.
- Validation severities map to the semantic families: blocker → red, warning → amber, info → blue.
- AI-generated text **must** be wrapped in `.ai-mark` (purple family) until reviewed.
- Data-driven pages render `<Skeleton />` while loading, `<EmptyState />` for zero rows, and `<ErrorState onRetry />` on fetch failure.
- **Forbidden:** Tailwind utility-only styling, Material UI / Ant / Chakra / Bootstrap, dark-mode variants, emoji-as-icons, generic dark-grey "developer-tool" palettes, additional accent colours, primary navigation patterns other than the canonical left-sidebar shell.

If a UI requirement cannot be met with the existing tokens/components, stop and add the new token/class to `globals.css` (or `shell.css`) + `docs/02-design/design.md` in the same PR — never ship a one-off style.

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
frontend/               Next.js app (App Router) — UI/UX locked to Open Design system
desktop/                Tauri shell
mobile/                 Capacitor project
cli/RadioPad.Cli/       .NET global tool
rulebooks/              YAML rulebooks (chest_ct_v1, brain_mri_v1, …)
templates/              JSON report templates
docs/                   Documentation (00-product, 02-design, 03-architecture, 04-security, …)
src/                    LEGACY Open Design web app — kept for reference only
daemon/                 LEGACY Open Design Node daemon — kept for reference only
*.legacy.*              Archived original Open Design root files
```

You may freely edit anything under `frontend/`, `backend/`, `desktop/`, `mobile/`, `cli/`, `docs/`, `rulebooks/`, `templates/`. **Do not modify** `src/` or `daemon/` — those are the legacy reference. Treat `*.legacy.*` files as read-only history.

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
- Read the matching legacy file in `src/` or `daemon/` (Open Design reference) for UX patterns — but do not copy implementation logic; reimplement on the strict stack.
- Open an explicit "open question" in `PROGRESS.md` rather than guessing.
