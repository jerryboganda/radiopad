# Regulatory Dossier (skeleton)

**Status:** Draft  ·  **Owner:** Regulatory  ·  **Last Updated:** 2026-05-04

This directory holds the regulatory dossier skeleton for RadioPad. It is a **work-in-progress technical file**, not a submission. Its purpose is to give clinical, engineering, and regulatory contributors a stable place to record device classification, risk management, software lifecycle evidence, and clinical-evaluation strategy as the product matures.

## Scope

RadioPad v0.1 is positioned as a **radiology reporting documentation assistant**: the licensed radiologist drafts, reviews, edits, and signs every report. The system **does not** interpret images, reach diagnoses autonomously, or auto-sign. See [intended-use.md](intended-use.md) and [samd-classification.md](samd-classification.md).

Even though v0.1 is intentionally non-SaMD, several capabilities (impression generation, contradiction detection, draft drafting) are AI-assisted clinical text. Regulators (FDA, EU MDR, EU AI Act high-risk Annex III) increasingly scope such tools under "Software as a Medical Device" or "high-risk AI" depending on claims. The dossier here documents the design controls that would be required if/when RadioPad's claims expand toward SaMD.

## Frameworks referenced

| Framework | Purpose | Mapping doc |
| --- | --- | --- |
| IMDRF SaMD risk categorisation (N12) | SaMD class candidate I–IV | [samd-classification.md](samd-classification.md) |
| IEC 62304:2006/A1:2015 | Medical device software lifecycle | [iec-62304-sdlc.md](iec-62304-sdlc.md) |
| ISO 14971:2019 | Application of risk management to medical devices | [iso-14971-risk-register.md](iso-14971-risk-register.md) |
| EU MDR 2017/745 Annex II | Technical-file content | [ce-mark-checklist.md](ce-mark-checklist.md) |
| EU AI Act (Regulation 2024/1689) | High-risk AI obligations | [ce-mark-checklist.md](ce-mark-checklist.md) |
| MEDDEV 2.7/1 rev. 4 | Clinical evaluation | [clinical-evaluation-plan.md](clinical-evaluation-plan.md) |
| Project requirements traceability | PRD ↔ implementation ↔ test | [traceability-matrix.md](traceability-matrix.md) |

## Sub-documents

- [intended-use.md](intended-use.md) — Intended Use, Indications for Use, User Profile, Operational Environment, Contraindications.
- [samd-classification.md](samd-classification.md) — IMDRF SaMD categorisation; SaMD-vs-non-SaMD decision tree.
- [iec-62304-sdlc.md](iec-62304-sdlc.md) — Software lifecycle process mapping to repo artefacts.
- [iso-14971-risk-register.md](iso-14971-risk-register.md) — Hazard / sequence / harm risk register.
- [traceability-matrix.md](traceability-matrix.md) — All 119 PRD requirement ids ↔ implementation ↔ tests ↔ status.
- [ce-mark-checklist.md](ce-mark-checklist.md) — EU MDR Annex II + EU AI Act technical-file checklist.
- [clinical-evaluation-plan.md](clinical-evaluation-plan.md) — Clinical evaluation strategy stub.

## Authoritative sources inside this repo

- Engineering PRD (lightweight): [PRD.md](../../PRD.md)
- Enterprise PRD (full requirement ids, §15 Regulatory Strategy, §23 Risks): [RadioPad — Enterprise PRD _ Project Requirement Detail Document.md](../../RadioPad%20%E2%80%94%20Enterprise%20PRD%20_%20Project%20Requirement%20Detail%20Document.md)
- Architecture: [docs/03-architecture/architecture.md](../03-architecture/architecture.md)
- Security policy: [docs/04-security/](../04-security/)
- Build / iteration log: [PROGRESS.md](../../PROGRESS.md)
- Change log: [CHANGELOG.md](../../CHANGELOG.md)

## Out of scope for v0.1

- 510(k) / De Novo / PMA submission packages.
- Clinical investigation protocols beyond the strategy stub.
- Notified-body engagement.
- Post-market surveillance plan (PMS) — placeholder only.

These will be opened as separate dossier expansions when product positioning changes from "documentation assistant" to "diagnostic SaMD".
