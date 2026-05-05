# Support Playbook

**Status:** Current  ·  **Owner:** Support  ·  **Last Updated:** 2026-05-04

## Channels

- Tier 1 (general questions): support email / portal.
- Tier 2 (technical): on-call engineer queue.
- Security: `security@radiopad.example` (per [SECURITY.md](../../SECURITY.md)).
- Critical clinical incident: phone bridge per the BAA.

## SLA targets

| Severity | First response | Workaround | Resolution |
| --- | --- | --- | --- |
| SEV-1 (clinical / data) | 30 min | ASAP | hotfix tag |
| SEV-2 (major degradation) | 1 hour | < 1 day | next minor |
| SEV-3 (defect / question) | 1 business day | best effort | next minor |

## What to ask the customer

- Request id (from the banner / API response).
- Tenant slug.
- Approximate timestamp (UTC).
- Browser / OS / desktop version.
- Steps to reproduce (without PHI).
- Any screenshots — confirm PHI obscured.

## What to never ask

- Real patient data.
- API keys or passwords.
- Full report text in support channels (instead, ask for the report id and pull it in a controlled environment).

## Escalation

1. Tier 1 attempts solution from this playbook + [troubleshooting.md](troubleshooting.md).
2. Tier 1 escalates to Tier 2 if unresolved within SLA.
3. Tier 2 may engage on-call engineer.
4. SEV-1 → on-call engineer + Engineering Lead immediately.

## Common patterns

- "AI says blocked" → confirm provider compliance + PHI flag; admin can switch provider.
- "Can't acknowledge" → status not Validated; resolve Blocker findings.
- "Mobile shows no reports" → expected if filter excludes; confirm read scope.
- "Audit verify failed" → SEV-1; freeze tenant writes and follow the runbook.

## Knowledge base

- `docs/08-user-docs/` is the canonical KB.
- Each support response should link the relevant doc rather than restate it.
