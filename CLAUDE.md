# CLAUDE.md — RadioPad project memory for Claude Code

# CLAUDE.md — RadioPad project memory for Claude Code

## ⚠️ MISSION-CRITICAL: Tokens LOCKED, shell modernized to sidebar

RadioPad's visual **tokens** (palette, typography, accent `#c96442`, `.ai-mark`, semantic families) are LOCKED. The **app shell** has been modernized from the original Open Design topbar+split layout into a left-sidebar SaaS shell — that sidebar shell is now canonical. Read [docs/02-design/design.md](docs/02-design/design.md) before touching any UI; the canonical stylesheets are [frontend/app/globals.css](frontend/app/globals.css) (tokens) + [frontend/app/shell.css](frontend/app/shell.css) (sidebar shell + page chrome).

Hard rules:

1. Use the documented design tokens only (`--bg: #faf9f7`, `--accent: #c96442`, semantic families: green/blue/purple/red/amber). Do not invent new tokens or add a dark mode.
2. Render every page inside `<AppShell>` (`frontend/components/shell/AppShell.tsx`). Use `<Container>` + `<PageHeader>` for the top of every page. Use the documented component classes only (`.rp-shell`, `.rp-sidebar`, `.rp-topbar`, `.rp-page-header`, `.rp-panel`, `.section-block`, `.composer`, `.msg`, `.finding`, `.ai-mark`, `.brand-mark`, `.badge`, button variants `.primary` / `.primary-ghost` / `.ghost` / `.subtle`). The legacy `.app` / `.topbar` classes are reserved for in-page editor chrome inside `.split` two-pane surfaces and must not be the application root.
3. AI-generated text wears `.ai-mark` (purple family) until acknowledged.
4. Validation severities map: Blocker→red, Warning→amber, Info→blue.
5. Data-driven pages use `<Skeleton />` / `<EmptyState />` / `<ErrorState onRetry />` for loading / empty / error states.
6. **Forbidden:** Tailwind utility-only styling, MUI/Ant/Chakra/Bootstrap, dark mode, emoji as icons, additional accent colours, primary navigation patterns other than the canonical left-sidebar shell.

If a token or component doesn't exist for what you need, extend `globals.css` (tokens) or `shell.css` (shell/chrome) and `docs/02-design/design.md` in the same change — never inline.

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
- `frontend/` — Next.js app
- `desktop/` — Tauri shell
- `mobile/` — Capacitor project
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
