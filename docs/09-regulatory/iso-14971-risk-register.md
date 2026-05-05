# ISO 14971 Risk Register (starter)

**Status:** Draft  ·  **Owner:** Regulatory  ·  **Last Updated:** 2026-05-04

Source: reformatted from Enterprise PRD §23 *Risks and Mitigations*. Severity / Probability scales are the ISO 14971 informative scales; Risk Class is the 1–5 product. This is a **starter register**; expansions add columns for risk-control verification evidence and post-market data.

## Scales

- **Severity** (S): 1 Negligible · 2 Minor · 3 Serious · 4 Critical · 5 Catastrophic.
- **Probability** (P): 1 Improbable · 2 Remote · 3 Occasional · 4 Probable · 5 Frequent.
- **Risk Class** = max(S, P) before control; reassessed as residual after control.

## Register

| # | Hazard | Foreseeable Sequence of Events | Hazardous Situation | Harm | S | P | Risk Class | Existing Control | Residual Risk | Verification |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| R-01 | AI hallucination introduces a finding not supported by the radiologist's input. | LLM completes Impression with an unsupported claim → radiologist signs without noticing. | False finding present in finalised report. | Misdiagnosis, inappropriate downstream care. | 4 | 3 | 4 | `.ai-mark` highlight (RPT-008), unsupported-claim detection (AI-008), contradiction validator (AI-007), mandatory acknowledgement (RPT-012), golden cases. | 2 (Minor / Remote) | Validation tests in `backend/RadioPad.Api/tests/RadioPad.Api.Tests/ValidationTests.cs`; rulebook golden suites under `rulebooks/_tests/`. |
| R-02 | PHI routed to a non-compliant AI provider. | Operator misconfigures provider; user submits report containing PHI. | PHI leaves the controlled environment. | Privacy breach, regulatory penalty. | 5 | 2 | 5 | `AiGateway.EnforcePhiPolicy` blocks unless `ProviderComplianceClass` is `PhiApproved` or `LocalOnly`; `ProviderBlocked` audit (AI-004, PROV-001..004). | 2 (Minor / Remote) | `AiGatewayPolicyTests.cs` PHI + non-compliant provider ⇒ `ProviderPolicyException` + audit row. |
| R-03 | Unintended scope expansion turns RadioPad into a regulated SaMD. | Marketing or product claim exceeds documentation-assistant boundary. | Unregulated device on market. | Enforcement action, withdrawal. | 4 | 2 | 4 | Intended-use lock ([intended-use.md](intended-use.md)), claims governance, [samd-classification.md](samd-classification.md) re-classification triggers. | 2 | Quarterly claims review (process). |
| R-04 | Radiologist over-reliance on AI draft. | Radiologist signs without thorough review. | Errors in draft propagate to final report. | Misdiagnosis. | 4 | 3 | 4 | `.ai-mark` UX (RPT-008), explicit acknowledgement step (RPT-012), validation severities surfaced, training. | 3 (Serious / Remote) | UX inspection of `frontend/`; design lock ([docs/02-design/design.md](../02-design/design.md)). |
| R-05 | Inconsistent report quality across radiologists / sites. | Free-text drift away from institutional templates. | Reports lack required sections / standard terminology. | Care variability, downstream coding errors. | 3 | 4 | 4 | Rulebooks (RB-001..010), templates (TMP-001..008), validation engine (RPT-001..003), golden cases. | 2 | Rulebook golden suites; CI validates every rulebook YAML and runs every matching golden suite. |
| R-06 | Poor PACS/RIS interoperability causes report loss or duplication. | Copy-to-RIS fails silently; user assumes export succeeded. | Report not present in record of truth. | Delayed care; medico-legal exposure. | 4 | 3 | 4 | FHIR DiagnosticReport export (STD-001..003, RPT-011), explicit export confirmations, audit. | 3 | Integration tests under `backend/RadioPad.Api/tests/RadioPad.Api.Tests/Integration/`. |
| R-07 | OAuth / shared subscription provider misuse for PHI workflows. | Shared OAuth account routes PHI to a non-PHI tier. | PHI exposure. | Privacy breach. | 5 | 2 | 5 | Provider compliance class enforcement (PROV-001..004); contractual gating; audit. | 2 | Same as R-02. |
| R-08 | Prompt or rulebook drift across versions. | Rulebook silently changed; old reports no longer reproducible. | Validation results not auditable. | Loss of clinical traceability. | 3 | 3 | 3 | Rulebook semver + approval workflow (RB-001..006), per-report rulebook snapshot, append-only audit log. | 2 | `RulebookApproved` audit; promotion workflow in [docs/05-clinical/rulebook-authoring.md](../05-clinical/rulebook-authoring.md). |
| R-09 | AI cost overruns degrade availability. | Token usage spikes; tenant rate-limited mid-report. | Radiologist cannot complete draft via AI. | Workflow disruption (no patient harm). | 2 | 4 | 4 | Provider routing (PROV-005..008), tenant budgets / usage caps (BILL-001..004), graceful fallback to manual editing. | 2 | Performance docs ([docs/03-architecture/observability.md](../03-architecture/observability.md)). |
| R-10 | Desktop or daemon compromise leaks local report drafts. | Attacker gains local access to an unlocked workstation. | Draft reports / clipboard exfiltrated. | Privacy breach. | 4 | 2 | 4 | Signed bundles, encrypted local storage, device trust (AUTH-007), hardened daemon, secure clipboard (DESK-*). | 2 | Desktop security checklist in [desktop/PLUGIN_TRUST.md](../../desktop/PLUGIN_TRUST.md); dependencies pinned (security policy). |

## Acceptance criteria

A residual Risk Class of **3 or lower** is the v0.1 acceptance threshold for individual hazards. Risks with residual class ≥ 4 require an explicit risk/benefit memo from Regulatory + Clinical owners and a recorded mitigation roadmap before a public release.

## Maintenance

Update this register whenever:

- a new risk is identified during incident review;
- a control is added, removed, or weakened (S/P recalculated);
- the SaMD scope changes per [samd-classification.md](samd-classification.md) §4 triggers.

Bump `Last Updated` on every edit per [.github/instructions/documentation.instructions.md](../../.github/instructions/documentation.instructions.md).
