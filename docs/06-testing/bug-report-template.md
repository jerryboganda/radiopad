# Bug Report Template

**Status:** Current  ·  **Owner:** QA  ·  **Last Updated:** 2026-05-04

> Internal QA template. The public GitHub template lives at `.github/ISSUE_TEMPLATE/bug_report.md`.

```md
## Summary
One-sentence description.

## Severity
SEV-1 / SEV-2 / SEV-3 (per [../04-security/incident-response.md](../04-security/incident-response.md))

## Environment
- App version (frontend + backend):
- Deployment (local / hosted / on-prem):
- Browser / desktop / mobile:
- Tenant slug:
- Request id (X-Request-Id) if available:

## Steps to reproduce
1. ...
2. ...
3. ...

## Expected
...

## Actual
...

## Logs / evidence
- Redacted log lines (no PHI / secrets).
- Screenshots with PHI obscured.
- API responses (no patient identifiers).

## Suspected area
e.g. ReportsController, AiGateway PHI policy, Validation engine.

## Workaround
None / describe.

## Audit chain status
Output of `radiopad audit verify --tenant <slug>` (if relevant).
```
