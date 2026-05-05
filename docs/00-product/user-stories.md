# User stories

> All stories follow `As a <persona>, I want <capability> so that <outcome>`.
> Story IDs (`US-###`) are stable; subsequent edits append, never renumber.

## Reporting workflow

- **US-001 — As a P1 radiologist**, I want to open a study and see a
  templated set of sections (Indication / Technique / Comparison / Findings /
  Impression / Recommendations), so that I can fill structure-first.
- **US-002 — As a P1 radiologist**, I want to ask the AI to draft an
  impression from my findings, so that I save typing time on routine cases.
- **US-003 — As a P1 radiologist**, I want every AI-drafted block to be
  visibly marked until I edit or accept it, so that I never accidentally
  sign unverified text.
- **US-004 — As a P1 radiologist**, I want the workspace to block sign-off
  when blocker-class validation findings exist, so that I cannot release an
  unsafe report.
- **US-005 — As a P1 radiologist**, I want to export the signed report as
  FHIR R4 DiagnosticReport for downstream consumption.

## Rulebooks

- **US-010 — As a P3 admin**, I want to author rulebooks in YAML and
  validate them in the browser, so that I can iterate without redeploying.
- **US-011 — As a P3 admin**, I want to test a rulebook against a directory
  of golden cases before approving it, so that regression risk is bounded.
- **US-012 — As a P3 admin**, I want approved rulebooks to be immutable
  (versioned), so that institutional standards are reproducible.

## Providers & governance

- **US-020 — As a P3 admin**, I want to register AI providers with explicit
  compliance classes (`Blocked`, `Sandbox`, `DeIdentifiedOnly`,
  `PhiApproved`, `LocalOnly`), so that PHI is only routed to approved
  destinations.
- **US-021 — As a P4 compliance officer**, I want every AI call, report
  edit, export, and acknowledgement to appear in an append-only audit log
  with chain hashes, so that tampering is detectable.
- **US-022 — As a P4 compliance officer**, I want a governance dashboard
  summarising AI volume, PHI routing decisions, and rulebook health, so
  that I can answer auditor questions in minutes.

## Integration

- **US-030 — As a P5 engineer**, I want a `radiopad` CLI that validates
  rulebooks offline and runs golden cases in CI, so that rulebook changes
  are gated like any other code change.
- **US-031 — As a P5 engineer**, I want to script bulk audit exports to
  JSON, so that I can hand them to the SIEM or compliance archive.
- **US-032 — As a P5 engineer**, I want the desktop app to bundle the
  static frontend and reach a local backend on `127.0.0.1:7457`, so that
  air-gapped deployments work without external network egress.

## Out-of-scope (tracked separately)

- Real-time multi-user co-editing of the same report.
- PACS image viewer integration.
- Voice / dictation pipeline (third-party only).
