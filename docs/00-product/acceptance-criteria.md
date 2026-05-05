# Acceptance Criteria

**Status:** Current  ·  **Owner:** QA  ·  **Last Updated:** 2026-05-04

> Acceptance criteria are grouped by feature and include functional, UX, security, performance, and documentation gates. Every PR closing a feature must check the relevant box.

## F-01 Create draft report

**Functional**
- [ ] `POST /api/reports` returns 201 with the new report id.
- [ ] Tenant-scoped: a request with a different `X-RadioPad-Tenant` cannot read the report.

**UX**
- [ ] New-report modal uses only locked tokens & component classes.

**Security**
- [ ] No PHI in fixtures used for this test.

**Documentation**
- [ ] `frd.md` row updated.

## F-02 Run rulebook validation

**Functional**
- [ ] Every matching golden suite passes on `main`.
- [ ] Findings come back grouped by severity in the response.
- [ ] `Report.Status` flips to `Validated` only when no Blocker remains.

**UX**
- [ ] Validation panel groups findings by severity (Blocker / Warning / Info) using locked classes.

**Performance**
- [ ] p95 < 400 ms on the seeded corpus.

## F-03 AI-assisted draft

**Functional**
- [ ] Non-PHI request to any provider returns a body wrapped in `.ai-mark`.
- [ ] `containsPhi: true` to a non-compliant provider returns 403 `{ kind: "provider_policy" }`.
- [ ] Audit log contains `AiRequest`, `AiResponse`, or `ProviderBlocked` for every call.

**Security**
- [ ] No provider call leaves the process before `EnforcePhiPolicy`.

## F-04 Edit a report

**Functional**
- [ ] Each `PATCH` writes a `ReportVersion` snapshot with monotonically increasing `Sequence`.
- [ ] `GET /api/reports/{id}/versions` returns the most-recent 50 snapshots.

## F-05 Acknowledge & sign

**Functional**
- [ ] Acknowledge from `Validated` succeeds; from `Draft` returns 409.
- [ ] Audit row `ReportAcknowledged` exists.

## F-06 FHIR export

**Functional**
- [ ] Text export is non-empty and contains all required sections.
- [ ] Audit row `ReportExported` exists.

## F-07 Audit chain

**Functional**
- [ ] `radiopad audit verify` exits 0 on a clean dataset.
- [ ] Tampering with any event causes a non-zero exit and prints the offending id.

## F-08 Admin (providers / rulebooks / templates)

**Functional**
- [ ] Approving a rulebook flips `status` to `approved` and audits `RulebookApproved`.
- [ ] Approving requires at least one passing golden case.
