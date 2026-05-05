# Domain Events

**Status:** Draft (no event bus in v0.x)  ·  **Owner:** Engineering  ·  **Last Updated:** 2026-05-04

> RadioPad does not run an event bus today. Domain transitions are recorded as **audit events** (see [../04-security/audit-logging.md](../04-security/audit-logging.md)) and consumed by humans / `radiopad audit verify`. This document describes the planned event surface for Phase 2 webhooks.

## Planned events

| Event | Payload | Trigger |
| --- | --- | --- |
| `report.drafted` | `{ tenantId, reportId, modality, bodyPart, authorUserId, createdAt }` | `POST /api/reports` |
| `report.edited` | `{ tenantId, reportId, sequence, authorUserId, updatedAt }` | `PATCH /api/reports/{id}` |
| `report.validated` | `{ tenantId, reportId, rulebookId, rulebookVersion, findingCounts: { blocker, warning, info } }` | `POST /api/reports/{id}/validate` |
| `report.acknowledged` | `{ tenantId, reportId, authorUserId, acknowledgedAt }` | `POST /api/reports/{id}/acknowledge` |
| `report.exported` | `{ tenantId, reportId, format, exportedAt }` | `GET /api/reports/{id}/export/*` |
| `provider.blocked` | `{ tenantId, providerId, reason, requestKindHash }` | PHI gate block |
| `rulebook.approved` | `{ tenantId, rulebookId, version, approvedBy }` | `POST /api/rulebooks/{id}/approve` |
| `rulebook.deprecated` | `{ tenantId, rulebookId, version, deprecatedBy }` | `POST /api/rulebooks/{id}/deprecate` |

## Producers

- All current producers are inside `RadioPad.Application` services. They write audit events synchronously today; a future webhook dispatcher will read the audit stream.

## Consumers

- v0.x: human operators via `radiopad audit export`.
- Phase 2: tenant-configured webhook endpoints with HMAC-SHA256 signatures.

## Idempotency

- Each event carries the `auditEventId` so consumers can dedupe.
- Webhook deliveries (Phase 2) include `Idempotency-Key` derived from the audit event id.

## Schema versioning

- Event payloads are versioned by `schemaVersion: 1` (planned). Breaking changes increment the schema version; old subscribers receive both versions for one minor release.
