# Privacy

**Status:** Draft  ·  **Owner:** Security + Legal  ·  **Last Updated:** 2026-05-04

## Personal data we may process

| Category | Examples | Necessity |
| --- | --- | --- |
| Health data (PHI) | Indication, findings, impression text | Core product purpose. |
| Identifiers | Accession number, MRN (if entered into a section) | Required for clinical context. |
| Authentication identifiers | Email, IdP `sub` claim (Phase 3) | Authentication. |
| Operational metadata | IP, user agent, request id, timestamps | Security monitoring. |
| Usage telemetry (planned) | feature usage counts | Product improvement; opt-out per tenant. |

## Purpose

- Provide an AI-assisted reporting workflow.
- Maintain a verifiable audit trail for clinical and regulatory accountability.
- Operate the platform safely (rate limits, security monitoring).

## Lawful basis (GDPR-style)

- Performance of a contract (the customer/tenant agreement).
- Legitimate interests (security monitoring, fraud prevention).
- Legal obligation (audit log retention).

For health data specifically: explicit basis under Article 9 GDPR is the responsibility of the customer (controller); RadioPad acts as processor.

## Consent

- Patients are not direct users of RadioPad. Consent for AI processing is the customer's responsibility under their privacy notice / BAA.
- Opt-out of telemetry: per-tenant flag (Phase 2).

## User rights

- Access / portability: `radiopad tenant export` (Phase 3).
- Erasure: tenant-level deletion with 30-day grace; audit retained per regulatory requirements.
- Rectification: handled via report PATCH and addendum workflow (Phase 2).

## Data processors / sub-processors

| Processor | Purpose | DPA status |
| --- | --- | --- |
| AI providers (Anthropic, OpenAI, etc.) | Optional AI generation | Customer-supplied; BAA required for PHI. |
| Cloud hosting (customer-supplied) | Compute / storage | Customer's responsibility. |
| Email / status (planned) | Operational notifications | TBD. |

See [vendor-risk.md](vendor-risk.md) for the active vendor list.

## Privacy risks

- AI provider receives PHI despite policy → mitigated by `EnforcePhiPolicy` and audit.
- Cross-tenant data leak → mitigated by `ResolveContextAsync` and integration tests.
- Logs accidentally containing PHI → mitigated by redaction rules in [logging](../03-architecture/logging.md).
- Backup storage on a non-compliant provider → mitigated by deployment guidance.
