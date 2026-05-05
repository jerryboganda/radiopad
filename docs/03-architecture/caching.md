# Caching

**Status:** Current (minimal)  ·  **Owner:** Engineering  ·  **Last Updated:** 2026-05-04

## Today

- **HTTP caching:** Disabled for `/api/*` (responses include `Cache-Control: no-store`). Clinical data must never be cached intermediately.
- **Browser caching:** Static assets in `frontend/out/` are fingerprinted and cached for 1 year (`immutable`); HTML/index for 60 s.
- **In-process caching:** None. Rulebook YAML is parsed on each `validate` call; this is acceptable at current scale (< 50 rulebooks/tenant).

## Planned (Phase 2)

| Layer | TTL | Invalidation | Notes |
| --- | --- | --- | --- |
| Rulebook in-memory cache | 5 min | `POST /rulebooks/save` clears | Avoid re-parsing YAML on hot paths. |
| Provider list per tenant | 1 min | provider write clears | Drives the AI dropdown. |
| OIDC JWKS | 1 hour | refresh on validation failure | Phase 3. |

## Consistency risks

- Stale rulebook would cause a finding mismatch between server and CLI; mitigated by short TTL and write-side invalidation.
- Stale provider list could show a provider that no longer exists; the API still validates server-side so the worst case is a friendly error.

## What we never cache

- Reports, report versions, audit events, AI responses.
- Anything that is tenant-scoped and security-sensitive.

## Implementation notes

- Use `IMemoryCache` for in-process caches; do not use a shared Redis until clinical-safety primitives are stable.
- All cached entries are tenant-keyed; never reuse a cache entry across tenants.
