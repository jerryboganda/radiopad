# Personas

These personas drive prioritisation. Every story in
[user-stories.md](user-stories.md) ties back to one of them.

## P1 — Attending Radiologist (primary user)

- **Goal:** Read studies and produce signed reports with maximum clinical
  accuracy and minimum keystrokes.
- **Context:** Hospital reading room or home workstation; high study volume;
  liability for every signed report.
- **Pain points:** Boilerplate fatigue; AI tools that hallucinate; opaque
  audit when a report is questioned.
- **What RadioPad provides:** Templates + rulebooks; AI that wears `.ai-mark`
  until reviewed; blocker-class findings prevent unsafe sign-off.

## P2 — Resident / Fellow

- **Goal:** Learn institutional reporting standards; produce drafts for
  attending sign-off.
- **Pain points:** Inconsistent feedback; unclear required-section
  expectations.
- **What RadioPad provides:** Rulebook-driven validation that catches
  required-section gaps and laterality conflicts in real time.

## P3 — Department Admin / Reporting Lead

- **Goal:** Standardise reporting across the department; manage AI provider
  routing; review AI utilisation.
- **Pain points:** Different radiologists copy-pasting different prompts into
  different AI tools with no oversight.
- **What RadioPad provides:** Tenant provider registry with compliance
  classes; governance dashboard; rulebook approval workflow.

## P4 — Compliance / Governance Officer

- **Goal:** Demonstrate to auditors that PHI handling, AI usage, and
  reporting standards are under control.
- **Pain points:** No tamper-evident record of AI calls; no evidence that
  rulebooks were tested before approval.
- **What RadioPad provides:** Append-only audit log with SHA-256 chain;
  rulebook status workflow tied to golden-case results.

## P5 — Integration / IT Engineer

- **Goal:** Wire RadioPad into RIS/PACS, identity, and reporting downstream
  systems.
- **Pain points:** Closed APIs; vendor lock-in; no FHIR export.
- **What RadioPad provides:** REST API + FHIR R4 DiagnosticReport export +
  CLI for scripted operations.

## Anti-personas (not the target)

- Patients reading their own reports — RadioPad is a reporting workspace, not
  a patient portal.
- Marketing or sales teams — there is no built-in CRM or outreach surface.
