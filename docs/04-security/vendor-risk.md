# Vendor Risk

**Status:** Draft  ·  **Owner:** Security + Procurement  ·  **Last Updated:** 2026-05-04

| Vendor | Purpose | Data shared | Risk | Contract / DPA | Alternatives |
| --- | --- | --- | --- | --- | --- |
| **Anthropic** | AI model provider for impressions / recommendations | De-identified report text only by default; PHI only after BAA + reclassifying provider as `PhiApproved` | Medium | Customer-supplied API key; BAA required for PHI | OpenAI, customer's local Ollama, Mock |
| **OpenAI** (optional) | AI model provider | Same as Anthropic | Medium | Customer-supplied | Same as above |
| **Ollama** (local) | On-prem AI runtime | All clinical text — runs locally, never leaves the network | Low | n/a (local) | Other local model runtimes |
| **GitHub** | Source hosting + CI | No PHI, no real customer data | Low | Standard GitHub terms; private repos | GitLab, self-hosted Gitea |
| **Stripe** (Phase 2+) | Billing | Cardholder + tenant billing metadata | Low — Stripe is PCI-compliant | Stripe DPA | Paddle, manual invoicing |
| **Cloud provider** (AWS / GCP / Azure / on-prem) | Compute, storage, secrets | All RadioPad data | High | Customer-selected; BAA required for PHI deployments | Other clouds; on-prem |
| **Email provider** (Phase 2) | Operational notifications | User email + non-PHI metadata | Low | Vendor DPA | Other transactional email vendors |
| **OIDC IdP** (Phase 3) | Authentication | User identifiers + claims | Medium | Customer-supplied; standard OIDC contract | Any OIDC-compliant IdP |

## Onboarding policy

- New vendor requires:
  - Documented purpose + data shared.
  - DPA signed (or equivalent clause).
  - Security questionnaire reviewed.
  - Risk rating recorded here.

## Offboarding

- Rotate any keys held by the offboarded vendor.
- Confirm data deletion per their DPA.
- Update this file with the offboarded date.
