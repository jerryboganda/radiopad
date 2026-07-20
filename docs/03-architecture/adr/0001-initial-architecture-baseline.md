# ADR 0001: Initial Architecture Baseline

## Status
Accepted (2026-05-04). Mirror of [ADR-0001-stack.md](ADR-0001-stack.md) using the canonical doc-generator numbering.

## Context

RadioPad inherits a polyglot UX prototype (the "Open Design" Node daemon + React playground) and must converge on a stack capable of:

- Clinical-grade reliability (audit chain, append-only).
- Strong PHI controls.
- Multi-surface delivery (web, desktop, mobile, CLI) without parallel codebases.
- Long-term maintainability with a small core team.

## Decision

Adopt the strict stack:

- **Web:** Next.js 16 App Router (TypeScript, React 18) with static export.
- **Backend:** ASP.NET Core 8 + EF Core (SQLite dev / PostgreSQL prod).
- **Desktop:** Tauri 2 wrapping the static export.
- **Mobile:** Capacitor 6 wrapping the static export.
- **CLI:** .NET 8 global tool (`radiopad`).

Lock the UI/UX to the Open Design (Claude.ai-inspired) warm-paper visual language. Codify the lock in `docs/02-design/design.md`, `frontend/app/globals.css`, `AGENTS.md`, `CLAUDE.md`, and Cursor rules.

Record clinical-safety primitives early:

- `AiGateway.EnforcePhiPolicy` blocks PHI requests to non-compliant providers. (Superseded: this gate was removed on 2026-07-20 by operator decision. PHI now routes to any enabled provider and is recorded in the audit trail rather than blocked.)
- `IAuditLog.AppendAsync` is append-only and SHA-256 chained.
- Tenant isolation through `TenantedController.ResolveContextAsync`.

## Consequences

**Positive**
- One TypeScript codebase for all UI surfaces; one C# codebase for backend + CLI; minimal context-switching.
- Strong typing end-to-end.
- Security primitives are part of the foundation, not bolt-ons.

**Negative**
- Constrains future technology choices; new ideas must clear the human-review bar.
- Static-export model imposes some limits on dynamic SSR features (none required for v0.x).

**Open questions**
- Long-term mobile editing scope (currently read/acknowledge only).
- Whether to introduce Hangfire for background jobs in Phase 2 or stay synchronous.

## Alternatives Considered

- Express/Fastify/NestJS for backend — rejected (smaller ecosystem for clinical-grade typing + EF Core).
- Tailwind / MUI for UI — rejected (incompatible with the locked design system).
- Electron for desktop — rejected (heavier and slower than Tauri).

## Follow-up Actions

- Maintain [ADR-0002-design-lock.md](ADR-0002-design-lock.md) and [ADR-0003-audit-chain.md](ADR-0003-audit-chain.md) as the next two baseline ADRs.
- Add ADRs for SSO/OIDC adoption and background-job system once those land.
