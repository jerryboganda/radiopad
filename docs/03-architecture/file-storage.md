# File Storage

**Status:** Draft (no first-party blob storage in v0.x)  ·  **Owner:** Engineering  ·  **Last Updated:** 2026-05-04

RadioPad v0.x does not store images, attachments, or large blobs. Reports are pure structured text. This document describes the upload surface that will be added in Phase 2/3.

## Today

- The only "file" surfaces are **export** endpoints (text + FHIR JSON) which are streamed; nothing is persisted.
- Audit export bundles are produced on demand by `radiopad audit export` and streamed to the operator's local disk.

## Planned (Phase 2)

### Upload flow (attachments to a report)

1. Client calls `POST /api/reports/{id}/attachments` with `Content-Type: multipart/form-data`.
2. Backend validates: size ≤ 25 MB, mime type ∈ allowlist (`application/pdf`, `image/png`, `image/jpeg`).
3. Backend streams to object storage with an opaque key `t/{tenantId}/r/{reportId}/{guid}`.
4. Backend records an `Attachment` row + audit `AttachmentCreated`.
5. Response: `{ id, sha256, sizeBytes, contentType, uploadedAt }`.

### Storage provider

- v0.x: not applicable.
- Hosted: customer-supplied S3-compatible bucket per tenant (BYO bucket model).
- On-prem: local filesystem under `/var/lib/radiopad/attachments/<tenant>/`.

### Access controls

- Pre-signed URLs valid for 5 minutes, scoped to read-only.
- Tenant id encoded into the key prefix and validated by the API on download.

### Scanning

- ClamAV (or equivalent) scans every upload before the row is committed.
- Failed scan → reject + audit `AttachmentRejected`.

### Retention

- Tied to report retention. Deleting a report deletes attachments after the configured grace window.
- Audit events for attachments are retained per [../04-security/data-retention.md](../04-security/data-retention.md).
