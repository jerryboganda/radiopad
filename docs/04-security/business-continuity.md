# Business Continuity

**Status:** Draft  ·  **Owner:** Ops + Security  ·  **Last Updated:** 2026-05-04

Business continuity is the broader plan around [disaster-recovery.md](disaster-recovery.md): how RadioPad keeps working through people, vendor, and process disruptions.

## Cloud outage

- Hosted SKU: multi-region deployment (Phase 3) with automatic failover.
- Until Phase 3: documented manual failover runbook; RPO/RTO per [disaster-recovery.md](disaster-recovery.md).
- On-prem: customer responsibility.

## Vendor outage

- **AI provider down** → 502 to client; no silent fallback. Customers may switch to a local Ollama provider for non-PHI input.
- **OIDC provider down (Phase 3)** → degraded login; existing sessions continue. No bypass.
- **Email / status page down** → fall back to direct customer email.

## Data loss

- Primary protection: encrypted backups + audit chain verification.
- Worst-case path: restore the most recent verified backup; rerun integrity checks.
- Customer notification: per BAA / privacy policy timelines.

## Security incident (continuity angle)

- Critical: refer to [incident-response.md](incident-response.md).
- Continuity: maintain a known-good "cold-spare" deploy template that can run a clean tenant if the primary is compromised.

## Staff unavailability

- On-call rotation with at least two engineers + one ops contact.
- Runbooks ([../07-devops/runbook.md](../07-devops/runbook.md)) are written so any on-call engineer can execute them.
- Decision authority for production changes: documented escalation chain (planned).

## Communications continuity

- Status page (planned) publicly accessible.
- Customer-direct email per the support contract.
- Internal `#incidents` channel mirrored to a secondary chat tool.

## Test cadence

- Annual tabletop exercise: cloud outage scenario.
- Annual tabletop exercise: data loss scenario.
- Quarterly on-call drill (10 minutes; common failures).
