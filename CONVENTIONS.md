# RadioPad Conventions

**Status:** Current  ·  **Owner:** Engineering  ·  **Last Updated:** 2026-05-04  ·  **Source of Truth:** Yes

---

## Naming

| Surface | Convention | Example |
| --- | --- | --- |
| C# types | PascalCase | `ReportValidator` |
| C# methods | PascalCase, async ⇒ `Async` suffix | `RouteAsync` |
| C# fields | `_camelCase` (private), `PascalCase` (public/record props) | `_db`, `TenantId` |
| TS files | `kebab-case.ts` (libs), `page.tsx` / `layout.tsx` (App Router) | `lib/api.ts` |
| TS components | PascalCase | `FindingsPanel` |
| TS variables | camelCase | `aiHighlights` |
| Routes | `/api/<resource>/<id>/<action>` | `/api/reports/{id}/ai` |
| Rulebooks | `snake_case` id, `semver` version | `chest_ct_v1` |
| Templates | `kebab-case.json` | `chest-ct.json` |
| Branches | `feat/`, `fix/`, `docs/`, `chore/`, `sec/` | `feat/version-history` |

## Files & folders

- App Router only. No `pages/` directory.
- Co-locate page-specific helpers next to the page.
- Backend solution layered: `Domain`, `Application`, `Validation`, `Infrastructure`, `Api`.
- Tests live under `<project>.Tests` or `frontend/__tests__/`.

## Formatting

- C#: `dotnet format`. File-scoped namespaces, `Nullable enable`, no `!` without justification.
- TS: project default Prettier; no inline `style={{ color, background, border, borderRadius }}` — use locked classes.
- Markdown: ATX headings, fenced code blocks with language tags, KaTeX for math.

## Error handling

- Backend: throw typed exceptions (`ProviderPolicyException`, `ValidationException`); `GlobalExceptionMiddleware` translates to RFC-7807 problem details.
- Frontend: surface errors via the locked `.banner.warn` / `.finding.blocker` classes; never silently swallow.
- Never catch + log + ignore a `ProviderPolicyException` — it must reach the user.

## Logging

- Structured (`Microsoft.Extensions.Logging` + `AddSimpleConsole`).
- Correlation header: `X-Request-Id` (added by `RequestCorrelationMiddleware`).
- Never log PHI, secrets, or full report bodies.

## Testing

- xUnit + plain `Assert` for backend.
- `WebApplicationFactory<Program>` for integration tests; tenant slug `it`, user `it-radiologist@radiopad.local`.
- Vitest (frontend) where unit-testable; otherwise rely on `pnpm typecheck`.
- Approved rulebooks need golden cases under `rulebooks/_tests/<id>/`.

## API

- REST + JSON. camelCase. `JsonIgnoreCondition.WhenWritingNull`.
- Tenant headers: `X-RadioPad-Tenant`, `X-RadioPad-User`.
- Pagination: `skip`, `take` (≤500), `X-Total-Count` response header.
- Errors: `{ error: string, kind?: string }` with appropriate HTTP code; PHI policy block ⇒ 403 `kind: "provider_policy"`.

## Database

- EF Core. SQLite for dev; PostgreSQL for prod (connection-string sniff).
- Every tenanted table has `TenantId Guid` + indexed FK; queries must filter through `ResolveContextAsync`.
- `AuditEvents` is append-only with SHA-256 chain.
- Migrations require human review.

## Documentation

- Update the canonical doc in the same PR as the behaviour change.
- New design tokens/components require `globals.css` + `docs/02-design/design.md` updated together.
- Use `Status / Owner / Last Updated` header on living docs.
