# On-Call

**Status:** Draft  ·  **Owner:** Ops + Engineering  ·  **Last Updated:** 2026-05-04

## Rotation

- Primary on-call engineer: 1-week shift.
- Secondary on-call: 1-week shift, offset by 3 days.
- Ops contact: 1-week shift; covers infrastructure-only escalations.

## Tooling

- Paging: configured against the alerts in [monitoring.md](monitoring.md).
- Comms: `#incidents` channel; bridge for P1.
- Status page: customer-visible updates per [incident-response.md](../04-security/incident-response.md).

## Expectations

- Acknowledge P1 within 30 min, P2 within 1 hour.
- Carry a working laptop + access during shift.
- Update the incident scribe channel at least every 30 min during a P1.
- Hand off cleanly: write a brief at the end of shift summarising open issues.

## Escalation path

1. Primary on-call.
2. Secondary on-call (after 15 min unanswered P1).
3. Engineering Lead.
4. CTO / equivalent decision authority.

## Drills

- Monthly tabletop: pick a runbook entry; walk through it.
- Quarterly: live-fire failover drill in staging.
- Annually: full DR drill ([disaster-recovery.md](../04-security/disaster-recovery.md)).

## After-hours support

- Standard: best-effort outside business hours.
- Enterprise: 24×7 per the SLA.
- On-prem: customer's internal team handles after-hours; we provide written guidance.

## Compensation

- On-call hours tracked; compensation per company policy.
- If a shift is interrupted (paged) the time off in lieu is granted per policy.
