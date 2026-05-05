# Postmortem Template

**Status:** Current  ·  **Owner:** Ops + Engineering  ·  **Last Updated:** 2026-05-04

> Use for SEV-1 within 5 business days, SEV-2 within 10. Blameless. Focus on systems and processes, not individuals.

```md
# Postmortem: <one-line title>

- Date: YYYY-MM-DD
- Severity: SEV-1 / SEV-2
- Author: <name>
- Reviewers: <names>
- Status: Draft / Reviewed / Closed

## Summary
2–4 sentences: what happened, who was affected, how it was resolved.

## Impact
- Tenants affected:
- User-visible symptoms:
- Duration:
- Data integrity impact (any audit / report data lost / corrupted):
- Clinical safety impact:

## Timeline (UTC)
| Time | Event |
| --- | --- |
| HH:MM | First alert / report. |
| HH:MM | Containment action. |
| HH:MM | Root cause identified. |
| HH:MM | Mitigation in place. |
| HH:MM | Service restored. |

## Root cause
Technical explanation. Trace back to the underlying change / configuration / vendor issue.

## Contributing factors
What made the incident more likely or harder to detect / mitigate.

## What went well
Clear wins. Tools, process, individuals (call out roles, not blame).

## What didn't go well
Honest gaps. Detection latency, runbook gaps, alert miss.

## Action items
| ID | Owner | Description | Severity | Due |
| --- | --- | --- | --- | --- |

## Lessons learned
Two or three durable takeaways suitable for sharing across the team.

## Related
- Audit chain verification result for the period:
- CHANGELOG entry:
- Customer comms link:
- Tickets / PRs:
```
