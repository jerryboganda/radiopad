# RadioPad → FHIR R4 `DiagnosticReport` mapping

The serializer of record is [`backend/RadioPad.Api/src/RadioPad.Application/Services/FhirDiagnosticReportSerializer.cs`](../../backend/RadioPad.Api/src/RadioPad.Application/Services/FhirDiagnosticReportSerializer.cs). This page documents the field-by-field mapping so downstream EMR / VNA integrators can validate.

| FHIR field | Source | Notes |
| --- | --- | --- |
| `resourceType`              | constant `"DiagnosticReport"` | |
| `id`                        | `Report.Id` (GUID) | Stable across versions of the same report. |
| `status`                    | `Report.Status` | `Draft → preliminary`, `Validated → preliminary`, `Acknowledged → final`, `Exported → final`. |
| `category[0].coding[0]`     | constant LOINC `LP29684-5` "Radiology" | |
| `code.coding[0]`            | `Rulebook.modality_code` (LOINC if known) | Falls back to `study.modality` text. |
| `subject`                   | external — populated by the EMR adapter | RadioPad does not store patient identity. |
| `effectiveDateTime`         | `Report.StudyDateTime` (when present) | |
| `issued`                    | `Report.UpdatedAt` (UTC ISO-8601) | |
| `performer[0].display`      | `User.DisplayName` of the radiologist who acknowledged | |
| `presentedForm[0].contentType` | `text/plain` | |
| `presentedForm[0].data`     | base64 of the plain-text export | Same content as `GET /api/reports/{id}/export/text`. |
| `conclusion`                | `Report.Impression` (raw text) | |
| `conclusionCode`            | currently empty | Reserved for SNOMED / RadLex coding. |
| `extension[radiopad-prompt-version]` | `AiRequest.PromptVersion` of the most recent AI generation | URL `https://radiopad.com/fhir/StructureDefinition/prompt-version`. |
| `extension[radiopad-rulebook]`        | `Rulebook.RulebookId@version` | URL `https://radiopad.com/fhir/StructureDefinition/rulebook`. |

## Hash-chain extension

Every export also embeds an `extension` referencing the audit event id of the export (`AuditAction.ReportExported`) so a downstream system can request `/api/audit` and verify the chain.

## What we do **not** export

- Free-text PHI inferred from `findings`/`impression` is exported verbatim — RadioPad does not redact at export time. PHI handling lives at ingest and at the AI gateway.
- Non-radiology metadata (referrer, encounter, billing) — provided by the calling EMR.

## Round-trip test

`backend/RadioPad.Api/tests/RadioPad.Api.Tests/ValidationTests.cs::FhirSerializerTests` covers a clean and a Blocker-flagged report and asserts the JSON shape.
