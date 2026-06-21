# RadioPad — Enterprise PRD v2.0
## Project Requirement Detail Document — Open-Source, Lightweight, Cross-Platform Edition

| Field | Value |
| :---- | :---- |
| **Product** | RadioPad |
| **Category** | AI-assisted radiology reporting platform — Web + Desktop + Mobile + CLI + Daemon |
| **Document version** | **v2.0** (supersedes v1.0) |
| **Edition strategy** | Open-Source Community Edition (Apache 2.0 core + AGPL clinical modules) · Enterprise Edition (commercial add-ons) |
| **Target releases** | MVP-α → MVP-β → Clinical Beta → Enterprise GA → LTS |
| **Primary users** | Radiologists, residents, reporting administrators, imaging groups, hospitals, teleradiology providers, clinical AI governance teams, medical educators, researchers |
| **License posture** | Open-source first; zero vendor lock-in; BYOK; self-hostable end-to-end |
| **Performance posture** | Lightweight, low-latency, edge-capable, offline-first where viable |
| **Compatibility posture** | Linux (x86_64, ARM64) · Windows (x86_64, ARM64) · macOS (Intel, Apple Silicon) · Web (any modern browser) · iOS · Android |

---

## 0. Document Control

### 0.1 Changelog from v1.0 → v2.0

This document expands and rewrites v1.0 along five orthogonal axes:

| Axis | v1.0 posture | v2.0 posture |
| :---- | :---- | :---- |
| **Openness** | Provider-agnostic but suggested proprietary tooling | **Open-source-first across every layer**, with a clearly published OSS dependency manifest (Appendix A). |
| **Footprint** | Electron/Tauri suggested for desktop; no resource budgets | **Lightweight by contract** — Rust-based daemon, Tauri desktop, WASM-based local inference, per-module memory ceilings and bundle-size SLOs (§25). |
| **Portability** | Windows + macOS only for desktop | **True cross-platform** — Linux, ARM, mobile companions, PWA fallback, container-only deployments. |
| **Surface area** | 6 reporting modules | **20+ modules**, including subspecialty packs, peer review, teaching files, RECIST tracking, evidence linker, mobile read-only, plugin marketplace, federated learning. |
| **Operability** | Observability mentioned at high level | **First-class SRE design** — SLOs, error budgets, chaos engineering, disaster recovery, blue/green and canary delivery, full OpenTelemetry instrumentation. |

### 0.2 How to read this document

* **Priority labels:** `P0` = MVP-blocking. `P1` = Beta-blocking. `P2` = GA-blocking. `P3` = post-GA roadmap.
* **Edition labels:** `[CE]` = available in the free Community Edition. `[EE]` = Enterprise Edition only. `[EE-private]` = Enterprise private-deployment add-on.
* **Compliance labels:** `[PHI]` = handles protected health information. `[de-id]` = de-identified data only. `[meta]` = metadata only.
* Every requirement table preserves stable IDs from v1.0; new requirements use the same prefix with higher numbers so existing trace matrices keep working.

### 0.3 Naming conventions

* **RadioPad Core** — open-source, self-hostable monorepo (Apache 2.0).
* **RadioPad Clinical** — AGPL-3.0 clinical safety modules (validation engine, RADS packs, rulebook regression harness).
* **RadioPad Enterprise** — commercial license, closed-source connectors, SSO/SCIM/audit pro features.
* **RadioPad Studio** — the unified desktop client (Tauri).
* **`radiopad`** — the cross-platform CLI binary.
* **`radiopadd`** — the local background daemon (Rust).

---

## 1. Executive Summary

RadioPad is an **open-source, cross-platform, AI-assisted radiology reporting platform** that helps licensed radiologists draft, refine, validate, standardize, and export reports through dictation, natural-language instructions, reusable prompts, versioned **rulebooks**, structured templates, terminology mappings, and policy-aware multi-provider AI orchestration. It is designed to be the **AI reporting operating system for radiology**: opinionated about safety, agnostic about model providers, lightweight enough to run on a clinician's laptop, and powerful enough to be the institutional AI governance plane for an entire hospital network.

RadioPad is delivered as a single coherent product across five surfaces:

1. **Web App** — administrative console, reporting workspace, templates, rulebooks, analytics, collaboration, governance, and integrations.
2. **Desktop App (RadioPad Studio)** — Tauri-based, ≤80 MB installed, with dictation, global hotkeys, PACS/RIS bridge, local-encrypted cache, secure AI routing, offline drafts, and live overlay above third-party reporting systems.
3. **Mobile Companion** — iOS and Android read-only viewer for worklists, draft review, peer-review acknowledgment, critical-results notifications, and TOTP/WebAuthn approvals.
4. **CLI (`radiopad`)** — single-binary cross-platform tool for login, daemon control, rulebook lint/test, batch validation, model evaluation harness, audit export, and CI/CD integration.
5. **Local Daemon (`radiopadd`)** — Rust background service that owns provider routing, local inference (llama.cpp / ONNX / WebGPU), PHI policy enforcement, secure secret storage, and tenant pairing.

The architecture is drawn from the **local-first daemon + multi-client + provider-orchestration** pattern proven by open ecosystem projects (Paseo's daemon-with-clients model, jcode's subscription-backed OAuth providers, nexu/open-design's BYOK and skill-based extensibility) and adapted into a healthcare-grade model with strong PHI guardrails, deterministic validation, and immutable audit.

**RadioPad is not an autonomous diagnostic system.** It is positioned as a **radiologist-in-the-loop reporting productivity, quality, and standardization platform**. The clinical safety boundary is explicit: AI proposes, the radiologist disposes. Where the product approaches Software as a Medical Device (SaMD) territory — for example by generating diagnostic impressions or follow-up recommendations — the regulatory strategy (§19) defines the intended-use envelope, change-control commitments, and human-oversight controls required under FDA AI-enabled device guidance and the EU AI Act high-risk regime.

### 1.1 What makes RadioPad worldclass

| Pillar | How RadioPad earns it |
| :---- | :---- |
| **Open-source-first** | Every core component runs on permissive OSS. No proprietary dependency is required to deploy a working clinical pipeline (Appendix A). |
| **Lightweight by contract** | Daemon ≤30 MB RSS at idle; desktop ≤80 MB installed; web app first-paint ≤1.2 s on 4G; cold inference on a 7B quantized local model in ≤4 s on commodity laptops. |
| **Cross-platform truly** | First-class Linux, Windows, macOS (Intel + ARM), iOS, Android, and PWA. CI matrix builds all targets every commit. |
| **Standards-native** | DICOMweb, HL7 v2, FHIR R4 + R5, RadLex, RadElement, LOINC, SNOMED CT, all RADS frameworks, IHE profiles. |
| **Safety by construction** | Deterministic validation layer runs *before and after* every AI invocation. Hallucination guardrails. Provenance graph for every token shown to the user. |
| **Governable end-to-end** | Versioned rulebooks, prompt blocks, model cards, validation packs; promotion gates require regression tests; rollback is one command. |
| **Operable** | OpenTelemetry-native; SLOs published; error budgets enforced; chaos drills scheduled; supply chain hardened via SBOM and SLSA-3 builds. |

---

## 2. Product Vision

### 2.1 Vision Statement

RadioPad becomes the **default open-source AI reporting layer for radiology**: a secure, configurable, clinically governed platform that turns radiologist intent — dictation, prior exams, measurements, rulebooks, institutional standards — into high-quality draft reports that remain fully controlled, reviewed, and signed by licensed radiologists, on any device, in any environment, with any compliant AI provider.

### 2.2 North Star Metric

**Verified Time-to-Quality-Report (vTTQR)** — the median wall-clock time from "study opened" to "report exported and audit-complete" *for reports that pass all tenant rulebook checks at first review*. Target: a 40 % reduction versus baseline within 90 days of tenant rollout, without degrading reader-physician quality scores.

### 2.3 Product Principles (expanded)

| Principle | Meaning for RadioPad |
| :---- | :---- |
| **Radiologist remains final authority** | AI drafts, suggests, validates, and formats; it never signs or finalizes independently. All AI-touched text is provenance-marked until human-reviewed. |
| **Configurable by institution** | Hospitals and imaging groups define templates, style guides, impression rules, escalation language, modality-specific policies, and approval workflows. Defaults exist for fast onboarding but are never sticky. |
| **Local-first when needed** | Desktop and CLI surfaces work offline. Local inference is a first-class peer of cloud inference. PHI never leaves the perimeter unless tenant policy explicitly permits a specific provider for a specific endpoint. |
| **Open by default** | Code, schemas, telemetry, data dictionaries, and rulebook formats are public and machine-readable. Lock-in is impossible by construction. |
| **Standards-aware** | RSNA RadReport, RadLex, RadElement, ACR RADS, DICOMweb, HL7 v2, FHIR R4/R5, IHE, LOINC, SNOMED CT, ICD-10/11, CPT, and institution-defined templates are all addressable through stable interfaces. |
| **Explainable and auditable** | Every AI-generated segment is traceable to its input context, prompt version, rulebook version, model identity, provider, latency, cost, and the user edits that followed. |
| **Safe by design** | Built-in deterministic guardrails (laterality, measurement, negation, anatomy, modality), AI-assisted hallucination detection, PHI controls, immutable audit, RBAC, and a published model governance lifecycle. |
| **Lightweight is a feature** | Resource budgets are contractual, not aspirational. Each module ships with measured P50/P95/P99 latency and memory footprints. |
| **Cross-platform is a feature** | If it doesn't run on Linux, macOS, Windows, and at least one mobile OS, it isn't shipped. |
| **Operable on day one** | Health checks, SLOs, runbooks, dashboards, and tracing are written as part of the feature, not after. |

---

## 3. Open-Source Philosophy & Licensing Strategy

This is a new section in v2.0 and is foundational to the whole product.

### 3.1 Why open-source first

1. **Trust in clinical settings.** Hospitals require source-available code paths for PHI processing they cannot audit otherwise.
2. **Community-driven standardization.** Rulebooks, RADS packs, and specialty templates evolve faster when contributed by the community.
3. **Avoidance of vendor capture.** No single AI provider, no single cloud, no single integration is load-bearing.
4. **Cost containment.** Hospitals can run RadioPad Community Edition on commodity hardware with no per-seat license cost; commercial features are explicit and optional.
5. **Educational reach.** Residents, training programs, and academic centers can deploy RadioPad freely.

### 3.2 License matrix

| Component | License | Rationale |
| :---- | :---- | :---- |
| **RadioPad Core** (web app, daemon, CLI, desktop shell, AI gateway, base templates, base rulebooks, observability, SDKs) | **Apache 2.0** | Permissive for maximum adoption, including by closed-source plugins. |
| **RadioPad Clinical** (validation engine, RADS modules, rulebook regression harness, hallucination guardrails, golden-case packs) | **AGPL-3.0** | Copyleft to prevent commercial forks from closing clinical safety improvements; SaaS users must publish their modifications. |
| **RadioPad Enterprise** (SCIM, SAML pro, SIEM connectors, customer-managed keys, governance dashboard, federated learning coordinator) | **Commercial** | Funded development of high-touch enterprise features; clean separation through plugin boundary. |
| **Plugins / connectors** | Plugin author's choice (Apache 2.0, MIT, BSD, GPL, commercial) | Stable plugin API permits any license. |
| **Documentation, schemas, rulebook formats** | **CC-BY-4.0** | Encourages sharing across institutions. |
| **Synthetic golden-case packs (non-PHI)** | **CC0-1.0** | Public domain to accelerate research. |

### 3.3 Open-source promises (the "OSS Bill of Rights")

1. **The Community Edition is fully clinically useful.** Hospitals can run end-to-end PHI workflows on CE alone, with self-hosted open-source AI providers.
2. **No "open core" bait-and-switch.** Features in CE today will not be moved to EE later. EE features are net-new.
3. **Schemas are forever open.** Rulebooks, templates, audit events, FHIR mappings, and validation results are JSON Schema / OpenAPI / FHIR-defined.
4. **Data portability is mandatory.** Every tenant can export everything — reports, rulebooks, audit, models — in standards-based formats with one CLI command.
5. **The plugin API is stable.** Breaking changes follow a 12-month deprecation policy with semver guarantees.
6. **SBOMs are published every release.** Each release ships SPDX and CycloneDX SBOMs, signed in-toto attestations, and SLSA-3 provenance.

### 3.4 Community governance

* **Steering committee** with seats reserved for academic radiology, hospital IT, AI safety, and OSS maintainers.
* **RFC process** modeled on the Rust / Kubernetes patterns — public design docs, public review, time-boxed comment windows.
* **Clinical Safety Working Group** owns the validation engine and RADS modules; changes require sign-off from at least two board-certified radiologists.
* **Security disclosure** via a signed GPG mailbox, 90-day embargo, CVE coordination.
* **CLA / DCO** — Developer Certificate of Origin sign-off on every commit; no CLA assignment required.

---
## 4. Market and Standards Context

Radiology reporting has mature standardization scaffolding. **RSNA RadReport** publishes peer-reviewed reporting templates that incorporate **RadLex** terminology and **RadElement** common data elements; shared vocabularies, structured templates, and common data elements improve EHR integration, patient record organization, personalized care, workflow speed, and quality. **ACR RADS** frameworks (BI-RADS, LI-RADS, PI-RADS, Lung-RADS, TI-RADS, O-RADS, NI-RADS, C-RADS, etc.) provide standardized categorization and reporting language to reduce inter-reader variability.

For interoperability, **HL7 FHIR R4 `DiagnosticReport`** is the canonical resource for imaging investigations (X-ray, CT, MRI, US, NM), and supports text conclusions, coded observations, and formatted attachments. **HL7 v2 ORU** remains the workhorse messaging format in most US hospitals. **DICOM** and **DICOMweb** (QIDO-RS, WADO-RS, STOW-RS, UPS-RS) provide RESTful access to imaging data and workflow. **IHE profiles** (XDS-I, SWF, AIR, AIW-I) define the integration patterns most hospital ecosystems already follow.

For security, **HIPAA's Security Rule** requires administrative, physical, and technical safeguards for ePHI in the United States. The **EU GDPR** treats medical data as a special category requiring explicit lawful basis and DPIAs. The **UK Data Protection Act 2018** layers on the NHS Data Security and Protection Toolkit. Cloud AI providers offering PHI processing typically require a **Business Associate Agreement (BAA)**, with explicit Zero Data Retention / Modified Abuse Monitoring controls that are endpoint-specific and require pre-approval.

RadioPad treats these standards as **first-class citizens** in the data model — not as export formats bolted on at the end.

### 4.1 Standards coverage matrix

| Standard | RadioPad surface | Edition |
| :---- | :---- | :---- |
| HL7 FHIR R4 `DiagnosticReport`, `ImagingStudy`, `Observation`, `Patient`, `Practitioner`, `Encounter` | Native data model + export + bulk FHIR | CE |
| HL7 FHIR R5 `DiagnosticReport`, `ImagingSelection` | Export + ingest | CE |
| HL7 v2.5+ ORU, ORM, ADT | Bidirectional channel via MLLP / TLS | EE |
| DICOMweb QIDO/WADO/STOW/UPS-RS | Metadata retrieval, instance access, workflow | CE |
| DICOM SR (Structured Reporting) | Export for measurement-bearing reports | CE |
| RadLex | Native terminology binding, FHIR ValueSet | CE |
| RadElement / CDEs | Template-bound common data elements | CE |
| ACR RADS (BI/LI/PI/Lung/TI/O/NI/C-RADS) | Subspecialty rulebook packs | CE base, EE advanced |
| LOINC | Procedure and observation coding | CE |
| SNOMED CT | Clinical concept binding (license-aware) | CE |
| ICD-10-CM, ICD-11 | Diagnosis coding for billing pipelines | CE |
| CPT | Procedure coding | CE |
| IHE XDS-I.b, SWF.b, AIR | Cross-enterprise document sharing | EE |
| IHE AIW-I (AI Workflow for Imaging) | Workflow orchestration | EE |
| openEHR archetypes (optional) | Structured archetype export | EE |

### 4.2 Reference AI ecosystem signals

* The **FDA** publishes guidance for AI-enabled device software functions, including Predetermined Change Control Plans (PCCP) for models that learn over time.
* The **EU AI Act** classifies AI software intended for medical purposes as high-risk and requires risk mitigation, high-quality data, transparency, technical documentation, post-market monitoring, and human oversight.
* The **NHS** publishes the AI Buyer's Guide and the Algorithmic Impact Assessment.
* **NIST AI RMF 1.0** and the **GMLP (Good Machine Learning Practice)** principles from FDA/Health Canada/MHRA define safe-AI-in-healthcare expectations.

RadioPad's regulatory strategy (§19) maps every clinical feature to these frameworks.

---

## 5. Product Goals

### 5.1 Business Goals

1. Launch a **clinically credible open-source AI reporting platform** with active deployments in academic medical centers, radiology groups, imaging centers, and teleradiology providers.
2. Support **flexible monetization**: free Community Edition, per-seat Pro, hospital Enterprise, and Enterprise Plus on-prem, plus optional usage-based AI credits and managed-cloud add-ons.
3. **Differentiate** through rulebook-driven generation, local desktop/CLI execution, multi-provider AI orchestration, cross-platform reach, and institutional governance.
4. **Reduce vendor dependence** with provider abstraction, BYOK, OAuth-subscription support where compliant, local open models, and approved enterprise APIs.
5. Establish RadioPad as a **reporting quality platform**, not merely a dictation assistant — measured by tenant rulebook pass rates, edit-distance improvements, and TAT impact.
6. Build a **healthy contributor community**: ≥100 external contributors, ≥20 community-maintained rulebook packs, ≥5 specialty modules from external authors within 12 months of CE GA.

### 5.2 Clinical Workflow Goals

1. Reduce per-report drafting time by ≥30 % for routine modalities at parity quality.
2. Improve report completeness (required-section pass rate) to ≥98 %.
3. Enforce institution-specific reporting standards with zero unapproved-rulebook bleed into production.
4. Reduce contradictions between findings and impression to <1 per 100 reports.
5. Convert free dictation into structured, clean, clinically appropriate report sections without altering clinical meaning (validated via golden-case regression).
6. Support every subspecialty (chest, abdominal, neuro, MSK, breast, cardiac, IR, pediatric, nuclear, ER, vascular, OB/GYN) with dedicated rulebook packs.
7. Track longitudinal lesions, measurements, and RECIST/PERCIST/iRECIST/Lugano criteria across studies.
8. Surface incidental findings consistently with Fleischner / White Paper recommendations.

### 5.3 Technical Goals

1. Provide secure, performant **web, desktop, mobile companion, and CLI** experiences from a single source of truth.
2. Support **hybrid cloud, full SaaS, on-prem, air-gapped, and edge** deployments without forking the codebase.
3. Maintain end-to-end **provenance** for every AI event (prompt, rulebook, model, provider, input, output, edits, validation, exports).
4. Provide **standards-based integrations** with PACS, RIS, EHR, dictation systems, billing systems, and AI marketplaces.
5. Support **policy-aware AI routing** by modality, site, tenant, PHI class, model type, compliance profile, cost ceiling, and latency budget.
6. Ship with **measured performance budgets** — not aspirational targets — for every user-visible operation (§25).
7. Provide **zero-downtime upgrades**, **graceful degradation**, and **survivable failure modes** so a single dependency loss never blocks reporting.

### 5.4 Open-Source / Community Goals

1. CE is **clinically complete** on day one: a hospital can deploy and report safely with zero EE features.
2. **Public roadmap** with monthly community calls.
3. **Conformance test suite** that any third-party AI provider or PACS connector can run to certify compatibility.
4. **Rulebook marketplace** seeded with permissively-licensed packs for each major subspecialty.

---

## 6. Non-Goals

RadioPad v2.0 will **not**:

1. Autonomously diagnose imaging studies without radiologist review.
2. Replace PACS, RIS, EHR, viewer, or dictation systems wholesale.
3. Claim FDA clearance, CE marking, MHRA registration, or equivalent regulatory status unless explicitly obtained for a defined intended-use envelope.
4. Use patient data for model training without explicit contractual, legal, governance, and tenant-level opt-in.
5. Send PHI to consumer AI services or to providers without an approved compliance configuration.
6. Auto-sign radiology reports.
7. Order follow-up imaging or labs without physician confirmation.
8. Provide direct patient-facing diagnostic advice.
9. Interpret pixel data directly in MVP (image-aware modules are a clearly scoped post-GA roadmap item with its own regulatory pathway).
10. Be Electron-heavy or bundle Chromium-per-app — desktop must remain lightweight (Tauri/native WebView).
11. Be Windows/macOS-only — Linux is a first-class target from day one.
12. Lock customers into a single AI provider or cloud.

---

## 7. Core User Personas

### 7.1 Radiologist (general)
**Needs:** Fast drafting, dictation cleanup, impression generation, prior comparison, error detection, custom style, hotkeys, low friction, offline tolerance, mobile peek-at-worklist.  
**Pain points:** Repetitive reports, inconsistent templates, long turnaround, dictation errors, manual impression writing, missed measurements, contradictions, fatigue.

### 7.2 Subspecialist Radiologist (NEW)
**Needs:** Subspecialty-aware rulebooks (e.g., LI-RADS for HCC, PI-RADS for prostate, Lung-RADS for screening), domain-specific structured templates, longitudinal lesion tracking, evidence linkage to society guidelines.  
**Pain points:** Generic AI that misuses RADS language; lack of automated category assignment; manual lesion bookkeeping across studies.

### 7.3 Resident / Fellow (NEW)
**Needs:** Teaching mode, attending feedback workflow, peer-review queue, learning-oriented rulebook hints, citation surfaces, study question generator.  
**Pain points:** Steep learning curve for institutional style; corrections lost in informal channels.

### 7.4 Teleradiologist (NEW)
**Needs:** Multi-tenant rapid context switching, per-client rulebooks and templates, low-latency dictation across geographies, encrypted offline drafting in case of network loss, mobile second-opinion review.  
**Pain points:** Switching reporting styles between contracts; bandwidth-sensitive PACS connections; cross-jurisdiction PHI rules.

### 7.5 Reporting Administrator
**Needs:** Manage templates, macros, rulebooks, roles, department standards, subspecialty styles, onboarding kits, audit search.  
**Pain points:** Template drift, inconsistent language, weak governance, hard-to-enforce rules across teams.

### 7.6 Chief Radiologist / Medical Director
**Needs:** Quality metrics, adoption analytics, safety monitoring, subspecialty standardization, governance approvals, peer-review oversight, discrepancy tracking.  
**Pain points:** Reporting variation, medico-legal exposure, missing critical-finding language, untracked AI use.

### 7.7 Hospital IT / Security
**Needs:** SSO, RBAC, audit logs, encryption (at-rest, in-transit, in-memory where viable), deployment control, BAA/vendor review, network boundaries, standards-based integration, SBOMs, vuln-scan integration.  
**Pain points:** PHI leakage, shadow AI tools, lack of auditability, non-compliant integrations, opaque vendor supply chains.

### 7.8 AI Governance / Compliance Team
**Needs:** Model inventory, prompt versioning, validation packs, drift monitoring, incident review, approval workflow, PCCP support, bias auditing.  
**Pain points:** Shadow AI usage, irreproducible outputs, missing lifecycle documentation.

### 7.9 Referring Physician (NEW, indirect persona)
**Needs:** Clear, consistent impressions, referring-physician-summary mode, structured key images and measurements, well-articulated follow-up.  
**Pain points:** Long narrative reports, inconsistent recommendations, unclear urgency.

### 7.10 Patient (NEW, indirect persona — out of MVP scope)
**Needs:** Plain-language report summary in patient portal **with explicit physician approval and disclaimer**.  
**Pain points:** Jargon, incomplete context, anxiety from raw reports.

### 7.11 Researcher / Educator (NEW)
**Needs:** De-identified dataset export, teaching-file generator, synthetic case generator, governance-friendly research enclave, citation linker.  
**Pain points:** Manual de-identification, lack of structured exports, difficulty assembling teaching files.

### 7.12 Quality / Peer-Review Coordinator (NEW)
**Needs:** RADPEER-aligned peer review queue, discrepancy capture, anonymized review, learning loop into rulebooks.  
**Pain points:** Manual case selection, low review volume, disconnected from reporting tool.

---

## 8. Product Architecture Overview

### 8.1 High-level architecture

RadioPad uses a **hybrid control-plane + local-execution** architecture. The control plane manages tenants, policy, governance, billing, analytics, model registry, and audit search. The local execution layer (desktop + daemon) handles real-time reporting, PHI-bound operations, PACS/RIS bridging, and local inference. Both planes are independently deployable, and the desktop + daemon can operate fully offline against the local rulebook cache for drafting workflows.

```
                  ┌─────────────────────────────────────────────────────────────┐
                  │                       RadioPad Cloud / Control Plane         │
                  │  Identity · Tenancy · Rulebook Registry · Template Library   │
                  │  Model Registry · Analytics · Audit Search · Billing · APIs  │
                  │  AI Governance Dashboard · Validation Pack Registry          │
                  └─────────────────────────────────────────────────────────────┘
                         ▲        ▲          ▲                ▲           ▲
                         │mTLS    │OIDC      │WebSocket       │REST       │FHIR
                         │        │          │ (signed JWT)   │           │
              ┌──────────┴───┐ ┌──┴────────┐ ┌┴────────────┐ ┌┴────────┐ ┌┴──────────┐
              │ Web App      │ │ Studio    │ │ Mobile      │ │ CLI     │ │ External  │
              │ (React/Next) │ │ (Tauri)   │ │ (RN/Capac.) │ │ (Rust)  │ │ EHR/PACS  │
              └──────────────┘ └────┬──────┘ └─────────────┘ └────┬────┘ └───────────┘
                                    │                              │
                                    ▼                              ▼
                            ┌──────────────────────────────────────────────┐
                            │              Local Daemon (radiopadd)         │
                            │  Rust · ≤30 MB RSS · gRPC + UNIX socket       │
                            │                                              │
                            │  · Provider router & policy engine            │
                            │  · Local inference (llama.cpp / ONNX / WebGPU)│
                            │  · Secure secret vault (OS keychain backed)   │
                            │  · Encrypted local cache (SQLCipher)          │
                            │  · PACS/RIS bridge plugins                    │
                            │  · Audit buffer (offline-tolerant)            │
                            │  · MCP-style tool registry (sandboxed)        │
                            └──────────────────────────────────────────────┘
                                    │                       │
                ┌───────────────────┴─────────┐  ┌──────────┴───────────────┐
                │   Local resources           │  │   Remote AI providers     │
                │  · DICOMweb / PACS / RIS    │  │  · OpenAI / Anthropic /   │
                │  · EHR FHIR endpoint        │  │    Google / Azure / AWS    │
                │  · Dictation device         │  │    (PHI w/ BAA only)       │
                │  · Local GPU / NPU          │  │  · OSS providers (vLLM,   │
                │  · OS keychain (PKCS11)     │  │    Ollama, Llama.cpp,     │
                └─────────────────────────────┘  │    LocalAI, LM Studio)    │
                                                 └───────────────────────────┘
```

### 8.2 Architectural patterns

| Pattern | Where used | Rationale |
| :---- | :---- | :---- |
| **Local daemon + multi-client** | `radiopadd` serves Studio, CLI, and PACS bridge plugins via gRPC over UNIX socket / named pipe | Single source of policy, secrets, and inference on a workstation. |
| **Policy-as-code** | Provider routing, rulebook activation, PHI rules expressed as declarative YAML evaluated by an OPA-compatible engine | Auditable, testable, versionable, copyable across tenants. |
| **Event-sourced audit** | All AI events, edits, exports, and admin actions emit immutable events to an append-only log (Postgres logical replication → S3-compatible object store) | Reconstruct any past report state; tamper-evident. |
| **Provenance graph** | Every output token is linked to its inputs, prompt block, rulebook version, model, and provider | Explainability, debugging, compliance. |
| **Plugin boundary** | Connectors, providers, and tools live behind a stable plugin API (Rust trait + WASM ABI) | Third parties contribute without forking core. |
| **Twelve-factor + reproducibility** | Stateless services, config from env, ephemeral filesystems, idempotent migrations | Predictable deploys. |
| **Hexagonal / ports-and-adapters** | Domain logic separated from FHIR/HL7/DICOMweb adapters | Replaceable integrations. |
| **CRDT for collaborative editing** | Yjs-style CRDT for multi-user report draft editing | Offline-tolerant collaboration. |

### 8.3 Layer responsibilities

| Layer | Responsibility |
| :---- | :---- |
| **Cloud control plane** | Tenants, users, billing, rulebook registry, model registry, audit search, analytics, governance, admin settings, plugin marketplace. |
| **Web app** | Browser-based reporting workspace, admin console, governance dashboards, settings. |
| **Studio (desktop)** | Local reporting workspace, dictation, hotkeys, DICOM/PACS bridge UI, daemon UI, offline drafts. |
| **Mobile companion** | Worklist peek, draft review, peer-review acknowledgement, critical-results, WebAuthn approvals. |
| **CLI** | Login, daemon control, rulebook lint/test, batch validation, audit export, model evaluation, CI/CD. |
| **Local daemon** | Provider routing, policy enforcement, local inference, secure secret storage, encrypted cache, audit buffering, plugin host. |
| **AI gateway** | Stateless service that fronts all provider calls (local and remote), runs guardrails, applies routing policy, emits telemetry. |
| **Rulebook engine** | Loads, validates, versions, evaluates rulebooks; runs deterministic validators; emits validation results. |
| **Validation engine** | Deterministic checks (laterality, measurement, modality, anatomy, negation, required sections) + AI-assisted checks (unsupported claims, contradictions). |
| **Integration layer** | DICOMweb, HL7 v2 MLLP, FHIR R4/R5, webhooks, SSO, billing, SIEM. |

---
## 9. Open-Source Technology Stack

This stack is selected for **performance, footprint, cross-platform reach, and OSS-friendliness**. Every choice has at least one viable alternative listed so deployments are not pinned to a single project.

### 9.1 Core stack

| Layer | Primary choice | Alternates | Why |
| :---- | :---- | :---- | :---- |
| **Frontend (web)** | **React 18 + Vite + TypeScript** | Next.js (for SSR-heavy admin), SvelteKit | Vite-fast HMR; SSR optional, not required; small bundle (<200 KB initial). |
| **Frontend (state)** | Zustand + TanStack Query | Redux Toolkit, Jotai | Tiny, predictable, no boilerplate. |
| **Frontend (styling)** | Tailwind CSS + Radix UI primitives | Mantine, shadcn/ui | Accessible primitives, no design lock-in. |
| **Frontend (editor)** | TipTap (ProseMirror) | Slate, CodeMirror 6 | Schema-driven structured editing, table support, collaborative-ready. |
| **Frontend (collab)** | Yjs CRDT + y-websocket | Automerge | Offline-tolerant collaborative editing. |
| **Desktop shell** | **Tauri 2.x** | Electron (only if Tauri blocked) | Native WebView, ~10× smaller than Electron, Rust backend, MSAA accessibility. |
| **Mobile** | **Capacitor + React** for shared codebase | React Native (if native module needs grow) | Reuses web UI; iOS/Android in one repo. |
| **Backend (services)** | **Go (chi/echo) and Rust (axum) for hot-path** | Python (FastAPI) for ML services | Go for breadth (high-throughput stateless services), Rust for AI gateway and daemon (latency + memory safety). |
| **CLI + daemon** | **Rust** (clap, tokio, tonic) | Go (cobra) | Single statically-linked binary per platform; minimal RAM footprint. |
| **Inference (local)** | **llama.cpp** (GGUF), **ONNX Runtime**, **vLLM** (server), **WebGPU/wllama** (browser) | Ollama (wrapper), LocalAI | Cross-platform, quantization-friendly, GPU/NPU/CPU portable. |
| **Speech-to-text (local)** | **whisper.cpp** with quantized models | faster-whisper, Vosk | Runs offline, multilingual, accurate. |
| **Voice activity / wake** | Silero VAD (ONNX) | webrtc-vad | Tiny ONNX model. |
| **Database** | **PostgreSQL 16** + **SQLite** (local) | CockroachDB (multi-region EE), DuckDB (analytics) | Postgres for cloud, SQLite for daemon — same SQL surface via sqlx. |
| **Encrypted local store** | **SQLCipher** | rusqlite + age | PHI at rest on workstation. |
| **Cache + queue** | **NATS JetStream** + **Redis 7** | RabbitMQ, Kafka | NATS for streams + KV; Redis for hot cache. |
| **Search** | **OpenSearch** + **pgvector** | Meilisearch, Typesense, Qdrant | OpenSearch for audit/text, pgvector for embeddings. |
| **Vector DB** | **pgvector** by default; **Qdrant** for high-volume | Milvus, Weaviate | pgvector keeps the stack lean; Qdrant when scale warrants. |
| **Object storage** | **MinIO** (self-host), **S3-compatible** elsewhere | Ceph, Garage | Universal API. |
| **Auth (open)** | **Keycloak** (OIDC + SAML) | Ory Kratos + Hydra, Authentik, Zitadel | Mature, supports SCIM, hardened. |
| **Secrets** | **HashiCorp Vault** (or **OpenBao** fork) | SOPS+age (lightweight), Infisical | Vault for cloud, OS keychain for desktop. |
| **Identity on device** | **WebAuthn / FIDO2** (libfido2) | TOTP fallback | Phishing-resistant strong auth. |
| **Reverse proxy / ingress** | **Caddy 2** (auto-TLS) | Traefik, Envoy | Tiny binary, automatic HTTPS, OCSP stapling. |
| **Service mesh (optional)** | **Linkerd** | Istio (only if needed) | Light footprint, Rust data plane. |
| **Infra-as-code** | **Terraform** + **Pulumi** | Crossplane | Plus Helm charts for k8s. |
| **Container runtime** | **Docker / Podman / containerd** | — | Standard. |
| **Orchestration** | **Kubernetes** (k3s for edge / small hospitals) | Nomad | k3s allows single-node hospitals. |
| **CI** | **GitHub Actions** + **Dagger** (portable) | GitLab CI, Drone | Dagger for portable pipelines. |
| **Supply chain** | **sigstore (cosign + Fulcio + Rekor)**, **in-toto** | Notary v2 | Signed images, SLSA-3 attestations. |
| **Observability — traces** | **OpenTelemetry** + **Tempo** / **Jaeger** | Honeycomb (EE add-on) | OTel-native end to end. |
| **Observability — metrics** | **Prometheus** + **Grafana Mimir** | VictoriaMetrics | Long-term metric store. |
| **Observability — logs** | **Grafana Loki** | Vector + ClickHouse | Cheap log retention. |
| **Observability — UI** | **Grafana** | — | Single pane of glass. |
| **Continuous profiling** | **Pyroscope / Grafana Phlare** | Parca | CPU/heap profiles in prod. |
| **Feature flags** | **OpenFeature** + **flagd** | Unleash | Vendor-neutral spec. |
| **Job scheduling** | **River** (Postgres) or **NATS workqueues** | Temporal (heavy but durable) | Lean by default; Temporal for long workflows in EE. |
| **Migrations** | **golang-migrate** / **sqlx** | Atlas | Plain SQL, reversible. |
| **Localization** | **FormatJS / ICU MessageFormat** + **gettext** | i18next | ICU for clinical accuracy. |
| **Forms / schemas** | **JSON Schema** + **AJV** + **Zod** | Yup | Schema is the contract. |
| **DICOM tooling** | **DCMTK**, **pydicom**, **dcm4che**, **Orthanc** (test PACS), **OHIF Viewer** (read-only embed) | — | All proven OSS. |
| **FHIR tooling** | **HAPI FHIR**, **Medplum**, **firely-server** (community) | — | HAPI for Java side, Medplum for TS side. |
| **Medical NLP (deterministic)** | **scispaCy**, **medspaCy**, **NegEx**, **ConText**, **QuickUMLS** | cTAKES (heavy), MetaMap | Used for negation, anatomy, measurement parsing. |
| **De-identification** | **Microsoft Presidio** (open) + **CliniDeID-OS** patterns | scrubadub | Two-pass with regex + NER. |
| **Spelling / grammar** | **Hunspell** (medical dictionaries) + **LanguageTool** (open core) | — | Plus institution-specific lexicons. |
| **Policy engine** | **Open Policy Agent (OPA)** + Rego | Cedar | Tenant-friendly policy. |
| **Build / package** | **Bazel** (monorepo) or **Nx** | Turborepo | Hermetic builds, large-scale caching. |

### 9.2 Open-source AI provider menu (no vendor lock-in)

Every deployment can choose any combination of these. PHI eligibility is a tenant-level switch per provider.

| Provider class | Examples | PHI default | Footprint |
| :---- | :---- | :---- | :---- |
| **Local CPU (quantized)** | llama.cpp + Llama-3 8B Q4_K_M, Qwen2.5 7B, Mistral 7B, Phi-3 mini, MedGemma (when license permits) | ✅ Allowed | 4–6 GB RAM |
| **Local GPU** | vLLM + Llama-3 70B / Qwen2.5 32B / Mixtral 8×7B | ✅ Allowed | 1× consumer GPU sufficient for 7–13B |
| **Local NPU / edge** | ONNX Runtime + DirectML / CoreML / OpenVINO | ✅ Allowed | Embedded inference |
| **Browser (WebGPU)** | wllama / web-llm | ✅ Allowed (no exfil) | 2–4 GB VRAM |
| **Self-hosted server** | Ollama, LocalAI, vLLM, TGI, TabbyAPI | ✅ Allowed | Customer infra |
| **Cloud BAA-approved** | OpenAI (with BAA + ZDR), Azure OpenAI, AWS Bedrock, Google Vertex (HIPAA-eligible models), Anthropic (with BAA) | ✅ If contract present | N/A |
| **OAuth-subscription** (where compliant for non-PHI) | Any provider whose ToS permits | ❌ Default no-PHI | N/A |
| **BYOK customer API** | Customer's existing API account | Depends on customer contract | N/A |
| **Sandbox / demo** | Mock provider | ❌ Synthetic only | Trivial |

The default Community Edition ships pre-configured for **local-only inference** so a hospital can run the full clinical pipeline with no external network egress.

### 9.3 Specialized medical NLP toolkit

Bundled as `radiopad-clinical-nlp`:

* **Negation/uncertainty:** NegEx, ConText, custom radiology negation patterns.
* **Measurement extraction:** Quantulum3 + custom regex for radiology units (mm, cm, HU, SUV, ADC).
* **Anatomy mapping:** RadLex + FMA cross-references.
* **Laterality detection:** rule-based (left/right/bilateral) plus dependency parsing for ambiguous cases.
* **Modality/body-part classifier:** scikit-learn linear model on study description (no PHI required).
* **Section segmentation:** transformer-based fine-tune on RadReport corpus (or rule-based fallback).
* **RADS category extractor:** per-RADS family classifier head.
* **Critical-result detector:** keyword + classifier ensemble with tenant tunable thresholds.

All deterministic — used **before and after** every LLM call to constrain inputs and validate outputs.

---

## 10. Cross-Platform Strategy

### 10.1 Target matrix

| Surface | Linux | Windows | macOS | iOS | Android | Browser |
| :---- | :---- | :---- | :---- | :---- | :---- | :---- |
| Web App | ✅ (any modern browser) | ✅ | ✅ | ✅ (Safari/Chrome) | ✅ (Chrome/FF) | ✅ |
| Studio (Tauri) | ✅ (x86_64 + ARM64, .deb/.rpm/.AppImage/Flatpak/Snap) | ✅ (x86_64 + ARM64, MSI + MSIX) | ✅ (Intel + Apple Silicon, .dmg + Mac App Store) | — | — | — |
| Mobile companion | — | — | — | ✅ | ✅ | ✅ (PWA) |
| CLI (`radiopad`) | ✅ (musl + glibc) | ✅ | ✅ | — | — | — |
| Daemon (`radiopadd`) | ✅ (systemd unit) | ✅ (Windows service) | ✅ (launchd) | — | — | — |
| Server | ✅ (k8s, k3s, bare, Docker) | (not officially supported as server) | (dev only) | — | — | — |

### 10.2 Build & release tooling

* **CI matrix builds all targets every commit** — Linux x86_64/ARM64, Windows x86_64/ARM64, macOS Intel/ARM, iOS, Android, browser.
* **Reproducible builds** for the daemon and CLI (deterministic Rust + locked deps + nix or earthly).
* **Code-signed binaries** for all desktop platforms; notarized macOS app; Microsoft SmartScreen reputation strategy.
* **Auto-update** through Tauri updater (signed + EdDSA verified), with rollout rings.
* **Distribution channels:** GitHub Releases, Homebrew, Scoop, winget, apt repo, dnf repo, AUR, Flatpak, Snap, Mac App Store (read-only viewer), Microsoft Store, Apple App Store, Google Play.

### 10.3 Browser compatibility floor

* **Evergreen browsers**: Chromium ≥120, Firefox ≥120, Safari ≥17. Last-2-versions policy.
* **No IE / no Edge legacy**.
* **WebGPU** opportunistic — falls back to WASM SIMD or remote inference.
* **WebAuthn required** for clinical sign-on; TOTP fallback only with explicit policy.

### 10.4 Accessibility floor

* **WCAG 2.2 AA** compliance is a CI gate on the web and Studio surfaces.
* **Screen-reader–first reporting workspace** — every AI action announces its source and provenance.
* **Keyboard-first** — every workflow must be completable without mouse.
* **High-contrast and color-blind safe palettes** included; no critical info conveyed by color alone.
* **Localized** at minimum: English, Spanish, Portuguese, French, German, Arabic, Hindi, Urdu, Chinese (Simplified), Japanese.

### 10.5 Internationalization

* All UI strings are ICU MessageFormat resources.
* Date, time, number, and unit formatting use the platform locale by default with explicit tenant overrides.
* **Right-to-left** (RTL) supported end-to-end for Arabic, Hebrew, Urdu.
* Bilingual reports supported (e.g., English clinical body + Arabic patient summary), with separate provenance per language.

---

## 11. Performance Engineering Principles

### 11.1 Performance is a contract

For every user-visible operation, RadioPad publishes:

* **P50, P95, P99 latency target** (steady-state, 4G network, mid-range hardware).
* **Memory ceiling** at idle and under load.
* **CPU steady-state ceiling**.
* **Network egress per operation**.
* **Battery cost** on laptops (joules per operation, measured on a reference device).

These are **CI-gated** via performance budgets (Lighthouse + custom microbenchmarks). A PR that regresses any budget by >5 % cannot land without an explicit waiver.

### 11.2 Headline budgets (CE defaults)

| Operation | P50 | P95 | P99 |
| :---- | :---- | :---- | :---- |
| Web cold load (Time to Interactive, 4G) | ≤1.0 s | ≤1.8 s | ≤2.5 s |
| Web warm reload | ≤300 ms | ≤500 ms | ≤800 ms |
| Studio cold start (warm OS cache) | ≤1.5 s | ≤3.0 s | ≤5.0 s |
| Studio idle RAM (no draft open) | ≤120 MB | ≤180 MB | ≤220 MB |
| Daemon idle RAM | ≤25 MB | ≤30 MB | ≤45 MB |
| CLI invocation overhead | ≤30 ms | ≤80 ms | ≤150 ms |
| Local 7B quantized draft (≤500 input tokens) on M-series / RTX 30-series | ≤2.5 s | ≤4.0 s | ≤6.0 s |
| Cloud LLM draft (BAA provider) | ≤4.0 s | ≤8.0 s | ≤15.0 s |
| Deterministic validation pass | ≤120 ms | ≤300 ms | ≤500 ms |
| AI-assisted validation pass | ≤2.0 s | ≤4.0 s | ≤6.0 s |
| Export (PDF/DOCX/FHIR) | ≤500 ms | ≤1.5 s | ≤3.0 s |
| DICOMweb metadata fetch (single study) | ≤300 ms | ≤700 ms | ≤1.5 s |
| Audit event write | ≤80 ms | ≤200 ms | ≤500 ms |

### 11.3 Bundle and binary budgets

| Artifact | Budget |
| :---- | :---- |
| Web app initial JS bundle (gzipped) | ≤200 KB |
| Web app full route lazy-loaded | ≤450 KB |
| Studio installed size | ≤80 MB |
| CLI binary | ≤25 MB (stripped, no UPX in clinical) |
| Daemon binary | ≤35 MB |
| Mobile companion installed | ≤40 MB (iOS), ≤55 MB (Android) |
| Container image (server) | ≤180 MB compressed |
| Container image (slim/distroless) | ≤80 MB compressed |

### 11.4 Performance techniques used

* **Streaming everywhere** — SSE/WebSocket for AI output and validation; progressive draft hydration.
* **Speculative prefetch** — anticipate "Generate Impression" once findings stabilize.
* **Prompt-prefix caching** — daemon caches static prompt segments per rulebook to cut tokens billed by ~30 %.
* **Quantization** — GGUF Q4_K_M / Q5_K_M defaults for local models; INT8 ONNX for CPU; FP16 for GPU.
* **Workstation NPU offload** — Apple Neural Engine, Intel NPU, Qualcomm Hexagon, AMD XDNA when present.
* **Differential validation** — re-validate only changed sections, not full report.
* **Async-first I/O** — tokio in Rust, goroutines in Go, suspendable rendering in React.
* **HTTP/2 + HTTP/3 (QUIC)** for client↔control-plane.
* **Brotli compression** + `cache-immutable` content addressing.
* **CDN edge caching** for static assets; signed cookies for tenant-scoped assets.
* **WebSocket multiplexing** to avoid head-of-line blocking on draft autosave.
* **Background sync** — drafts and audit events buffer locally and flush in batches.
* **Memory pooling** in the daemon for inference tensors.
* **Battery awareness** on laptops — Studio drops to power-saver mode when on battery <30 %.

### 11.5 Stability principles

* **Crash-only design** — restarts are normal; state lives in WAL + snapshots.
* **Backpressure** — every queue has bounded depth and an explicit overload policy.
* **Circuit breakers** on every external dependency (provider, FHIR, HL7, DICOMweb).
* **Bulkheads** — per-tenant resource quotas to prevent noisy-neighbor failures.
* **Graceful degradation modes** explicit and tested (§27): drafting always works even if cloud is down.
* **Deterministic shutdown** — services flush, deregister, and exit ≤10 s on SIGTERM.
* **No silent retries** on PHI paths — operator visibility is mandatory.

---

## 12. Deployment Models

### 12.1 Five supported deployments

| Model | Target | Control plane | Data plane | AI |
| :---- | :---- | :---- | :---- | :---- |
| **Managed SaaS** | Imaging centers, small groups | Vendor cloud (multi-tenant) | Vendor cloud | Vendor-approved providers + BYOK |
| **Hybrid Enterprise** | Hospitals | Vendor cloud | Customer network (Studio + daemon + connectors) | Mixed: local + approved cloud |
| **Private Cloud / VPC** | Regulated health systems | Customer VPC | Customer VPC | Customer-chosen; no egress without policy |
| **On-Prem / Sovereign** | National health systems, defense | Customer data center | Customer data center | Local only or whitelisted endpoints |
| **Air-Gapped Edge** | Military, remote clinics, disaster response | Local k3s on a single workstation/NUC | Same node | Local quantized models only |

### 12.2 Air-gapped edge specifics (NEW)

* Single-binary `radiopad-edge` bundles control plane + data plane + local model + viewer.
* Boots from USB into a hardened OS image (NixOS or Talos Linux) optional.
* Manual update via signed offline bundles; SBOM included.
* Local CA bootstrapped per device; rotation via QR code.

### 12.3 Multi-region & high-availability (EE)

* Active-active across regions with **per-tenant residency pinning** (data sovereignty).
* Postgres logical replication + read replicas.
* NATS JetStream cluster spanning ≥3 AZs.
* Object storage with cross-region replication (CRR).
* Cloudflare/Fastly DNS failover with health-checked endpoints.
* Recovery: RTO 15 min, RPO 1 min for control plane; RTO 0 (local) for reporting.

---
## 13. Scope by Release

### 13.1 Release train

* **MVP-α (Alpha — internal)** — closed alpha at design-partner sites, no PHI, synthetic data only.
* **MVP-β (Beta — public)** — public CE beta, local inference only by default, de-identified or consented PHI at participating sites.
* **Clinical Beta** — limited production PHI with BAA-approved providers and clinical safety lite controls.
* **Enterprise GA** — full production, all editions, full integrations.
* **LTS (Long-Term Support)** — security and clinical-safety patches for 24 months from a designated LTS minor.

### 13.2 MVP-β scope — Reporting Copilot (Community Edition)

Goal: produce useful, safe, editable AI-assisted draft reports with full provenance and local-first deployment.

1. Tenant/user/RBAC with SSO/OIDC.
2. Web reporting workspace (full TipTap editor with section-aware schema).
3. Studio desktop alpha (Tauri) with global hotkeys and dictation.
4. Local daemon with policy engine.
5. CLI alpha (`login`, `daemon`, `rulebook validate`, `rulebook test`, `generate`).
6. Local-first dictation (whisper.cpp + VAD) and free-text input.
7. AI draft generation against pluggable provider (local-only default).
8. Prompt block library + 5 base rulebooks (Chest CT, Chest X-ray, Abdomen CT, MRI Brain, Mammography MG).
9. Template library seeded from RadReport (open subset) — 30+ templates.
10. Findings → Impression generator.
11. Impression cleanup and rewriting modes.
12. Deterministic validation engine v1 (laterality, required sections, negation conflict, modality mismatch).
13. AI-assisted validation v1 (unsupported claims, contradiction).
14. Provenance highlighting on AI-touched text.
15. Export: plain text, PDF, DOCX, JSON, FHIR `DiagnosticReport`.
16. Audit log v1 (event-sourced).
17. Admin panel for templates, prompts, rulebooks, providers.
18. Provider abstraction with at least 3 backends (llama.cpp local, Ollama, OpenAI BAA).
19. Usage metering + billing foundation (Stripe or Lago).
20. PWA offline shell for the web app.
21. Mobile PWA read-only worklist preview.
22. Telemetry (OTel) and Grafana starter dashboards.
23. SBOMs + signed releases.

### 13.3 Clinical Beta scope — Clinical Workflow Integrations

1. PACS/RIS worklist connector via DICOMweb (QIDO/WADO/UPS-RS).
2. HL7 v2 MLLP bidirectional channel (ADT, ORM, ORU).
3. FHIR R4 bulk export/import.
4. Advanced rulebook visual editor + RFC-style approval workflow.
5. Voice command mode ("RadioPad, generate impression", "next finding").
6. Prior report comparison with diff view.
7. Measurement extraction across studies (RECIST 1.1, Lugano, iRECIST scaffolding).
8. Critical-finding language enforcement + notification workflow.
9. Report quality score (composite metric per tenant rulebook).
10. Role-based approval workflows for rulebooks and prompts.
11. Studio stable + Linux build.
12. CLI: `audit export`, `rulebook diff`, `rulebook promote`, `templates sync`.
13. Subspecialty packs: Thoracic, Body, Neuro, MSK, Breast, Cardiac, Pediatric.
14. Peer-review queue with RADPEER-compatible scoring.
15. Teaching file generator.
16. Mobile native companion (iOS + Android) read-only with WebAuthn approvals.
17. Evidence/citation linker (PubMed, ACR Appropriateness Criteria where licensed).
18. Plugin SDK + first community plugins.
19. Federated learning client (opt-in, EE preview).

### 13.4 Enterprise GA scope

1. SSO/SAML 2.0 (in addition to OIDC), SCIM 2.0 provisioning.
2. Customer-managed encryption keys (BYOK + HYOK) via AWS KMS/Azure Key Vault/HashiCorp Vault.
3. Advanced audit search + SIEM connectors (Splunk, Sentinel, Elastic, Chronicle).
4. AI governance dashboard with model drift, bias, and PHI-routing monitors.
5. Model evaluation harness with golden-case packs.
6. Site-specific model routing with cost & latency telemetry.
7. Federated/on-prem deployment with offline license activation.
8. Multilingual reports (bilingual with parallel provenance).
9. Advanced analytics (reading patterns, error topology, TAT, RVU attribution).
10. Versioned clinical validation packs per subspecialty.
11. Legal/compliance exports (HIPAA, GDPR, EU AI Act, ISO 27001).
12. Rulebook & template marketplace with paid + free packs.
13. Centralized fleet management for Studio + daemon (MDM-like).
14. PCCP-ready model change-control workflow.
15. Confidential computing inference (Intel SGX / AMD SEV-SNP) for the most sensitive tenants.

### 13.5 Post-GA roadmap (high-level)

* Image-aware modules (key-image generation, measurement auto-extraction from DICOM) with their own regulatory pathway.
* Worklist optimizer (assignment by subspecialty, fatigue, RVU).
* Patient-friendly report generator (physician-approved, behind a separate guardrail).
* Tumor board prep assistant.
* RadElement-bound structured report API.
* Auto-anonymization service for research enclaves.
* Synthetic case generator for resident training.
* Reading-room ambient mode (multi-monitor, hands-free).

---

## 14. Functional Requirements

### 14.1 Authentication, Tenancy, and Access Control

| ID | Requirement | Priority | Edition |
| :---- | :---- | :---- | :---- |
| AUTH-001 | Support email/password, magic link, OIDC, SAML 2.0 for enterprise tenants | P0 | CE/EE |
| AUTH-002 | RBAC: Radiologist, Resident, Fellow, Subspecialist, Admin, Medical Director, Compliance Reviewer, IT Admin, Billing Admin, Researcher, Auditor (read-only) | P0 | CE |
| AUTH-003 | Tenant isolation at app, DB schema, storage prefix, cache namespace, audit log, queue subject | P0 | CE |
| AUTH-004 | MFA enforcement by tenant policy; WebAuthn/FIDO2 preferred, TOTP fallback | P0 | CE |
| AUTH-005 | SCIM 2.0 provisioning/deprovisioning | P1 | EE |
| AUTH-006 | Emergency account lockout and global session revocation in ≤30 s | P0 | CE |
| AUTH-007 | Device trust policies for Studio and daemon (signed device cert + posture check) | P1 | EE |
| AUTH-008 | Step-up auth for high-risk actions (rulebook promotion, billing changes, mass export) | P0 | CE |
| AUTH-009 | Session limits per role and idle timeouts configurable per tenant | P0 | CE |
| AUTH-010 | Break-glass access with mandatory justification, dual approval, and audit alert | P1 | EE |
| AUTH-011 | Service-to-service auth via short-lived JWTs (≤15 min) with mTLS at the gateway | P0 | CE |
| AUTH-012 | API key lifecycle: scoped, rotatable, revocable, hashed at rest | P0 | CE |
| AUTH-013 | IP allowlist per tenant and per role | P1 | EE |
| AUTH-014 | Geo-restrictions (block sign-ins from countries outside tenant policy) | P1 | EE |
| AUTH-015 | Impersonation by support requires dual approval and is fully audited | P0 | CE |

### 14.2 Reporting Workspace

| ID | Requirement | Priority | Edition |
| :---- | :---- | :---- | :---- |
| RPT-001 | Editor with sections: Indication, Technique, Comparison, Findings, Impression, Recommendations (configurable per template) | P0 | CE |
| RPT-002 | Tenant-specific section layouts with conditional sections by modality | P0 | CE |
| RPT-003 | Free text, template-based, structured fields, table input, numeric measurement input | P0 | CE |
| RPT-004 | "Generate Draft Report" from dictation + notes + measurements + study metadata | P0 | CE |
| RPT-005 | "Generate Impression" from Findings | P0 | CE |
| RPT-006 | "Rewrite in my style" using personal style memory (per-user, opt-in, encrypted) | P1 | CE |
| RPT-007 | Modes: concise, formal, patient-friendly, referring-physician summary, bilingual, structured | P1 | CE |
| RPT-008 | AI-generated text visibly marked (color + screen-reader-friendly annotation) until reviewed or edited | P0 | CE |
| RPT-009 | Side-by-side prior report comparison with semantic diff | P1 | CE |
| RPT-010 | One-click copy to RIS/PACS reporting system via secure clipboard or PACS bridge | P0 | CE |
| RPT-011 | Export to plain text, PDF/A-2u, DOCX, JSON, FHIR R4/R5 `DiagnosticReport`, HL7 v2 ORU, DICOM SR | P0 | CE |
| RPT-012 | Radiologist acknowledgement required before final export/signing | P0 | CE |
| RPT-013 | Autosave to encrypted local cache every 2 s with WAL-style durability | P0 | CE |
| RPT-014 | Per-report change history (full undo/redo across sections, with edit attribution) | P0 | CE |
| RPT-015 | Collaboration: real-time multi-user editing via CRDT (Yjs) | P1 | CE |
| RPT-016 | Comments and @-mentions tied to text ranges; resolvable | P1 | CE |
| RPT-017 | Per-section AI actions with section-scoped prompts | P0 | CE |
| RPT-018 | Command palette (⌘/Ctrl-K) with fuzzy actions and rulebook-aware suggestions | P0 | CE |
| RPT-019 | Dictation: continuous, push-to-talk, hands-free voice commands | P0 | CE |
| RPT-020 | Auto-formatting (bullet impression, sentence case, measurement normalization) | P0 | CE |
| RPT-021 | Macros / autotext (user, tenant, subspecialty scopes) | P0 | CE |
| RPT-022 | Snippet library with versioning | P1 | CE |
| RPT-023 | Citation insertion (linked to evidence module) | P1 | CE |
| RPT-024 | Keyboard-first navigation and full screen-reader support | P0 | CE |
| RPT-025 | Bilingual side-by-side rendering for tenants requiring it (e.g., AR + EN) | P1 | CE |
| RPT-026 | "Why this suggestion?" panel showing rulebook, prompt, model, and input excerpt | P0 | CE |
| RPT-027 | Print-optimized PDF with QR code linking back to the report's provenance hash (for audit) | P1 | EE |
| RPT-028 | "Send to peer review" action with anonymization toggle | P1 | CE |
| RPT-029 | Worklist sidebar with prioritization (STAT/urgent/routine/screening) | P0 | CE |
| RPT-030 | Multi-study reporting view (combined exam reads, e.g., CT chest+abdomen+pelvis) | P1 | CE |

### 14.3 NLP and AI Report Generation

| ID | Requirement | Priority | Edition |
| :---- | :---- | :---- | :---- |
| AI-001 | Convert raw dictation into clean structured report sections without altering clinical meaning | P0 | CE |
| AI-002 | Generate report drafts from structured inputs, free text, measurements, prior snippets | P0 | CE |
| AI-003 | Generate impression from findings preserving clinical content | P0 | CE |
| AI-004 | Detect contradictions between findings and impression | P0 | CE |
| AI-005 | Detect missing required sections based on rulebook | P0 | CE |
| AI-006 | Detect laterality conflicts, measurement mismatches, modality/body-part mismatch | P1 | CE |
| AI-007 | Detect uncertain, unsupported, or hallucinated claims (with confidence band, never displayed as clinical certainty) | P1 | CE |
| AI-008 | Suggest follow-up language using only tenant-approved phrasebooks | P1 | CE |
| AI-009 | Support system, specialty, user, and case-level prompts with layered overrides | P0 | CE |
| AI-010 | Model routing by tenant, user role, modality, PHI class, cost ceiling, latency budget | P0 | CE |
| AI-011 | Local model execution for de-identified or PHI-sensitive workflows | P1 | CE |
| AI-012 | Full traceability of prompt version, rulebook version, model id, provider, input hash, output hash, edits | P0 | CE |
| AI-013 | Streaming token output to UI with stop/regenerate controls | P0 | CE |
| AI-014 | Deterministic temperature ceiling for clinical outputs (default 0.2) overridable per rulebook | P0 | CE |
| AI-015 | Structured output enforcement (JSON Schema / FHIR shape) for downstream sections | P0 | CE |
| AI-016 | Output watermarking (cryptographic provenance, optional perturbation) | P1 | EE |
| AI-017 | Adversarial input detection (prompt-injection in clinical text — e.g., dictation containing "ignore previous instructions") | P0 | CE |
| AI-018 | Token budget enforcement per request and per user/day | P0 | CE |
| AI-019 | Multi-model ensemble for impression generation (majority + diff) — opt-in | P2 | EE |
| AI-020 | Local PII/PHI scrubbing pass before any cloud egress (Presidio + tenant rules) | P0 | CE |
| AI-021 | "Quote-it" mode: AI must cite the source sentence in findings for each impression bullet | P1 | CE |
| AI-022 | Disagreement detection between two providers when ensemble enabled | P2 | EE |
| AI-023 | Multilingual report generation with translation provenance | P1 | CE |
| AI-024 | Personal style memory (encrypted, per-user, exportable, deletable, opt-in) | P1 | CE |
| AI-025 | Speculative completion of structured fields based on study metadata | P1 | CE |
| AI-026 | "Explain like I'm the referring physician" mode | P1 | CE |
| AI-027 | Length and tone controls per tenant default | P0 | CE |
| AI-028 | Hallucination guardrail: every claim in impression must trace to a source span in findings or input context; unsupported claims are surfaced before export | P0 | CE |

### 14.4 Rulebooks and Prompt Engineering System

The rulebook system is the **single most important differentiator** for RadioPad. v2.0 expands it substantially.

#### 14.4.1 Anatomy of a rulebook (expanded)

```yaml
rulebook_id: chest_ct_v2
name: Chest CT Reporting Rulebook
version: 2.1.0
extends: chest_ct_base@1.4.0       # rulebook inheritance
owner: Thoracic Imaging Committee
status: approved                    # draft|review|approved|deprecated
applies_to:
  modalities: ["CT"]
  body_parts: ["Chest"]
  procedures: ["CT chest with contrast", "CT chest without contrast", "HRCT chest"]
  report_types: ["diagnostic", "follow_up", "screening"]
  age_ranges: ["adult"]
license: CC-BY-4.0
clinical_owners:
  - "Dr. Jane Doe, MD"
  - "Dr. John Smith, MD"

style:
  tone: "concise_clinical"
  impression_max_bullets: 5
  impression_min_bullets: 1
  preferred_terms:
    "consistent with": "compatible with"
  avoid_terms:
    - "unremarkable"
    - "cannot rule out"
    - "grossly normal"
  units:
    length: "mm"
    density: "HU"

required_sections:
  - Indication
  - Technique
  - Comparison
  - Findings
  - Impression

structured_fields:
  - id: lung_nodule
    type: array
    items:
      properties:
        size_mm: number
        location: string
        attenuation: enum [solid, part_solid, ground_glass]
        fleischner_recommendation: derived

rules:
  - id: laterality_consistency
    severity: blocker
    description: "Left/right findings must match impression."
    implementation: deterministic
  - id: measurement_consistency
    severity: warning
    description: "Nodule measurements must match across sections."
    implementation: deterministic
  - id: fleischner_followup
    severity: warning
    description: "Nodule >6 mm without recommendation flagged."
    implementation: hybrid
  - id: critical_result_language
    severity: blocker
    description: "Use approved critical finding language."
    implementation: deterministic
  - id: impression_supported_by_findings
    severity: blocker
    description: "Each impression bullet traces to a finding span."
    implementation: ai_assisted

prompt_blocks:
  system: |
    You are assisting a board-certified thoracic radiologist...
  findings_to_impression: |
    Generate a concise impression with at most 5 bullets...
  cleanup: |
    Improve grammar without changing clinical meaning...

output_schema:
  $ref: "./schemas/chest_ct_v2.schema.json"

evidence_links:
  - "Fleischner Society 2017 Pulmonary Nodule Guidelines"
  - "ACR Lung-RADS v2022"

test_cases:
  - id: golden_001
    input: ...
    must_contain: [...]
    must_not_contain: [...]
    must_pass_rules: [laterality_consistency, ...]

signed_by:
  - name: "Thoracic Imaging Committee"
    date: "2026-05-01"
    signature: "ed25519:..."
```

#### 14.4.2 Rulebook requirements

| ID | Requirement | Priority | Edition |
| :---- | :---- | :---- | :---- |
| RB-001 | Create, edit, clone, archive, version rulebooks | P0 | CE |
| RB-002 | YAML/JSON source editing AND visual editing | P1 | CE |
| RB-003 | Approval workflow: Draft → Review → Approved → Deprecated, with electronic signatures | P0 | CE |
| RB-004 | Test cases (golden cases) per rulebook with diff viewer | P0 | CE |
| RB-005 | Prompt blocks, output schemas, style rules, forbidden language, required sections, validation rules | P0 | CE |
| RB-006 | Modality- and subspecialty-specific rulebooks | P0 | CE |
| RB-007 | Tenant / department / user inheritance with explicit override semantics | P1 | CE |
| RB-008 | Rollback to prior approved version (single-command) | P0 | CE |
| RB-009 | Rulebook version captured in every AI event audit log | P0 | CE |
| RB-010 | Block unapproved rulebooks from production usage; sandbox mode is explicit | P0 | CE |
| RB-011 | Rulebook regression CI — promotion gated on golden-case pass rate ≥ threshold | P0 | CE |
| RB-012 | Cryptographic signing of approved rulebooks (Ed25519); daemon verifies signatures before activation | P1 | EE |
| RB-013 | Rulebook diffing (semantic) with reviewer comments | P1 | CE |
| RB-014 | Rulebook A/B testing in sandbox tenant (no PHI) | P2 | EE |
| RB-015 | Rulebook marketplace (publish/install/rate); curated badges for clinical-society endorsement | P2 | CE |
| RB-016 | Bilateral rulebook composition (compose by `extends` + override) | P1 | CE |
| RB-017 | Rulebook telemetry: which rules fire how often, which are most overridden | P1 | CE |
| RB-018 | Rulebook editor with inline lint (YAML schema, prompt token estimation, breaking-change warnings) | P1 | CE |
| RB-019 | Rulebook import/export as a single signed `.rpkg` bundle (zip + signed manifest) | P0 | CE |
| RB-020 | Per-rulebook PHI policy override (e.g., this rulebook may only use local models) | P0 | CE |
| RB-021 | Rulebook deprecation grace period (default 90 days) with usage warnings | P1 | CE |
| RB-022 | Cross-tenant share with selective masking (org-internal sharing) | P1 | EE |
| RB-023 | Conformance test runner ensures rulebooks comply with RadioPad rulebook spec | P0 | CE |

### 14.5 Template Management

RadioPad ships with **80+ open templates** seeded from public RadReport templates plus community contributions, organized by modality, body part, subspecialty, procedure, and report type.

| ID | Requirement | Priority | Edition |
| :---- | :---- | :---- | :---- |
| TMP-001 | Template library by modality, anatomy, subspecialty, procedure, report type | P0 | CE |
| TMP-002 | Structured fields, optional fields, required fields, conditional sections | P0 | CE |
| TMP-003 | Variants: normal, abnormal, follow-up, screening, urgent, post-procedure | P1 | CE |
| TMP-004 | RadLex / RadElement / LOINC / SNOMED CT bindings where licensed | P1 | CE |
| TMP-005 | Tenant-specific template approval workflow | P0 | CE |
| TMP-006 | Template usage analytics (per template, per radiologist, per modality) | P1 | CE |
| TMP-007 | Import/export as JSON/YAML/.rpkg; round-trip with RadReport MRRT | P0 | CE |
| TMP-008 | Live preview before publishing with sample data | P0 | CE |
| TMP-009 | Template versioning + diff + rollback | P0 | CE |
| TMP-010 | Template marketplace + curated society packs | P1 | CE |
| TMP-011 | Auto-suggest template from study metadata + indication | P0 | CE |
| TMP-012 | Conditional rendering based on patient/study attributes (e.g., contrast administered) | P1 | CE |
| TMP-013 | Localized template strings (multilingual content) | P1 | CE |
| TMP-014 | Bound rulebook(s) — template can require a specific rulebook to be active | P0 | CE |
| TMP-015 | Template cloning across tenants (with attribution) | P1 | CE |
| TMP-016 | "Auto-normalize" mode for inconsistent legacy templates on import | P1 | CE |
| TMP-017 | Per-section editor toolbar customization | P2 | CE |

### 14.6 Standards and Terminology

| ID | Requirement | Priority | Edition |
| :---- | :---- | :---- | :---- |
| STD-001 | Map findings and procedure names to RadLex when available | P1 | CE |
| STD-002 | ACR RADS rule modules: BI-RADS, LI-RADS, PI-RADS, Lung-RADS, TI-RADS, O-RADS, NI-RADS, C-RADS (subject to licensing) | P1 | CE base; EE expanded |
| STD-003 | FHIR R4 + R5 `DiagnosticReport` export and ingest | P0 | CE |
| STD-004 | DICOMweb (QIDO/WADO/STOW/UPS-RS) | P1 | CE |
| STD-005 | DICOM SR export for measurement-bearing reports (TID 1500, TID 4019) | P1 | CE |
| STD-006 | LOINC procedure and observation codes | P1 | CE |
| STD-007 | SNOMED CT clinical concept binding (license-aware) | P1 | CE |
| STD-008 | ICD-10-CM / ICD-11 / CPT for billing pipelines | P1 | CE |
| STD-009 | Terminology dictionary management (per tenant) | P1 | CE |
| STD-010 | Institution-specific lexicons, abbreviations, autotext | P0 | CE |
| STD-011 | IHE XDS-I.b, SWF.b, AIR profiles for cross-enterprise sharing | P2 | EE |
| STD-012 | IHE AIW-I (AI Workflow for Imaging) profile | P2 | EE |
| STD-013 | RadElement CDE binding for structured sections | P1 | CE |
| STD-014 | HL7 v2 ORU export over MLLP/TLS | P1 | EE |
| STD-015 | Bulk FHIR export ($export) per tenant | P1 | EE |
| STD-016 | openEHR archetype export for participating sites | P2 | EE |
| STD-017 | Terminology server federation (IT-Snomed, Ontoserver, Termonaut) | P2 | EE |
| STD-018 | Terminology change-log tracking and version pinning per release | P1 | CE |

---
### 14.7 Desktop App (RadioPad Studio)

Studio is the **reporting-room hero surface**. It must feel native, instant, and unobtrusive.

| ID | Requirement | Priority | Edition |
| :---- | :---- | :---- | :---- |
| DESK-001 | Windows (x86_64 + ARM64), macOS (Intel + Apple Silicon), Linux (x86_64 + ARM64) | P0 | CE |
| DESK-002 | Auto-start and manage `radiopadd` local daemon | P0 | CE |
| DESK-003 | Global hotkeys for dictation, generate impression, rewrite, copy, paste, voice command toggle | P0 | CE |
| DESK-004 | Secure clipboard mode with automatic timeout (configurable, default 30 s) | P1 | CE |
| DESK-005 | Local encrypted cache (SQLCipher) for drafts and temporary inputs | P0 | CE |
| DESK-006 | Offline draft editing with full conflict resolution on reconnect | P1 | CE |
| DESK-007 | Local PACS/RIS bridge plugin host | P1 | CE |
| DESK-008 | Device authorization and tenant pairing (QR code or device-flow OAuth) | P0 | CE |
| DESK-009 | Local model/plugin execution where enabled | P1 | CE |
| DESK-010 | Local logs with PHI redaction (Presidio + tenant rules) and rotation | P0 | CE |
| DESK-011 | Multi-monitor support; floating mini overlay above third-party reporting tools | P1 | CE |
| DESK-012 | Battery-aware power-saver mode | P1 | CE |
| DESK-013 | OS-native notifications (critical results, peer-review assignments) | P1 | CE |
| DESK-014 | Tray/menu bar daemon control + provider switcher | P0 | CE |
| DESK-015 | Auto-update with EdDSA-signed bundles and rollback if first boot fails | P0 | CE |
| DESK-016 | Code-signed installers; macOS notarized; Windows EV cert | P0 | CE |
| DESK-017 | Crash reporter (opt-in, PHI-stripped) — Sentry self-hosted or Bugsnag-OSS | P1 | CE |
| DESK-018 | Accessibility: NVDA, JAWS, VoiceOver, Orca compatibility | P0 | CE |
| DESK-019 | Wacom / pen / touch input for finding annotations and structured drawing | P2 | CE |
| DESK-020 | Foot-pedal support (USB HID) for dictation start/stop and jog | P1 | CE |
| DESK-021 | Multi-language UI with hot reload of locale | P1 | CE |
| DESK-022 | Per-user color themes and high-contrast modes | P1 | CE |
| DESK-023 | Local-first plugin sandbox (WASM, capability-limited) | P1 | CE |
| DESK-024 | "Quick mini panel" hotkey opens a 380×600 overlay for dictation + impression without leaving PACS | P1 | CE |
| DESK-025 | Centralized fleet management hook (MDM, Intune, Jamf, Ansible) | P2 | EE |

### 14.8 CLI and Local Daemon

The CLI/daemon is the **power-user and enterprise-automation surface**.

#### 14.8.1 CLI requirements

| ID | Requirement | Priority | Edition |
| :---- | :---- | :---- | :---- |
| CLI-001 | `radiopad login` via OAuth Device Authorization Grant or browser-based PKCE | P0 | CE |
| CLI-002 | `radiopad daemon start|stop|status|logs|restart` | P0 | CE |
| CLI-003 | `radiopad generate` for report generation from local input files (text/JSON/FHIR) | P1 | CE |
| CLI-004 | `radiopad validate` for rulebook and report validation | P0 | CE |
| CLI-005 | `radiopad rulebook test` for regression testing prompt/rulebook changes against golden cases | P0 | CE |
| CLI-006 | `radiopad templates import|export|list|diff` | P1 | CE |
| CLI-007 | `radiopad ai providers list|test|set-default` for provider adapters and policy | P1 | CE |
| CLI-008 | Enforce tenant model policies locally before any request leaves the machine | P0 | CE |
| CLI-009 | `radiopad audit export` event sync to control plane (or to local file) | P0 | CE |
| CLI-010 | Headless mode for enterprise deployment with config file | P1 | EE |
| CLI-011 | `radiopad eval` model evaluation harness against golden cases (metrics: edit-distance, rule pass-rate, hallucination flags) | P1 | EE |
| CLI-012 | `radiopad fhir import|export` bulk FHIR operations | P1 | EE |
| CLI-013 | `radiopad doctor` system diagnostic (network, model availability, daemon health, latency probes) | P0 | CE |
| CLI-014 | `radiopad model pull|list|prune` for managing local models (GGUF/ONNX) | P1 | CE |
| CLI-015 | Shell completions: bash, zsh, fish, PowerShell | P0 | CE |
| CLI-016 | JSON output mode for every command for piping | P0 | CE |
| CLI-017 | Plugin install: `radiopad plugins install <name>` from registry or local path | P1 | CE |
| CLI-018 | `radiopad pacs query|fetch` for testing DICOMweb against a node | P1 | CE |
| CLI-019 | `radiopad benchmark` for local performance regressions on a workstation | P2 | CE |
| CLI-020 | `radiopad migrate` for schema migrations on self-hosted deployments | P0 | CE |

#### 14.8.2 Example CLI sessions

```bash
# First-time setup on a workstation
radiopad login --tenant acme-radiology
radiopad doctor                                  # checks system, models, daemon, network
radiopad model pull llama3-8b-q4_K_M             # ~5 GB
radiopad daemon start
radiopad ai providers set-default --provider local-llama3

# Rulebook authoring workflow
radiopad rulebook lint chest_ct_v2.yaml
radiopad rulebook test chest_ct_v2.yaml --cases ./golden-cases --report html
radiopad rulebook diff chest_ct_v1.yaml chest_ct_v2.yaml
radiopad rulebook promote chest_ct_v2.yaml --tenant acme-radiology --signed-by jdoe

# Daily ops
radiopad generate --template chest-ct \
                  --input findings.txt \
                  --mode draft \
                  --output report.fhir.json

# Compliance ops
radiopad audit export --from 2026-05-01 --to 2026-05-31 \
                      --tenant acme-radiology \
                      --format ndjson \
                      --redact-phi
```

#### 14.8.3 Daemon (`radiopadd`) requirements

| ID | Requirement | Priority | Edition |
| :---- | :---- | :---- | :---- |
| DMN-001 | Rust, statically-linked, ≤35 MB binary, ≤30 MB RSS at idle | P0 | CE |
| DMN-002 | UNIX socket (Linux/macOS), named pipe (Windows), or local TCP+mTLS for client RPC | P0 | CE |
| DMN-003 | gRPC API with reflection enabled; OpenAPI proxy for browser clients | P0 | CE |
| DMN-004 | Provider router: policy engine evaluates per-request before any external call | P0 | CE |
| DMN-005 | Embeds llama.cpp + ONNX Runtime; auto-selects best backend (CPU/CUDA/Metal/ROCm/Vulkan/DirectML) | P0 | CE |
| DMN-006 | Secure secret vault using OS keychain (macOS Keychain, Windows DPAPI/CNG, Linux Secret Service / kwallet); fallback to age-encrypted file | P0 | CE |
| DMN-007 | Encrypted local cache (SQLCipher) with WAL mode and tenant-keyed encryption | P0 | CE |
| DMN-008 | Plugin host (WASM with WASI preview 2) for connectors and tools, capability-limited | P1 | CE |
| DMN-009 | Audit buffer: durable local queue (SQLite WAL) survives crashes; flushes to control plane when reachable | P0 | CE |
| DMN-010 | Resource quotas: max RAM, max GPU memory, max parallel requests per provider, configurable per tenant | P0 | CE |
| DMN-011 | Self-update channel separate from Studio (signed, EdDSA) | P0 | CE |
| DMN-012 | Health endpoint, ready endpoint, metrics endpoint (Prometheus) | P0 | CE |
| DMN-013 | Hot reload of policy and provider config without restart | P1 | CE |
| DMN-014 | Auto-restart on crash with exponential backoff; max 5 restarts in 5 minutes before alerting | P0 | CE |
| DMN-015 | Crash dumps are PHI-stripped before any optional upload | P0 | CE |
| DMN-016 | Confidential computing mode (SGX/SEV) for inference tenants requiring it | P2 | EE |

### 14.9 AI Provider Abstraction & Subscription Module

| ID | Requirement | Priority | Edition |
| :---- | :---- | :---- | :---- |
| PROV-001 | Tenant-level model/provider registry | P0 | CE |
| PROV-002 | Mark providers as PHI-approved, de-identified-only, or blocked | P0 | CE |
| PROV-003 | Per-provider cost, latency, token, availability telemetry | P1 | CE |
| PROV-004 | Fallback routing only between providers of equal-or-higher compliance class | P0 | CE |
| PROV-005 | Model comparison in sandbox evaluation mode | P1 | EE |
| PROV-006 | API key vaulting and rotation | P0 | CE |
| PROV-007 | OAuth token storage only in encrypted local/tenant vaults | P0 | CE |
| PROV-008 | Provider policy enforcement before inference (hard fail on violation) | P0 | CE |
| PROV-009 | Data retention labeling per provider and endpoint | P0 | CE |
| PROV-010 | Block PHI to providers without approved compliance configuration | P0 | CE |
| PROV-011 | First-class provider adapters: OpenAI, Azure OpenAI, Anthropic, Google Vertex, AWS Bedrock, Cohere, Mistral La Plateforme, Groq, Together, Fireworks, Replicate, plus all local OSS backends (llama.cpp, Ollama, vLLM, LocalAI, TGI, Tabby) | P1 | CE |
| PROV-012 | Provider conformance test suite (any provider passing the suite is RadioPad-compatible) | P1 | CE |
| PROV-013 | Cost ceiling per tenant per day with hard cutoff; warning at 80 % | P0 | CE |
| PROV-014 | Latency budget routing (pick fastest compliant provider under N ms) | P1 | CE |
| PROV-015 | "Cheapest-compliant" routing mode for non-clinical drafts | P2 | CE |
| PROV-016 | Provider compliance matrix view for admins (BAA status, DPA status, ZDR endpoints, training-on-data status) | P0 | CE |
| PROV-017 | Provider drift watch — daily probe with golden cases to detect model regressions | P1 | EE |
| PROV-018 | Per-modality default provider override (e.g., mammo uses on-prem Llama-3 only) | P1 | CE |
| PROV-019 | Bring-your-own-OAuth-app for subscription-backed providers (non-PHI only by default) | P2 | CE |
| PROV-020 | Confidential-computing inference attestation surfaced to admins | P2 | EE |

### 14.10 MCP / Tool Integration Layer

| ID | Requirement | Priority | Edition |
| :---- | :---- | :---- | :---- |
| MCP-001 | Tool registry for approved internal tools (MCP-style) | P1 | CE |
| MCP-002 | Each tool requires explicit admin approval per tenant | P0 | CE |
| MCP-003 | Least-privilege tool scopes (read-only, write to specific endpoints, etc.) | P0 | CE |
| MCP-004 | Every tool call logged with user, study/patient context, tool, input hash, output hash, timestamp | P0 | CE |
| MCP-005 | Shell/file/network tools blocked by default in clinical production | P0 | CE |
| MCP-006 | Sandboxed execution for tool tests (WASM with WASI capability allowlist) | P1 | CE |
| MCP-007 | Allowlist-based connectors for PACS/RIS/EHR (no wildcard egress) | P1 | CE |
| MCP-008 | Tool conformance test suite (third parties can certify) | P1 | CE |
| MCP-009 | Tool usage quotas (per user, per tenant) | P1 | CE |
| MCP-010 | Tool capability registration is signed; daemon refuses to load unsigned tools in production tenants | P1 | EE |
| MCP-011 | Built-in safe tools: PubMed search, ACR Appropriateness Criteria lookup, ICD/CPT lookup, Fleischner/Lung-RADS calculator, RECIST measurement helper | P1 | CE |
| MCP-012 | Prompt-injection-resistant tool prompts and output validation | P0 | CE |

### 14.11 Subspecialty Modules (NEW)

Each subspecialty module is a versioned bundle of rulebooks, templates, terminology bindings, validation checks, evidence links, and golden-case packs.

| ID | Module | MVP | Beta | GA |
| :---- | :---- | :---- | :---- | :---- |
| SUB-CH | **Thoracic / Chest** — chest CT, X-ray, HRCT, Lung-RADS, Fleischner, PE protocols | ✅ | ✅ | ✅ |
| SUB-AB | **Abdomen / Body** — abdomen CT/MR, LI-RADS (HCC), O-RADS, MRCP, enterography | ✅ | ✅ | ✅ |
| SUB-NE | **Neuro** — brain CT/MR, stroke (ASPECTS), NI-RADS, MS lesion tracking, dementia atrophy | ✅ | ✅ | ✅ |
| SUB-MS | **Musculoskeletal** — joint MR, trauma X-ray, BLOKS/WORMS for OA, bone tumor staging | — | ✅ | ✅ |
| SUB-BR | **Breast** — mammography, US, MR; BI-RADS v5 | ✅ | ✅ | ✅ |
| SUB-CV | **Cardiac** — cardiac CT/MR, coronary CT (CAD-RADS), CHD reporting | — | ✅ | ✅ |
| SUB-PD | **Pediatric** — age-adjusted normals, dose-aware language, bone age, intussusception | — | ✅ | ✅ |
| SUB-IR | **Interventional / Procedure** — procedure notes, fluoro/CT-guided biopsies, vascular | — | ✅ | ✅ |
| SUB-NM | **Nuclear / Molecular** — PET-CT (Deauville, PERCIST), bone scan, MIBG | — | — | ✅ |
| SUB-ER | **Emergency** — STAT protocol templates, critical-result language emphasis | ✅ | ✅ | ✅ |
| SUB-OB | **Obstetric / Gynecology** — fetal US, anatomy survey, O-RADS US | — | ✅ | ✅ |
| SUB-US | **General Ultrasound** — abdominal, thyroid (TI-RADS), DVT, FAST | ✅ | ✅ | ✅ |
| SUB-DT | **Dental / Maxillofacial** — CBCT, ortho cephalometry | — | — | ✅ |
| SUB-VS | **Vascular** — CTA/MRA, aneurysm follow-up, peripheral arterial | — | ✅ | ✅ |
| SUB-HN | **Head & Neck** — nasopharyngeal, oral cavity, thyroid (TI-RADS) | — | ✅ | ✅ |

Each module provides:

* **Subspecialty rulebooks** (≥5 per module).
* **Subspecialty templates** (≥10 per module).
* **Terminology bindings** (RadLex sublexicon, RadElement CDEs).
* **Society guideline links** (ACR, RSNA, ESR, society endorsements).
* **Golden cases** (≥50 per module).
* **Subspecialty validation checks** (e.g., LI-RADS category derivation rules).

### 14.12 Structured Reporting & RADS Engine (NEW)

| ID | Requirement | Priority | Edition |
| :---- | :---- | :---- | :---- |
| RADS-001 | Auto-derive RADS category from structured fields where possible (BI-RADS 0–6, LI-RADS LR-1..LR-M, PI-RADS 1–5, Lung-RADS 1–4X, TI-RADS 1–5, O-RADS US/MRI, NI-RADS, CAD-RADS) | P1 | CE |
| RADS-002 | Forbid manually-entered category that contradicts structured fields | P1 | CE |
| RADS-003 | Embed RADS explanation snippet (society-approved wording) | P1 | CE |
| RADS-004 | Calculate Fleischner Society follow-up recommendation for lung nodules | P1 | CE |
| RADS-005 | RECIST 1.1 / iRECIST / mRECIST / Lugano / Cheson lesion tracker across studies | P2 | EE |
| RADS-006 | Structured key images and key-finding placeholders (text-only in MVP; image refs in beta) | P1 | CE |
| RADS-007 | Bidirectional sync of structured fields ↔ narrative text | P1 | CE |
| RADS-008 | RADS analytics: distribution per radiologist, category churn between draft/final, inter-reader variance | P2 | EE |
| RADS-009 | TIRADS, O-RADS-US/MRI scoring forms | P1 | CE |
| RADS-010 | C-RADS scoring for CT colonography | P2 | CE |
| RADS-011 | NIRADS for treated head & neck cancer | P2 | EE |
| RADS-012 | "Why this category?" justification panel | P1 | CE |

### 14.13 Peer Review & Quality Module (NEW)

Aligned with ACR RADPEER patterns but not a clinical decision system.

| ID | Requirement | Priority | Edition |
| :---- | :---- | :---- | :---- |
| PR-001 | Random and rule-based selection of cases for peer review | P1 | CE |
| PR-002 | Anonymized assignment to peer reviewer | P1 | CE |
| PR-003 | Reviewer scoring (concordance, minor discrepancy, major discrepancy) with structured rationale | P1 | CE |
| PR-004 | Learning loop: discrepancies inform rulebook improvement suggestions | P2 | EE |
| PR-005 | Quality dashboard for medical director | P1 | EE |
| PR-006 | "Second-read" workflow for STAT/high-risk findings | P2 | EE |
| PR-007 | Mobile review approvals for time-sensitive cases | P1 | CE |
| PR-008 | Educational mode for residents (attending feedback, structured comments, scoring of resident drafts) | P1 | CE |
| PR-009 | Discrepancy analytics by subspecialty, modality, reader, and rulebook | P2 | EE |
| PR-010 | Export-only mode for external peer-review services | P2 | EE |

### 14.14 Teaching File & Education Module (NEW)

| ID | Requirement | Priority | Edition |
| :---- | :---- | :---- | :---- |
| TF-001 | One-click "Add to teaching file" with mandatory de-identification | P1 | CE |
| TF-002 | Auto-anonymization of report text, dates, and study identifiers | P1 | CE |
| TF-003 | DICOM image attachment with pixel-level de-identification (when image module is enabled) | P2 | EE |
| TF-004 | Case categorization by modality, diagnosis, RADS, teaching pearls | P1 | CE |
| TF-005 | Quiz / question generator from teaching file metadata | P2 | CE |
| TF-006 | Export bundle compatible with MIRC-style teaching file formats | P2 | CE |
| TF-007 | Per-tenant private teaching library; opt-in public sharing with attribution | P1 | CE |
| TF-008 | Resident learning analytics (cases viewed, quiz performance) | P2 | EE |

### 14.15 Critical Results Workflow (NEW)

Aligned with Joint Commission expectations and ACR Practice Parameters.

| ID | Requirement | Priority | Edition |
| :---- | :---- | :---- | :---- |
| CR-001 | Critical-result language detector (rule + AI ensemble) | P0 | CE |
| CR-002 | Tenant-defined criticality classes (red/orange/yellow) with required communication SLAs | P0 | CE |
| CR-003 | Communication workflow: who was notified, when, by what means, acknowledgement timestamp | P0 | CE |
| CR-004 | Auto-page / SMS / app notification to ordering physician (via integration) | P1 | EE |
| CR-005 | Mandatory text block insertion for confirmed critical findings | P0 | CE |
| CR-006 | Audit-grade record of every critical-result event | P0 | CE |
| CR-007 | Mobile companion acknowledgement with WebAuthn | P1 | CE |
| CR-008 | Configurable callback / read-back script | P1 | EE |
| CR-009 | Escalation policy if no acknowledgement within N minutes | P1 | EE |
| CR-010 | Compliance reports for The Joint Commission / external accreditation | P1 | EE |

### 14.16 Worklist & Workflow Optimization (NEW)

| ID | Requirement | Priority | Edition |
| :---- | :---- | :---- | :---- |
| WL-001 | Read-only worklist view ingesting DICOMweb UPS-RS or HL7 ORM | P1 | CE |
| WL-002 | Filtering by modality, body part, priority, age, prior status, subspecialty | P1 | CE |
| WL-003 | "Smart assign" suggestion (subspecialty match, fatigue, fairness) — informational only | P2 | EE |
| WL-004 | Reader load and fatigue heuristics (hours read, breaks suggested) | P2 | EE |
| WL-005 | RVU and procedure-mix analytics | P2 | EE |
| WL-006 | Prior-study auto-linker (HL7 + DICOM patient ID + accession-based) | P1 | CE |
| WL-007 | Hanging-protocol hooks for major viewers (OHIF, Weasis, Horos, Radiant, plus PACS APIs where available) | P2 | EE |
| WL-008 | Pause/resume reads with state preserved across sessions and devices | P1 | CE |
| WL-009 | Multi-tenant worklist switcher for teleradiologists | P1 | CE |
| WL-010 | Configurable timeouts and idle-state warnings | P1 | CE |

### 14.17 Comparison & Longitudinal Tracking (NEW)

| ID | Requirement | Priority | Edition |
| :---- | :---- | :---- | :---- |
| COMP-001 | Auto-link prior reports for the same patient and similar modality | P1 | CE |
| COMP-002 | Semantic diff of prior vs current findings | P1 | CE |
| COMP-003 | RECIST 1.1 / iRECIST / Lugano / mRECIST / PERCIST / Cheson tracker | P2 | EE |
| COMP-004 | Lesion table across studies with date, size, location, response classification | P2 | EE |
| COMP-005 | Auto-suggest "stable / increased / decreased" language with thresholds | P1 | CE |
| COMP-006 | Visualization: lesion trend chart per patient | P2 | EE |
| COMP-007 | Cross-modality comparison (e.g., CT lesion correlated to MR finding) | P3 | EE |

### 14.18 Evidence & Citation Module (NEW)

| ID | Requirement | Priority | Edition |
| :---- | :---- | :---- | :---- |
| EV-001 | One-click PubMed lookup with abstract preview | P1 | CE |
| EV-002 | ACR Appropriateness Criteria search (where licensed) | P1 | EE |
| EV-003 | Society guideline library (Fleischner, RSNA, ACR, ESR, ESUR, JBR, etc.) | P1 | CE |
| EV-004 | Citation insertion as footnote-style references in report | P2 | CE |
| EV-005 | Per-tenant evidence library and rules ("only cite within these journals") | P2 | EE |
| EV-006 | Provenance trail: which evidence informed which suggestion | P2 | EE |

### 14.19 Mobile Companion (NEW)

iOS and Android apps via Capacitor; PWA fallback.

| ID | Requirement | Priority | Edition |
| :---- | :---- | :---- | :---- |
| MOB-001 | Read-only worklist with filters | P1 | CE |
| MOB-002 | Draft report viewer (no editing in MVP for safety) | P1 | CE |
| MOB-003 | Critical-results notifications with WebAuthn acknowledgement | P1 | CE |
| MOB-004 | Peer-review assignments and quick scoring | P1 | CE |
| MOB-005 | Approval flows: rulebook promotion, template publishing (step-up auth) | P1 | EE |
| MOB-006 | Biometric unlock; no PHI cached locally beyond session unless explicitly enabled | P0 | CE |
| MOB-007 | Offline read with encrypted cache (size-limited) — opt-in per tenant | P2 | CE |
| MOB-008 | Push notifications via APNs (iOS) and FCM (Android); EE may use self-hosted relay | P1 | CE |
| MOB-009 | Screenshot protection (FLAG_SECURE / iOS guidance) | P0 | CE |
| MOB-010 | Quick voice memo to attach to a study (transcribed locally) | P2 | CE |

### 14.20 Plugin & Extension Marketplace (NEW)

| ID | Requirement | Priority | Edition |
| :---- | :---- | :---- | :---- |
| PL-001 | Plugin SDK (Rust + WASM ABI; TypeScript shim for connectors) | P1 | CE |
| PL-002 | Plugin manifest with capabilities, permissions, signing, semver | P1 | CE |
| PL-003 | Marketplace listing with reviews, downloads, security scan status, license, SBOM | P2 | CE |
| PL-004 | Plugin certification track (clinical / connector / utility tiers) | P2 | CE/EE |
| PL-005 | Per-tenant plugin allowlist and pinning | P1 | EE |
| PL-006 | Plugin sandbox (WASI capability-limited) with CPU/memory/time quotas | P1 | CE |
| PL-007 | Hot install/uninstall without daemon restart | P2 | CE |
| PL-008 | Revenue-share model for paid plugins (post-GA) | P3 | EE |

---
## 15. Core Workflows

### 15.1 Draft a report from dictation (happy path)

1. Radiologist opens a study (from worklist, PACS context, or manual).
2. Studio/web requests study metadata via DICOMweb QIDO (modality, body part, indication, accession, comparison study IDs).
3. Resolver attaches the most-recent relevant prior report (if any).
4. Radiologist dictates rough findings — whisper.cpp transcribes locally with VAD.
5. **Pre-flight validation**: deterministic checks confirm modality/body-part match the active template/rulebook.
6. **PHI scrubbing**: if a non-local provider is selected, Presidio pass produces a de-identified payload; mapping is held only in the daemon's encrypted cache.
7. **AI gateway** routes the request based on tenant policy (modality, PHI class, cost ceiling, latency budget) to the lowest-compliance-class allowed provider that meets the budget.
8. AI cleans dictation into structured report sections (streamed back to the editor).
9. **Rulebook engine** validates required sections, language style, and forbidden terms.
10. AI generates impression bullets (requires source-span citation from findings).
11. **Safety engine** runs:
    * Laterality consistency
    * Measurement consistency
    * Negation conflict
    * Modality/anatomy mismatch
    * Unsupported claim detection
    * Critical-finding language check
12. Radiologist reviews, edits; AI text un-highlights as it's touched/confirmed.
13. Optional peer review tag or teaching-file flag.
14. Radiologist confirms — final report is exported to RIS/PACS as FHIR `DiagnosticReport` (or HL7 v2 ORU) and a signed PDF/A copy is archived.
15. Audit events fire to the immutable log; daemon buffer flushes within next reachable window.

### 15.2 Findings → Impression

1. Radiologist writes or pastes findings.
2. User invokes "Generate Impression" (button or hotkey).
3. System resolves active rulebook (template-bound, with user/department/tenant inheritance).
4. AI gateway selects compliant provider; prompt block + style rules applied.
5. AI emits structured impression with mandatory `source_spans` linking each bullet to one or more findings spans.
6. Validation engine confirms every bullet has at least one source span; unsupported bullets are flagged.
7. Style enforcer rewrites if avoidance terms present.
8. Radiologist accepts / edits / rejects per bullet.

### 15.3 Rulebook governance lifecycle

1. Admin or medical director creates draft rulebook (extends a base or starts blank).
2. Adds prompt blocks, required sections, style, validation rules, structured fields, and golden cases.
3. Local CLI lint + test: `radiopad rulebook lint` then `radiopad rulebook test`.
4. CI pipeline runs full regression against the tenant's golden-case pack.
5. Medical director reviews diff (semantic, not just text) and example outputs in sandbox tenant.
6. Two-signer approval (configurable) signs the rulebook with Ed25519.
7. Rulebook is published; daemons verify signature before activation.
8. Telemetry watches for: rule fire frequency, override frequency, validation failure rate.
9. Drift alert fires if any rule fires >2σ outside its 30-day baseline.
10. Deprecation path: 90-day notice, then archived; existing reports keep their pinned version.

### 15.4 Desktop + PACS workflow

1. Radiologist uses PACS normally.
2. Studio detects active study via OS automation (window title, accession scrape) or via PACS plugin.
3. Hotkey (e.g., F12) opens a 380×600 mini-overlay above PACS.
4. Radiologist dictates or pastes findings.
5. Daemon routes per policy.
6. Output streams back into mini-overlay.
7. Secure-clipboard paste into PACS reporting field, or auto-insert via PACS bridge plugin.
8. Audit fires.

### 15.5 CLI evaluation workflow (for rulebook authors)

```bash
# Build / pull golden cases
radiopad cases pull --pack chest-ct-base
# Add tenant cases
radiopad cases import ./tenant-cases/

# Run regression against candidate rulebook
radiopad rulebook test chest_ct_v2.yaml \
                      --cases ./golden-cases \
                      --providers local-llama3,openai-gpt-4o-baa \
                      --report report.html \
                      --json results.json

# Inspect failures
radiopad rulebook test --explain results.json

# Promote (signed)
radiopad rulebook promote chest_ct_v2.yaml \
                          --tenant acme-radiology \
                          --signed-by jdoe@acme \
                          --co-signed-by med-director@acme
```

### 15.6 Air-gapped workflow

1. RadioPad-edge installed from signed offline bundle.
2. Local model and template packs preloaded.
3. Studies are reported locally; reports archived in local Postgres + object store.
4. Manual export via signed media on a defined cadence; nothing leaves the air gap automatically.

### 15.7 Federated learning (opt-in, EE preview)

1. Tenants opt in per-modality per-task.
2. Training is **never on raw text** — only on de-identified structured signals (e.g., rule overrides, edit-distance, RADS distributions).
3. Federated rounds coordinated through EE federation server; per-round local DP-SGD with privacy budget published.
4. New model candidates flow through model-evaluation harness before any promotion.
5. Tenants can opt out at any round.

---

## 16. AI Safety and Quality Requirements

### 16.1 Validation Engine (deterministic + AI-assisted)

| Check | Implementation | Severity default | Notes |
| :---- | :---- | :---- | :---- |
| Laterality consistency | Deterministic (NER + rule) | Blocker | Cross-section + impression |
| Measurement consistency | Deterministic (regex + unit normalization) | Warning | All sections + structured fields |
| Required-section check | Deterministic (schema) | Blocker | Per template/rulebook |
| Unsupported-impression | AI-assisted + source-span | Blocker | Impression must trace to findings |
| Critical-finding language | Deterministic (lexicon) + AI | Blocker | Mandatory tenant phrasebook |
| Follow-up recommendation | Deterministic (allowlist) | Warning | Only tenant-approved phrases |
| Negation conflict | Deterministic (NegEx/ConText) | Blocker | "No PE" vs "small PE" |
| Modality mismatch | Deterministic (template + study meta) | Warning | CT language in MR report |
| Anatomy mismatch | Deterministic (anatomy NER vs body_part) | Warning | Wrong body part wording |
| Hallucination risk | AI-assisted + provenance | Warning | Claims not in input context |
| Ambiguity detection | AI-assisted | Info | "Could represent" without follow-up |
| Sex/age-appropriate language | Deterministic | Warning | Pediatric language in adult report |
| Allergy / contrast safety | Deterministic | Warning | Contrast mentioned with allergy flag |
| Dose language (CT, IR) | Deterministic | Info | DLP / CTDIvol mention check |
| Spelling / grammar | Hunspell + LanguageTool | Info | Medical dictionaries layered |
| Acronym expansion | Tenant lexicon | Info | Expand on first use |
| Prior-report inconsistency | AI-assisted + semantic diff | Warning | Unexpected interval change without note |
| Quantitative comparison | Deterministic (RECIST math) | Info | Lesion change classification |

### 16.2 Human-in-the-Loop Controls

1. AI-generated text is **visibly marked** until reviewed (color + screen-reader-friendly annotation + opt-out for accessibility).
2. Final export requires explicit user confirmation (cannot be bypassed by automation).
3. Critical-finding suggestions require explicit confirmation and routing through critical-results workflow.
4. **Report signing** happens in the customer's official reporting/RIS/EHR system unless RadioPad is approved as an in-system signing endpoint for that tenant.
5. User edits are tracked at the keystroke aggregation level (not raw keystrokes) for analytics and rulebook evaluation; raw text stays in tenant.
6. No silent retries on PHI paths — operator visibility is mandatory for every retry.
7. "Why this suggestion?" panel is mandatory and always accessible.

### 16.3 Confidence presentation

RadioPad **does not** present vague AI confidence as clinical certainty. The Validation Panel surfaces:

* Rule validation status (pass/warn/block).
* Unsupported-statement warnings with source-span highlights.
* Missing-input warnings.
* Contradiction warnings.
* Source/context trace (provenance graph).
* Model/rulebook version pin.
* "Needs radiologist review" banner.

### 16.4 Safety RFC process

Any change that could alter clinical output must:

1. Have an RFC document.
2. Pass golden-case regression (≥99 % for blockers, ≥95 % for warnings) on at least 3 reference rulebooks.
3. Be reviewed by the Clinical Safety Working Group.
4. Pre-announce planned behavior change to tenants ≥30 days in advance for non-security changes.

### 16.5 Adversarial robustness

* **Prompt-injection in dictation**: tested with red-team corpus; daemon strips suspected directives before model invocation.
* **Output watermarking** (EE): cryptographic provenance + optional perturbation watermark.
* **Source-span attestation**: model outputs include character spans into the input; UI verifies spans exist.
* **Refusal handling**: if model refuses, validation engine falls back to a deterministic template; never silently emits empty content.
* **Toxic content filter** off by default for clinical accuracy (clinical text discusses violence, drugs, suicidality) but configurable for patient-facing modes.

---

## 17. Data and Audit Model

### 17.1 Core entities

| Entity | Notes |
| :---- | :---- |
| Tenant | Hospital / group / center; isolation boundary |
| User | Radiologist, admin, reviewer, IT, researcher, auditor |
| Role / Permission | Fine-grained; tenant-scoped |
| Device | Studio install or daemon instance; signed cert |
| Study Context | Exam metadata: modality, body part, accession, patient ref (HL7 PID + FHIR Patient.id) |
| Report Draft | Editable report; CRDT document |
| Report Version | Snapshot per major edit/export with content hash |
| Report Final | Immutable export record |
| Template | Versioned structured template with bound rulebook(s) |
| Rulebook | Versioned governance package |
| Prompt Block | Reusable component |
| AI Request | Input to provider |
| AI Response | Output, with token counts, latency, cost |
| Validation Result | Each check, severity, outcome, span references |
| Provider Config | Compliance class, endpoints, credentials (ref, not raw) |
| Audit Event | Immutable; cryptographically chained (per tenant) |
| Subscription | Plan, seats, usage, billing |
| Critical Result | Notification, ack, escalation chain |
| Peer Review | Case, reviewer, score, rationale |
| Teaching Case | Anonymized; categorized |
| Citation | Bibliographic record, persistent identifier |
| Worklist Item | DICOM UPS or HL7-derived item |
| Lesion (longitudinal) | Cross-study lesion with measurements |
| Plugin | Manifest, signature, capabilities, version |

### 17.2 Audit event schema (selected fields)

Every AI event logs:

```json
{
  "event_id": "uuidv7",
  "event_time": "ISO-8601",
  "event_type": "ai.request.completed | report.export.signed | rulebook.promoted | ...",
  "tenant_id": "string",
  "actor": {
    "user_id": "string",
    "role": "string",
    "device_id": "string",
    "auth_method": "webauthn|totp|saml|oidc"
  },
  "subject": {
    "report_id": "string",
    "study_ref": "string (hash)",
    "patient_ref": "string (hash) | null",
    "modality": "string"
  },
  "ai": {
    "provider": "string",
    "model_id": "string",
    "model_version": "string",
    "prompt_block_versions": ["string", "..."],
    "rulebook_version": "string",
    "template_version": "string",
    "input_hash": "blake3:...",
    "output_hash": "blake3:...",
    "input_tokens": 0,
    "output_tokens": 0,
    "latency_ms": 0,
    "phi_class": "phi | de-identified | meta"
  },
  "validation": {
    "results": [{ "rule": "laterality", "outcome": "pass", "severity": "blocker" }]
  },
  "user_action": "accepted | edited | rejected",
  "export": {
    "destination": "ris | pdf | docx | fhir | hl7v2 | none",
    "destination_id": "string"
  },
  "chain": {
    "prev_event_hash": "blake3:...",
    "this_event_hash": "blake3:..."
  },
  "signature": "ed25519:..."
}
```

Audit events are **hash-chained per tenant** and **periodically anchored** to an external timestamping authority (RFC 3161) for tamper-evidence. EE adds optional transparency log inclusion (Rekor-style).

### 17.3 Data retention

* Tenant-configurable retention per data class (drafts, finals, AI logs, audit, telemetry).
* PHI minimization defaults: do not retain raw AI inputs/outputs beyond N days unless tenant policy requires.
* "Hash-only audit" mode stores only hashes of input/output for proof-of-event without content.
* Legal hold flag overrides retention.
* Right-to-delete workflows for jurisdictions that require it (with audit of the delete itself).
* Exportable compliance logs (NDJSON, signed bundle).

---

## 18. Privacy, Security, and Compliance

### 18.1 Security architecture principles

* **Zero-trust**: every service-to-service call is authenticated and authorized; the network is not a security boundary.
* **mTLS** between every internal service; SPIFFE/SPIRE for workload identity.
* **Least privilege**: every credential is scoped and time-bound.
* **Defense in depth**: WAF, RASP, dependency scanning, SAST, DAST, fuzzing.
* **Secure by default**: PHI never goes to a non-approved provider; opt-in to lower-compliance modes is explicit and audited.
* **Supply chain**: SBOM published per release, signed with cosign; SLSA-3 builds; reproducible Rust binaries.

### 18.2 Security requirements

| ID | Requirement | Priority | Edition |
| :---- | :---- | :---- | :---- |
| SEC-001 | TLS 1.3 only on the perimeter (TLS 1.2 allowed for legacy ORU/MLLP partners) | P0 | CE |
| SEC-002 | AES-256-GCM at rest; per-tenant KMS keys | P0 | CE |
| SEC-003 | Customer-managed keys (BYOK + HYOK) | P1 | EE |
| SEC-004 | Per-tenant PHI policy, enforced in daemon + gateway | P0 | CE |
| SEC-005 | Audit log immutability (hash-chained, append-only object store, optional timestamping) | P0 | CE |
| SEC-006 | Least-privilege RBAC + ABAC for sensitive resources | P0 | CE |
| SEC-007 | SSO (OIDC/SAML), MFA (WebAuthn preferred), SCIM | P0/P1 | CE/EE |
| SEC-008 | IP allowlists, device posture checks (Studio + daemon) | P1 | EE |
| SEC-009 | Secret storage in OS keychain + Vault; never plaintext on disk | P0 | CE |
| SEC-010 | PHI redaction in debug logs; structured logging only | P0 | CE |
| SEC-011 | Intrusion detection + anomaly alerts (Falco + custom) | P1 | EE |
| SEC-012 | Provider compliance profiles + enforcement | P0 | CE |
| SEC-013 | Signed binaries; verified updates; rollback if first run fails health checks | P0 | CE |
| SEC-014 | Crypto: Ed25519 signatures, X25519 KEX, AES-GCM-SIV for nonce-misuse resistance | P0 | CE |
| SEC-015 | Encrypted backups; restore drills quarterly | P0 | CE |
| SEC-016 | Secrets rotation policy: API keys 90d, mTLS certs 30d, session tokens 15m | P0 | CE |
| SEC-017 | Phishing-resistant MFA enforced for admin roles | P0 | CE |
| SEC-018 | Browser CSP (strict), Trusted Types, SRI for scripts | P0 | CE |
| SEC-019 | SBOM (SPDX + CycloneDX) published per release | P0 | CE |
| SEC-020 | SLSA-3 provenance attestations | P1 | CE |
| SEC-021 | Container images signed; admission policy rejects unsigned | P1 | EE |
| SEC-022 | Per-tenant honeypot endpoints to detect credential probing | P2 | EE |
| SEC-023 | Confidential computing (Intel SGX / TDX, AMD SEV-SNP) for highest tiers | P2 | EE |
| SEC-024 | Network segmentation: data plane and control plane separate VPCs / mesh policies | P0 | EE |
| SEC-025 | Egress allowlist enforced by NetworkPolicy / firewall | P0 | EE |
| SEC-026 | Hardware security keys for break-glass | P1 | EE |

### 18.3 Compliance posture

| Framework | Approach |
| :---- | :---- |
| **HIPAA Security Rule** | Administrative, physical, technical safeguards documented; BAA template ready; encryption, access control, audit, integrity, transmission, workforce policies, breach notification workflow. |
| **HIPAA Privacy Rule** | Minimum necessary, accounting of disclosures, right to access, right to amend (where applicable). |
| **HITECH** | Breach notification within statutory windows; reportable-incident workflow built in. |
| **42 CFR Part 2** | Substance-use disorder records handling per US federal rule. |
| **EU GDPR** | DPA available, DPO contact published, DPIA template, lawful-basis tracking, data residency pinning per tenant. |
| **EU AI Act** | High-risk AI system controls: technical documentation, log retention, transparency notice, human oversight, post-market monitoring, incident reporting. |
| **UK DPA 2018 / NHS DSPT** | NHS DSPT mapping document; UK data residency option. |
| **PIPEDA (Canada)** | Provincial overlays (PHIPA, etc.) supported via residency. |
| **HDS (France)** | "Hébergeur de Données de Santé" deployment template. |
| **ISO 27001** | ISMS scope, controls mapping (Annex A), evidence collection. |
| **ISO 27017 / 27018** | Cloud-specific controls and PII-in-cloud controls. |
| **ISO 13485** | Quality management system if/when RadioPad pursues SaMD status. |
| **IEC 62304** | Medical device software lifecycle if SaMD path is pursued. |
| **IEC 82304-1 / IEC 81001-5-1** | Health software product safety and security lifecycle. |
| **NIST AI RMF 1.0** | Map / Measure / Manage / Govern functions. |
| **NIST SP 800-53 / 800-66** | Control mappings for federal customers. |
| **SOC 2 Type II** | Trust Services Criteria; annual audit. |
| **HITRUST CSF** | Optional EE certification path. |
| **PCI-DSS** | Billing path tokenized via Stripe / similar; RadioPad is out of CDE scope. |
| **GMLP / Good ML Practice (FDA/HC/MHRA)** | Lifecycle, traceability, human factors, monitoring, change-control. |

### 18.4 AI provider compliance

* **PHI-allowed providers** require a documented BAA / DPA and an explicit ZDR / non-training endpoint, surfaced in the admin UI.
* **Provider compliance profile** is a first-class object: BAA status, DPA status, training-on-customer-data status, retention behavior, endpoint eligibility, jurisdictional notes.
* **Fallback rules** never downgrade compliance class. If only a higher-class provider is available and a lower-class fallback is configured, the request fails with a clear error rather than silently downgrading.
* **Provider risk registry** is exportable for vendor management programs.

---

## 19. Regulatory Strategy

### 19.1 Intended-use statement (MVP/Beta)

> "RadioPad is intended to assist licensed radiologists in drafting, editing, formatting, standardizing, and validating radiology report text. RadioPad does not independently interpret medical images, diagnose disease, recommend patient management without physician confirmation, or sign reports. All outputs require review and approval by a licensed radiologist. RadioPad is not a substitute for clinical judgment."

### 19.2 Risk classification matrix

| Capability | Default tier | Trigger for higher-risk tier |
| :---- | :---- | :---- |
| Dictation cleanup, grammar, formatting | Non-device / wellness-tier | None |
| Style rewriting, length control | Non-device | None |
| Impression generation from findings | Non-device with safeguards | Marketing as "diagnostic assistant" → SaMD |
| Critical-result language enforcement | Non-device with safeguards | Promoting auto-paging → SaMD |
| RADS category derivation from structured fields | Non-device, deterministic | Use of model-driven categorization as primary clinical signal |
| Follow-up recommendation from rulebook (deterministic) | Non-device | Generative recommendation without rulebook backing |
| Image interpretation (post-GA roadmap) | SaMD candidate | Any pixel-derived diagnostic output |

### 19.3 Predetermined Change Control Plan (PCCP) (EE)

For tenants where RadioPad's model changes are part of a regulated pathway:

* Documented model change envelope (architecture, training data, hyperparameters).
* Acceptable performance bounds per metric.
* Monitoring plan (production drift, error topology, fairness across cohorts).
* Re-training triggers.
* Communication plan to tenants.

### 19.4 Post-market monitoring

* Drift watch (daily probes against golden cases).
* Field-failure reporting workflow.
* Incident classification and notification SLAs.
* Annual safety review with Clinical Safety Working Group.

### 19.5 Claims governance

Marketing materials, documentation, and UI strings are gated by a **claims policy** to prevent over-claiming. Words like "diagnose", "interpret", "decide", "automated reading" are flagged in CI for human review.

---

## 20. User Experience Requirements

### 20.1 Web app navigation

Primary modules: Dashboard · Reporting Workspace · Worklist · Templates · Rulebooks · Prompt Studio · AI Providers · Validation Center · Peer Review · Teaching Files · Analytics · Integrations · Users & Roles · Audit Logs · Billing · Plugins · Marketplace · Settings.

### 20.2 Reporting Workspace panels

| Panel | Purpose |
| :---- | :---- |
| Study Context | Patient/study metadata, indication, modality, body part, prior report summary, RVU class |
| Editor | Main report drafting area (TipTap, section-aware, structured fields) |
| AI Actions | Generate, rewrite, summarize, impression, validate, compare, evidence |
| Rulebook Panel | Active rules, warnings, required fields, "why this suggestion?" |
| Prior Report Panel | Prior findings, semantic diff, lesion tracker |
| Validation Panel | Contradictions, missing sections, style issues, unsupported claims, source-span highlights |
| Export Panel | Copy, PDF, DOCX, FHIR, HL7 v2, RIS integration |
| Worklist | Filterable queue with priority |
| Notes / Comments | Collaboration sidebar |
| Provenance Inspector | Token-level provenance graph, model, prompt, rulebook |

### 20.3 Studio UX principles

1. Global hotkey opens command palette in ≤200 ms.
2. Mini overlay floats above PACS without stealing focus.
3. Dictation starts within ≤300 ms of hotkey.
4. AI actions accessible by keyboard alone.
5. Secure paste into PACS with timeout indicator.
6. Offline mode is clearly indicated (badge + tooltip + tray icon).
7. Provider / compliance mode visible (e.g., "Local-only" badge) but not intrusive.
8. Recovery from sleep / suspend resumes the last draft state.

### 20.4 Prompt Studio UX

* Visual prompt-block editor with live token counter.
* Rulebook preview with sample outputs against tenant golden cases.
* Test-case runner with pass/fail and diff.
* Output diff viewer (semantic + literal).
* Golden-case library editor.
* Approval workflow with electronic signatures.
* Prompt / rule version comparison and rollback.
* "Explain this prompt" mode that translates raw prompt text into a plain-language description for non-technical reviewers.

### 20.5 Mobile companion UX

* One-handed reachable bottom navigation.
* Push notifications respect Do-Not-Disturb except for tenant-defined critical classes.
* Biometric unlock; nothing displayed on lock screen beyond a non-PHI badge.
* Critical-result acknowledgement with mandatory free-text confirmation in addition to biometric.

### 20.6 Accessibility & inclusivity

* WCAG 2.2 AA conformance is CI-gated.
* Color is never the sole carrier of meaning.
* All interactive elements keyboard-reachable in logical order.
* All form controls labeled and described.
* Screen-reader announcements for AI streaming updates ("AI is generating impression"… "AI completed").
* High-contrast theme switchable per-user.
* Dyslexia-friendly font option.
* RTL layouts complete for AR/HE/UR locales.

---
## 21. Billing and Subscription

### 21.1 Plans

| Plan | Target | Highlights |
| :---- | :---- | :---- |
| **Community Edition (free, self-host)** | Solo radiologists, training programs, academic centers, OSS deployments | Full reporting, rulebooks, templates, local inference, web + Studio + CLI, basic audit, FHIR/HL7 export, mobile PWA. **No PHI restrictions** — runs entirely offline. |
| **Pro (per-seat SaaS)** | Small groups, imaging centers | Hosted, BAA-approved cloud providers, advanced analytics, marketplace access. |
| **Enterprise** | Hospitals, teleradiology | SSO/SAML/SCIM, advanced audit, SIEM connectors, governance dashboard, peer review, on-prem option, dedicated support. |
| **Enterprise Plus** | Health systems, national programs | Customer-managed keys, federated learning, confidential computing, sovereign-cloud, PCCP, white-glove. |

### 21.2 Usage metering

Track: seats; AI requests; tokens (prompt/completion); dictation minutes; report generations; validation runs; storage; integration calls; daemon activations; rulebook test runs; plugin executions; federated rounds.

### 21.3 Subscription requirements

| ID | Requirement | Priority | Edition |
| :---- | :---- | :---- | :---- |
| BILL-001 | Seat-based subscription | P0 | Pro+ |
| BILL-002 | Usage-based AI credits | P0 | Pro+ |
| BILL-003 | Enterprise invoicing (PO, net 30/60, multi-currency) | P1 | EE |
| BILL-004 | Tenant-level usage dashboard | P0 | All |
| BILL-005 | Provider cost attribution | P1 | Pro+ |
| BILL-006 | Plan-based feature flags | P0 | All |
| BILL-007 | Trial tenants and sandbox environments | P1 | All |
| BILL-008 | Hard budget ceilings with grace + notifications | P0 | Pro+ |
| BILL-009 | Pre-paid credits + auto-recharge optional | P1 | Pro+ |
| BILL-010 | Tax handling (US sales tax via Stripe Tax / equivalent, VAT, GST) | P1 | Pro+ |
| BILL-011 | Educational / non-profit pricing tier | P2 | Pro+ |
| BILL-012 | Marketplace revenue share for plugin authors | P3 | EE |
| BILL-013 | Open billing engine: **Lago** or **Killbill** as alternates to Stripe | P0 | All |

---

## 22. Analytics and KPIs

### 22.1 Product KPIs

| KPI | Definition | Target by GA |
| :---- | :---- | :---- |
| Draft acceptance rate | % of AI drafts used after review | ≥80 % |
| Impression acceptance rate | % of generated impressions accepted with minor edits | ≥75 % |
| Edit distance per section | Average characters changed per AI-generated section | trending ↓ MoM |
| vTTQR (verified time to quality report) | Median wall-clock from study open to clean export | -40 % vs baseline |
| Report validation pass rate | Reports passing tenant rulebook | ≥95 % |
| Contradiction detection rate | Warnings per 100 reports | tracked, not minimized |
| Critical-finding capture rate | Confirmed criticals flagged by RadioPad / total | ≥98 % |
| Active radiologists | WAU / MAU | tenant-defined |
| Rulebook adoption | % reports generated under an approved rulebook | ≥90 % |
| Provider cost per report | AI cost / completed report volume | tenant budget |
| TAT impact | Turnaround time before/after | -20 % at 90 days |
| Peer-review discrepancy rate | Discrepancies / reviews | benchmark, not minimized |

### 22.2 Governance KPIs

| KPI | Definition |
| :---- | :---- |
| Unapproved-prompt usage attempts | Should be 0 in production |
| PHI policy violations blocked | Count of prevented unsafe provider calls |
| Rulebook regression failures | Failed tests before approval |
| Model drift alerts | Quality degradation vs golden cases |
| Audit completeness | % AI events with complete provenance |
| Time-to-incident-acknowledgement | From alert to first response |
| Vulnerability MTTR | Median time to remediate CVE in deps |
| Open critical CVEs | 0 in production manifest |
| SBOM freshness | Days since last SBOM publish |

### 22.3 Reliability KPIs

| KPI | Target |
| :---- | :---- |
| Web app availability | 99.95 % (EE), 99.9 % (Pro), 99.5 % (CE self-host floor) |
| AI gateway availability | 99.9 % (excluding provider outages) |
| Critical-result notification delivery | 99.99 % within SLA |
| Audit event durability | RPO ≤1 min; RTO ≤5 min on control plane |
| Error budget burn | Tracked weekly |

---

## 23. Integration Requirements

### 23.1 PACS / RIS / EHR integration paths

* DICOMweb for study metadata and worklist (UPS-RS).
* FHIR R4/R5 `DiagnosticReport` and `ImagingStudy` exchange.
* HL7 v2 ORU (results), ORM (orders), ADT (admit/discharge) over MLLP+TLS.
* DICOM SR for measurement-bearing reports.
* Clipboard / desktop bridge as universal fallback.
* Custom enterprise connectors (plugin) for proprietary systems (Epic Aura, Cerner Imaging, Sectra, GE Centricity, Philips IntelliSpace, Carestream, Visage, Merge, Change Healthcare, Fujifilm Synapse, Intelerad). Connectors are EE add-ons but the connector SDK is open (CE).

### 23.2 Integration matrix

| Integration | MVP-β | Clinical Beta | GA |
| :---- | :---- | :---- | :---- |
| SSO/OIDC | ✅ | ✅ | ✅ |
| SAML 2.0 | — | ✅ | ✅ |
| SCIM 2.0 | — | — | ✅ |
| DICOMweb metadata + UPS-RS | — | ✅ | ✅ |
| FHIR R4 DiagnosticReport export | ✅ | ✅ | ✅ |
| FHIR R5 + Bulk FHIR | — | ✅ | ✅ |
| HL7 v2 ORU/ORM/ADT (MLLP/TLS) | — | ✅ | ✅ |
| DICOM SR export | — | ✅ | ✅ |
| PACS local bridge | — | ✅ | ✅ |
| RIS copy/paste bridge | ✅ | ✅ | ✅ |
| Stripe / Lago / Killbill | ✅ | ✅ | ✅ |
| SIEM (Splunk, Sentinel, Elastic, Chronicle, Loki) | — | ✅ | ✅ |
| IHE XDS-I.b, SWF.b | — | — | ✅ |
| IHE AIW-I | — | — | ✅ |
| Slack / Teams / Webhook for critical results | ✅ | ✅ | ✅ |
| MDM (Intune, Jamf, Workspace ONE) | — | — | ✅ |

### 23.3 Public API surface

* **REST + OpenAPI** for everything CRUD.
* **WebSocket / SSE** for streaming AI and real-time collaboration.
* **gRPC** between internal services and to the daemon.
* **GraphQL** optional for read-heavy admin views (post-GA).
* **AsyncAPI** for event channels (NATS subjects, webhooks).
* **SDKs**: TypeScript, Python, Go, Rust.

---

## 24. Technical Architecture (detailed)

### 24.1 Service modules (server-side)

| Service | Language | Responsibility |
| :---- | :---- | :---- |
| `identity` | Go | OIDC/SAML proxy, RBAC, SCIM, MFA enrollment |
| `tenant` | Go | Tenant lifecycle, settings, plan |
| `reporting` | Go | Drafts, versions, exports, CRDT relay |
| `rulebook` | Go | CRUD, versioning, approvals, tests, signing |
| `template` | Go | Templates, structured fields, specialty libraries |
| `ai-gateway` | Rust (axum) | Provider routing, policy enforcement, guardrails, telemetry — hot path |
| `validation` | Rust + Python (sidecar for NLP) | Deterministic checks + AI-assisted checks |
| `integration` | Go | DICOMweb, FHIR, HL7 v2, webhook fanout |
| `audit` | Go + ClickHouse for search | Immutable event log, hash chaining, anchoring |
| `billing` | Go | Plans, seats, usage, invoices |
| `desktop-sync` | Go | Device pairing, daemon registration, policy push |
| `marketplace` | Go | Plugin & template marketplace |
| `analytics` | Go + ClickHouse | Aggregations, dashboards, exports |
| `notification` | Go | Push, email, webhook, SMS via plugin |
| `worklist` | Go | DICOM UPS / HL7 ingest, prioritization |
| `peer-review` | Go | Queues, scoring, discrepancy tracking |
| `governance` | Go | Model registry, PCCP, drift watcher |
| `federation` (EE) | Rust | FL coordinator, DP accountant |

### 24.2 Data stores

* **Postgres 16** (primary OLTP) — per-tenant schemas under one cluster for small tenants; dedicated clusters per EE tenant.
* **ClickHouse** (audit + analytics) — high-volume event search.
* **Redis 7** (cache, rate limit, session).
* **NATS JetStream** (event bus + workqueues).
* **OpenSearch** (text search across reports + rulebooks).
* **pgvector / Qdrant** (semantic search, similar-case retrieval).
* **MinIO / S3** (object storage for PDFs, exports, large attachments).
* **SQLite + SQLCipher** (daemon-local).

### 24.3 Inference architecture

* **Local daemon** embeds llama.cpp via FFI (Rust → C) and ONNX Runtime via crates; switches backends per platform.
* **AI gateway** runs vLLM or TGI in containers for hosted self-hosted inference; auto-scales by tenant queue depth.
* **Speculative decoding** + **prefix caching** + **continuous batching** (vLLM) deliver high throughput.
* **Model registry** tracks model IDs, versions, hashes, license, eval scores, drift baseline.
* **Quantized models**: GGUF Q4_K_M / Q5_K_M default; INT8 ONNX for CPU; FP16 for GPU.
* **NPU acceleration**: CoreML on Apple Silicon; DirectML / OpenVINO on Windows; ROCm / Vulkan on Linux.

### 24.4 Twelve-factor adherence

* Stateless services; state in Postgres/NATS/S3.
* Config via env (+ short-lived JWTs / Vault for secrets).
* Logs to stdout (structured JSON, OTel-aware).
* Disposable workers with graceful shutdown.
* Dev/prod parity via Docker Compose for dev, Helm for prod.
* Build = release = run separation enforced by signed artifacts.

### 24.5 Deployment artifacts

* **Helm charts** for full stack (`radiopad-stack`) + per-service charts.
* **Docker Compose** for laptop / single-VM evaluations.
* **k3s air-gapped bundle** for hospital / edge.
* **Nix flakes** for reproducible dev environments.
* **Tauri installers** for Studio: MSI, MSIX, DMG, PKG (notarized), AppImage, .deb, .rpm, Flatpak, Snap.
* **CLI** distribution: Homebrew, Scoop, winget, apt/dnf repos, AUR, direct download, container.

### 24.6 Frontend architecture

* React 18 + Vite + TS.
* Module federation for plugin UI.
* Route-based code-splitting; ≤200 KB initial JS gzip.
* Service Worker for PWA shell + offline drafts.
* Web Workers for parsing, validation pre-checks, FHIR transforms.
* WebAssembly for local NLP (medspaCy-style) where applicable.
* Strict CSP + Trusted Types + SRI.

### 24.7 Desktop architecture (Tauri)

* Rust core hosts:
  * UI bridge (window mgmt, IPC, OS integration).
  * Daemon bridge (gRPC over UNIX socket / named pipe).
  * OS hotkeys (rdev-based).
  * Native notifications.
  * Auto-updater (signed bundles).
  * Crash reporter.
* UI shipped as a WebView pointing at packaged static assets (same TS codebase as web, but with desktop-only modules).
* Tauri sidecar pattern for `radiopadd`.

### 24.8 Mobile architecture

* Capacitor + React; shared component library with web.
* Native modules for:
  * Biometrics (Face ID / Touch ID / Android BiometricPrompt).
  * WebAuthn passkeys.
  * Push (APNs/FCM).
  * Screenshot protection.
  * Encrypted local store (Keychain / Keystore-backed).

---

## 25. Performance, Reliability & Scalability

### 25.1 SLOs and error budgets

| Surface | Availability SLO | Latency SLO | Error-budget window |
| :---- | :---- | :---- | :---- |
| Web app (Pro/EE) | 99.9 % monthly | P95 TTI ≤1.8 s | 30 d rolling |
| AI gateway | 99.9 % monthly (provider-independent) | P95 routing ≤80 ms | 30 d |
| FHIR / HL7 endpoints | 99.9 % monthly | P95 ≤500 ms | 30 d |
| Daemon API (local) | 99.99 % daily | P95 ≤20 ms (RPC) | 1 d |
| Critical-result notifications | 99.99 % weekly | E2E ≤2 s | 7 d |
| Audit writes | 99.99 % daily | P99 ≤500 ms | 1 d |

Burn-rate alerts fire at 2× and 5× burn for fast-burn detection.

### 25.2 Capacity targets

| Scale unit | Target |
| :---- | :---- |
| Reports / month per node (control plane) | ≥250 000 |
| Concurrent active reporters per node | ≥200 |
| Audit events / sec per shard | ≥10 000 |
| AI requests / sec at the gateway (per pod, no inference) | ≥500 |
| Local daemon concurrent requests | ≥8 |
| Largest report draft (CRDT doc) | ≤1 MB without performance loss |

### 25.3 Scalability strategy

* **Horizontal stateless services**, vertical only for hot inference.
* **Shard Postgres** by tenant once a single cluster nears 70 % of comfortable limits.
* **Logical replicas** for read scaling per tenant.
* **NATS JetStream clustering** with replicated subjects for events.
* **OpenSearch with index lifecycle management** for audit hot/warm/cold tiers.
* **Edge caching** of static assets via Cloudflare / Fastly / self-hosted Varnish.
* **Connection pooling** at PgBouncer in transaction mode.

### 25.4 Resource budgets (recap, with stretch)

Already covered in §11; stretch goals for v2.0 LTS:

* Studio idle RAM ≤90 MB P95.
* Daemon idle RAM ≤22 MB P95.
* CLI startup overhead ≤25 ms.
* Cold local 7B Q4 generation under 3.5 s P95 on M2/M3 / RTX 3060.

---

## 26. Observability & Operations

### 26.1 Instrumentation

* **OpenTelemetry** end-to-end: traces, metrics, logs.
* **Correlated** by `trace_id` from browser → gateway → service → daemon → provider.
* **Per-tenant** dashboards (Grafana) with templated variables.
* **Per-modality** dashboards for clinical metrics.
* **SLO dashboards** with error-budget burn, fast-burn and slow-burn alerts (multi-window multi-burn alert pattern).

### 26.2 Logs

* Structured JSON only; no print-debug.
* PHI redaction layer in the logger; CI scan rejects strings that look like PHI templates.
* Sampling for high-volume info; full retention for warning+; tiered storage (Loki) with hot/warm/cold.

### 26.3 Tracing

* W3C Trace Context everywhere.
* Span attributes follow OTel semantic conventions; clinical attributes use a published `radiopad.*` namespace.
* High-cardinality fields (study_id, tenant_id) are hashed in trace attributes by default; full values only on demand.

### 26.4 Continuous profiling

* Pyroscope / Grafana Phlare collects CPU + heap from all services and daemon.
* Profiles are part of the post-incident runbook.

### 26.5 Runbooks

* Every service has a runbook (`runbooks/<service>.md`) checked into the repo.
* Runbooks include: on-call playbook, dashboards, common alerts, escalation matrix, dependency map.
* Runbooks are tested in chaos drills.

### 26.6 Synthetic monitoring

* Golden-path probes from multiple regions: login, open study, generate impression, export.
* Critical-result delivery probe end-to-end.
* DICOMweb / FHIR endpoint probes per integration.

### 26.7 Status page

* Public status page (Cachet or Atlassian Statuspage equivalent OSS).
* Per-component status, with planned-maintenance schedule.

---

## 27. Disaster Recovery & Business Continuity

### 27.1 RTO / RPO targets

| Class | RTO | RPO |
| :---- | :---- | :---- |
| Drafting (local Studio + daemon) | **0 (continues offline)** | 0 (local WAL) |
| Reporting workspace (control plane) | 15 min | 1 min |
| Audit log durability | N/A | 1 min |
| FHIR / HL7 outbound | 30 min | 5 min |
| Marketplace / analytics | 2 h | 15 min |
| Federated learning (EE) | 24 h | 1 h |

### 27.2 DR architecture

* **Active-active** across regions for EE Plus; active-warm-standby for Pro.
* **Postgres** logical + physical replication; pgBackRest for PITR (15-min RPO floor).
* **NATS** mirror streams across regions.
* **Object storage** with CRR.
* **DNS health-checked failover** at the edge.
* **Restore drills** quarterly with audit-verified outcomes.

### 27.3 Degradation modes (tested, not aspirational)

| Failure | Behavior |
| :---- | :---- |
| Cloud unreachable | Studio continues drafting; daemon queues audit + AI requests offline-tolerant where local model is available; export defers. |
| All cloud AI providers down | Daemon routes to local model; user is notified of provider failover. |
| Daemon down | Studio surfaces banner; web continues with cloud-only path. |
| Postgres primary loss | Auto-failover to replica; CRDT-based draft state replays from client + WAL. |
| NATS partition | Producers buffer to local WAL; consumers replay on partition heal. |
| OIDC provider down | Cached session tokens honored until expiry; refresh degraded; admin alert. |
| DICOMweb endpoint down | Manual metadata entry path remains usable. |

### 27.4 Backups

* Daily full + WAL archiving; tested PITR.
* Cross-region replication.
* Encrypted with tenant-scoped keys.
* Backup integrity checks (random restore sample weekly).

### 27.5 Incident response

* Severity levels Sev1–Sev4 with documented response times.
* Sev1 acknowledged in ≤5 min; comms within ≤15 min.
* Postmortems published internally within 72 h for Sev1/Sev2; externally for tenant-affecting outages.

---

## 28. Testing Strategy

### 28.1 Test pyramid

* **Unit tests** — every package, ≥80 % branch coverage gate.
* **Property-based** (proptest in Rust, hypothesis in Python) for parsers, schema, validators.
* **Integration tests** with test containers (Postgres, Redis, NATS, MinIO, Orthanc, HAPI FHIR).
* **End-to-end tests** with Playwright (web) and tauri-driver (Studio).
* **Performance tests** with k6 / Vegeta against staging; perf budgets enforced.
* **Load tests** monthly with synthetic tenant burst (10× steady-state).
* **Chaos tests** with Litmus / Chaos Mesh against staging; SLOs must hold under tested failure modes.
* **Fuzz tests** for FHIR / HL7 / DICOMweb parsers; cargo-fuzz / go-fuzz / atheris.
* **Mutation tests** for safety-critical validators.
* **Snapshot tests** for AI outputs against golden cases (rulebook regression).

### 28.2 Clinical safety tests

* Per-subspecialty golden-case packs (≥50 each at GA, growing to ≥500 at LTS).
* Hallucination harness with crafted cases.
* Adversarial prompt-injection corpus.
* Bias evaluation across cohorts (sex, age, language) using public + synthetic data.

### 28.3 Security tests

* SAST (semgrep, gosec, cargo-audit, npm audit).
* DAST (ZAP) against staging.
* Dependency vuln scan (Trivy, Grype, Dependabot).
* Container scan in CI; admission controller rejects unsigned/vuln images.
* Annual external penetration test; quarterly internal red-team for EE.
* Bug bounty (responsible disclosure) with safe-harbor language.

### 28.4 Conformance tests

* Public **RadioPad Conformance Suite** that any provider, PACS connector, or rulebook author can run.
* Public dashboard of conformance status per integration.

---

## 29. Developer Experience & APIs

### 29.1 Local development

```bash
git clone https://github.com/radiopad/radiopad
cd radiopad
./scripts/bootstrap          # nix or asdf-managed toolchain
docker compose up -d         # postgres, redis, nats, minio, orthanc, hapi-fhir, keycloak
pnpm dev:web                 # web app at :3000
make run-daemon              # local daemon
make run-studio              # tauri dev
make test                    # full unit + integration suite
```

### 29.2 SDKs

* `@radiopad/sdk` (TS): typed client for browser + node.
* `radiopad-sdk` (Python): for analytics, dataset export, research enclave.
* `radiopad-go`: for connector authors.
* `radiopad-rust`: full surface; used internally and by plugin authors.

### 29.3 OpenAPI / AsyncAPI

* OpenAPI 3.1 specs live in repo, rendered to `docs/api/`.
* AsyncAPI specs for all NATS subjects and webhooks.
* Schema-first development — code is generated from specs, not the other way.

### 29.4 Conventions

* **Semver** for SDKs, APIs, rulebook spec, plugin ABI.
* **Conventional Commits** + **release-please** for automated changelogs.
* **Rust edition 2024**, **Go 1.22+**, **TS strict mode**.
* **MSRV** documented and CI-checked.

### 29.5 Contributor docs

* `CONTRIBUTING.md` with bootstrap, build, test, lint, debug.
* `ARCHITECTURE.md` per subdir.
* `SECURITY.md` with private disclosure mailbox.
* `CODE_OF_CONDUCT.md` (Contributor Covenant).
* `GOVERNANCE.md` describing the steering committee, RFC process, working groups.

### 29.6 Documentation site

* Hosted at `docs.radiopad.dev` (planned domain), generated from MDX.
* Search via Pagefind (no third-party JS).
* Versioned per release.
* Public API reference, tutorials, conceptual docs, runbooks (public subset), governance, regulatory FAQs.

---

## 30. Open-Source Governance & Community

* **License clarity** — every file carries SPDX header.
* **DCO sign-off** required on commits.
* **RFC process** for breaking changes, new modules, rulebook spec changes.
* **Working groups** with public charters: Clinical Safety, Standards Interop, Security, Performance, Accessibility, Internationalization.
* **Steering committee** seats: 2 academic radiology, 1 hospital IT, 1 AI safety, 2 OSS maintainers, 1 vendor (rotating).
* **Public roadmap** with quarterly community calls.
* **Code of Conduct** with named enforcement contacts.
* **Security disclosure** — 90-day embargo, CVE coordination, public advisory after fix.
* **Funding model**: vendor commercial revenue funds core dev; sponsorships welcome; no decisions purchasable.
* **CLA**: none. DCO only.
* **Conformance program** with logo for compliant providers / connectors.
* **Annual community report** with finance and contributor stats.

---
## 31. Acceptance Criteria

Acceptance criteria are written as **objective, verifiable, machine- or human-testable** statements. Every criterion maps to one or more functional requirements and is gated by an automated test, an integration suite, or a documented clinical evaluation. No release ships until every P0 criterion for that release is green in CI for two consecutive nightly runs.

### 31.1 MVP-α (internal alpha) acceptance

| # | Criterion | Verification |
|---|-----------|--------------|
| MVP-α-01 | A radiologist can create a report draft from free-text input or paste in under 2 clicks. | UI e2e (Playwright). |
| MVP-α-02 | Findings → Impression generation produces an output for 100% of valid inputs in a 200-case fixture set, with P95 latency ≤ 5 s using the bundled local model. | Perf harness + golden cases. |
| MVP-α-03 | AI-generated text is marked with a distinct visual treatment until edited or accepted. | Visual regression + unit tests. |
| MVP-α-04 | Export to plain text, PDF, DOCX, JSON, and FHIR DiagnosticReport produces files that pass schema validation. | Schema validators (FHIR validator, docx schema). |
| MVP-α-05 | Audit events record tenant, user, study reference, model, prompt version, rulebook version, input hash, output hash, and user action. | Audit log integrity test, hash-chain verifier. |
| MVP-α-06 | Provider policy blocks PHI to any provider not marked PHI-approved with active BAA/DPA metadata. | Policy enforcement test suite. |
| MVP-α-07 | Desktop Studio installs in ≤ 80 MB on Windows, macOS, and Linux and starts in ≤ 1.5 s P95 on the reference hardware. | Bundle-size CI gate + perf harness. |
| MVP-α-08 | `radiopad` CLI single-binary is ≤ 25 MB compressed for `linux/amd64`, `linux/arm64`, `windows/amd64`, `darwin/arm64`. | Release artifact size gate. |
| MVP-α-09 | Daemon resident memory ≤ 30 MB on idle and ≤ 120 MB under steady-state load. | Memory benchmark in CI. |
| MVP-α-10 | All P0 unit tests pass with ≥ 90% line coverage on safety-critical modules (validation, rulebook engine, audit, AI gateway). | Coverage report. |
| MVP-α-11 | Container images and binaries are signed with Sigstore Cosign; signatures are verifiable offline. | Verification script in release pipeline. |
| MVP-α-12 | SBOM (CycloneDX 1.5) is generated per release and published with each artifact. | Syft + grype scans. |
| MVP-α-13 | No high or critical CVEs in dependencies at release time (with documented exceptions). | grype CI gate. |
| MVP-α-14 | The "Generate Impression" round-trip emits a span tree (OpenTelemetry) with at least: gateway, rulebook, validator, provider, audit. | OTel collector assertion. |
| MVP-α-15 | An admin can create, edit, version, and approve at least one rulebook end-to-end through the UI. | UI e2e. |

### 31.2 MVP-β / Clinical Beta acceptance

| # | Criterion | Verification |
|---|-----------|--------------|
| MVP-β-01 | DICOMweb QIDO-RS / WADO-RS metadata retrieval works against the dcm4che reference server with mutual TLS. | Integration harness. |
| MVP-β-02 | FHIR DiagnosticReport export validates against US Core and IHE Radiology Reporting profiles. | FHIR validator with profile packs. |
| MVP-β-03 | HL7 v2 ORU^R01 export round-trips through a Mirth Connect test channel. | Integration test. |
| MVP-β-04 | Rulebook regression suite supports golden-case execution with deterministic diffs and tolerance bands for AI-generated text. | Golden-case CLI + diff viewer. |
| MVP-β-05 | Contradiction checker catches laterality and negation conflicts with ≥ 95% recall on the curated 500-case adversarial test set. | Eval harness. |
| MVP-β-06 | Critical-results workflow notifies a referring user via at least one configured channel within ≤ 30 s of acknowledgement. | E2E timing test. |
| MVP-β-07 | Enterprise audit export streams to a SIEM via syslog/CEF and S3 NDJSON with full provenance. | SIEM ingest test. |
| MVP-β-08 | SSO via OIDC and SAML is production-ready against Keycloak, Okta, Entra ID, and Google Workspace. | IDP matrix test. |
| MVP-β-09 | Provider fallback respects compliance class — fallback from a PHI-approved provider to a non-approved provider is impossible. | Property-based test. |
| MVP-β-10 | Voice dictation (whisper.cpp) reaches WER ≤ 12% on the CHiME-style radiology test corpus in English. | WER benchmark. |
| MVP-β-11 | Peer review queue, blinded review, and concordance scoring are operational. | UI + service tests. |
| MVP-β-12 | Teaching files export RSNA MIRC-compatible packages. | Schema validator. |
| MVP-β-13 | RECIST 1.1 lesion tracking computes target/non-target sums, response categories, and produces a structured timeline. | Numerical regression test against published RECIST examples. |

### 31.3 Enterprise GA acceptance

| # | Criterion | Verification |
|---|-----------|--------------|
| GA-01 | SCIM 2.0 provisioning works end-to-end with at least three IDPs. | Integration matrix. |
| GA-02 | SIEM export supports CEF, LEEF, OCSF, and S3 NDJSON. | Format conformance tests. |
| GA-03 | Customer-managed keys (KMS, HSM, Vault Transit) are integrated and verifiable via key-id metadata on stored artifacts. | Key-rotation test. |
| GA-04 | Private/on-prem deployment runbook completes from zero to working tenant in ≤ 4 hours on a documented hardware reference. | Internal install drill. |
| GA-05 | AI governance dashboard displays model inventory, prompt/rulebook versions, drift alerts, PHI routing posture, and audit completeness. | UI acceptance. |
| GA-06 | Model evaluation harness runs golden-case suites against any registered provider/model and produces a published, signed evaluation report. | Eval CLI + signed artifact. |
| GA-07 | Rulebook approval workflow is auditable, signed, and includes evidence packs for medical director sign-off. | Audit trail verification. |
| GA-08 | Desktop Studio and daemon are centrally manageable via MDM-friendly configuration files and a fleet-config API. | Fleet test. |
| GA-09 | Multilingual support is operational for English, French, German, Spanish, Portuguese, Arabic, Japanese, and simplified Chinese in UI; report generation supports English plus at least two additional languages. | i18n test suite. |
| GA-10 | Federated learning rounds complete on the bundled FL test rig without leaking raw PHI across boundaries. | Privacy audit script. |
| GA-11 | Plugin marketplace ships with at least 5 reference plugins, signature verification, and conformance suite. | Conformance runner. |
| GA-12 | Air-gapped deployment passes a documented offline install with no network egress, including model bundles and SBOM mirror. | Network-blackhole test. |
| GA-13 | SOC 2 Type II and ISO 27001 audits are in progress or complete; HITRUST i1 readiness assessment is complete. | External audit evidence. |
| GA-14 | RadioPad ships an EU AI Act technical documentation pack covering risk management, data governance, transparency, human oversight, accuracy, robustness, and cybersecurity. | Documentation review. |
| GA-15 | Conformance program publishes signed conformance reports for at least three external rulebook authors / connector vendors. | Conformance registry. |

### 31.4 LTS acceptance

| # | Criterion | Verification |
|---|-----------|--------------|
| LTS-01 | At least 24 months of security backports and bug-fix releases are committed in writing. | Public LTS policy. |
| LTS-02 | A documented upgrade path from N to N+2 LTS versions is automated with a `radiopad migrate` command. | Migration suite. |
| LTS-03 | Backward-compatible API guarantees are enforced via contract tests on every PR. | Contract test gate. |
| LTS-04 | Reproducible builds verified by at least two independent build farms. | Reproducible build attestation. |

---

## 32. Risks and Mitigations

The risk register is a living document maintained by the Clinical Safety, Security, and Product Risk working groups. Each risk has an owner, a severity (Critical / High / Medium / Low), a likelihood, a current control set, residual risk, and review cadence. The version below is the snapshot at PRD v2.0.

| ID | Risk | Severity | Likelihood | Mitigation | Residual |
|----|------|----------|-----------|------------|----------|
| R-01 | AI hallucination produces a clinically misleading finding that survives review. | Critical | Medium | Unsupported-claim detector, mandatory radiologist review banner, contradiction checks, rulebook required-section enforcement, hallucination eval gate per release, evidence linker citations, golden-case regression. | Medium |
| R-02 | PHI is exfiltrated to a non-compliant AI provider. | Critical | Low | Provider compliance class registry, hard-block routing, daemon-side policy enforcement, DLP scan on payloads, network egress allowlist, signed BAA metadata, fallback restricted to equal-or-higher compliance class. | Very Low |
| R-03 | Product is reclassified as a SaMD without a regulatory plan. | Critical | Medium | Intended-use control gate, claims governance committee, regulatory review on each new clinical feature, IEC 62304 lifecycle artifacts maintained from day one. | Low |
| R-04 | Radiologist automation bias leads to under-review of AI drafts. | High | High | UX warnings, AI text highlighting until edited, mandatory edit-or-accept action, edit-distance analytics, automation-bias training material, optional "second look" mode that requires re-read of unedited AI text. | Medium |
| R-05 | Inconsistent report quality across sites due to template drift. | High | High | Rulebooks-as-code, central registry, signed approval workflow, regression tests, golden cases, drift dashboards. | Low |
| R-06 | Poor PACS/RIS integration prevents production use. | High | Medium | Standards-first (DICOMweb, FHIR, HL7), desktop fallback via secure clipboard, vendor-specific connector library, IHE profile conformance. | Medium |
| R-07 | OAuth-subscription provider use violates contract or regulation. | High | Medium | Subscription providers locked to non-PHI workflows by default; explicit tenant opt-in with legal review required; audit trail on every subscription request. | Low |
| R-08 | Prompt or rulebook drift causes regression in a production tenant. | Medium | Medium | Versioning, approval workflow, regression testing, canary rollouts, automated rollback on safety-metric regression. | Low |
| R-09 | Model cost overruns from an inefficient provider. | Medium | Medium | Tenant budgets, per-request cost telemetry, route optimization, local-model fallbacks, cost dashboards with anomaly alerts. | Low |
| R-10 | Desktop binary compromised via supply chain. | High | Low | Sigstore signing, SLSA-3 provenance, reproducible builds, dependency pinning + auto-update via signed channels, hardware-backed signing keys. | Low |
| R-11 | Plugin marketplace ships a malicious or buggy plugin. | High | Medium | Conformance + security review before listing, sandboxed execution, capability manifest, signed plugins, kill-switch via plugin registry. | Low |
| R-12 | Local LLM produces clinically dangerous output despite a "safe" label. | Critical | Medium | Same validation layer as cloud providers, hallucination detector, evidence requirement, mandatory review, model evaluation packs, SOUP documentation per IEC 62304. | Medium |
| R-13 | Federated learning leaks training data via gradients. | High | Medium | Differential privacy with documented ε budgets, secure aggregation, gradient clipping, dropout privacy, FL audit logs. | Medium |
| R-14 | Audit log tampering hides clinical or security incident. | Critical | Low | Hash-chained append-only audit log, optional WORM bucket sink, signed audit batches, dual-write to SIEM, reconciliation reports. | Very Low |
| R-15 | Voice dictation misrecognizes critical terms (e.g., "no" vs "know"). | High | Medium | Domain-tuned whisper.cpp models, radiology lexicon biasing, dictation confidence display, post-dictation NLP normalization, mandatory review. | Medium |
| R-16 | Air-gapped customer falls behind on security patches. | High | High | Offline update bundles, signed mirror format, scheduled update reminders, security-patch SLA in support contracts. | Medium |
| R-17 | Accessibility failures exclude radiologists with disabilities. | Medium | Medium | WCAG 2.2 AA conformance, accessibility regression tests, keyboard-only workflows, screen-reader compatibility, color-blind palettes. | Low |
| R-18 | Internationalization mistranslates clinically meaningful text. | High | Medium | Bilingual review for all clinical strings, do-not-translate glossaries for terminology codes, locale-specific QA. | Medium |
| R-19 | Telemetry inadvertently captures PHI. | Critical | Low | Strict redaction filter, PHI-classifier scrub, default-off telemetry, opt-in with explicit consent, on-prem collector for enterprise. | Very Low |
| R-20 | Sustained DoS via inference flood from a compromised user account. | High | Medium | Per-user, per-tenant rate limits, token budgets, anomaly detection, account-lock + revoke flow. | Low |
| R-21 | Long-form NLP context exceeds model window and silently truncates. | Medium | Medium | Token budget calculator, explicit truncation banner, summarization step for priors, context window enforcement at gateway. | Low |
| R-22 | Open-source license violation by a downstream redistributor. | Medium | Medium | SPDX headers, LICENSE files, explicit AGPL boundary, redistribution guide, conformance trademark policy. | Low |
| R-23 | Catastrophic regional outage on hosted SaaS. | High | Low | Multi-region active-passive deployment, documented DR runbook, RPO 5 min / RTO 30 min for control plane, customer-visible status page. | Low |
| R-24 | Vendor or maintainer attrition slows clinical safety response. | High | Medium | Public governance, multi-vendor maintainer base, documented succession, paid maintainership funded by EE revenue. | Medium |
| R-25 | Conflicting clinical guidelines across jurisdictions (e.g., follow-up phrasing). | High | High | Jurisdiction-aware rulebook inheritance, tenant override controls, jurisdiction tag on every report, optional "international mode" disclaimers. | Medium |

Each Critical risk has a documented incident-response playbook in §27.6 and a board-level review cadence. The risk register is reviewed quarterly and after every Sev-1 incident.

---

## 33. Implementation Roadmap

The roadmap is divided into six phases. Each phase has explicit deliverables, owners, exit criteria, and a "definition of done" gate review. Dates are deliberately not in the PRD because they shift with funding, community velocity, and clinical evidence requirements; instead, sequence and dependencies are fixed.

### 33.1 Phase 0 — Foundation, Compliance, and Discovery

**Goal:** Lock in the regulatory posture, intended use, and clinical evidence plan before writing production code that touches PHI.

| Deliverable | Owner | Exit criterion |
|-------------|-------|----------------|
| Intended-use statement (English + EU + UK + AU + KSA variants) | Regulatory lead | Signed by clinical advisory board. |
| Risk management file (ISO 14971) | Quality lead | Reviewed by external auditor. |
| Privacy impact assessment (GDPR + HIPAA + KSA PDPL + UK DPA) | Privacy counsel | Approved by DPO. |
| Provider compliance matrix with BAA / DPA / processing-record templates | Compliance lead | All MVP providers covered. |
| Clinical advisory board charter | Medical director | First meeting minutes published. |
| Golden case corpus v0 (≥ 500 de-identified studies, 10 modalities, 4 languages) | Clinical evidence lead | Curation review complete. |
| OSS license decisions (Apache 2.0 + AGPL-3.0 clinical) | Legal | Filed publicly. |
| Public RFC for rulebook spec | Core engineering | RFC accepted by working group. |
| Threat model (STRIDE + LINDDUN) | Security lead | Reviewed by external pen test firm. |
| Reference hardware spec for Desktop Studio | Platform team | Published in docs. |

### 33.2 Phase 1 — Core Reporting MVP-α

**Goal:** Deliver a self-contained reporting copilot that runs locally with bundled OSS models, with everything observable and auditable from day one.

| Deliverable | Owner | Exit criterion |
|-------------|-------|----------------|
| Identity + tenant + RBAC service (Keycloak-backed) | Platform | OIDC + SAML + MFA functional. |
| Reporting service (drafts, versions, exports) | Reporting team | All P0 RPT requirements met. |
| AI Gateway with provider abstraction and policy engine | AI platform | Local + cloud providers functional. |
| Rulebook engine v1 (parser, validator, runner, registry) | Rulebook team | Five reference rulebooks shipped. |
| Validation service v1 (laterality, negation, required sections, contradictions) | Safety team | ≥ 95% recall on baseline test set. |
| Web reporting workspace (React + Vite) | Frontend | All P0 RPT UI surfaces present. |
| Desktop Studio alpha (Tauri) | Desktop team | Bundle size, startup time SLOs met. |
| CLI alpha (Rust) | CLI team | All P0 CLI commands functional. |
| Daemon alpha (Rust) | Daemon team | Memory + startup SLOs met. |
| Audit service with hash-chained log | Audit team | Integrity verifier passes. |
| Billing foundation (Lago) | Billing team | Seat + usage metering functional. |
| Observability stack (OTel, Prometheus, Grafana, Loki, Tempo) | SRE | Dashboards live for all services. |
| Bundled local model pack (llama.cpp + whisper.cpp + GGUF Q4_K_M) | ML platform | Models pass acceptance evals. |
| Reproducible build pipeline + Sigstore signing | Release team | Attestations verifiable. |

### 33.3 Phase 2 — Desktop, CLI, and Operational Hardening

**Goal:** Make the desktop and CLI experiences first-class, harden the daemon, and add the operability features needed for early adopter deployments.

| Deliverable | Owner | Exit criterion |
|-------------|-------|----------------|
| Desktop Studio beta with global hotkeys, secure clipboard, PACS/RIS bridge | Desktop team | Power-user usability study passed. |
| CLI beta with full rulebook test runner, batch validation, headless mode | CLI team | All P0 + P1 CLI requirements. |
| Daemon hardening (sandboxing, memory caps, watchdog, health probes) | Daemon team | Chaos test suite passes. |
| Provider adapter SDK + reference adapters (OpenAI, Azure OpenAI, Anthropic, Google, Cohere, local) | AI platform | All adapters pass conformance. |
| Tool registry + sandbox executor | Tool team | Five reference tools shipped. |
| Performance gate in CI (latency, bundle, memory budgets) | Platform | Budgets enforced on every PR. |
| Synthetic monitoring + canary deployments | SRE | All services have probes. |
| Public docs site v1 + interactive API reference | DX team | Docs site live. |
| TS, Python, Go, Rust SDKs v1 | DX team | SDKs published to registries. |
| Conformance suite v1 | Standards team | Test pack downloadable. |

### 33.4 Phase 3 — Clinical Safety and Integration Beta

**Goal:** Make RadioPad clinically credible: integrate with real PACS/RIS/EHR, prove the safety layer, and add the workflows real radiologists need.

| Deliverable | Owner | Exit criterion |
|-------------|-------|----------------|
| DICOMweb (QIDO/WADO/STOW) integration | Interop team | dcm4che reference conformance. |
| FHIR DiagnosticReport + ImagingStudy export with US Core + IHE profiles | Interop team | FHIR validator green. |
| HL7 v2 ORU^R01 import/export via Mirth Connect channels | Interop team | Round-trip test passes. |
| Voice dictation production (whisper.cpp + domain biasing) | Speech team | WER ≤ 12% on test corpus. |
| Prior-report comparison + longitudinal tracking | Reporting team | UI + service tests pass. |
| Critical-results workflow with SMS / email / pager channels | Reporting team | ≤ 30 s notification SLO. |
| Worklist optimization (priority scoring, SLA tracking) | Reporting team | Worklist KPIs documented. |
| Peer review + teaching files | Education team | RSNA MIRC export validated. |
| RECIST 1.1 + iRECIST tracking | Reporting team | Regression test against published cases passes. |
| Structured reporting + RADS engines (BI/LI/PI/Lung/TI/O-RADS) | Subspecialty team | All RADS calculators validated. |
| Evidence linker (RadLex + SNOMED CT + LOINC + literature) | NLP team | Citation accuracy ≥ 90%. |
| Mobile companion (PWA + native shells) | Mobile team | iOS + Android beta shipped. |
| Hallucination eval pack v1 | Safety team | Published; baselines public. |

### 33.5 Phase 4 — Enterprise, Governance, and Marketplace

**Goal:** Add the enterprise features required by hospital IT, security, and AI governance teams.

| Deliverable | Owner | Exit criterion |
|-------------|-------|----------------|
| SCIM 2.0 provisioning | Identity team | Three IDPs verified. |
| SIEM export (CEF, LEEF, OCSF, NDJSON) | SRE | Three SIEMs verified. |
| Customer-managed keys (KMS / HSM / Vault) | Platform | Key rotation drill passes. |
| AI governance dashboard | Governance team | All KPIs functional. |
| Model evaluation harness with signed reports | Safety team | Reports verifiable. |
| Rulebook approval workflow with medical-director sign-off | Governance team | Audit trail signed. |
| Fleet management API (MDM-friendly) | Desktop team | Fleet drill passes. |
| Plugin marketplace v1 with conformance + signing | Platform | Five reference plugins live. |
| Multilingual UI + report generation | i18n team | Nine UI locales + three report languages. |
| Air-gapped deployment + signed offline bundles | SRE | Network-blackhole drill passes. |
| Federated learning rig | ML platform | Privacy audit passes. |

### 33.6 Phase 5 — General Availability and LTS

**Goal:** Make RadioPad a default option for radiology AI reporting: certified, supported, and proven.

| Deliverable | Owner | Exit criterion |
|-------------|-------|----------------|
| External audits (SOC 2 Type II, ISO 27001, HITRUST i1, ISO 13485 if applicable) | Compliance | Reports issued. |
| EU AI Act technical documentation pack | Regulatory | Filed and reviewed. |
| Clinical evidence dossier (prospective + retrospective studies) | Clinical evidence | Published peer-reviewed evidence. |
| LTS branch with 24-month support commitment | Release | LTS policy published. |
| Reproducible builds verified by two independent farms | Release | Attestations cross-signed. |
| Community conformance program | Standards | Three external conformance certificates issued. |
| Public bug bounty with documented payouts | Security | Hall of fame live. |
| Marketplace v2 with revenue share for community authors | Platform | Payouts processed. |
| Reference customer case studies (≥ 5 across modalities and geographies) | Marketing / clinical | Case studies published. |

### 33.7 Stop-the-line conditions

Each phase has explicit stop-the-line conditions that override the schedule:

* Any Sev-1 patient-safety incident attributable to the product.
* A regulatory finding that requires re-classification.
* A confirmed PHI breach.
* A critical CVE in a P0 component with no available patch.
* A documented bias finding above the published threshold in any clinical evaluation.

When triggered, all feature work stops until a documented root-cause analysis, mitigation, and verification pass complete and are signed off by the clinical safety working group.

---

## 34. Example Product Modules (Enhanced)

The modules below are the concrete user-facing products that combine the services in §24. Each is a complete user surface with its own UX, telemetry, and acceptance gates.

### 34.1 Report Composer

The primary radiologist workspace.

**Surfaces:** Web Workspace, Desktop Studio, Mobile Companion (read + dictate).

**Capabilities**

* Multi-pane layout: Study Context, Findings editor, Impression editor, Validation, Priors, Tools.
* Hotkey-driven everything (every action has a default binding; user-customizable).
* Dictation modes: command-mode (verbal commands), free-dictation, structured-field-fill.
* AI actions: Generate Draft, Generate Impression, Rewrite (style modes), Make Concise, Translate, Patient-Friendly Summary, Referring-Physician Summary.
* Smart prior linking: surface the relevant snippet from the most recent comparable prior with diff highlighting.
* Inline citations: every AI claim can be expanded to show the source span in input, prior, or evidence linker.
* Confidence banners: per-finding "needs review" markers driven by validation, not raw model confidence.
* Auto-save with conflict-free replicated drafts (CRDT) for resilience across devices.
* One-click export to PACS/RIS via clipboard, FHIR push, HL7 ORU, or direct connector.

**Performance budgets:** action ≤ 100 ms (local), AI action ≤ 5 s P95, panel switch ≤ 50 ms.

### 34.2 Prompt Studio

The admin and medical-director environment for prompt and rulebook authoring.

**Capabilities**

* Visual prompt-block editor with type-aware fields, autocomplete on rulebook keys, and live preview.
* Side-by-side diff of prompt versions with token-level annotations.
* Test-case library: golden inputs, expected outputs, tolerance bands.
* Run on any registered provider; compare outputs across providers in a single grid.
* "Promotion lane" UI: Draft → Review → Approved → Deprecated, with required reviewers configurable per tenant.
* Inline lint: forbidden phrases, unsafe instruction patterns, prompt-injection risk hints, length budget.
* Output schema designer with JSON Schema export.

### 34.3 Rulebook Center

For clinical governance.

**Capabilities**

* Hierarchical rulebooks: global → tenant → department → user.
* RADS modules (BI/LI/PI/Lung/TI/O-RADS) with calculator presets.
* Required-section, forbidden-phrase, style, laterality, measurement, and follow-up-language rules.
* "Critical finding language" library with site-specific phrasing.
* Rulebook regression tests with diff viewer; promotion blocked on regression.
* Bulk import/export as YAML/JSON; signed bundles for marketplace.
* Inheritance visualizer showing exactly which rule applies to a given study.

### 34.4 AI Gateway Console

For provider control.

**Capabilities**

* Provider registry with compliance class, BAA/DPA metadata, endpoint catalog, model versions.
* Per-route policies: tenant, modality, PHI status, cost ceiling, latency SLO, fallback chain.
* Live cost and latency telemetry with anomaly alerts.
* Sandbox runner for evaluating new models against the tenant's golden cases.
* BYOK + OAuth subscription + local-model + private-endpoint adapters.
* Hard-block table: never-route combinations made impossible by configuration, not just policy.

### 34.5 Desktop Studio

For daily clinical use.

**Capabilities**

* Global command palette (Ctrl/Cmd-Shift-P).
* Hotkey overlay that floats above PACS/RIS without focus-stealing.
* Local daemon manager with live health and resource view.
* Offline drafts with sync-on-reconnect.
* Local model picker, including model-size and quantization presets per hardware profile.
* Fleet-managed configuration (MDM-friendly).
* Hardware-aware perf: small-RAM mode disables advanced features rather than failing.

### 34.6 Governance Dashboard

For enterprise oversight.

**Capabilities**

* Live model and prompt/rulebook inventory.
* PHI routing posture with per-route compliance status.
* Validation pass-rate and drift trends.
* Per-radiologist and per-department adoption and edit-distance.
* Incident review timeline with linkable audit events.
* Export to board-ready PDF with signed evidence.

### 34.7 Worklist Optimizer

For chief radiologists, leads, and teleradiology coordinators.

**Capabilities**

* Priority scoring with configurable inputs (modality, indication, SLA, referrer, criticality).
* Load-balancing across radiologists with skill matching (subspecialty, language, location).
* SLA dashboard with predicted breach alerts.
* Drag-and-drop reassignment with audit trail.
* Burnout-aware allocation: per-user soft caps, fatigue signals, encouraged breaks.

### 34.8 Teaching Files & Peer Review

For academic departments and quality programs.

**Capabilities**

* De-identification pipeline (DICOM + text) with reviewer checklist.
* RSNA MIRC-compatible export and import.
* Quiz mode for residents with self-assessment.
* Blinded peer-review queue with RADPEER-style scoring.
* Concordance analytics; aggregate scores never expose individual identity by default.

### 34.9 Mobile Companion

For on-call and review-on-the-go.

**Capabilities**

* Read-only secure report review.
* Voice dictation with offline whisper.cpp.
* Critical-results acknowledgement workflow.
* Push notifications for assignment and critical results.
* Biometric unlock; no PHI on-device beyond an encrypted ephemeral cache.

### 34.10 Plugin Marketplace

For community extension.

**Capabilities**

* Browse, install, update, and remove plugins.
* Signature + provenance verification before install.
* Capability manifest review (what the plugin can read / write / call).
* Sandbox preview before activation.
* Conformance status badge.
* Revenue share for community authors (Enterprise edition).

### 34.11 Research Workbench

For clinical researchers and evidence teams.

**Capabilities**

* De-identification jobs across cohorts.
* Query language over reports + structured fields.
* Cohort builder with FHIR + DICOM filters.
* Export to OMOP, i2b2, and CSV with audit and IRB-evidence packs.
* Hooks into federated learning rounds.

### 34.12 Air-Gapped Operator Console

For high-security and military / government deployments.

**Capabilities**

* Local-only configuration with signed update bundles.
* Mirror manager for SBOM, dependencies, models, and rulebooks.
* Hardware HSM integration with PKCS#11.
* Offline license and metering with reconciliation packets.
* Tamper-evident logs with WORM media support.

---

## 35. Competitive Differentiation

RadioPad's position in the radiology AI reporting market is defined by a small number of properties that no current proprietary product holds together. Each property below is a deliberate, hard-to-copy design choice.

1. **Truly open-source, end-to-end.** Apache-2.0 core + AGPL-3.0 clinical safety modules. No "open core, closed brain" — the validation engine, rulebook engine, AI gateway, audit log, and inference layer are all open. Enterprise revenue funds development; enterprise features are additive, not exclusionary.

2. **Rulebook-first AI reporting.** RadioPad treats reporting policy as versioned, testable, signed configuration — not vibes. Rulebooks pass regression tests before they touch production. Comparable products either hard-code rules or rely on opaque prompt engineering.

3. **Radiology-specific deterministic validation layer.** Laterality, negation, measurement, modality, anatomy, contradiction, and unsupported-claim checks are first-class deterministic checks, not LLM judgments. The AI augments the deterministic checks; it never replaces them for safety.

4. **Desktop + CLI + Web + Mobile + PWA + air-gapped, on day one.** Most products ship a browser. RadioPad ships native Tauri apps, a Rust CLI, a Rust daemon, a PWA, and an air-gapped installer. Every surface is a real product with its own performance budgets.

5. **Lightweight by contract.** Desktop ≤ 80 MB installed, daemon ≤ 30 MB RSS, CLI ≤ 25 MB compressed, web TTI ≤ 1.8 s P95. Budgets are CI-enforced. Bloat fails the build.

6. **Compliance-aware provider routing with hard blocks.** Provider class is a config-driven invariant. Fallback to a lower-compliance provider is impossible by construction, not by policy.

7. **Local-first option with shipping bundled OSS models.** llama.cpp + whisper.cpp + GGUF model bundles. Zero-egress workflows are a supported product mode, not a future possibility.

8. **Standards-aware to a fault.** DICOMweb, FHIR (US Core + IHE Radiology profiles), HL7 v2, RadLex, RadElement, LOINC, SNOMED CT, ACR Common, RSNA RadReport. Conformance is tested in CI, not asserted in marketing.

9. **Auditable to the byte.** Hash-chained audit log, OpenTelemetry traces, SBOMs per release, SLSA-3 provenance, Sigstore signatures, reproducible builds. Every claim is verifiable.

10. **Subspecialty depth.** 15 subspecialty modules shipping with reference rulebooks, calculators, and golden cases at GA. Each is OSS and extensible.

11. **Real performance engineering.** Latency SLOs per action, perf-gated CI, profiling baked in, budget tracking in dashboards.

12. **Community governance with paid maintainership.** Public RFC process, working groups, named maintainers, funded by EE revenue. No "throw it over the wall" OSS.

13. **AI safety as a working group, not a slide.** Adversarial test packs, hallucination evals, bias evals, robustness tests run on every release; baseline scores are public.

14. **Federated learning and privacy-preserving evidence loops.** Differentially private aggregation, secure aggregation, documented ε budgets.

15. **Interoperable export of clinical evidence.** Every release publishes a signed evaluation report that customers can present to regulators.

16. **No vendor lock-in.** BYOK, BYOM (bring your own model), data export, audit export, rulebook export — all are first-class commands, documented and tested.

17. **Multi-language, multi-jurisdiction.** Nine UI locales, jurisdiction-aware rulebooks, locale-specific clinical evidence.

18. **Plugin ecosystem with revenue share.** Community contributors can be paid through the marketplace; conformance program ensures plugin quality.

19. **Sustainability commitments.** LTS branches, reproducible builds, multi-vendor maintainership, 24-month security backports, documented succession.

20. **Healthcare-grade defaults everywhere.** Encryption, MFA, audit, redaction, retention, breach playbooks, regulatory pack templates — defaults are clinical-safe, not developer-convenient.

---

## 36. Glossary

| Term | Definition |
|------|------------|
| **AGPL-3.0** | Affero GPL v3, copyleft license that requires source disclosure for network-deployed modifications; used for clinical safety modules where derivative obligations matter. |
| **Air-gapped deployment** | A deployment topology in which RadioPad has no network egress; updates, models, and SBOMs arrive via signed offline bundles. |
| **AI Gateway** | The service that brokers all inference requests, enforces policy, applies fallback chains, and emits telemetry. |
| **Apache 2.0** | Permissive OSS license; used for the RadioPad core. |
| **Audit event** | An immutable, hash-chained record describing a clinically or operationally significant action. |
| **BAA** | Business Associate Agreement, the HIPAA contract that permits PHI processing by a vendor. |
| **BYOK / BYOM** | Bring Your Own Key / Bring Your Own Model: customer-supplied encryption keys or AI models. |
| **CE (Community Edition)** | The Apache + AGPL OSS distribution of RadioPad, free to use under license terms. |
| **CRDT** | Conflict-free Replicated Data Type, used for offline-safe draft editing. |
| **DCO** | Developer Certificate of Origin, the per-commit sign-off used in lieu of a CLA. |
| **DICOMweb** | The DICOM standard for RESTful access to imaging data (QIDO-RS, WADO-RS, STOW-RS). |
| **Drift** | Quality degradation of an AI model or rulebook over time, detected via golden-case regressions and live KPIs. |
| **EE (Enterprise Edition)** | The commercial distribution of RadioPad with additional enterprise modules, support, and indemnification. |
| **Evidence linker** | The module that attaches citations (RadLex, SNOMED CT, LOINC, literature) to AI-generated text. |
| **FHIR** | Fast Healthcare Interoperability Resources, the HL7 standard used for DiagnosticReport, ImagingStudy, and related exchanges. |
| **Golden case** | A curated, de-identified study with expected outputs used to gate prompt and rulebook changes. |
| **HITRUST i1 / r2** | HITRUST CSF certification levels for healthcare information assurance. |
| **HL7 v2** | The pre-FHIR HL7 messaging standard; ORU^R01 is the primary report message. |
| **IEC 62304** | Medical device software lifecycle standard. |
| **Intended use** | A regulatory statement defining what RadioPad is and is not, controlling its risk classification. |
| **Local daemon** | The Rust-based background process running on radiologist machines that enforces policy and brokers AI calls. |
| **LTS** | Long-Term Support release with 24-month security backports. |
| **MCP** | Model Context Protocol, the open protocol for tool/data integration with LLMs. |
| **MIRC** | Medical Imaging Resource Center, RSNA's teaching files standard. |
| **NLP** | Natural Language Processing. |
| **OCSF** | Open Cybersecurity Schema Framework, an open standard for security event logging. |
| **OTel / OpenTelemetry** | The open standard for telemetry (logs, metrics, traces). |
| **PCCP** | Predetermined Change Control Plan, an FDA mechanism for pre-approving AI updates. |
| **PHI** | Protected Health Information under HIPAA. |
| **RADS** | Reporting and Data Systems (BI-RADS, LI-RADS, PI-RADS, Lung-RADS, TI-RADS, O-RADS), ACR's standardized reporting frameworks. |
| **RadElement** | RSNA's common data element registry. |
| **RadLex** | RSNA's radiology lexicon. |
| **RadReport** | RSNA's reviewed radiology reporting template library. |
| **RECIST** | Response Evaluation Criteria in Solid Tumors. |
| **Rulebook** | A versioned, signed, testable package that controls report generation policy. |
| **SaMD** | Software as a Medical Device. |
| **SBOM** | Software Bill of Materials; CycloneDX is RadioPad's format of record. |
| **SCIM** | System for Cross-domain Identity Management, used for enterprise provisioning. |
| **Sigstore / Cosign** | OSS signing infrastructure for binaries and containers. |
| **SLO / SLA / SLI** | Service Level Objective / Agreement / Indicator. |
| **SLSA** | Supply-chain Levels for Software Artifacts; RadioPad targets SLSA-3. |
| **SOUP** | Software of Unknown Provenance, the IEC 62304 term for third-party components. |
| **Studio (Desktop Studio)** | The Tauri-based desktop application. |
| **Subspecialty module** | A bundled package of rulebooks, templates, calculators, and golden cases for a clinical subspecialty. |
| **Validation engine** | The deterministic + AI-assisted layer that checks reports against rulebook policy. |
| **WORM** | Write Once Read Many, used for tamper-evident audit storage. |

---

## 37. Appendices

### Appendix A — OSS Dependency Manifest (Selected)

A subset of the full dependency manifest, illustrating the OSS-first stack. The complete SBOM is published per release in CycloneDX 1.5 format.

| Component | Purpose | License | Notes |
|-----------|---------|---------|-------|
| React 18 + Vite | Web frontend | MIT | Strict bundle budgets. |
| Tauri 2 | Desktop shell | Apache-2.0 / MIT | Native, small footprint. |
| Rust toolchain | Daemon, CLI, AI gateway hot path | Apache-2.0 / MIT | Reproducible builds. |
| Tokio | Async runtime | MIT | Pinned to LTS. |
| Axum | Rust HTTP server | MIT | Used in gateway. |
| Go 1.22+ | Service implementations | BSD-3-Clause | |
| Node.js 20 LTS | DX tooling, some services | MIT | LTS only. |
| PostgreSQL 16 | Primary database | PostgreSQL License | |
| SQLite | Local store on edge | Public domain | |
| ClickHouse | Analytics warehouse | Apache-2.0 | |
| NATS | Messaging | Apache-2.0 | JetStream enabled. |
| MinIO | Object storage | AGPL-3.0 | S3 API. |
| Keycloak | Identity, OIDC, SAML, SCIM | Apache-2.0 | |
| OpenSearch | Search | Apache-2.0 | |
| pgvector | Vector search | PostgreSQL License | |
| OpenTelemetry | Telemetry | Apache-2.0 | |
| Prometheus | Metrics | Apache-2.0 | |
| Grafana | Dashboards | AGPL-3.0 | |
| Loki | Logs | AGPL-3.0 | |
| Tempo | Traces | AGPL-3.0 | |
| Sigstore Cosign | Signing | Apache-2.0 | |
| Syft + Grype | SBOM + vuln scan | Apache-2.0 | |
| llama.cpp | Local LLM runtime | MIT | GGUF Q4_K_M defaults. |
| whisper.cpp | Local STT | MIT | Domain-tuned models. |
| ONNX Runtime | Cross-runtime inference | MIT | |
| vLLM | High-throughput server inference | Apache-2.0 | Optional. |
| dcm4che | DICOM toolkit | MPL-2.0 + LGPL | DICOMweb reference. |
| HAPI FHIR | FHIR toolkit | Apache-2.0 | |
| Mirth Connect | HL7 v2 integration | MPL-2.0 | |
| Lago | Open billing engine | AGPL-3.0 | |
| Pagefind | Docs search | MIT | No third-party JS. |
| Playwright | E2E testing | Apache-2.0 | |
| k6 | Load testing | AGPL-3.0 | |
| OWASP ZAP | DAST | Apache-2.0 | |
| Trivy | Container scanning | Apache-2.0 | |
| Falco | Runtime security | Apache-2.0 | |
| Cilium | Network policy / observability | Apache-2.0 | |

### Appendix B — Performance Benchmarks (Reference Hardware)

Benchmarks are measured on the documented reference profiles. The "modest" profile is a 4-core, 8 GB laptop; the "standard" profile is an 8-core, 16 GB workstation; the "server" profile is a 16-core, 64 GB node with optional GPU.

| Operation | Modest P95 | Standard P95 | Server P95 |
|-----------|-----------|--------------|------------|
| Desktop Studio cold start | 1.5 s | 1.0 s | 0.8 s |
| Web app TTI | 2.5 s | 1.8 s | 1.4 s |
| Daemon idle RSS | 30 MB | 30 MB | 30 MB |
| Daemon steady-state RSS | 120 MB | 150 MB | 200 MB |
| Findings → Impression (local 7B Q4) | 8 s | 5 s | 3 s |
| Findings → Impression (cloud) | 4 s | 3 s | 2 s |
| Validation (deterministic) | 200 ms | 100 ms | 60 ms |
| Validation (full, with AI checks) | 4 s | 2.5 s | 1.5 s |
| Voice dictation start | 600 ms | 400 ms | 300 ms |
| Voice dictation WER (English radiology) | 13% | 11% | 10% |
| Rulebook regression (100 cases) | 90 s | 50 s | 25 s |
| Audit event write | 8 ms | 5 ms | 3 ms |
| Export to FHIR DiagnosticReport | 600 ms | 400 ms | 250 ms |
| Export to PDF | 1.2 s | 800 ms | 500 ms |
| CLI startup | 50 ms | 30 ms | 20 ms |

### Appendix C — SLO / SLA Matrix

| Service | SLI | SLO | SLA (EE) | Error budget |
|---------|-----|-----|----------|--------------|
| Web app availability | Successful HTTP / total HTTP | 99.95% | 99.9% credit | 0.05% / 30 d |
| AI Gateway inference success | Successful inference / total | 99.5% | 99.0% credit | 0.5% / 30 d |
| Audit write availability | Successful writes / total | 99.99% | 99.95% credit | 0.01% / 30 d |
| FHIR export availability | Successful exports / total | 99.9% | 99.5% credit | 0.1% / 30 d |
| Critical-results latency | ack-to-notify ≤ 30 s | 99.0% | 95.0% credit | 1.0% / 30 d |
| Validation latency P95 | < 3 s | 99.0% | 95.0% credit | 1.0% / 30 d |
| Identity availability | Successful auth / total | 99.95% | 99.9% credit | 0.05% / 30 d |
| Desktop daemon uptime | Healthy minutes / total | 99.5% | 99.0% credit | 0.5% / 30 d |

Credits are documented in the EE SLA. CE makes no SLA promises but publishes the same SLIs on a community status page.

### Appendix D — Compliance Mapping (Selected)

| Framework | Coverage | Owning section |
|-----------|----------|----------------|
| HIPAA Security Rule (administrative, physical, technical safeguards) | Full | §18 |
| HIPAA Privacy Rule (uses, disclosures, minimum necessary) | Tenant-configurable | §18 |
| HIPAA Breach Notification Rule | Playbooks + workflows | §27 |
| HITECH | BAA + breach | §18 |
| GDPR (data subject rights, lawful basis, transfer mechanisms) | Full | §18 |
| UK DPA 2018 | Mapped via GDPR | §18 |
| KSA PDPL | Localization + DPO | §18 |
| EU AI Act (high-risk obligations) | Technical doc pack | §19 |
| ISO 27001 / 27017 / 27018 | Information security management | §18 |
| ISO 13485 | Medical device QMS (when applicable) | §19 |
| IEC 62304 | Medical device software lifecycle | §19 |
| IEC 82304 | Health software product safety | §19 |
| NIST AI RMF 1.0 | AI risk management | §16, §19 |
| NIST SSDF | Secure software development | §28 |
| SOC 2 Type II (Security, Availability, Confidentiality) | Full | §18, §28 |
| HITRUST i1 / r2 | Readiness then certification | §18 |
| FDA AI/ML SaMD guidance | Intended use + PCCP | §19 |
| MHRA AI as a Medical Device | Position monitored | §19 |
| HDS (France) | Hosting option | §12 |
| WCAG 2.2 AA | Accessibility | §20 |
| C2PA | Provenance for exported documents | §16 |

### Appendix E — API Examples

```http
POST /v1/reports/draft
Authorization: Bearer <token>
Content-Type: application/json

{
  "study_ref": "study/12345",
  "modality": "CT",
  "body_part": "Chest",
  "indication": "Dyspnea, evaluate for PE",
  "rulebook": "chest_ct_v1@1.3.0",
  "inputs": {
    "dictation": "1.2 cm spiculated nodule right upper lobe...",
    "measurements": [
      {"site": "RUL nodule", "value": 1.2, "unit": "cm"}
    ],
    "priors": ["report/9988"]
  },
  "policy": {
    "phi": true,
    "max_cost_usd": 0.10,
    "max_latency_ms": 6000
  }
}
```

```http
HTTP/1.1 200 OK
Content-Type: application/json
X-Audit-Id: 01HZ...
X-Trace-Id: 1d4f...

{
  "draft_id": "draft_01HZ...",
  "rulebook_version": "chest_ct_v1@1.3.0",
  "model": "local/llama-3-8b-instruct-q4_k_m",
  "sections": {
    "indication": "Dyspnea; evaluate for pulmonary embolism.",
    "technique": "...",
    "findings": "...",
    "impression": [
      "1.2 cm spiculated nodule, RUL — suspicious; recommend follow-up per Lung-RADS.",
      "No pulmonary embolism."
    ]
  },
  "validation": {
    "passed": true,
    "warnings": []
  }
}
```

```bash
# CLI: validate a rulebook
radiopad rulebook validate ./chest_ct_v1.yaml

# CLI: run regression
radiopad rulebook test ./chest_ct_v1.yaml --cases ./golden-cases --report results.json

# CLI: generate from local input
radiopad generate \
  --template chest-ct \
  --rulebook chest_ct_v1@1.3.0 \
  --input findings.txt \
  --output draft.json \
  --provider local

# CLI: audit export
radiopad audit export --from 2026-05-01 --to 2026-05-31 --format ndjson --out audit.ndjson

# CLI: provider health
radiopad ai providers status
```

```yaml
# Example minimal rulebook
rulebook_id: chest_ct_v1
version: 1.3.0
status: approved
applies_to:
  modalities: ["CT"]
  body_parts: ["Chest"]
required_sections: [Indication, Technique, Comparison, Findings, Impression]
style:
  tone: concise_clinical
  impression_max_bullets: 5
  avoid_terms: ["unremarkable", "cannot rule out"]
rules:
  - id: laterality_consistency
    severity: blocker
  - id: lung_rads_followup_language
    severity: blocker
prompt_blocks:
  findings_to_impression: "Generate a concise impression..."
output_schema:
  $ref: "schemas/report_v1.json"
```

---

## 38. Final Product Definition

RadioPad is an **open-source AI reporting operating system for radiology**. It is engineered to be:

1. **Worldclass in quality** — clinically governed, standards-aware, rigorously evaluated, with safety as a first-class engineering discipline rather than a marketing claim.

2. **100% performance-based** — every action has a measured latency budget, every binary has a size budget, every service has an SLO, and every PR runs the budget gates. Performance is a CI gate, not a stretch goal.

3. **Cross-compatible** — first-class native experiences on Linux (x86_64 + ARM64), Windows (x86_64 + ARM64), macOS (Intel + Apple Silicon), iOS, Android, and any modern browser via PWA. Desktop, CLI, daemon, web, and mobile are real products, not afterthoughts.

4. **Lightweight by contract** — Desktop ≤ 80 MB installed, daemon ≤ 30 MB RSS, CLI ≤ 25 MB compressed, web TTI ≤ 1.8 s P95 on reference hardware. The bundled local model pack ships in a small footprint with quantized GGUF defaults.

5. **Open-source, free to use** — Apache-2.0 core, AGPL-3.0 clinical safety modules, free Community Edition with no feature crippling on safety-critical paths. Optional Enterprise Edition adds operability, indemnification, and certified support, funded openly.

6. **Radiologist-in-the-loop, always** — RadioPad assists; it does not diagnose, it does not sign, and it never bypasses the radiologist. The clinical safety boundary is enforced by deterministic checks before any AI judgment is trusted.

7. **Compliant by design** — HIPAA, HITECH, GDPR, UK DPA, KSA PDPL, EU AI Act, ISO 27001/13485, IEC 62304/82304, NIST AI RMF, SOC 2, HITRUST. Compliance posture is published, tested, and audited.

8. **Built for the long term** — public RFC process, multi-vendor maintainership, LTS branches with 24-month backports, reproducible builds, and a community governance model funded by transparent enterprise revenue.

RadioPad combines NLP report generation, structured templates, signed rulebooks, deterministic validation, multi-provider AI orchestration, local-first execution, desktop and CLI power, mobile reach, standards-aware interoperability, federated and privacy-preserving learning, plugin extensibility, and enterprise-grade auditability — all delivered as open source with worldclass performance, cross-platform reach, a lightweight footprint, and zero lock-in.

**The product principle stands unchanged from v1.0 and is reinforced in v2.0:**

> RadioPad assists the radiologist; the radiologist owns the final report.

— *End of Document* —
