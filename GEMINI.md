# GEMINI.md — RadioPad project context for Gemini CLI

> Read this file before any task. It mirrors [AGENTS.md](AGENTS.md) and [CLAUDE.md](CLAUDE.md) for Gemini agents.

## Project summary

RadioPad is an AI-assisted radiology reporting platform. A radiologist drafts a structured report, AI suggests phrasing, a rulebook validates findings, and the radiologist signs/exports — RadioPad never auto-signs.

## ⚠️ UI/UX is LOCKED

The frontend uses the Open Design (Claude.ai-inspired) warm-paper visual language. Spec lives in [docs/02-design/design.md](docs/02-design/design.md); canonical stylesheet is [frontend/app/globals.css](frontend/app/globals.css). Use only the documented tokens and component classes. **Do not** introduce Tailwind utilities, MUI/Ant/Chakra/Bootstrap, dark mode, emoji icons, or alternate accents.

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
