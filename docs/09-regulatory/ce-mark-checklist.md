# CE-mark + EU AI Act Technical-File Checklist

**Status:** Draft  ·  **Owner:** Regulatory  ·  **Last Updated:** 2026-05-04

This is a **technical-file readiness checklist**, not a CE-mark application. It maps the obligations of EU MDR 2017/745 Annex II and the EU AI Act (Regulation 2024/1689) high-risk obligations to artefacts that exist (or are planned) in this repository. Use it to track gap-closure between iterations.

Legend: `[x]` evidence shipped · `[~]` partial · `[ ]` open.

## EU MDR 2017/745 — Annex II (Technical Documentation)

### 1. Device description and specification

- [x] Intended purpose statement — [intended-use.md](intended-use.md).
- [x] Indications, patient population, user profile — [intended-use.md](intended-use.md).
- [x] Contraindications — [intended-use.md](intended-use.md) §6.
- [x] Classification rationale — [samd-classification.md](samd-classification.md).
- [~] Configurations / variants (web / desktop / mobile / CLI) — described in [docs/03-architecture/architecture.md](../03-architecture/architecture.md); per-variant labelling pending.
- [ ] UDI plan.

### 2. Information to be supplied by the manufacturer

- [x] In-product warnings (`.ai-mark`, validation severities) — [docs/02-design/design.md](../02-design/design.md).
- [~] User manual / IFU — [docs/08-user-docs/](../08-user-docs/) and [cli-guide.md](../08-user-docs/cli-guide.md); clinical IFU pending.
- [ ] Translations.
- [x] Release notes — [CHANGELOG.md](../../CHANGELOG.md).

### 3. Design and manufacturing information

- [x] Architecture descriptions — [docs/03-architecture/](../03-architecture/) (C4 context/container/component, backend layered design).
- [x] ADRs — [docs/03-architecture/adr/](../03-architecture/adr/).
- [x] Lifecycle process mapping — [iec-62304-sdlc.md](iec-62304-sdlc.md).
- [x] Configuration management — Git, semver, [VERSIONING.md](../../VERSIONING.md).

### 4. General Safety and Performance Requirements (GSPR)

- [x] Risk register — [iso-14971-risk-register.md](iso-14971-risk-register.md).
- [~] GSPR clause-by-clause table — to be added when clinical scope expands.
- [x] Verification mapping — [traceability-matrix.md](traceability-matrix.md).

### 5. Benefit-risk analysis and risk management

- [x] ISO 14971 starter register — [iso-14971-risk-register.md](iso-14971-risk-register.md).
- [ ] Benefit-risk memo per residual-class-≥4 hazard.
- [~] Periodic review cadence — captured by the iteration loop in [PROGRESS.md](../../PROGRESS.md).

### 6. Product verification and validation

- [x] Unit / integration tests — `backend/RadioPad.Api/tests/RadioPad.Api.Tests/`.
- [x] Rulebook golden suites — `rulebooks/_tests/<rulebook_id>/`.
- [x] CI gates — every rulebook YAML validated + every matching golden suite run per `.github/instructions/testing.instructions.md`.
- [x] Frontend `pnpm typecheck` and component tests.
- [ ] Formal IQ/OQ/PQ for production deployments.

### 7. Post-market surveillance plan

- [ ] PMS plan template.
- [ ] Vigilance / incident reporting workflow.
- [~] Problem resolution — issue tracking + [SECURITY.md](../../SECURITY.md) for security disclosures.

## EU AI Act — high-risk obligations (Regulation 2024/1689, Title III, Chapter 2)

Even though v0.1 is positioned as non-SaMD ([samd-classification.md](samd-classification.md)), RadioPad voluntarily implements the following high-risk obligations:

### Art. 9 — Risk management system

- [x] Documented risk register — [iso-14971-risk-register.md](iso-14971-risk-register.md).
- [~] Continuous iterative process — re-assessed each iteration in [PROGRESS.md](../../PROGRESS.md).
- [ ] Independent regulatory sign-off cadence.

### Art. 10 — Data and data governance

- [x] Synthetic test fixtures only — `rulebooks/_tests/`; no PHI in repo (`.github/instructions/security.instructions.md`).
- [~] Data sourcing register for any future training data.
- [ ] Bias / representativeness review for clinical datasets (deferred until model training is in scope).

### Art. 11 — Technical documentation

- [x] This dossier — [docs/09-regulatory/](.).
- [x] Architecture, ADRs — [docs/03-architecture/](../03-architecture/).
- [x] OpenAPI — [openapi/openapi.yaml](../../openapi/openapi.yaml).

### Art. 12 — Record-keeping (logging)

- [x] Append-only audit log — `IAuditLog.AppendAsync` + SHA-256 chain.
- [x] Audited PHI block events — `AuditAction.ProviderBlocked`.
- [x] Per-report rulebook + prompt + model trace — AI-012 in [traceability-matrix.md](traceability-matrix.md).
- [~] Operator log retention policy.

### Art. 13 — Transparency and information to deployers

- [x] In-product `.ai-mark` indication — [docs/02-design/design.md](../02-design/design.md).
- [x] User documentation — [docs/08-user-docs/](../08-user-docs/).
- [~] Model card per provider in the catalog — [provider-catalog.md](../03-architecture/provider-catalog.md).

### Art. 14 — Human oversight

- [x] Mandatory radiologist acknowledgement before export (RPT-012).
- [x] AI text visually distinguished until reviewed (RPT-008 / `.ai-mark`).
- [x] No autonomous sign-off — encoded in safety boundary §1 of [AGENTS.md](../../AGENTS.md).

### Art. 15 — Accuracy, robustness, cybersecurity

- [~] Performance budgets — PERF-001..008 in [traceability-matrix.md](traceability-matrix.md).
- [~] Robustness — golden suites + validation engine.
- [x] Cybersecurity — `.github/instructions/security.instructions.md`, dependency pinning, append-only audit, secret env-var policy, `127.0.0.1` default bind.

### Art. 16–22 — Provider, importer, distributor, deployer obligations

- [ ] Conformity assessment plan.
- [ ] EU declaration of conformity template.
- [ ] CE marking artwork / labelling.
- [ ] Authorised representative designation (when EU placement begins).

## Open actions

1. Create GSPR clause-by-clause table once intended-use scope expands.
2. Draft PMS plan template and vigilance workflow.
3. Add operator-side log retention guidance to [docs/04-security/](../04-security/).
4. Add a per-provider model card section in [provider-catalog.md](../03-architecture/provider-catalog.md).
