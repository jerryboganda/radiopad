# Penetration Test Plan

**Status:** Draft  ·  **Owner:** Security  ·  **Last Updated:** 2026-05-04

## Scope

- Backend HTTP API on the staging tenant.
- Frontend web app.
- AI gateway including PHI policy boundary.
- Audit log integrity.

## Out of scope

- Customer-supplied AI providers (their security is the customer's BAA).
- Underlying cloud infrastructure (responsibility of the cloud provider).
- Physical or social-engineering attacks.

## Rules of engagement

- All tests run against the staging environment, never production.
- A dedicated `pentest-<vendor>` tenant is provisioned with synthetic data.
- Test windows agreed in writing with on-call.
- No destructive testing without explicit written approval.
- Findings communicated through the agreed channel (encrypted email or the vendor's portal).

## Test accounts

- One Owner, one Admin, one Radiologist, one Resident, one Auditor (Phase 3).
- One non-tenant external account for cross-tenant escape attempts.

## Environments

- Staging URL provided per engagement.
- Staging API rate limits raised to allow scanning.

## Reporting

- Findings rated CVSS v3.1.
- Each finding includes:
  - Reproducer (with raw HTTP requests).
  - Impact analysis.
  - Recommended remediation.
- Vendor delivers a PDF + a JSON export within 7 days of completion.

## Remediation

- Critical / High: see [vulnerability-management.md](vulnerability-management.md) SLAs.
- Retest scheduled within 2 weeks of fix.
- Closing the engagement requires no Critical/High findings outstanding.

## Cadence

- v1.0.0 GA blocker: one full pen-test with no Critical / High open.
- Annually thereafter, plus after any architecture change touching auth / PHI / audit.
