# Frontend Architecture

**Status:** Current  ·  **Owner:** Engineering  ·  **Last Updated:** 2026-05-04

## Stack

Next.js 16 App Router · React 18 · TypeScript · static export (`output: 'export'`) · locked Open Design CSS in `frontend/app/globals.css` + `radiopad.css`.

## Routing

- **App Router only.** No `pages/` directory.
- Page-specific helpers co-located next to the page (e.g. `frontend/app/reports/[id]/page.tsx` next to its helpers).
- Dynamic routes use the `[param]` convention: `app/reports/[id]/page.tsx`.

## State management

- React local state + URL state. **No Redux / Zustand / Recoil.**
- Pagination state is URL-bound (`?skip=&take=`) so refresh preserves position.
- Server data is fetched via `frontend/lib/api.ts` and cached only in component state. There is no global cache layer in v0.x; React Query is a candidate for Phase 2.

## Data fetching

- Always go through `frontend/lib/api.ts` — typed `request<T>` + paginated `requestPaged<T>`.
- Never call `fetch` directly from a page.
- Errors are caught at the call site and displayed via a locked `.banner.warn`.

## Component organisation

- Pages own their layout; they compose locked CSS classes directly.
- No cross-page component library in v0.x — keep it local.
- A `<FindingsPanel>` or similar will graduate to a shared component when reused thrice (rule of three).

## Styling

- Tokens and component classes live in `frontend/app/globals.css` (verbatim Open Design copy) and `frontend/app/radiopad.css` (RadioPad-specific additions).
- **No inline `style={{ color, background, border, borderRadius }}`.** Use classes.
- New tokens require a paired update to [../02-design/design.md](../02-design/design.md).

## Auth handling

- v0.1: dev tenant via `X-RadioPad-Tenant` header set by `frontend/lib/api.ts`.
- Phase 3: OIDC via `next-auth` or equivalent; session cookie set by backend.

## Error handling

- Async boundaries use `try/catch` in components.
- The typed client throws on non-OK; pages handle the error and surface `.banner.warn` with the request id.

## Testing strategy

- `pnpm typecheck` on every PR.
- Vitest for utility functions.
- No screenshot snapshot tests (the design lock IS the test).

## Build / output

- `pnpm build` produces `frontend/out/` — consumed by Tauri (`webDir: ../frontend/out`) and Capacitor.
- Dev rewrites `/api/*` → `http://127.0.0.1:7457` (see `next.config.ts`).
