# EU AI Act + GDPR profile

**Status:** Draft  ·  **Owner:** Regulatory  ·  **Last Updated:** 2026-05-04  ·  **Iteration:** 31

This profile describes how RadioPad is configured for EU customers. It complements [intended-use.md](intended-use.md), [samd-classification.md](samd-classification.md), and [iso-14971-risk-register.md](iso-14971-risk-register.md). The decision to **stay non-SaMD** in v0.x is recorded in `samd-classification.md` and reaffirmed at iter 31 (see PRD §15 and [/memories/session/iter31-plan.md](/memories/session/iter31-plan.md) decision 8).

## 1. EU AI Act posture

The EU AI Act (Regulation (EU) 2024/1689) classifies AI systems by risk. RadioPad's intended use ("documentation assistant for licensed radiologists; no autonomous diagnosis; human reviews and signs every report") is **out of scope** for the high-risk Annex III medical-device category **provided** the deployment respects the four boundary conditions below.

### 1.1 Boundary conditions (must hold for every deployment)

| Condition | RadioPad enforcement |
| --- | --- |
| No autonomous diagnosis | `AiGateway` returns text only; the radiologist signs in the customer RIS/EHR (PRD §12.2). |
| AI text is visually marked | `.ai-mark` on every generated span until the radiologist accepts (RPT-008). |
| Final export requires human acknowledgement | `POST /api/reports/{id}/acknowledge` audited as `ReportAcknowledged` (RPT-012). |
| No image interpretation | RadioPad processes text only; DICOMweb metadata is read-only context. |

If any condition is removed by future scope changes, the system is reclassified as **high-risk** and Article 9–15 obligations (risk management, data governance, technical documentation, record-keeping, transparency, human oversight, accuracy/robustness/cybersecurity) apply.

### 1.2 Article 50 transparency (out of scope today)

RadioPad does not emit synthetic media or generate content shown to patients. Article 50 transparency obligations therefore do not apply at v0.x. If a patient-facing summary mode is added, this section must be reopened.

## 2. GDPR posture

### 2.1 Roles

- **Customer** — Data Controller for personal data (including health data) processed in their RadioPad tenant.
- **RadioPad** — Data Processor under Article 28. A Data Processing Agreement (DPA) is executed alongside the BAA template ([baa-template.md](baa-template.md)) for joint US/EU customers.

### 2.2 Lawful basis (Article 6) and special-category condition (Article 9)

Health data is special-category data. The lawful processing condition is typically:

- Article 6(1)(c) (legal obligation — medical record-keeping under national law); and
- Article 9(2)(h) (provision of health care).

The customer's privacy notice must reference both. RadioPad does not establish the lawful basis itself.

### 2.3 Data subject rights

Articles 15–22 are honoured via:

- **Access (Art. 15)** — customer admin can export reports + audit events for a given subject via the audit search and report export endpoints.
- **Rectification (Art. 16)** — radiologist edits create a new report version; the audit chain preserves history.
- **Erasure (Art. 17)** — audit log is **append-only and never erased**. Personal-data fields in `Report` / `Patient` may be redacted via the right-to-delete workflow (PRD §13.3 item 6); the chain remains intact because it stores hashes, not free text.
- **Restriction (Art. 18)**, **portability (Art. 20)** — FHIR DiagnosticReport export (RPT-011 / STD-003).
- **Objection (Art. 21)** — customer-level switch in tenant settings.
- **Automated decision-making (Art. 22)** — *not applicable*: the radiologist signs every report.

### 2.4 Cross-border transfers (Chapter V)

- **EU-only** customers should pin all approved AI providers to EU regions and disable any provider lacking an EU residency option. This is enforced per provider via `Provider.RegionPolicy` (env-driven, iter-31 adapter scaffolding).
- **EU→US** transfers (e.g. OpenAI direct, AWS Bedrock us-east) require Standard Contractual Clauses (SCCs) on file with the customer, plus a Transfer Impact Assessment. The vendor risk register ([vendor-risk-register.md](vendor-risk-register.md)) tracks each provider's transfer mechanism.
- **Local-only** deployments (Ollama / vLLM via OpenAI-compatible adapter) avoid the question entirely. This is the recommended posture for EU-only customers handling PHI.

### 2.5 Breach notification (Article 33–34)

- Notify supervisory authority within **72 hours** of becoming aware of a personal-data breach affecting EU data subjects.
- Notify affected data subjects without undue delay if the breach is likely to result in a high risk to their rights and freedoms.
- Workflow: see [pms-plan.md](pms-plan.md) §3.

### 2.6 DPIA (Article 35)

A Data Protection Impact Assessment is **required** when:

- the processing involves systematic large-scale processing of special-category data — **yes**, every RadioPad tenant qualifies; and
- the processing uses new technologies (LLMs).

A DPIA template lives at `docs/09-regulatory/dpia-template.md` (planned). Customers should complete it before going live.

## 3. AI-Act-aligned controls already shipped

| AI Act area | Control | Where |
| --- | --- | --- |
| Risk management | ISO 14971 risk register | [iso-14971-risk-register.md](iso-14971-risk-register.md) |
| Data governance | PHI policy gate; provider compliance class registry | `AiGateway.EnforcePhiPolicy`; PROV-002/004/010 |
| Technical documentation | IEC 62304 SDLC + this matrix | [iec-62304-sdlc.md](iec-62304-sdlc.md), [traceability-matrix.md](traceability-matrix.md) |
| Record-keeping | SHA-256 immutable audit chain | SEC-005 / RB-009 |
| Transparency | `.ai-mark` highlighting + per-report rulebook+model+prompt snapshot | RPT-008 / AI-012 |
| Human oversight | Radiologist signs every report; AI never auto-signs | RPT-012 / PRD §12.2 |
| Accuracy & robustness | Golden-case test suites for every approved rulebook | `rulebooks/_tests/<id>/` (17 rulebooks at iter 31) |
| Cybersecurity | TLS 1.2+/1.3, append-only audit, env-only secrets, default 127.0.0.1 bind | SEC-001/005/009 |

## 4. Customer-facing checklist

Before going live in the EU, customers should confirm:

- [ ] DPA signed alongside the RadioPad BAA template.
- [ ] DPIA completed and filed with the customer's DPO.
- [ ] Approved AI provider list pinned to EU regions, or `LocalOnly` providers configured.
- [ ] Tenant-specific lawful basis documented in the customer's privacy notice.
- [ ] Breach-notification SLA (72 h) wired into the customer's incident-response runbook.
- [ ] Customer's reporting RIS/EHR holds the legal radiology record (RadioPad does not).
