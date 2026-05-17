# API Documentation

**Status:** Current  ·  **Owner:** Engineering  ·  **Last Updated:** 2026-05-17  ·  **Source of Truth:** [openapi/openapi.yaml](../../openapi/openapi.yaml)

## Overview

REST + JSON. RFC-7807 problem details with a stable `kind` field. ISO-8601 timestamps. UUIDs for ids.

## Authentication

- Production-like API requests use `Authorization: Bearer rp_<opaque>` with tenant/user lookup hints, or a validated OIDC JWT.
- `X-RadioPad-Tenant` and `X-RadioPad-User` are authoritative only in explicit dev/test mode.
- SCIM and ingest endpoints use their own tenant-scoped bearer secrets.

## Base URL

- Dev: `http://127.0.0.1:7457`.
- Production: `https://<your-host>` (Next.js proxy forwards `/api/*`).

## Endpoints (summary)

- `GET  /api/health`
- `GET  /api/health/ready`
- `GET  /api/reports?skip&take&modality&status&q` → `X-Total-Count`
- `POST /api/reports`
- `GET  /api/reports/{id}`
- `PATCH /api/reports/{id}`
- `POST /api/reports/{id}/validate`
- `POST /api/reports/{id}/ai`  (rate-limited group `"ai"`)
- `POST /api/reports/{id}/acknowledge`
- `GET  /api/reports/{id}/export/text`  (text/plain)
- `GET  /api/reports/{id}/export/fhir`  (application/fhir+json)
- `GET  /api/reports/{id}/versions`
- `GET  /api/rulebooks`, `POST /api/rulebooks/save`
- `POST /api/rulebooks/{id}/approve`, `POST /api/rulebooks/{id}/deprecate`
- `GET  /api/templates`, `POST /api/templates/save`
- `GET  /api/providers`, `POST /api/providers/save`
- `GET  /api/audit`

See [openapi.yaml](../../openapi/openapi.yaml) for full schemas.

## Error format

```json
{
  "type": "https://radiopad.dev/errors/provider-policy",
  "title": "Provider not allowed for PHI",
  "status": 403,
  "detail": "...",
  "kind": "provider_policy",
  "traceId": "abc123"
}
```

`kind` enum: `provider_policy`, `validation`, `tenant_isolation`, `not_found`, `conflict`, `provider_unavailable`, `internal`.

## Pagination

- `skip` (default 0), `take` (default 25, max 500).
- `X-Total-Count` header on list endpoints.

## Rate limits

- AI endpoints: 60 req/min/tenant.
- Other endpoints: no rate limit in v0.x (planned per-tenant + per-IP buckets in Phase 2).

## Versioning

See [VERSIONING.md](../../VERSIONING.md). v0.x: no version segment in URL. Breaking changes will introduce `/api/v2/...`.

## Examples

```bash
# List reports
curl -H "X-RadioPad-Tenant: dev" -H "X-RadioPad-User: dev@example.com" \
  "http://127.0.0.1:7457/api/reports?take=10"

# Ask AI for an impression
curl -X POST -H "Content-Type: application/json" \
  -H "X-RadioPad-Tenant: dev" -H "X-RadioPad-User: dev@example.com" \
  -d '{"prompt":"impression","containsPhi":false,"providerId":"<guid>"}' \
  "http://127.0.0.1:7457/api/reports/<id>/ai"
```
