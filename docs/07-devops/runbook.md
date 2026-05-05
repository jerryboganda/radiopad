# Runbook

**Status:** Current  ·  **Owner:** Ops  ·  **Last Updated:** 2026-05-04

> Operational playbooks for known failure modes. Each entry: detection → containment → diagnosis → resolution → comms.

## DB outage

- **Detection:** `/api/health/ready` 503; alerts fire.
- **Containment:** load balancer drains failing pods; reduce write traffic by enabling read-only mode (Phase 2).
- **Diagnose:** check managed DB dashboard; check connection pool; check storage.
- **Resolve:** failover to replica or restore latest backup if corruption suspected.
- **Comms:** status page + customer email per BAA timelines.
- **After:** postmortem; consider increasing replica count.

## Audit chain mismatch

- **Detection:** `radiopad audit verify` exits non-zero.
- **Containment:** **freeze writes** to the affected tenant until the cause is understood.
- **Diagnose:** identify the offending event id; inspect surrounding rows; check for direct SQL access (forbidden) or DB restore mishap.
- **Resolve:** never silently rewrite the chain. Either restore from a verified backup OR isolate the affected tenant for forensic review.
- **Comms:** SEV-1; legal team engaged.
- **After:** SEV-1 postmortem mandatory.

## Provider outage

- **Detection:** AI requests fail consistently to a provider; `radiopad_provider_blocked_total` (planned) does not move; user reports.
- **Containment:** customer can switch provider for the request; UI surfaces the failure with the request id.
- **Diagnose:** provider status page; egress connectivity; API key rotation issue.
- **Resolve:** restore connectivity; rotate keys if necessary; communicate to users.
- **Comms:** mention in status page; email if outage > 30 min.
- **After:** add the failure mode to the eval / monitoring suite if novel.

## PHI leak suspected

- **Detection:** customer report; observation in logs (which should never contain PHI — investigate the leak path).
- **Containment:** block the offending route / provider; freeze tenant traffic if active exfiltration.
- **Diagnose:** trace the request via correlation id; inspect audit log; check provider compliance class.
- **Resolve:** remediate the leak; rotate any potentially exposed credentials.
- **Comms:** immediate to affected tenant; legal / privacy team; per BAA timelines.
- **After:** SEV-1 postmortem; regression test added; CHANGELOG `### Security` entry.

## Login down (Phase 3)

- **Detection:** OIDC handshake failures.
- **Containment:** retain existing sessions; halt forced re-auth.
- **Diagnose:** IdP status; clock skew; JWKS issue.
- **Resolve:** restore IdP connectivity; refresh JWKS cache.
- **Comms:** status page.
- **After:** consider IdP redundancy.

## Quick references

- Health: `curl https://<host>/api/health/ready`.
- Audit verify: `radiopad audit verify --tenant <slug>`.
- Provider test: `radiopad provider test --id <guid>`.
- Logs: filter by `requestId` from a customer-reported error.
