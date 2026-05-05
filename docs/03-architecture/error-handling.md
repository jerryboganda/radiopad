# Error Handling

**Status:** Current  ·  **Owner:** Engineering  ·  **Last Updated:** 2026-05-04

## Taxonomy

| Class | Source | HTTP | `kind` | Notes |
| --- | --- | --- | --- | --- |
| Validation | Request body | 422 | `validation` | Field path + reason. |
| Authentication | Missing / bad token | 401 | `unauthenticated` | Phase 3. |
| Authorization | Missing permission | 403 | `forbidden` | Tenant or RBAC. |
| Tenant isolation | Cross-tenant access attempt | 404 | `not_found` | Always 404 to avoid leaking existence. |
| Not found | Missing entity | 404 | `not_found` | |
| Conflict | State transition not allowed | 409 | `conflict` | e.g. acknowledge from Draft. |
| Provider policy | PHI to non-compliant provider | 403 | `provider_policy` | Audited as `ProviderBlocked`. |
| Provider failure | Upstream 5xx / timeout | 502 | `provider_unavailable` | Audited as `AiResponse` with error. |
| Internal | Bug | 500 | `internal` | Stack trace not surfaced. |

## User-facing errors

- The frontend renders the `title` and `detail` in a `.banner.warn` and shows the `traceId` for support.
- Stack traces are **never** rendered.

## Internal errors

- `GlobalExceptionMiddleware` logs the full stack with the request id.
- The response body uses RFC-7807 with `kind: "internal"` and a generic title; details are the type name only.

## Retryable vs non-retryable

| Error | Retryable? |
| --- | --- |
| 401 / 403 / 404 / 422 / 409 | No |
| 429 | Yes — respect `Retry-After`. |
| 5xx | Yes once for idempotent verbs; otherwise prompt user. |
| Provider policy block (`provider_policy`) | No — change provider or remove PHI flag. |

## Logging expectations

- One log line per error with `level: warn` for 4xx and `level: error` for 5xx.
- Include: `requestId`, `tenantId`, `userId`, `kind`, `path`, `method`, `status`, `latencyMs`.
- **Never** log PHI, secrets, request bodies, or response bodies.
