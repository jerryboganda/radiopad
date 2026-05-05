# Business Associate Agreement (BAA) — Template

**Status:** Template (legal review required before execution)  ·  **Owner:** Regulatory + Legal  ·  **Last Updated:** 2026-05-04

> This is a **template only**. Do not execute it without review by the customer's HIPAA privacy officer, legal counsel, and the RadioPad legal team. RadioPad provides a HIPAA-aligned platform; the customer remains the **Covered Entity** responsible for its own privacy and security program (45 CFR §§160, 164).

---

## 1. Parties

This Business Associate Agreement ("Agreement") is entered into by:

- **Covered Entity** — the customer organisation operating a RadioPad tenant (the "CE").
- **Business Associate** — RadioPad ("BA") and any contracted subprocessors listed in [vendor-risk-register.md](vendor-risk-register.md).

## 2. Definitions

Terms used in this Agreement have the meanings set forth in HIPAA, including: "Breach", "Designated Record Set", "Electronic Protected Health Information" ("ePHI"), "Individual", "Privacy Rule", "Required by Law", "Secretary", "Security Rule", "Subcontractor", and "Unsecured Protected Health Information".

## 3. Permitted uses & disclosures

BA may use or disclose ePHI:

- to perform the **functions, activities, or services** for, or on behalf of, the CE as set out in the underlying RadioPad service agreement (drafting, editing, formatting, standardising, validating radiology report text);
- as Required by Law;
- for the proper management and administration of BA, or to carry out BA's legal responsibilities;
- never for marketing, sale, or research without express written authorisation from the CE.

BA must **not** use or disclose ePHI in a manner that would violate the Privacy Rule if done by the CE.

## 4. Safeguards

BA implements appropriate **administrative, physical, and technical safeguards** that reasonably and appropriately protect the confidentiality, integrity, and availability of ePHI. The current safeguards inventory, mapped to 45 CFR §164.308–§164.312, is maintained in [security-controls.md](../04-security/security-controls.md).

Concretely, the platform enforces:

- TLS 1.2+/1.3 in transit (SEC-001) — operator configures the TLS reverse proxy.
- AES-256-GCM at rest for column-level secrets (SEC-002, iter 31 `AesGcmColumnEncryptor`).
- Append-only SHA-256 audit chain (SEC-005, `IAuditLog.AppendAsync` only).
- Tenant isolation on every query via `TenantedController.ResolveContextAsync` (AUTH-003).
- PHI policy gate in `AiGateway.EnforcePhiPolicy` — never bypassed (SEC-004 / PROV-010).
- Secret references via `env:<NAME>`; key material never in source or logs (SEC-009).

## 5. Subcontractors

BA must obtain a written agreement from each Subcontractor that processes ePHI on its behalf, with terms equivalent to this Agreement. The current Subcontractor inventory is maintained in [vendor-risk-register.md](vendor-risk-register.md).

For AI providers, only those declared `ProviderComplianceClass = PhiApproved` or `LocalOnly` may receive ePHI; the gateway throws `ProviderPolicyException` for non-compliant providers and audits `AuditAction.ProviderBlocked` (PROV-004 / PROV-010).

## 6. Reporting

BA reports to the CE:

- **Breaches** of Unsecured PHI without unreasonable delay and no later than **5 business days** after discovery.
- **Security incidents** (other than breaches) — aggregated quarterly via the audit-export channel.
- **Successful unauthorised access** events — within **24 hours** of discovery.

The breach-notification workflow lives in [pms-plan.md](pms-plan.md) §3.

## 7. Access, amendment, accounting

BA provides, within **15 calendar days** of CE request:

- access to ePHI in a Designated Record Set (45 CFR §164.524);
- amendment of ePHI as directed by the CE (§164.526);
- an accounting of disclosures (§164.528) — sourced from the immutable audit chain.

## 8. Term & termination

- This Agreement is co-terminous with the underlying RadioPad service agreement.
- Upon termination, BA returns or destroys all ePHI within **30 calendar days**; if neither is feasible, BA continues to extend the protections of this Agreement for as long as it retains the data and limits further uses and disclosures to those purposes that make return or destruction infeasible.

## 9. Miscellaneous

- **No third-party beneficiaries** other than the CE and BA.
- **Survival** — §§4–7 survive termination.
- **Amendments** — must be in writing, signed by both parties.
- **Governing law** — as specified in the underlying service agreement.

---

## Execution

| Party | Name | Title | Signature | Date |
| --- | --- | --- | --- | --- |
| Covered Entity | | | | |
| Business Associate (RadioPad) | | | | |
