# Functional Requirements Document (FRD)

**Status:** Current  ·  **Owner:** Product  ·  **Last Updated:** 2026-05-04

> Each requirement maps to the API surface in [openapi/openapi.yaml](../../openapi/openapi.yaml) and to the acceptance tests under `backend/RadioPad.Api/tests/` and `rulebooks/_tests/`.

## F-01 Create draft report

- **Inputs:** `modality`, `bodyPart`, `indication`, `accessionNumber`, optional `comparison`, optional `rulebookId`, optional `templateId`.
- **Outputs:** `Report { id, status: Draft, … }` (HTTP 201).
- **Workflow:** `POST /api/reports` → server stamps `TenantId`, `CreatedAt`, sequence 0 of `ReportVersion` is implicit (first PATCH writes sequence 1).
- **Business rules:** `accessionNumber` unique per tenant. Modality + body part must match the chosen template / rulebook if supplied.

## F-02 Run rulebook validation

- **Inputs:** `report.id`.
- **Outputs:** `ValidationResult { findings: [{ ruleId, severity, message, section? }] }`.
- **Workflow:** `POST /api/reports/{id}/validate` → resolves rulebook by `report.RulebookId` → engine evaluates rules → response carries findings; `Report.Status` flips to `Validated` when no Blocker remains.
- **Severities:** `Blocker` (red), `Warning` (amber), `Info` (blue).

## F-03 AI-assisted draft

- **Inputs:** `mode ∈ {impression, technique, recommendation}`, `providerId`, optional `containsPhi`.
- **Outputs:** `{ text, highlightsJson }` or HTTP 403 `{ error, kind: "provider_policy" }`.
- **Workflow:** `POST /api/reports/{id}/ai` → `AiGateway.RouteAsync` → provider-availability gate → provider adapter → audit `AiRequest` + `AiResponse`. Requests refused by the availability gate audit `ProviderBlocked` and 403.
- **Business rules:** `containsPhi: true` no longer constrains provider choice — the PHI gate was removed on 2026-07-20 by operator decision, so PHI routes to any enabled provider. `EnforcePhiPolicy` now rejects only providers that are disabled or carry `Compliance = Blocked`, both operator switches rather than PHI gating. `containsPhi` is still computed and recorded on the audit and usage rows. Suggested text wears `.ai-mark` until acknowledged.

## F-04 Edit a report (with version history)

- **Inputs:** `PatchReportDto` partial sections.
- **Outputs:** Updated `Report`. Side-effect: `ReportVersion` snapshot appended (sequence, author, action="edit", JSON snapshot, `RulebookId`).
- **Workflow:** `PATCH /api/reports/{id}` → apply changes → append version → save.
- **Read history:** `GET /api/reports/{id}/versions` returns the most-recent 50 snapshots, newest first.

## F-05 Acknowledge & sign

- **Inputs:** `report.id`.
- **Outputs:** `Report.Status = Acknowledged`. Audit `ReportAcknowledged`.
- **Workflow:** `POST /api/reports/{id}/acknowledge` — only allowed from `Validated`. Resident-vs-attending policy is a Phase 2 enhancement.

## F-06 FHIR export

- **Inputs:** `report.id`.
- **Outputs:** Plain-text narrative (`text/plain`) or FHIR JSON `DiagnosticReport` (`application/fhir+json`).
- **Workflow:** `GET /api/reports/{id}/export/text` and `GET /api/reports/{id}/export/fhir`. Audit `ReportExported`.

## F-07 Audit chain

- **Inputs:** none.
- **Outputs:** Streamed JSON-Lines for the tenant.
- **Workflow:** `GET /api/audit` (tenant-scoped). CLI `radiopad audit verify` recomputes the chain locally and exits non-zero on mismatch.

## F-08 Admin: providers / rulebooks / templates

- **Providers:** list, save, test (`/api/providers`, CLI `provider test`).
- **Rulebooks:** list, get, save, validate YAML, approve, deprecate (`/api/rulebooks`).
- **Templates:** list, save (`/api/templates`).

All admin endpoints are tenant-scoped. Approve/deprecate transitions audit `RulebookApproved` / `RulebookDeprecated`.
