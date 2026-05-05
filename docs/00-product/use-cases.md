# Use Cases

**Status:** Current  ·  **Owner:** Product  ·  **Last Updated:** 2026-05-04

## UC-01 Draft and sign a chest CT report

| Field | Value |
| --- | --- |
| Actor | Attending radiologist |
| Preconditions | User authenticated; tenant has `chest_ct_v1` rulebook approved. |
| Main flow | (1) Click *New report* → modality CT, body part Chest. (2) System scaffolds sections from `chest-ct.json` template. (3) Radiologist fills findings. (4) Click *Validate* → all rules pass. (5) Click *Ask AI: impression* → mock provider returns suggestion wrapped in `.ai-mark`. (6) Edit and accept. (7) Click *Acknowledge* → status `Acknowledged`. (8) Click *Export FHIR text* → file downloaded; status `Exported`. |
| Alternative flows | (4a) Validation returns Blocker → radiologist edits and re-validates. (5a) Provider blocked by PHI policy → toast shows "Provider not allowed for PHI". |
| Error flows | (5b) Provider timeout → audit `AiResponse` with error; UI surfaces banner. |
| Postconditions | `Report.Status = Exported`. ≥ 4 audit events recorded. ≥ 1 `ReportVersion` snapshot. |

## UC-02 Approve a new rulebook version

| Field | Value |
| --- | --- |
| Actor | Radiology informatics admin |
| Preconditions | Draft rulebook YAML with valid semver; golden cases under `rulebooks/_tests/<id>/`. |
| Main flow | (1) Run `radiopad rulebook validate <yaml>` → ok. (2) Run `radiopad rulebook test <id>` → all golden cases pass. (3) `POST /api/rulebooks` save. (4) `POST /api/rulebooks/{id}/approve` flips `status=approved`. Audit `RulebookApproved`. |
| Alternative flows | (2a) Golden case fails → fix YAML or fixture; restart. |
| Postconditions | New rulebook is selectable in the report editor; old version remains for in-flight reports. |

## UC-03 Verify the audit chain after a security incident

| Field | Value |
| --- | --- |
| Actor | Operator |
| Preconditions | Backend reachable; tenant slug known. |
| Main flow | (1) Run `radiopad audit verify --tenant <slug>`. (2) CLI streams events, recomputes SHA-256 chain, prints `OK` and event count. |
| Alternative flows | (2a) Mismatch → CLI exits non-zero, prints the offending event id; operator escalates per [../04-security/incident-response.md](../04-security/incident-response.md). |
| Postconditions | Chain integrity is documented in the incident timeline. |

## UC-04 Block a PHI request to a sandbox provider

| Field | Value |
| --- | --- |
| Actor | Radiologist (unintentional) |
| Preconditions | Provider has compliance class `Sandbox`. |
| Main flow | (1) Radiologist asks AI for impression with `containsPhi: true`. (2) `AiGateway.EnforcePhiPolicy` audits `ProviderBlocked` and throws `ProviderPolicyException`. (3) API returns 403 `{ error, kind: "provider_policy" }`. (4) UI shows the locked `.banner.warn` message. |
| Postconditions | No request was sent to the provider. Audit row exists. |
