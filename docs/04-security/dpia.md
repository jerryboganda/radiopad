# Data Protection Impact Assessment (DPIA)

**Status:** Draft skeleton  ·  **Owner:** Security + Legal  ·  **Last Updated:** 2026-05-04

> A DPIA is required when processing is likely to result in high risk to data subjects (GDPR Art. 35). Health data triggers this. This skeleton covers the architecture; customers complete their own DPIA for their specific deployment.

## 1. Description of processing

- Nature: AI-assisted radiology reporting.
- Scope: structured text sections of radiology reports (indication, findings, impression, etc.).
- Context: clinical setting (hospital / outpatient imaging centre).
- Purpose: produce a final, signed radiology report and export it to the EHR.

## 2. Necessity & proportionality

- Necessity: the customer's clinical workflow requires structured reporting + AI assistance.
- Proportionality: only the minimum text needed for AI suggestions is sent; PHI fields are routed only to compliant providers.
- Lawful basis (controller-side): performance of contract + Art. 9 explicit consent / vital interests / public health where applicable.

## 3. Risks identified

| Risk | Likelihood | Severity | Mitigation |
| --- | --- | --- | --- |
| PHI sent to non-compliant AI provider | Low | High | `EnforcePhiPolicy`, `ProviderBlocked` audit, integration test. |
| Cross-tenant data exposure | Very low | High | `ResolveContextAsync`, integration test. |
| Audit chain corruption | Very low | High | SHA-256 chain + offline verifier. |
| Logs leaking PHI | Low | High | Redaction rules, no body logging. |
| Insider abuse of DB access | Low | High | Audit log; least-privilege DB role; backup encryption. |
| Model hallucination influencing diagnosis | Medium | Medium | Human-in-the-loop sign-off; AI text wears `.ai-mark`. |
| Prompt injection in report text | Medium | Low | System prompt hardening; safety evals. |

## 4. Safeguards

- Data minimisation: only structured text sections are processed; no DICOM image content.
- Purpose limitation: provider responses used only for the requesting report.
- Storage limitation: per [data-retention.md](data-retention.md).
- Integrity / confidentiality: TLS + storage-layer encryption + audit chain.
- Accountability: append-only audit + verifiable chain + customer DPIA.

## 5. Consultation

- Internal: clinical leadership + security review.
- External: customers complete their own DPIA; ours is the processor template.

## 6. Residual risks & sign-off

- Residual risk: prompt injection and AI hallucination remain low-severity given sign-off requirement.
- Sign-off: documented per release in the release notes once we reach v1.0.0.
