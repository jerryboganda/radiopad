# API Design

**Status:** Current  ·  **Owner:** Engineering  ·  **Last Updated:** 2026-05-04  ·  **Source of Truth:** [openapi/openapi.yaml](../../openapi/openapi.yaml) + [api-reference.md](api-reference.md)

## Style

- REST + JSON.
- camelCase property names; ISO-8601 timestamps; UUIDs for ids.
- `JsonIgnoreCondition.WhenWritingNull`.
- Errors are RFC-7807 problem details.

## Versioning

- v0.x: no version segment in the URL (`/api/...`).
- Once `v1.0.0` ships, breaking changes go to `/api/v2/...`. Minor/patch changes keep the same prefix.
- See [../../VERSIONING.md](../../VERSIONING.md).

## Auth

- Tenant header: `X-RadioPad-Tenant: <slug>`.
- User header (dev): `X-RadioPad-User: <email>`.
- Phase 3: `Authorization: Bearer <jwt>` with tenant + user claims.

## Pagination

- Query: `skip` (default 0), `take` (default 25, max 500).
- Response header: `X-Total-Count`.
- Body is the array of items.

## Filtering

- Per-resource. Reports list supports `modality`, `status` (int), `q` (substring across accession / body part / indication).
- Filters are AND-ed.

## Error format

```json
{
  "type": "https://radiopad.dev/errors/provider-policy",
  "title": "Provider not allowed",
  "status": 403,
  "detail": "Provider 'anthropic-prod' is blocked by tenant policy.",
  "kind": "provider_policy",
  "traceId": "<X-Request-Id>"
}
```

`provider_policy` is now raised only when the provider is disabled or carries `Compliance = Blocked`. It is no longer raised for PHI content: the compliance-class routing gate was removed on 2026-07-20 by operator decision, so a `containsPhi: true` request to an enabled provider succeeds and is recorded in the audit trail rather than refused.

The `kind` field is RadioPad-specific and stable. Known kinds: `provider_policy`, `validation`, `tenant_isolation`, `not_found`, `conflict`.

## Idempotency

- `POST /api/reports` is idempotent on `accessionNumber` per tenant; a second POST with the same accession returns the existing report.
- `PATCH` is naturally idempotent.
- `POST /api/reports/{id}/acknowledge` returns 200 if already acknowledged (state transition is a no-op).

## Rate limiting

- AI endpoint group `[EnableRateLimiting("ai")]` = 60 req/min/tenant.
- Other endpoints: no rate limit in v0.x; planned per-tenant token bucket.

## Webhooks

- Not in v0.x. Planned for Phase 2 (`reports.signed`, `reports.exported`, `audit.written`).
