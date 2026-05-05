# Product Requirements Document (PRD)

**Status:** Current  ·  **Owner:** Product  ·  **Last Updated:** 2026-05-04  ·  **Source of Truth:** Yes

> The detailed engineering PRD lives at the repo root: [PRD.md](../../PRD.md). This document is the product-management view; both must stay in sync.

## 1. Overview

RadioPad is an AI-assisted radiology reporting platform delivered as Web (Next.js), Backend (ASP.NET Core), Desktop (Tauri), Mobile (Capacitor), and CLI (.NET global tool). Radiologists draft, validate via rulebooks, optionally ask an AI provider for phrasing/impression help, sign, and export to FHIR.

## 2. Goals

- Cut reporting time per case by 25% without degrading report quality.
- Enforce institutional rulebooks at draft time, not at QA time.
- Make PHI routing provable and auditable.
- Allow on-prem deployment for PHI-sensitive customers.

## 3. Non-goals

- RadioPad will **not** auto-sign reports.
- We will not replace dictation. Voice can be paired (planned), but the canonical input is structured text.
- We will not bundle a model — we route to providers (Mock, Anthropic, Ollama; pluggable).

## 4. Personas

See [personas.md](personas.md). Primary: attending radiologist. Secondary: resident radiologist, radiology informatics admin, IT/security officer.

## 5. User journeys

See [user-stories.md](user-stories.md) and [use-cases.md](use-cases.md).

## 6. Functional requirements (high level)

| ID | Requirement |
| --- | --- |
| F-01 | Create a draft report from a modality + body-part + indication tuple. |
| F-02 | Apply a rulebook for validation; surface findings grouped by severity. |
| F-03 | Ask an AI provider for impression/recommendation drafts; the result wears `.ai-mark`. |
| F-04 | Track edit history per report (sequence-numbered snapshots). |
| F-05 | Acknowledge & sign; status transitions `Draft → Validated → Acknowledged → Exported`. |
| F-06 | Export FHIR `DiagnosticReport` (text narrative). |
| F-07 | Verify the audit chain locally (`radiopad audit verify`). |
| F-08 | Manage providers, rulebooks, and templates from the admin surface. |

## 7. Non-functional requirements

See [nfr.md](nfr.md).

## 8. Feature priorities

| Priority | Feature |
| --- | --- |
| P0 | Locked UI/UX, PHI policy, audit chain, rulebook engine, draft/sign workflow. |
| P1 | Pagination, version history, severity grouping, readiness probes, CLI provider test. |
| P2 | SSO, multi-rad sign-off, bidirectional FHIR, RAG over priors. |

## 9. Success metrics

See [kpi-metrics.md](kpi-metrics.md).

## 10. Constraints

- Strict tech stack ([AGENTS.md](../../AGENTS.md)).
- Locked UI/UX system ([../02-design/design.md](../02-design/design.md)).
- Append-only audit log; PHI policy non-negotiable.
- Apache 2.0 licensed core.

## 11. Dependencies

- AI providers (Mock for dev, Anthropic/Ollama optional, customer-supplied for PHI-approved deployments).
- PostgreSQL 14+ for prod.

## 12. Risks

- Clinical safety regression in `ReportValidator` or `AiGateway`.
- Audit chain corruption.
- Design-system erosion.
- Provider vendor instability.

## 13. Launch criteria (v0.1.0 → v1.0.0)

- All P0/P1 acceptance criteria green.
- SOC 2 Type I readiness reached.
- ≥ 3 pilot tenants with ≥ 100 signed reports each.
- Pen-test report with no Critical/High findings outstanding.
