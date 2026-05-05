# Compliance Matrix

**Status:** Draft  ·  **Owner:** Security + Legal  ·  **Last Updated:** 2026-05-04

> Architectural compatibility — not certification status. Certifications are a customer-deployment exercise where applicable.

| Framework | Applicability | Architectural compatibility | Required for |
| --- | --- | --- | --- |
| **HIPAA** | US covered entities + their BAs | Compatible: tenant isolation, audit chain, PHI policy, append-only log. BAA required between customer and AI provider when remote AI is used. | US clinical deployments. |
| **GDPR** | EU residents | Compatible: lawful basis (contract / legitimate interest), data subject rights via export/delete (Phase 3), DPIA template, processor model. | EU clinical deployments. |
| **SOC 2 Type I** | Hosted SKU | Roadmap target Phase 3. Gaps tracked in [nist-ssdf-mapping.md](nist-ssdf-mapping.md). | Enterprise procurement. |
| **SOC 2 Type II** | Hosted SKU | Phase 4 target. | Enterprise renewals. |
| **ISO 27001** | Optional | Architectural alignment good; ISMS programme not yet established. | Certain regulated markets. |
| **PCI DSS** | Not applicable (we never store cardholder data) | Stripe handles card data. | — |
| **HITECH** | US (extension of HIPAA) | Same posture as HIPAA. | US clinical deployments. |

## Key controls and their RadioPad implementation

| Control area | Implementation |
| --- | --- |
| Access control | Tenant isolation via `ResolveContextAsync`; RBAC Phase 3. |
| Audit logging | Append-only with SHA-256 chain; `radiopad audit verify`. |
| Encryption in transit | TLS at the reverse proxy. |
| Encryption at rest | Storage-layer (DB / backup) responsibility of deployment. |
| Risk assessment | [threat-model.md](threat-model.md), updated yearly. |
| Vulnerability management | [vulnerability-management.md](vulnerability-management.md). |
| Incident response | [incident-response.md](incident-response.md). |
| Business continuity | [business-continuity.md](business-continuity.md), [disaster-recovery.md](disaster-recovery.md). |
| Vendor management | [vendor-risk.md](vendor-risk.md). |
| Data protection impact assessment | [dpia.md](dpia.md). |
| Privacy notice | Owned by the customer (controller). |

## Customer responsibilities

- Sign BAA with their AI provider before sending PHI.
- Maintain their privacy notice and patient-consent records.
- Configure backups and storage encryption per their regulatory environment.
- Manage user identity and offboarding via their IdP.
