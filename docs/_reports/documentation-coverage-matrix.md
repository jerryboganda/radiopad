# Documentation Coverage Matrix

**Status:** Final  ·  **Owner:** Engineering  ·  **Last Updated:** 2026-05-04

> Audit of every documentation area against the canonical inventory in `GENERATE_PROJECT_DOCUMENTATION.md`. `Status: Implemented / Partial / Missing`.

| Area | Document | Status | Evidence | Gaps | Priority |
| --- | --- | --- | --- | --- | --- |
| Root | README, AGENTS, CLAUDE, GEMINI, PRD, PROGRESS, CHANGELOG, VERSIONING, ROADMAP, CONTRIBUTING, CODE_OF_CONDUCT, SECURITY, CONVENTIONS, LICENSE, LICENSE.md, .env.example | Implemented | Files present. | — | — |
| .github | PR & issue templates, `.github/instructions/*.instructions.md` | Implemented | Files present. | — | — |
| .cursor | rules per area | Implemented | `.cursor/rules/*.mdc`. | — | — |
| 00-product | problem statement → release scope (16 docs) | Implemented | Folder fully populated. | Pricing tier dollar values are placeholders. | Medium |
| 01-ai-agent | AI agent contract (20 docs) | Implemented | Folder fully populated. | MCP server list is empty until we add servers. | Low |
| 02-design | UX → copywriting (12 docs incl. design.md) | Implemented | Folder fully populated. | Wireframes are ASCII only. | Low |
| 03-architecture | system → CLI → desktop + ADRs (30+ docs) | Implemented | Folder fully populated. | Helm / Terraform under planning. | High |
| 04-security | threat → vendor risk (19 docs + security-architecture) | Implemented | Folder fully populated. | DPIA + DAST results pending. | High |
| 05-data-ai | AI product → labelling (17 docs) | Implemented | Folder fully populated. | RAG implementation pending. | Medium |
| 05-clinical | rulebook authoring | Implemented (existing) | `rulebook-authoring.md`. | Per-modality clinical references planned. | Medium |
| 06-testing | unit → DoR (12 + test-strategy) | Implemented | Folder fully populated. | E2E (Playwright) implementation pending. | Medium |
| 07-devops | environment → capacity-planning (18 docs incl. existing) | Implemented | Folder fully populated. | Terraform + K8s are planned only. | High |
| 08-user-docs | user → status-page (13 docs incl. existing) | Implemented | Folder fully populated. | Real customer screenshots pending. | Low |
| openapi | `openapi.yaml` | Implemented | File present; matches code. | Auto-publish to docs site planned. | Medium |
| _reports | generation, analysis, coverage, open questions | Implemented | This folder. | — | — |
| _archived_documentation | legacy docs → archive index | Implemented | `2026-05-04/ARCHIVE_INDEX.md`. | Hard-delete decision pending. | Low |
| Top-level entry indexes | `docs/INDEX.md` master nav | Implemented | Refreshed with all sections. | — | — |

## Living docs

- The following are **living docs** — review on a cadence:
  - Quarterly: `04-security/threat-model.md`, `04-security/owasp-asvs-checklist.md`, `04-security/vendor-risk.md`, `00-product/roadmap.md`.
  - Per release: `CHANGELOG.md`, `PROGRESS.md`, `08-user-docs/migration-guide.md`.
  - Per provider/adapter change: `01-ai-agent/model-policy.md`, `05-data-ai/model-card.md`, `03-architecture/provider-catalog.md`.

## Open work

See [open-questions.md](open-questions.md).
