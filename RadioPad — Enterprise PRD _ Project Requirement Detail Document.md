# RadioPad — Enterprise PRD / Project Requirement Detail Document

**Product:** RadioPad **Category:** AI-assisted radiology reporting SaaS \+ Desktop \+ CLI companion **Document version:** v1.0 **Target release:** MVP → Clinical Beta → Enterprise GA **Primary users:** Radiologists, reporting administrators, imaging groups, hospitals, teleradiology providers, clinical AI governance teams

---

## 1\. Executive Summary

RadioPad is an AI-assisted NLP radiology reporting platform that helps radiologists create, refine, validate, and standardize reports using dictation, natural language instructions, custom prompts, reusable rulebooks, structured templates, terminology mappings, and multi-provider AI orchestration.

The product should be delivered as:

1. **Web App** — administrative console, reporting workspace, templates, rulebooks, analytics, collaboration, integrations.  
2. **Desktop App** — local companion for radiologists, dictation, hotkeys, PACS/RIS bridge, local storage controls, secure AI routing, CLI execution, offline-safe workflows.  
3. **CLI / Local Daemon** — enterprise-grade local execution layer that can connect to approved AI providers, local models, subscription-backed OAuth flows, and hospital-side tools.

The architectural inspiration from the referenced projects should be adapted into a healthcare-grade product pattern: jcode demonstrates subscription-backed OAuth provider access, Paseo demonstrates a local daemon with desktop/web/CLI clients, and nexu/open-design demonstrates local-first, BYOK, skill/rule-based extensibility, and CLI-driven AI surfaces. ([GitHub](https://github.com/1jehuang/jcode))

RadioPad must not be positioned as an autonomous diagnostic system. It should be positioned as a **radiologist-in-the-loop reporting productivity, quality, and standardization platform**. If the product generates diagnostic impressions, triage recommendations, or clinically meaningful suggestions, the regulatory strategy must evaluate whether it becomes Software as a Medical Device or equivalent regulated medical software in target markets. The FDA has specific AI-enabled medical device lifecycle guidance activity, and the EU AI Act treats AI software intended for medical purposes as high-risk with requirements such as risk mitigation, high-quality data, clear user information, and human oversight. ([U.S. Food and Drug Administration](https://www.fda.gov/medical-devices/software-medical-device-samd/artificial-intelligence-software-medical-device))

---

## 2\. Product Vision

### Vision Statement

RadioPad will become the enterprise AI reporting layer for radiology: a secure, configurable, clinically governed platform that converts radiologist intent, dictation, prior exams, measurements, rulebooks, and institution-specific reporting standards into high-quality draft reports that remain fully controlled, reviewed, and signed by licensed radiologists.

### North Star

Reduce radiologist reporting friction while improving consistency, completeness, turnaround time, report quality, and enterprise governance.

### Product Principles

| Principle | Meaning for RadioPad |
| :---- | :---- |
| Radiologist remains final authority | AI drafts, suggests, validates, and formats; it never signs or finalizes independently. |
| Configurable by institution | Hospitals and imaging groups can define templates, style guides, impression rules, escalation language, and modality-specific policies. |
| Local-first when needed | Desktop and CLI layers should support local execution, local PHI controls, and hybrid deployments. |
| Standards-aware | The product should support RSNA RadReport, RadLex, ACR RADS, DICOMweb, HL7/FHIR, and institution-defined templates. |
| Explainable and auditable | Every AI-generated section should be traceable to input context, prompt/rulebook version, model, and user edits. |
| Safe by design | Built-in guardrails, contradiction checks, hallucination detection, PHI controls, audit logs, access control, and model governance. |

---

## 3\. Market and Standards Context

Radiology reporting already has mature standardization efforts. RSNA’s RadReport provides reviewed radiology reporting templates and incorporates RadLex terminology and RadElement common data elements; RSNA also states that standards such as shared vocabulary, reporting templates, and common data elements help integrate with EHR systems, organize records, support personalized care, streamline workflows, and improve quality. ([RSNA](https://www.rsna.org/practice-tools/data-tools-and-standards/radreport-reporting-templates))

ACR RADS provides standardized frameworks for characterizing and reporting imaging findings, aiming to improve communication between radiologists and referring physicians and increase consistency in terminology. ([American College of Radiology](https://www.acr.org/Clinical-Resources/Clinical-Tools-and-Reference/Reporting-and-Data-Systems))

For interoperability, HL7 FHIR DiagnosticReport is suitable for imaging investigations such as X-ray, CT, and MRI, and can represent report conclusions as text, coded data, or formatted report attachments. ([FHIR](https://build.fhir.org/diagnosticreport.html)) DICOMweb is the DICOM standard for web-based medical imaging and provides RESTful services for modern access to DICOM-enabled systems. ([DICOM](https://www.dicomstandard.org/using/dicomweb))

For healthcare security, HIPAA’s Security Rule requires administrative, physical, and technical safeguards to protect electronic protected health information in the United States. ([HHS.gov](https://www.hhs.gov/hipaa/for-professionals/security/index.html)) Any OpenAI API usage involving PHI should require a proper BAA; OpenAI states that API use with PHI first requires a BAA, and Zero Data Retention / Modified Abuse Monitoring controls require approval and have endpoint-specific behavior and eligibility constraints. ([OpenAI Help Center](https://help.openai.com/en/articles/8660679-how-can-i-get-a-business-associate-agreement-baa-with-openai))

---

## 4\. Product Goals

### Business Goals

1. Launch a clinically credible AI reporting platform for radiology groups, hospitals, imaging centers, and teleradiology organizations.  
2. Support recurring SaaS subscriptions with enterprise plans, per-seat licensing, AI usage tiers, deployment add-ons, and integration fees.  
3. Differentiate with rulebook-driven report generation, local desktop/CLI execution, multi-provider AI orchestration, and institutional governance.  
4. Reduce customer dependence on one AI vendor by supporting provider abstraction, BYOK, approved enterprise APIs, local models, and OAuth-based workflows where compliant.  
5. Establish RadioPad as a reporting quality platform, not only a dictation assistant.

### Clinical Workflow Goals

1. Reduce report drafting time.  
2. Improve report completeness and consistency.  
3. Enforce institution-specific reporting standards.  
4. Reduce contradictions between findings and impression.  
5. Convert free dictation into structured, clean, clinically appropriate reports.  
6. Support subspecialty templates and rulebooks.

### Technical Goals

1. Provide secure web, desktop, and CLI experiences.  
2. Support hybrid cloud and local deployments.  
3. Maintain auditability for prompts, models, outputs, user edits, rulebook versions, and final reports.  
4. Provide standards-based integrations with PACS/RIS/EHR.  
5. Support configurable AI routing by modality, site, tenant, data sensitivity, model type, and compliance profile.

---

## 5\. Non-Goals

RadioPad v1 should not:

1. Autonomously diagnose studies without radiologist review.  
2. Replace PACS, RIS, EHR, or dictation systems entirely.  
3. Claim FDA clearance, CE marking, or equivalent regulatory status unless obtained.  
4. Use patient data for model training without explicit contractual, legal, governance, and tenant-level approval.  
5. Send PHI to consumer AI services or non-approved subscription/OAuth flows.  
6. Auto-sign radiology reports.  
7. Automatically order follow-up tests without physician confirmation.  
8. Provide patient-facing diagnostic advice in MVP.

---

## 6\. Core User Personas

### 6.1 Radiologist

**Needs:** Fast report drafting, dictation cleanup, impression generation, comparison with prior exams, error detection, custom report style, hotkeys, low friction.

**Pain Points:** Repetitive reports, inconsistent templates, long turnaround time, dictation errors, manual impression writing, missing measurements, report contradictions.

### 6.2 Reporting Administrator

**Needs:** Manage templates, macros, rulebooks, user roles, department standards, subspecialty report styles.

**Pain Points:** Template drift, inconsistent language, lack of reporting governance, difficulty enforcing rules across teams.

### 6.3 Chief Radiologist / Medical Director

**Needs:** Quality metrics, adoption analytics, safety monitoring, subspecialty standardization, governance approvals.

**Pain Points:** Variation in reporting, medico-legal risk, missing critical finding language, untracked AI use.

### 6.4 Hospital IT / Security

**Needs:** SSO, RBAC, audit logs, encryption, deployment control, BAA/vendor review, network boundaries, integration standards.

**Pain Points:** PHI leakage, unknown AI tools, lack of auditability, non-compliant integrations.

### 6.5 AI Governance / Compliance Team

**Needs:** Model inventory, prompt versioning, validation data, monitoring, drift detection, incident review, approval workflows.

**Pain Points:** Shadow AI usage, inability to reproduce outputs, lack of model lifecycle documentation.

---

## 7\. Product Architecture Overview

RadioPad should use a **hybrid control-plane \+ local execution architecture**.

                  ┌─────────────────────────────────────────┐

                  │             RadioPad Cloud              │

                  │  Tenant Admin, Rulebooks, Analytics,    │

                  │  Model Registry, Billing, Audit, APIs   │

                  └─────────────────────────────────────────┘

                                  │

                     Secure API / WebSocket / mTLS

                                  │

┌─────────────────────┐     ┌────────────────────────┐     ┌────────────────────┐

│ RadioPad Web App    │     │ RadioPad Desktop App   │     │ RadioPad CLI       │

│ Reporting Workspace │     │ Local Companion        │     │ Automation \+ Ops   │

│ Admin Console       │     │ Dictation \+ PACS/RIS   │     │ Provider Harness   │

└─────────────────────┘     └────────────────────────┘     └────────────────────┘

                                  │

                            Local Daemon

                                  │

        ┌───────────────┬─────────┴──────────┬────────────────┐

        │ PACS/RIS/EHR  │ Local AI Models     │ AI Provider APIs │

        │ Connectors    │ Approved On-Prem    │ OAuth/BYOK/API   │

        └───────────────┴────────────────────┴────────────────┘

### 7.1 Key Architectural Pattern

The linked tools suggest a powerful pattern: local daemon \+ multi-client surface \+ provider orchestration. Paseo’s README describes a local daemon that manages agents, with desktop, mobile, web, and CLI clients connecting to it; jcode describes subscription-backed OAuth flows and provider fallback; nexu/open-design emphasizes local-first, BYOK, skills, and no lock-in. ([GitHub](https://github.com/getpaseo/paseo))

RadioPad should adapt this into a healthcare-safe model:

| Layer | Responsibility |
| :---- | :---- |
| Cloud Control Plane | Tenants, users, billing, rulebook registry, model policies, audit search, analytics, admin settings. |
| Desktop App | Local reporting workspace, dictation, hotkeys, DICOM/PACS bridge, secure clipboard, local daemon UI. |
| Local Daemon | CLI execution, provider adapters, local PHI policy enforcement, tool registry, logs, model routing. |
| AI Gateway | Routes requests to approved models based on tenant policy, PHI status, cost, latency, and quality. |
| Rulebook Engine | Applies prompt templates, structured schemas, findings/impression rules, terminology mappings. |
| Integration Layer | DICOMweb, HL7/FHIR, RIS/PACS/EHR export, webhooks, SSO, billing. |

---

## 8\. Deployment Models

### 8.1 SaaS Cloud

Best for imaging centers and smaller groups.

* Hosted RadioPad cloud.  
* Browser-based reporting.  
* Optional desktop app.  
* API-based AI vendors only.  
* Strict PHI controls and tenant encryption.

### 8.2 Hybrid Enterprise

Best for hospitals.

* Cloud control plane.  
* Local desktop/daemon inside hospital network.  
* PACS/RIS integrations remain local.  
* AI routing can use approved cloud APIs, local models, or private endpoints.  
* Cloud receives metadata and audit records according to tenant policy.

### 8.3 Private Cloud / On-Prem

Best for highly regulated deployments.

* Full stack deployed into customer VPC or on-prem Kubernetes.  
* Local model gateway.  
* Customer-managed keys.  
* No external AI provider unless explicitly configured.  
* Enterprise support contract.

---

## 9\. Scope by Release

### MVP — Reporting Copilot

**Primary objective:** Produce useful, safe, editable AI-assisted draft reports.

Included:

1. User authentication and tenant workspace.  
2. Web reporting editor.  
3. Desktop companion alpha.  
4. Dictation text input and pasted findings.  
5. AI draft generation.  
6. Prompt presets.  
7. Basic rulebooks.  
8. Report template library.  
9. Findings → Impression generation.  
10. Impression cleanup.  
11. Contradiction checker.  
12. Export to clipboard, PDF, DOCX, plain text, and FHIR DiagnosticReport payload.  
13. Audit logs.  
14. Admin panel for templates and prompts.  
15. Model/provider configuration.  
16. Usage metering and subscription plans.

### Beta — Clinical Workflow Integrations

Included:

1. PACS/RIS worklist connector.  
2. DICOMweb metadata retrieval.  
3. HL7/FHIR export.  
4. Advanced rulebook editor.  
5. Voice dictation and command mode.  
6. Prior report comparison.  
7. Measurements extraction.  
8. Critical finding language enforcement.  
9. Report quality score.  
10. Role-based approvals for rulebooks.  
11. Desktop daemon stable release.  
12. CLI automation for templates, rulebooks, and batch validation.

### Enterprise GA

Included:

1. SSO/SAML/OIDC.  
2. SCIM provisioning.  
3. Customer-managed encryption keys.  
4. Advanced audit search.  
5. Enterprise AI governance dashboard.  
6. Model evaluation harness.  
7. Site-specific model routing.  
8. Federated/on-prem deployment.  
9. Multilingual support.  
10. Advanced analytics.  
11. Versioned clinical validation packs.  
12. Legal/compliance exports.  
13. Marketplace for approved rulebooks and subspecialty templates.

---

## 10\. Functional Requirements

### 10.1 Authentication, Tenancy, and Access Control

| ID | Requirement | Priority |
| :---- | :---- | :---- |
| AUTH-001 | Support email/password, magic link, SSO/OIDC, and SAML for enterprise tenants. | P0 |
| AUTH-002 | Support role-based access control: Radiologist, Admin, Medical Director, Compliance Reviewer, IT Admin, Billing Admin. | P0 |
| AUTH-003 | Support tenant isolation at application, database, storage, cache, and audit layers. | P0 |
| AUTH-004 | Support MFA enforcement by tenant policy. | P0 |
| AUTH-005 | Support SCIM provisioning and deprovisioning for enterprise. | P1 |
| AUTH-006 | Support emergency account lockout and session revocation. | P0 |
| AUTH-007 | Support device trust policies for desktop app and local daemon. | P1 |

---

### 10.2 Reporting Workspace

| ID | Requirement | Priority |
| :---- | :---- | :---- |
| RPT-001 | Provide a report editor with sections: Indication, Technique, Comparison, Findings, Impression, Recommendations. | P0 |
| RPT-002 | Allow tenant-specific section layouts. | P0 |
| RPT-003 | Allow free text, template-based, and structured field entry. | P0 |
| RPT-004 | Support “Generate Draft Report” from dictation, notes, measurements, and study metadata. | P0 |
| RPT-005 | Support “Generate Impression” from Findings. | P0 |
| RPT-006 | Support “Rewrite in my style.” | P1 |
| RPT-007 | Support “Make concise,” “Make formal,” “Patient-friendly,” and “Referring physician summary” modes. | P1 |
| RPT-008 | Highlight AI-generated text until reviewed or edited. | P0 |
| RPT-009 | Support side-by-side prior report comparison. | P1 |
| RPT-010 | Provide one-click copy to RIS/PACS reporting system. | P0 |
| RPT-011 | Provide export as plain text, PDF, DOCX, JSON, and FHIR DiagnosticReport. | P0 |
| RPT-012 | Require radiologist acknowledgement before final export/signing. | P0 |

---

### 10.3 NLP and AI Report Generation

| ID | Requirement | Priority |
| :---- | :---- | :---- |
| AI-001 | Convert raw dictation into clean report sections. | P0 |
| AI-002 | Generate report drafts from structured inputs, free text, measurements, and prior report snippets. | P0 |
| AI-003 | Generate impression from findings while preserving clinical meaning. | P0 |
| AI-004 | Detect contradictions between findings and impression. | P0 |
| AI-005 | Detect missing required sections based on template/rulebook. | P0 |
| AI-006 | Detect laterality conflicts, measurement mismatches, and modality/body-part mismatch. | P1 |
| AI-007 | Detect uncertain, unsupported, or hallucinated claims. | P1 |
| AI-008 | Suggest follow-up language based on tenant-approved rulebooks only. | P1 |
| AI-009 | Support custom system prompts, specialty prompts, user prompts, and case-level instructions. | P0 |
| AI-010 | Support model routing by tenant, user role, modality, PHI policy, and cost threshold. | P0 |
| AI-011 | Support local model execution for de-identified or PHI-sensitive workflows. | P1 |
| AI-012 | Maintain full traceability of prompt version, rulebook version, model, input, output, and user edits. | P0 |

---

### 10.4 Rulebooks and Prompt Engineering System

RadioPad’s core differentiator should be its **Rulebook Engine**.

A rulebook is a versioned, testable, institution-approved configuration package that controls how AI generates, validates, and rewrites reports.

#### Rulebook Components

rulebook\_id: chest\_ct\_v1

name: Chest CT Reporting Rulebook

version: 1.0.0

owner: Thoracic Imaging Committee

status: approved

applies\_to:

  modalities: \["CT"\]

  body\_parts: \["Chest"\]

  report\_types: \["diagnostic", "follow\_up"\]

style:

  tone: "concise\_clinical"

  impression\_max\_bullets: 5

  avoid\_terms:

    \- "unremarkable"

    \- "cannot rule out"

required\_sections:

  \- Indication

  \- Technique

  \- Comparison

  \- Findings

  \- Impression

rules:

  \- id: laterality\_consistency

    severity: blocker

    description: "Left/right findings must match impression."

  \- id: measurement\_consistency

    severity: warning

    description: "Nodule measurements must match across sections."

  \- id: critical\_result\_language

    severity: blocker

    description: "Use approved critical finding language."

prompt\_blocks:

  system: "You are assisting a board-certified radiologist..."

  findings\_to\_impression: "Generate a concise impression..."

  cleanup: "Improve grammar without changing clinical meaning..."

output\_schema:

  type: object

  properties:

    indication: string

    technique: string

    comparison: string

    findings: string

    impression: array

#### Rulebook Requirements

| ID | Requirement | Priority |
| :---- | :---- | :---- |
| RB-001 | Create, edit, clone, archive, and version rulebooks. | P0 |
| RB-002 | Support YAML/JSON rulebook source editing and visual editing. | P1 |
| RB-003 | Support approval workflow: Draft → Review → Approved → Deprecated. | P0 |
| RB-004 | Support test cases for each rulebook. | P0 |
| RB-005 | Support prompt blocks, output schemas, style rules, forbidden language, required sections, and validation rules. | P0 |
| RB-006 | Support modality-specific and subspecialty-specific rulebooks. | P0 |
| RB-007 | Support tenant-level, department-level, and user-level inheritance. | P1 |
| RB-008 | Support rollback to prior approved versions. | P0 |
| RB-009 | Capture rulebook version in every AI event audit log. | P0 |
| RB-010 | Prevent unapproved rulebooks from being used in production clinical workflows unless tenant allows sandbox mode. | P0 |

---

### 10.5 Template Management

RadioPad should support both institution-created templates and standards-inspired templates. RSNA RadReport offers reporting templates reviewed by radiology experts and many incorporate RadLex terminology and RadElement common data elements. ([RSNA](https://www.rsna.org/practice-tools/data-tools-and-standards/radreport-reporting-templates))

| ID | Requirement | Priority |
| :---- | :---- | :---- |
| TMP-001 | Provide template library by modality, anatomy, subspecialty, procedure, and report type. | P0 |
| TMP-002 | Allow structured fields, optional fields, required fields, and conditional sections. | P0 |
| TMP-003 | Support normal, abnormal, follow-up, screening, and urgent report variants. | P1 |
| TMP-004 | Support RadLex/RadElement mapping where licensed/available. | P1 |
| TMP-005 | Support tenant-specific template approval workflow. | P0 |
| TMP-006 | Provide template usage analytics. | P1 |
| TMP-007 | Support import/export as JSON/YAML. | P0 |
| TMP-008 | Support report preview before publishing. | P0 |

---

### 10.6 Standards and Terminology

| ID | Requirement | Priority |
| :---- | :---- | :---- |
| STD-001 | Support mapping findings and procedure names to RadLex where available. | P1 |
| STD-002 | Support ACR RADS rule modules such as BI-RADS, LI-RADS, PI-RADS, Lung-RADS, etc., subject to licensing and clinical governance. | P1 |
| STD-003 | Support FHIR DiagnosticReport export. | P0 |
| STD-004 | Support DICOMweb study metadata retrieval. | P1 |
| STD-005 | Support terminology dictionary management. | P1 |
| STD-006 | Support institution-specific lexicons and abbreviations. | P0 |

RadLex is RSNA’s radiology terminology for reporting, decision support, data mining, registries, education, and research, and RSNA notes that RadLex has an HL7 FHIR format that can support terminology binding for resources such as observations. ([RSNA](https://www.rsna.org/practice-tools/data-tools-and-standards/radlex-radiology-lexicon))

---

### 10.7 Desktop App

The desktop app should be a first-class product, not merely a wrapper.

| ID | Requirement | Priority |
| :---- | :---- | :---- |
| DESK-001 | Provide Windows and macOS desktop apps. | P0 |
| DESK-002 | Auto-start and manage the local RadioPad daemon. | P0 |
| DESK-003 | Support global hotkeys for dictation, generate impression, rewrite, copy, and paste. | P0 |
| DESK-004 | Support secure clipboard mode with automatic timeout. | P1 |
| DESK-005 | Support local encrypted cache for drafts and temporary inputs. | P0 |
| DESK-006 | Support offline draft editing. | P1 |
| DESK-007 | Support local PACS/RIS bridge plugins. | P1 |
| DESK-008 | Support device authorization and tenant pairing. | P0 |
| DESK-009 | Support local model/plugin execution where enabled. | P1 |
| DESK-010 | Provide local logs with PHI redaction controls. | P0 |

---

### 10.8 CLI and Local Daemon

The CLI/daemon is the “power user” and enterprise automation layer.

OAuth Device Authorization Grant is designed for devices or clients that cannot easily perform browser-based login, enabling a client to obtain authorization using a user agent on another device. (\[IETF Datatracker\]\[11\]) This is relevant for CLI and local daemon onboarding, but RadioPad must restrict PHI workflows to approved vendors, BAAs, and tenant policy.

| ID | Requirement | Priority |
| :---- | :---- | :---- |
| CLI-001 | Provide `radiopad login` using device authorization flow or browser-based OAuth. | P0 |
| CLI-002 | Provide `radiopad daemon start/stop/status`. | P0 |
| CLI-003 | Provide `radiopad generate` for report generation from local input files. | P1 |
| CLI-004 | Provide `radiopad validate` for rulebook and report validation. | P0 |
| CLI-005 | Provide `radiopad rulebook test` for regression testing prompt/rulebook changes. | P0 |
| CLI-006 | Provide `radiopad templates import/export`. | P1 |
| CLI-007 | Provide provider adapters for approved APIs, local models, and compliant OAuth-based tools. | P1 |
| CLI-008 | Enforce tenant model policies locally before any request leaves the machine. | P0 |
| CLI-009 | Provide audit event sync to cloud control plane. | P0 |
| CLI-010 | Support headless mode for enterprise deployment. | P1 |

#### Example CLI Commands

radiopad login \--tenant acme-radiology

radiopad daemon start

radiopad rulebook validate chest\_ct\_v1.yaml

radiopad rulebook test chest\_ct\_v1.yaml \--cases ./golden-cases

radiopad generate \--template chest-ct \--input findings.txt \--mode draft

radiopad ai providers list

radiopad audit export \--from 2026-05-01 \--to 2026-05-31

---

### 10.9 AI Provider and Subscription Module

RadioPad should support a **Provider Abstraction Layer**.

#### Provider Types

| Provider Type | Use Case | PHI Allowed? |
| :---- | :---- | :---- |
| Approved enterprise API with BAA / DPA | Production PHI workflows | Yes, if contract and tenant policy allow |
| Private cloud AI endpoint | Enterprise hospital deployments | Yes, if configured |
| Local model | Highest privacy / offline workflows | Yes, if local policy allows |
| BYOK provider | Customer-managed API keys | Depends on vendor contract |
| OAuth subscription-backed provider | Personal/team productivity and non-PHI workflows | Only if legally and contractually approved |
| Sandbox/demo provider | Testing only | No PHI |

#### Requirements

| ID | Requirement | Priority |
| :---- | :---- | :---- |
| PROV-001 | Maintain tenant-level model/provider registry. | P0 |
| PROV-002 | Allow admins to mark providers as PHI-approved, de-identified-only, or blocked. | P0 |
| PROV-003 | Support per-provider cost, latency, token, and availability telemetry. | P1 |
| PROV-004 | Support fallback routing only between providers with equal or higher compliance class. | P0 |
| PROV-005 | Support model comparison in sandbox evaluation mode. | P1 |
| PROV-006 | Support API key vaulting and rotation. | P0 |
| PROV-007 | Support OAuth token storage only in encrypted local/tenant vaults. | P0 |
| PROV-008 | Support provider policy enforcement before inference. | P0 |
| PROV-009 | Support data retention labeling by provider and endpoint. | P0 |
| PROV-010 | Block PHI from providers without approved compliance configuration. | P0 |

---

### 10.10 MCP / Tool Integration Layer

Model Context Protocol defines a standardized way for LLM applications to connect with external data sources and tools. ([Model Context Protocol](https://modelcontextprotocol.io/specification/2025-11-25)) RadioPad can use an MCP-inspired architecture or support MCP-compatible plugins, but it must harden tool permissions because radiology workflows involve PHI and clinical risk.

| ID | Requirement | Priority |
| :---- | :---- | :---- |
| MCP-001 | Provide tool registry for approved internal tools. | P1 |
| MCP-002 | Require explicit admin approval for each tool. | P0 |
| MCP-003 | Support least-privilege tool scopes. | P0 |
| MCP-004 | Log every tool call with user, patient/study context, tool, input hash, output hash, and timestamp. | P0 |
| MCP-005 | Block shell/file/network tools by default in clinical production. | P0 |
| MCP-006 | Support sandboxed tool execution for testing. | P1 |
| MCP-007 | Provide allowlist-based connectors for PACS/RIS/EHR systems. | P1 |

---

## 11\. Core Workflows

### 11.1 Draft Report from Dictation

1. Radiologist opens study.  
2. RadioPad receives metadata: modality, body part, indication, comparison, prior reports.  
3. Radiologist dictates rough findings.  
4. AI cleans dictation into structured report sections.  
5. Rulebook validates required sections and terminology.  
6. AI generates impression.  
7. Safety engine checks contradictions, laterality, measurements, missing critical language.  
8. Radiologist edits.  
9. Final report is copied/exported.  
10. Audit event is stored.

### 11.2 Findings to Impression

1. Radiologist writes findings.  
2. User clicks “Generate Impression.”  
3. System selects appropriate rulebook.  
4. AI produces concise impression bullets.  
5. Validation engine checks whether impression is supported by findings.  
6. Unsupported statements are highlighted.  
7. Radiologist accepts, edits, or rejects.

### 11.3 Rulebook Governance

1. Admin creates draft rulebook.  
2. Adds prompt blocks, required sections, style rules, validation checks.  
3. Runs test cases.  
4. Medical director reviews diff and output examples.  
5. Rulebook is approved.  
6. Production reports record rulebook version.  
7. Rulebook changes are monitored via analytics and error reports.

### 11.4 Desktop \+ PACS Workflow

1. Radiologist uses PACS normally.  
2. Desktop companion detects/reporting context via local integration or manual shortcut.  
3. Hotkey opens RadioPad mini-panel.  
4. Radiologist dictates or pastes findings.  
5. Local daemon routes request according to tenant AI policy.  
6. Output is inserted back into reporting system via secure clipboard or approved integration.

### 11.5 CLI Evaluation Workflow

1. Admin exports 100 de-identified golden cases.  
2. Runs `radiopad rulebook test`.  
3. System compares generated outputs against expected style and required rules.  
4. Results show pass/fail, hallucination flags, contradiction flags, and regression diffs.  
5. Rulebook cannot be promoted unless minimum acceptance threshold passes.

---

## 12\. AI Safety and Quality Requirements

### 12.1 Report Validation Engine

| Check | Description | Priority |
| :---- | :---- | :---- |
| Laterality check | Detect left/right mismatch between findings and impression. | P0 |
| Measurement consistency | Ensure measurements are consistent across report sections. | P1 |
| Required section check | Verify mandatory sections exist. | P0 |
| Unsupported impression check | Impression must be supported by findings or prior context. | P0 |
| Critical finding language | Ensure approved escalation language is used. | P0 |
| Follow-up recommendation rule | Only use tenant-approved follow-up phrases. | P1 |
| Negation conflict | Detect “no pneumothorax” vs “small pneumothorax” conflicts. | P0 |
| Modality mismatch | Detect CT language in MRI/X-ray report. | P1 |
| Anatomy mismatch | Detect wrong body part language. | P1 |
| Hallucination risk | Flag claims not present in input context. | P1 |

### 12.2 Human-in-the-Loop Controls

1. AI-generated report text must be visibly marked until reviewed.  
2. Final export requires user confirmation.  
3. Critical finding suggestions require explicit confirmation.  
4. Report signing must happen in the customer’s official reporting/RIS/EHR system unless RadioPad becomes an approved signing system.  
5. User edits must be tracked for analytics and model/rulebook evaluation.

### 12.3 AI Output Confidence

RadioPad should not present vague “AI confidence” as clinical certainty. Instead, it should present:

* Rule validation status.  
* Unsupported statement warnings.  
* Missing input warnings.  
* Contradiction warnings.  
* Source/context trace.  
* Model/rulebook version.  
* “Needs radiologist review” banner.

---

## 13\. Data and Audit Model

### 13.1 Core Entities

| Entity | Description |
| :---- | :---- |
| Tenant | Hospital, radiology group, imaging center. |
| User | Radiologist, admin, reviewer, IT user. |
| Study Context | Metadata about exam, modality, body part, accession, patient reference. |
| Report Draft | Editable report before final export. |
| Report Version | Snapshot of report at each major edit/export. |
| Template | Structured report template. |
| Rulebook | Versioned AI/reporting policy package. |
| Prompt Block | Reusable prompt component. |
| AI Request | Input sent to model/provider. |
| AI Response | Output from model/provider. |
| Validation Result | Safety/quality checks. |
| Provider Config | AI provider credentials and policies. |
| Audit Event | Immutable record of system/user/model activity. |
| Subscription | Plan, seats, usage, billing metadata. |

### 13.2 Audit Events

Every AI event must log:

* Tenant ID.  
* User ID.  
* Study/report reference.  
* Timestamp.  
* Input type.  
* PHI/de-identified classification.  
* Model/provider used.  
* Prompt version.  
* Rulebook version.  
* Template version.  
* Output hash.  
* User action: accepted, edited, rejected.  
* Validation results.  
* Export destination.  
* Device/daemon identity.

### 13.3 Data Retention

RadioPad must support:

1. Tenant-configurable retention periods.  
2. PHI minimization.  
3. Configurable storage of AI inputs/outputs.  
4. Hash-only audit mode.  
5. Legal hold.  
6. Right-to-delete workflows where applicable.  
7. Exportable compliance logs.

---

## 14\. Privacy, Security, and Compliance

### 14.1 Security Requirements

| ID | Requirement | Priority |
| :---- | :---- | :---- |
| SEC-001 | Encrypt data in transit using TLS 1.2+ / TLS 1.3. | P0 |
| SEC-002 | Encrypt data at rest. | P0 |
| SEC-003 | Support customer-managed keys for enterprise. | P1 |
| SEC-004 | Support tenant-level PHI policy. | P0 |
| SEC-005 | Support audit log immutability. | P0 |
| SEC-006 | Support least-privilege RBAC. | P0 |
| SEC-007 | Support SSO, MFA, SCIM. | P0/P1 |
| SEC-008 | Support IP allowlisting and device posture checks. | P1 |
| SEC-009 | Support secure secret storage for API keys and OAuth tokens. | P0 |
| SEC-010 | Support PHI redaction in debug logs. | P0 |
| SEC-011 | Support intrusion detection and anomaly alerts. | P1 |
| SEC-012 | Support vendor/provider compliance profiles. | P0 |

### 14.2 HIPAA-Oriented Requirements

For U.S. customers, RadioPad must be designed for HIPAA-aligned deployment. HIPAA Security Rule safeguards include administrative, physical, and technical safeguards for electronic PHI. ([HHS.gov](https://www.hhs.gov/hipaa/for-professionals/security/index.html))

Requirements:

1. BAA support for covered entities and business associates.  
2. Access controls.  
3. Audit controls.  
4. Integrity controls.  
5. Transmission security.  
6. Workforce access policies.  
7. Breach notification workflows.  
8. Vendor subprocessor inventory.  
9. Provider-specific data retention controls.  
10. PHI routing restrictions.

### 14.3 AI Provider Compliance

OpenAI’s public documentation says customers processing PHI through the API need a BAA first, and the platform’s Zero Data Retention and Modified Abuse Monitoring controls require approval and have endpoint-specific eligibility and storage behavior. ([OpenAI Help Center](https://help.openai.com/en/articles/8660679-how-can-i-get-a-business-associate-agreement-baa-with-openai))

Therefore, RadioPad must:

1. Block PHI to non-approved AI providers.  
2. Track each provider’s data retention behavior.  
3. Allow only approved endpoints for PHI.  
4. Surface provider compliance status to tenant admins.  
5. Prevent fallback from compliant provider to non-compliant provider.  
6. Maintain a provider risk registry.

---

## 15\. Regulatory Strategy

### 15.1 Intended Use Draft

RadioPad is intended to assist licensed radiologists in drafting, editing, formatting, standardizing, and validating radiology report text. RadioPad does not independently interpret medical images, diagnose disease, or sign reports. All outputs require review and approval by a licensed radiologist.

### 15.2 Regulatory Risk Boundary

RadioPad’s risk increases if it:

1. Interprets images directly.  
2. Generates diagnostic conclusions not present in user input.  
3. Recommends patient management.  
4. Performs triage.  
5. Claims clinical accuracy or diagnostic performance.  
6. Auto-signs or submits reports without review.

FDA activity around AI-enabled medical device software includes guidance and lifecycle recommendations for AI-enabled device software functions. ([U.S. Food and Drug Administration](https://www.fda.gov/medical-devices/software-medical-device-samd/artificial-intelligence-software-medical-device)) EU AI Act healthcare guidance states that high-risk AI systems such as AI-based software intended for medical purposes must comply with requirements including risk mitigation, high-quality data, clear user information, and human oversight. ([Public Health](https://health.ec.europa.eu/ehealth-digital-health-and-care/artificial-intelligence-healthcare_en))

### 15.3 Product Positioning Recommendation

MVP should be positioned as:

* Documentation assistant.  
* Reporting quality assistant.  
* Template/rulebook automation layer.  
* Human-reviewed drafting tool.

Avoid positioning as:

* Autonomous radiology diagnosis.  
* AI image interpretation.  
* Clinical decision replacement.  
* Automated patient management recommendation engine.

---

## 16\. User Experience Requirements

### 16.1 Web App Navigation

Primary modules:

1. Dashboard.  
2. Reporting Workspace.  
3. Templates.  
4. Rulebooks.  
5. Prompt Studio.  
6. AI Providers.  
7. Validation Center.  
8. Analytics.  
9. Integrations.  
10. Users & Roles.  
11. Audit Logs.  
12. Billing.

### 16.2 Reporting Workspace UI

Key panels:

| Panel | Purpose |
| :---- | :---- |
| Study Context | Patient/study metadata, indication, modality, body part, prior report summary. |
| Editor | Main report drafting area. |
| AI Actions | Generate, rewrite, summarize, impression, validate, compare. |
| Rulebook Panel | Active rules, warnings, required fields. |
| Prior Report Panel | Prior findings and comparison summary. |
| Validation Panel | Contradictions, missing sections, style issues, unsupported claims. |
| Export Panel | Copy, PDF, DOCX, FHIR, RIS integration. |

### 16.3 Desktop UX

The desktop app should optimize speed:

1. Global hotkey opens quick command palette.  
2. Mini overlay works above PACS/RIS.  
3. Dictation starts instantly.  
4. AI actions are available via keyboard.  
5. Secure paste into reporting system.  
6. Offline/local mode clearly indicated.  
7. Provider/compliance mode visible but not intrusive.

### 16.4 Prompt Studio UX

Prompt Studio should allow:

1. Visual prompt block editing.  
2. Rulebook preview.  
3. Test case runner.  
4. Output diff viewer.  
5. Golden case library.  
6. Approval workflow.  
7. Prompt and rule version comparison.  
8. Rollback.

---

## 17\. Billing and Subscription Requirements

### 17.1 Plans

| Plan | Target Customer | Features |
| :---- | :---- | :---- |
| Starter | Small clinics | Web app, templates, basic AI, limited users. |
| Professional | Radiology groups | Desktop app, custom prompts, analytics, advanced templates. |
| Enterprise | Hospitals/teleradiology | SSO, SCIM, integrations, audit, rulebook governance, private deployment. |
| Enterprise Plus | Large health systems | On-prem/private cloud, customer-managed keys, advanced governance, custom AI routing. |

### 17.2 Usage Metering

Track:

1. Seats.  
2. AI requests.  
3. Tokens.  
4. Dictation minutes.  
5. Report generations.  
6. Validation runs.  
7. Storage.  
8. Integration calls.  
9. Local daemon activations.  
10. Rulebook test runs.

### 17.3 Subscription Module

Requirements:

| ID | Requirement | Priority |
| :---- | :---- | :---- |
| BILL-001 | Support seat-based subscription. | P0 |
| BILL-002 | Support usage-based AI credits. | P0 |
| BILL-003 | Support enterprise invoicing. | P1 |
| BILL-004 | Support tenant-level usage dashboard. | P0 |
| BILL-005 | Support provider cost attribution. | P1 |
| BILL-006 | Support plan-based feature flags. | P0 |
| BILL-007 | Support trial tenants and sandbox environments. | P1 |

---

## 18\. Analytics and KPIs

### 18.1 Product KPIs

| KPI | Definition |
| :---- | :---- |
| Draft acceptance rate | Percentage of AI drafts used after review. |
| Impression acceptance rate | Percentage of generated impressions accepted or lightly edited. |
| Time saved per report | Baseline time minus RadioPad-assisted time. |
| Report validation pass rate | Reports passing tenant rulebook checks. |
| Contradiction detection rate | Number of contradiction warnings per 100 reports. |
| Edit distance | Amount of user editing after AI generation. |
| Active radiologists | Weekly/monthly active reporting users. |
| Rulebook adoption | Reports generated using approved rulebooks. |
| Provider cost per report | AI cost divided by completed report volume. |
| Turnaround time impact | TAT before vs after implementation. |

### 18.2 Governance KPIs

| KPI | Definition |
| :---- | :---- |
| Unapproved prompt usage | Attempts to use non-approved prompts in production. |
| PHI policy violations blocked | Number of prevented unsafe provider requests. |
| Rulebook regression failures | Failed tests before rulebook approval. |
| Model drift alerts | Quality degradation against golden cases. |
| Audit completeness | Percentage of AI events with complete trace metadata. |

---

## 19\. Integration Requirements

### 19.1 PACS / RIS / EHR

RadioPad should integrate through standards-first pathways:

1. DICOMweb for study metadata and imaging context.  
2. FHIR DiagnosticReport for report exchange.  
3. HL7 v2 ORU where required.  
4. Clipboard and desktop automation as fallback.  
5. Custom enterprise connectors.

DICOMweb provides RESTful services for web-based access to medical images and DICOM-enabled systems. ([DICOM](https://www.dicomstandard.org/using/dicomweb)) FHIR DiagnosticReport can represent imaging reports and conclusions as text, coded data, or formatted attachments. ([FHIR](https://build.fhir.org/diagnosticreport.html))

### 19.2 Required Integrations

| Integration | MVP | Beta | GA |
| :---- | ----: | ----: | ----: |
| SSO/OIDC | ✓ | ✓ | ✓ |
| SAML | — | ✓ | ✓ |
| DICOMweb metadata | — | ✓ | ✓ |
| FHIR DiagnosticReport export | ✓ | ✓ | ✓ |
| HL7 v2 export | — | ✓ | ✓ |
| PACS local bridge | — | ✓ | ✓ |
| RIS copy/paste bridge | ✓ | ✓ | ✓ |
| Stripe or billing provider | ✓ | ✓ | ✓ |
| SIEM log export | — | ✓ | ✓ |
| SCIM | — | — | ✓ |

---

## 20\. Technical Architecture Requirements

### 20.1 Suggested Stack

| Layer | Recommended Direction |
| :---- | :---- |
| Frontend Web | React / Next.js |
| Desktop | Tauri or Electron |
| Backend | Node.js/NestJS, Go, or Python/FastAPI services |
| AI Gateway | Dedicated service with provider abstraction |
| Rulebook Engine | Deterministic validator \+ LLM prompt orchestrator |
| Database | PostgreSQL |
| Cache/Queue | Redis \+ message queue |
| Search | OpenSearch / pgvector / vector DB |
| Storage | S3-compatible object storage |
| Auth | OIDC/SAML provider integration |
| Infra | Kubernetes, Terraform |
| Observability | OpenTelemetry, SIEM export |
| CLI | Go or Rust for secure cross-platform distribution |

### 20.2 Service Modules

| Service | Responsibility |
| :---- | :---- |
| Identity Service | Auth, SSO, RBAC, tenant membership. |
| Reporting Service | Drafts, versions, exports. |
| Rulebook Service | Rulebook CRUD, versioning, approvals, tests. |
| Template Service | Templates, structured fields, specialty libraries. |
| AI Gateway | Provider routing, compliance enforcement, inference logs. |
| Validation Service | Deterministic and AI-assisted report checks. |
| Integration Service | DICOMweb, FHIR, HL7, webhooks. |
| Audit Service | Immutable events and compliance export. |
| Billing Service | Plans, seats, usage, invoices. |
| Desktop Sync Service | Device pairing, daemon sync, local policy. |

---

## 21\. Performance and Reliability Requirements

| ID | Requirement | Target |
| :---- | :---- | :---- |
| PERF-001 | AI draft generation latency | p95 under 10 seconds for standard text-only report |
| PERF-002 | Impression generation latency | p95 under 5 seconds |
| PERF-003 | Validation latency | p95 under 3 seconds |
| PERF-004 | Web app availability | 99.9% MVP, 99.95% enterprise |
| PERF-005 | Desktop startup | Under 5 seconds |
| PERF-006 | CLI validation | Under 2 seconds for local rule syntax check |
| PERF-007 | Audit event write | p99 under 500 ms |
| PERF-008 | Export generation | p95 under 3 seconds |

---

## 22\. Acceptance Criteria

### MVP Acceptance Criteria

1. A radiologist can create a report draft from free text or dictation input.  
2. A radiologist can generate an impression from findings.  
3. AI output is editable and visibly marked.  
4. A radiologist must confirm before export.  
5. Admin can create and publish at least one template.  
6. Admin can create and approve at least one rulebook.  
7. System logs model, prompt, rulebook, input/output hash, and user action.  
8. System blocks PHI routing to unapproved providers.  
9. System exports report as text, PDF, DOCX, JSON, and FHIR DiagnosticReport.  
10. Desktop app supports login, daemon status, hotkey capture, generate, and copy.  
11. CLI supports login, daemon status, rulebook validation, and test execution.  
12. Billing system tracks users and AI usage.

### Beta Acceptance Criteria

1. PACS/RIS metadata can be imported through at least one integration path.  
2. DICOMweb metadata retrieval works in test environment.  
3. HL7/FHIR export works with customer test endpoint.  
4. Rulebook test suite supports golden case regression.  
5. Contradiction checker catches laterality and negation conflicts.  
6. Enterprise audit export is available.  
7. SSO is production-ready.  
8. Provider fallback respects compliance class.

### Enterprise GA Acceptance Criteria

1. SCIM provisioning works.  
2. SIEM export works.  
3. Customer-managed keys available.  
4. Private deployment runbook complete.  
5. AI governance dashboard complete.  
6. Model evaluation harness complete.  
7. Rulebook approval workflow fully auditable.  
8. Desktop app and daemon are centrally manageable.

---

## 23\. Risks and Mitigations

| Risk | Severity | Mitigation |
| :---- | ----: | :---- |
| AI hallucination in reports | Critical | Human review, unsupported-claim detection, rulebooks, traceability, validation. |
| PHI sent to non-compliant provider | Critical | Provider compliance registry, routing guardrails, hard blocks, audit logs. |
| Product becomes regulated SaMD unexpectedly | High | Intended-use control, regulatory review, claims governance, clinical safety documentation. |
| Radiologist overreliance | High | UX warnings, AI text highlighting, mandatory review, training. |
| Inconsistent report quality | High | Rulebooks, templates, golden tests, analytics. |
| Poor integration with PACS/RIS | High | Desktop fallback, standards-first integrations, phased connector strategy. |
| OAuth subscription misuse | High | Restrict to approved non-PHI or contract-approved workflows only. |
| Prompt/rulebook drift | Medium | Versioning, approval workflow, regression testing. |
| Model cost overruns | Medium | Usage caps, provider routing, tenant budgets. |
| Desktop security exposure | High | Signed binaries, encrypted storage, device trust, auto-update, hardened daemon. |

---

## 24\. Implementation Roadmap

### Phase 0 — Discovery and Compliance Design

* Confirm target jurisdictions.  
* Define intended use.  
* Conduct regulatory review.  
* Define PHI policy.  
* Define provider compliance matrix.  
* Interview radiologists.  
* Collect template examples.  
* Build golden case test set.

### Phase 1 — Core Reporting MVP

* Web app.  
* Auth.  
* Report editor.  
* AI gateway.  
* Template library.  
* Basic rulebooks.  
* Prompt Studio alpha.  
* Audit logs.  
* Billing foundation.

### Phase 2 — Desktop and CLI

* Desktop app.  
* Local daemon.  
* CLI login.  
* CLI rulebook validation.  
* Secure clipboard.  
* Hotkeys.  
* Local provider policy enforcement.

### Phase 3 — Clinical Safety Layer

* Contradiction checker.  
* Unsupported claim detection.  
* Laterality checks.  
* Required section validation.  
* Golden case regression tests.  
* Rulebook approval workflow.

### Phase 4 — Integrations

* FHIR DiagnosticReport export.  
* DICOMweb metadata.  
* HL7 v2 support.  
* PACS/RIS bridge.  
* SSO.  
* Enterprise audit export.

### Phase 5 — Enterprise GA

* SCIM.  
* SIEM.  
* Customer-managed keys.  
* Private cloud/on-prem.  
* AI governance dashboard.  
* Model evaluation suite.  
* Advanced analytics.

---

## 25\. Example Product Modules

### 25.1 Report Composer

Main radiologist workspace.

Features:

* Dictation input.  
* Findings editor.  
* Impression generator.  
* AI rewrite actions.  
* Template selector.  
* Prior report comparison.  
* Validation panel.  
* Export tools.

### 25.2 Prompt Studio

For admins and medical directors.

Features:

* Prompt block editor.  
* Role-based prompt permissions.  
* Rulebook binding.  
* Test cases.  
* Output comparison.  
* Approval workflow.

### 25.3 Rulebook Center

For governance.

Features:

* Rulebook library.  
* Versioning.  
* Required sections.  
* Specialty rules.  
* Critical finding language.  
* RADS support modules.  
* Regression testing.  
* Production promotion.

### 25.4 AI Gateway

For provider control.

Features:

* Provider registry.  
* BYOK.  
* OAuth connectors.  
* API connectors.  
* Local models.  
* Compliance class.  
* Cost/latency telemetry.  
* Fallback policies.

### 25.5 Desktop Companion

For daily clinical use.

Features:

* Hotkeys.  
* Dictation.  
* Clipboard bridge.  
* Local daemon.  
* PACS/RIS context.  
* Local provider enforcement.  
* Offline drafts.

### 25.6 Governance Dashboard

For enterprise oversight.

Features:

* Model inventory.  
* Prompt/rulebook versions.  
* AI usage.  
* PHI routing.  
* Validation results.  
* User adoption.  
* Drift monitoring.  
* Incident review.

---

## 26\. Competitive Differentiation

RadioPad should differentiate through:

1. **Rulebook-first AI reporting** — not generic prompting.  
2. **Radiology-specific validation** — contradictions, laterality, measurements, required sections.  
3. **Desktop \+ CLI \+ Web architecture** — suitable for real radiology workflows.  
4. **Compliance-aware provider routing** — no unsafe AI fallback.  
5. **Local-first option** — useful for hospitals and PHI-sensitive environments.  
6. **Standards-aware output** — FHIR DiagnosticReport, DICOMweb metadata, RadLex/RadReport alignment.  
7. **Prompt governance** — approval, versioning, testing, rollback.  
8. **Enterprise AI auditability** — every AI output is traceable.

---

## 27\. Recommended MVP Build Order

1. Tenant/auth/RBAC.  
2. Report editor.  
3. AI gateway.  
4. Basic prompt templates.  
5. Findings → impression.  
6. Report cleanup.  
7. Template manager.  
8. Rulebook v1.  
9. Validation engine v1.  
10. Audit logs.  
11. Export system.  
12. Desktop companion alpha.  
13. CLI alpha.  
14. Billing/usage.  
15. Admin dashboard.  
16. Provider policy enforcement.

---

## 28\. Final Product Definition

RadioPad is a **secure AI reporting operating system for radiology**.

It combines:

* NLP report generation.  
* Custom prompt engineering.  
* Rulebook-based governance.  
* Structured reporting templates.  
* Desktop-first clinical workflow.  
* CLI/local daemon power.  
* Standards-aware interoperability.  
* Enterprise-grade privacy, compliance, and auditability.  
* Multi-provider AI orchestration.

The product should be built around a clear clinical safety boundary: **RadioPad assists the radiologist; the radiologist owns the final report.**

