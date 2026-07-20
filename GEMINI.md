# GEMINI.md — RadioPad project context for Gemini CLI

> Read this file before any task. **[CLAUDE.md](CLAUDE.md) is the authoritative source of truth** — this file is a thin pointer to it for Gemini agents; if they ever disagree, CLAUDE.md wins.

## Project summary

RadioPad is an AI-assisted radiology reporting platform. A radiologist drafts a structured report, AI suggests phrasing, a rulebook validates findings, and the radiologist signs/exports — RadioPad never auto-signs.

## ⚠️ UI/UX is LOCKED — the "RC" design system

The frontend uses the **RC design system**: a light-first white/blue clinical-SaaS palette with a **first-class deep-navy dark theme — both themes are mandatory**. The canonical token source is [frontend/app/tokens.css](frontend/app/tokens.css) (RC primitives + alias layer) with `var()`-based scales in [frontend/tailwind.config.ts](frontend/tailwind.config.ts); build-time **Tailwind 3 IS part of the stack**. Full contract: [CLAUDE.md](CLAUDE.md) + [docs/02-design/design.md](docs/02-design/design.md). Use only the documented alias tokens and `.rp-*` component classes. **Do not** hardcode colours, add accent colours, introduce MUI/Ant/Chakra/Bootstrap, use emoji-as-icons, or use any primary navigation other than the left-sidebar shell. (The retired "Open Design warm-paper / no-dark-mode / no-Tailwind" rule no longer applies.)

## ⚠️ Compute runs on GitHub Actions — not this machine

**All heavy work — full builds, full test suites, lint/type-check sweeps, bundling, packaging, Docker builds — runs on GitHub Actions, never on the development laptop or the VPS.** Locally: edit code, run one targeted test (`dotnet test --filter <Name>` or a single Vitest file), run the app (`pnpm dev`, `dotnet run`). Nothing more. Full contract in [CLAUDE.md](CLAUDE.md).

## ⚠️ Code, don't babysit

**Write code; don't wait on machines.** Small quick checks only. When the work is done, commit, push, and stop — never watch or poll CI (`gh run watch` is out). The operator monitors runs and reports failures. Because CI is unobserved, report what you changed and let CI decide — never claim a change builds or passes when you have not seen it do so. Full contract in [CLAUDE.md](CLAUDE.md).

## Strict tech stack

| Layer | Technology |
| --- | --- |
| Web | Next.js 16 (App Router) + React 18 + TypeScript |
| Backend | ASP.NET Core 8 + EF Core |
| Desktop | Tauri 2 (Rust) |
| Mobile | Capacitor 6 |
| CLI | .NET 8 global tool |

Do not propose other frameworks/ORMs.

## Context map

- [docs/INDEX.md](docs/INDEX.md) — documentation entry point
- [docs/03-architecture/architecture.md](docs/03-architecture/architecture.md) — system overview
- [docs/04-security/security-architecture.md](docs/04-security/security-architecture.md) — trust boundaries & PHI policy
- [docs/05-data-ai/ai-product-spec.md](docs/05-data-ai/ai-product-spec.md) — AI gateway & guardrails
- [PRD.md](PRD.md) — engineering PRD
- [PROGRESS.md](PROGRESS.md) — Ralph-loop log

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

## Agent behavior rules

1. RadioPad never auto-signs; AI text wears `.ai-mark` until reviewed.
2. PHI requests are blocked unless provider compliance is `PhiApproved` or `LocalOnly` (enforced in `AiGateway`; throws `ProviderPolicyException`).
3. Audit log is append-only via `IAuditLog.AppendAsync`.
4. Backend binds `127.0.0.1` by default.
5. Tenant isolation enforced via `TenantedController.ResolveContextAsync`.
6. Update `PROGRESS.md` and the relevant `docs/` page when behaviour changes.

## Known risks

- Provider policy must audit `ProviderBlocked` before rethrowing.
- Audit `IntegrityChain` is SHA-256 over `{id}|{tenantId}|{(int)action}|{detailsJson}|{prevHash}`; never UPDATE/DELETE rows.
- Rulebooks with `status: approved` require golden-case tests under `rulebooks/_tests/<id>/`.
