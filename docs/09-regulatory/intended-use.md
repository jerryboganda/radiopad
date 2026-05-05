# Intended Use Statement

**Status:** Draft  ·  **Owner:** Regulatory  ·  **Last Updated:** 2026-05-04

> Source: paraphrased from Enterprise PRD §15.1 (Intended Use Draft) and §15.3 (Product Positioning).

## 1. Intended Use

RadioPad is intended to assist licensed radiologists in **drafting, editing, formatting, standardising, and validating** the textual content of radiology reports. RadioPad supports the radiologist by:

- generating draft Findings and Impression sections from radiologist-provided dictation, structured input, prior reports, or measurements;
- applying organisation-defined templates and rulebooks for report structure and terminology;
- highlighting potential contradictions, missing sections, terminology drift, and unsupported claims;
- exporting the final radiologist-approved report to downstream systems (RIS/PACS, FHIR `DiagnosticReport`, PDF, DOCX, plain text).

RadioPad **does not** independently interpret medical images, reach diagnostic conclusions, recommend patient management, or sign reports. Every output requires explicit review and acknowledgement by a licensed radiologist before it leaves the system.

## 2. Indications for Use

RadioPad is indicated for use as a **documentation and quality-assistance tool** in the production of radiology reports for diagnostic, follow-up, and screening studies, when used by a licensed radiologist who is the responsible reader of record.

## 3. Patient Population

**None — clinician-facing tool.** RadioPad is not a patient-facing application. It does not provide diagnostic information directly to patients and is not indicated for any patient population. Patient demographic data may pass through RadioPad only as identifiers within radiologist-authored reports; PHI handling is governed by [docs/04-security/](../04-security/) and the AI-gateway PHI policy (see [traceability-matrix.md](traceability-matrix.md), AI-004).

## 4. Operational Environment

| Dimension | Specification |
| --- | --- |
| Setting | Hospital, imaging-centre, or teleradiology workstation (web, desktop, or mobile companion). |
| Network | Backend binds `127.0.0.1` by default; remote exposure requires operator-set TLS reverse proxy. |
| Workstations | Modern desktop OS (Windows 10+, macOS 12+, major Linux). Mobile companion is non-diagnostic. |
| Display | RadioPad does **not** render diagnostic images. Reports are text. Diagnostic-grade displays are not required. |
| Connectivity | RIS/PACS interoperability via FHIR / HL7 v2 / DICOMweb metadata as the operator configures. |
| AI providers | Routed via the AI gateway with PHI policy enforcement; PHI requests blocked unless provider compliance class is `PhiApproved` or `LocalOnly`. |

## 5. User Profile

The intended user is a **licensed radiologist** (or radiology resident under attending supervision) who is the responsible reader of record for the report being produced. Secondary users include:

- radiology administrators (template/rulebook authoring, no clinical sign-off);
- compliance reviewers (read-only audit access);
- IT administrators (provisioning, integration);
- billing administrators (usage and cost reports).

Only the licensed radiologist may acknowledge AI-generated content and finalise the report.

## 6. Contraindications

RadioPad **must not** be used:

- for **autonomous diagnosis** or as a primary interpretation of medical images;
- to **auto-sign or auto-submit** reports without a licensed radiologist's explicit acknowledgement (RPT-012);
- as a **clinical decision support** tool that recommends patient management, triage, or treatment;
- in workflows where **PHI may be routed to AI providers whose compliance class is not `PhiApproved` or `LocalOnly`** (the AI gateway enforces this — see [traceability-matrix.md](traceability-matrix.md), AI-004 / PROV-001..004);
- as a **substitute for radiologist judgement**, training, or peer review.

## 7. Warnings

- AI-drafted text is rendered with the `.ai-mark` highlight (purple family) until acknowledged by the radiologist. **Acknowledgement is the radiologist's clinical attestation.** See [docs/02-design/design.md](../02-design/design.md).
- Validation findings are advisory. A `Blocker` severity prevents export by default but does not replace clinical judgement.
- Rulebooks reflect the authoring committee's preferences and are versioned. Verify the `rulebook_id` and `version` snapshotted on the report against the institution's currently approved rulebook.

## 8. Claims governance

Marketing or in-product claims that exceed the boundary in §1 (e.g. "diagnostic AI", "image interpretation", "auto-signs reports", "triage", "clinical decision making") are prohibited and require a regulatory review before publication. See Enterprise PRD §15.2 and [samd-classification.md](samd-classification.md).
