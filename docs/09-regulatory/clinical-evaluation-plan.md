# Clinical Evaluation Plan (stub)

**Status:** Draft  ·  **Owner:** Regulatory  ·  **Last Updated:** 2026-05-04

This is a **stub** for the clinical evaluation strategy. It is intentionally lightweight while RadioPad is positioned as a non-SaMD documentation assistant ([samd-classification.md](samd-classification.md)). When SaMD scope is asserted, this stub will be expanded into a full Clinical Evaluation Plan (CEP) per MEDDEV 2.7/1 rev. 4 and MDR Article 61.

## 1. Scope of clinical evaluation

The evaluation focuses on the **documentation-quality** and **safety-net** properties of RadioPad, not on diagnostic performance. Specifically:

- Does RadioPad reduce report defects (missing sections, laterality conflicts, contradictions, terminology drift)?
- Does the radiologist remain the responsible reader (no over-reliance, no autonomous sign-off)?
- Are AI-drafted segments correctly identified and reviewed before export?

Out of scope for v0.1: image-based diagnostic accuracy, clinical-outcome studies, head-to-head comparisons against alternative reporting systems.

## 2. Equivalence and state of the art

- **Equivalence claim**: RadioPad is positioned in the same category as commercial radiology reporting / structured-reporting authoring tools. A formal equivalence dossier is not required while the device remains non-SaMD.
- **Literature review** (planned): targeted PubMed / MEDLINE / Cochrane search on:
  - "AI assistance" + "radiology reporting" + ("error rate" OR "discrepancy" OR "quality").
  - "structured reporting" + ("BI-RADS" OR "LI-RADS" OR "PI-RADS" OR "Lung-RADS") + "guideline adherence".
  - "human-in-the-loop" + "clinical documentation" + "automation bias".
  Search protocol, inclusion/exclusion criteria, PRISMA flow, and appraisal scoring will be recorded as a sub-document when the literature review is run.

## 3. Performance metrics (draft)

The following metrics are candidate endpoints for both internal validation and any future prospective study. Targets are **placeholders** until clinical owners ratify them.

| Metric | Definition | Source | Draft target |
| --- | --- | --- | --- |
| Draft acceptance rate | (# AI-drafted segments retained verbatim by radiologist) / (# AI-drafted segments). | Application telemetry on `.ai-mark` acknowledgements. | ≥ 60 % across the bundled rulebooks. |
| Material edit rate | (# AI-drafted segments with > 30 % token diff after radiologist edit) / (# AI-drafted segments). | Diff against original draft. | ≤ 25 %. |
| Contradiction-detection sensitivity | TP / (TP + FN) on a synthetic golden set with seeded contradictions. | `rulebooks/_tests/<rulebook_id>/` golden cases + targeted negation-conflict cases. | ≥ 0.95. |
| Contradiction-detection specificity | TN / (TN + FP) on clean reports. | Same golden set, clean variants. | ≥ 0.95. |
| Required-section coverage | % of finalised reports containing all rulebook-required sections. | Validation engine logs. | ≥ 99 %. |
| Time-to-draft (latency) | Median time from "Generate Draft" request to first token rendered. | Provider telemetry. | See PERF-001 in [traceability-matrix.md](traceability-matrix.md). |

## 4. Internal validation strategy

1. **Golden cases.** Every approved rulebook ships at least one golden case under `rulebooks/_tests/<rulebook_id>/`. CI validates every rulebook YAML and runs every matching golden suite per `.github/instructions/testing.instructions.md`.
2. **Adversarial / red-team cases.** Add seeded-contradiction, seeded-laterality-flip, seeded-modality-mismatch, and seeded-unsupported-claim cases to each rulebook's golden directory.
3. **Regression dashboard.** Track contradiction sensitivity / specificity across rulebook versions.
4. **Audit-trail spot-checks.** Verify each finalised report has a complete trace (prompt + rulebook version + model + input + output + edits) per AI-012.

## 5. Prospective study outline

When a multi-site prospective study is opened, this CEP will be expanded with:

- Study design (e.g. paired pre/post comparison; randomised at the radiologist or worklist level).
- Participating sites, IRB / ethics approvals.
- Inclusion / exclusion criteria for radiologists and studies.
- Sample-size calculation against the chosen primary endpoint.
- Data management plan (de-identified data only; no PHI in research datasets per `.github/instructions/security.instructions.md`).
- Statistical analysis plan.
- Adverse-event reporting and unblinding procedures.
- Data-monitoring committee (if applicable).
- Reporting plan (CONSORT or STARD as applicable).

## 6. Post-market clinical follow-up (PMCF)

PMCF is **deferred** until v1.0. Planned inputs:

- Tenant-aggregated `RulebookApproved` and `ReportAcknowledged` audit metrics (no PHI).
- User-reported incidents via [SECURITY.md](../../SECURITY.md) and a future clinical-feedback channel.
- Quarterly review of the risk register ([iso-14971-risk-register.md](iso-14971-risk-register.md)).

## 7. Open questions

- Final clinical owner sign-off on the §3 draft targets.
- Whether any RADS module (BI-RADS / LI-RADS / PI-RADS / Lung-RADS) is in scope for clinical claims at v1.0.
- Whether contradiction-detection performance is asserted as a clinical claim or remains an internal quality metric.

Owners must resolve open questions in [PROGRESS.md](../../PROGRESS.md) before any clinical claim is published.
