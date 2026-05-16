# Copilot instructions for RadioPad

## ⚠️ UI/UX is LOCKED — read this first

The RadioPad frontend uses the Open Design (Claude.ai-inspired) visual language. **Do not deviate.** The full spec lives in [docs/02-design/design.md](../docs/02-design/design.md); the canonical stylesheet is [frontend/app/globals.css](../frontend/app/globals.css).

When generating any UI code:

- Use only the documented design tokens (`--bg: #faf9f7`, `--accent: #c96442`, `--text`, `--border`, semantic families: green/blue/purple/red/amber).
- Use only the documented component classes (`.app`, `.topbar`, `.split`, `.pane`, `.panel`, `.section-block`, `.composer`, `.composer-shell`, `.msg`, `.finding`, `.ai-mark`, `.brand-mark`, `.badge`).
- Buttons: `.primary`, `.primary-ghost`, `.ghost`, `.subtle`, `.icon-btn`. Exactly one `.primary` per surface.
- Reports/AI prose use `var(--serif)`; UI chrome uses `var(--sans)`; rule ids and accession numbers use `var(--mono)` via `<code>`.
- AI-generated text is wrapped in `.ai-mark` (purple family) until acknowledged.
- Validation severities: `Blocker → red`, `Warning → amber`, `Info → blue`.

**Forbidden:** Tailwind utility-only styling, MUI / Ant / Chakra / Bootstrap, dark mode, emoji as functional icons, generic dark "developer-tool" palettes, additional accent colours, replacing `.app > .topbar + .split` with another shell.

If a token/component doesn't exist for what you need, add it to `globals.css` AND `docs/02-design/design.md` in the same change — never inline a one-off style.

## Tech stack (strict)

| Layer | Technology |
| --- | --- |
| Web | Next.js 16 App Router, TypeScript, React 18 |
| Backend | ASP.NET Core 8, EF Core (SQLite dev / PostgreSQL prod) |
| Desktop | Tauri 2 (Rust) |
| Mobile | Capacitor 6 |
| CLI | .NET 8 global tool |

Do not introduce other frameworks or ORMs.

## Conventions

### TypeScript (frontend/)

- App Router only. `'use client'` only when needed.
- Always go through the typed client in `frontend/lib/api.ts` — never call `fetch` from a page.
- No inline `style={{ color, background, border, borderRadius }}`. Use classes.
- Keep components small; prefer composition over abstraction.

### C# (backend/, cli/)

- File-scoped namespaces, `Nullable enable`, async methods end in `Async` and take `CancellationToken ct` last.
- Records for DTOs; classes for entities.
- Tenant isolation: queries must filter by the tenant id from `TenantedController.ResolveContextAsync`.
- Audit log writes go through `IAuditLog.AppendAsync`. Never UPDATE/DELETE `AuditEvents`.
- PHI policy: the AI gateway throws `ProviderPolicyException` when policy blocks a request — never swallow it.

### Rulebooks (YAML)

- `rulebook_id` snake_case, semver in `version`. Approved rulebooks need passing golden-case tests.

## PR checklist

- [ ] UI uses locked tokens/classes only (no Tailwind / MUI / dark mode / emoji icons).
- [ ] `dotnet build && dotnet test` for backend changes.
- [ ] `pnpm typecheck` for frontend changes.
- [ ] No secrets / PHI in code or fixtures.
- [ ] `PROGRESS.md` updated when an item closes.
- [ ] Docs updated when behaviour changes.
