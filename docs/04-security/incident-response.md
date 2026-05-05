# Incident Response

**Status:** Draft  ·  **Owner:** Security + Ops  ·  **Last Updated:** 2026-05-04

## Severity levels

| SEV | Definition | Examples | Response time |
| --- | --- | --- | --- |
| **SEV-1** | Clinical safety, data leakage, audit chain corruption, or active exploit. | PHI sent to a Sandbox provider; audit `verify` mismatch; signed report content altered. | 30 min ack, 24h public update cadence. |
| **SEV-2** | Major degradation: auth outage, repeated 5xx, persistent unsafe AI output. | OIDC provider 5xx breaks login. | 1h ack, daily updates. |
| **SEV-3** | Localised bug or quality regression, no clinical safety impact. | Validation panel mis-renders for one rulebook. | 1 business day ack. |

## Roles

- **Incident Commander (IC)** — drives the response, assigns roles, owns comms.
- **Operations Lead** — works the systems (logs, deploys, mitigation).
- **Communications Lead** — drafts external statements, posts status-page updates.
- **Scribe** — records the timeline.

The on-call engineer takes IC by default; can hand off as the response stabilises.

## Detection

- Alert (planned: Prometheus + on-call paging).
- Customer report (via support channel).
- Internal observation (developer notices anomaly).

## Containment

- Disable the offending feature flag / provider / endpoint.
- Suspend the tenant if an attack is in progress.
- Take a DB snapshot for forensics before further changes.

## Eradication

- Patch the root cause.
- Verify the fix in staging.
- Roll out to production with the standard release process.

## Recovery

- Re-enable suspended features per a rollback plan.
- Verify the audit chain (`radiopad audit verify`) for affected tenants.
- Communicate restoration to the customer.

## Communication

- Internal: `#incidents` channel, IC-led updates every 30 min for SEV-1.
- External: Status page (planned) + tenant-direct email per the BAA.
- Public advisory: only after fix lands and customers are informed (CVD model for security issues).

## Postmortem

- Within 5 business days for SEV-1, 10 for SEV-2.
- Use [../07-devops/postmortem-template.md](../07-devops/postmortem-template.md).
- Action items tracked to closure.
