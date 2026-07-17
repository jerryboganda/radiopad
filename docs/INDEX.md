# RadioPad Documentation Index

**Status:** Current  ·  **Owner:** Engineering + Product  ·  **Last Updated:** 2026-05-04

The single jumping-off point for every documentation surface in this repository.

> ⚠️ The UI/UX is **locked** to the **RC design system** (light + first-class deep-navy dark; canonical tokens in `frontend/app/tokens.css`, build-time Tailwind 3). See [02-design/design.md](02-design/design.md) and the authoritative [CLAUDE.md](../CLAUDE.md) before touching any UI.

## Start here

- [README](../README.md) — project overview & quickstart.
- [PRD](../PRD.md) — engineering PRD.
- [PROGRESS](../PROGRESS.md) — Ralph-loop build log.
- [AGENTS](../AGENTS.md) / [CLAUDE](../CLAUDE.md) / [GEMINI](../GEMINI.md) — agent entry points.
- [CONTRIBUTING](../CONTRIBUTING.md) · [SECURITY](../SECURITY.md) · [CODE_OF_CONDUCT](../CODE_OF_CONDUCT.md) · [CONVENTIONS](../CONVENTIONS.md) · [VERSIONING](../VERSIONING.md) · [ROADMAP](../ROADMAP.md) · [CHANGELOG](../CHANGELOG.md).

## 00 · Product

- [vision](00-product/vision.md) · [problem-statement](00-product/problem-statement.md) · [personas](00-product/personas.md) · [user-stories](00-product/user-stories.md)
- [prd](00-product/prd.md) · [brd](00-product/brd.md) · [srs](00-product/srs.md) · [frd](00-product/frd.md) · [nfr](00-product/nfr.md)
- [scope](00-product/scope.md) · [mvp](00-product/mvp.md) · [use-cases](00-product/use-cases.md) · [acceptance-criteria](00-product/acceptance-criteria.md)
- [kpi-metrics](00-product/kpi-metrics.md) · [pricing-billing](00-product/pricing-billing.md) · [tenant-model](00-product/tenant-model.md)
- [roadmap](00-product/roadmap.md) · [release-scope](00-product/release-scope.md)

## 01 · AI Agent

- [ai-context](01-ai-agent/ai-context.md) · [ai-rules](01-ai-agent/ai-rules.md) · [prompting-guide](01-ai-agent/prompting-guide.md) · [prompts](01-ai-agent/prompts.md)
- [rulebooks](01-ai-agent/rulebooks.md) · [agent-workflows](01-ai-agent/agent-workflows.md) · [agent-permissions](01-ai-agent/agent-permissions.md) · [agent-safety](01-ai-agent/agent-safety.md)
- [context-map](01-ai-agent/context-map.md) · [task-template](01-ai-agent/task-template.md) · [implementation-plan](01-ai-agent/implementation-plan.md)
- [design-review-checklist](01-ai-agent/design-review-checklist.md) · [code-review-checklist](01-ai-agent/code-review-checklist.md) · [human-review-policy](01-ai-agent/human-review-policy.md)
- [evals](01-ai-agent/evals.md) · [model-policy](01-ai-agent/model-policy.md) · [mcp-servers](01-ai-agent/mcp-servers.md) · [memory-policy](01-ai-agent/memory-policy.md)
- [prompt-versioning](01-ai-agent/prompt-versioning.md) · [ai-incident-playbook](01-ai-agent/ai-incident-playbook.md)

## 02 · Design

- [design](02-design/design.md) — **locked design system + tokens**.
- [ux-strategy](02-design/ux-strategy.md) · [information-architecture](02-design/information-architecture.md) · [user-flows](02-design/user-flows.md) · [wireframes](02-design/wireframes.md)
- [ui-spec](02-design/ui-spec.md) · [design-system](02-design/design-system.md) · [component-spec](02-design/component-spec.md)
- [accessibility](02-design/accessibility.md) · [responsive-design](02-design/responsive-design.md) · [desktop-app-ux](02-design/desktop-app-ux.md) · [copywriting](02-design/copywriting.md)

## 03 · Architecture

- [architecture](03-architecture/architecture.md) · [system-design](03-architecture/system-design.md)
- [c4-context](03-architecture/c4-context.md) · [c4-container](03-architecture/c4-container.md) · [c4-component](03-architecture/c4-component.md)
- [tech-stack](03-architecture/tech-stack.md) · [monorepo-structure](03-architecture/monorepo-structure.md)
- [frontend-architecture](03-architecture/frontend-architecture.md) · [backend-architecture](03-architecture/backend-architecture.md) · [desktop-architecture](03-architecture/desktop-architecture.md) · [cli-design](03-architecture/cli-design.md)
- [api-design](03-architecture/api-design.md) · [api-reference](03-architecture/api-reference.md) · [database-design](03-architecture/database-design.md) · [migrations](03-architecture/migrations.md)
- [auth-architecture](03-architecture/auth-architecture.md) · [authorization-rbac](03-architecture/authorization-rbac.md) · [multi-tenancy](03-architecture/multi-tenancy.md)
- [integration-architecture](03-architecture/integration-architecture.md) · [events](03-architecture/events.md) · [queues-jobs](03-architecture/queues-jobs.md)
- [error-handling](03-architecture/error-handling.md) · [logging](03-architecture/logging.md) · [observability](03-architecture/observability.md) · [caching](03-architecture/caching.md)
- [file-storage](03-architecture/file-storage.md) · [search](03-architecture/search.md)
- [fhir-mapping](03-architecture/fhir-mapping.md) · [provider-catalog](03-architecture/provider-catalog.md)
- ADRs: [README](03-architecture/adr/README.md) · [0001-baseline](03-architecture/adr/0001-initial-architecture-baseline.md) · [ADR-0001-stack](03-architecture/adr/ADR-0001-stack.md) · [ADR-0002-design-lock](03-architecture/adr/ADR-0002-design-lock.md) · [ADR-0003-audit-chain](03-architecture/adr/ADR-0003-audit-chain.md)

## 04 · Security

- [security-architecture](04-security/security-architecture.md) · [threat-model](04-security/threat-model.md)
- [secure-coding](04-security/secure-coding.md) · [owasp-asvs-checklist](04-security/owasp-asvs-checklist.md) · [nist-ssdf-mapping](04-security/nist-ssdf-mapping.md)
- [secrets-management](04-security/secrets-management.md) · [encryption](04-security/encryption.md) · [audit-logging](04-security/audit-logging.md)
- [privacy](04-security/privacy.md) · [data-retention](04-security/data-retention.md) · [data-classification](04-security/data-classification.md)
- [vulnerability-management](04-security/vulnerability-management.md) · [sbom](04-security/sbom.md) · [penetration-test-plan](04-security/penetration-test-plan.md)
- [incident-response](04-security/incident-response.md) · [disaster-recovery](04-security/disaster-recovery.md) · [business-continuity](04-security/business-continuity.md)
- [compliance-matrix](04-security/compliance-matrix.md) · [dpia](04-security/dpia.md) · [vendor-risk](04-security/vendor-risk.md)

## 05 · Data & AI

- [ai-product-spec](05-data-ai/ai-product-spec.md) · [model-abstraction](05-data-ai/model-abstraction.md) · [model-card](05-data-ai/model-card.md)
- [prompt-architecture](05-data-ai/prompt-architecture.md) · [prompt-library](05-data-ai/prompt-library.md) · [prompt-evals](05-data-ai/prompt-evals.md)
- [rag-architecture](05-data-ai/rag-architecture.md) · [knowledge-base](05-data-ai/knowledge-base.md) · [fine-tuning-plan](05-data-ai/fine-tuning-plan.md)
- [ai-quality-rubric](05-data-ai/ai-quality-rubric.md) · [hallucination-policy](05-data-ai/hallucination-policy.md) · [human-in-the-loop](05-data-ai/human-in-the-loop.md)
- [ai-audit-log](05-data-ai/ai-audit-log.md) · [ai-cost-control](05-data-ai/ai-cost-control.md) · [ai-safety](05-data-ai/ai-safety.md)
- [dataset-card](05-data-ai/dataset-card.md) · [data-labeling-guide](05-data-ai/data-labeling-guide.md)

## 05 · Clinical

- [rulebook-authoring](05-clinical/rulebook-authoring.md)

## 06 · Testing

- [test-strategy](06-testing/test-strategy.md) · [unit-test-plan](06-testing/unit-test-plan.md) · [integration-test-plan](06-testing/integration-test-plan.md) · [e2e-test-plan](06-testing/e2e-test-plan.md)
- [regression-test-plan](06-testing/regression-test-plan.md) · [performance-test-plan](06-testing/performance-test-plan.md) · [security-test-plan](06-testing/security-test-plan.md) · [accessibility-test-plan](06-testing/accessibility-test-plan.md)
- [qa-checklist](06-testing/qa-checklist.md) · [bug-report-template](06-testing/bug-report-template.md) · [test-data](06-testing/test-data.md) · [definition-of-done](06-testing/definition-of-done.md) · [definition-of-ready](06-testing/definition-of-ready.md)

## 07 · DevOps

- [dev-setup](07-devops/dev-setup.md) · [environment-strategy](07-devops/environment-strategy.md)
- [ci-cd](07-devops/ci-cd.md) · [branching-strategy](07-devops/branching-strategy.md) · [commit-conventions](07-devops/commit-conventions.md)
- [deployment](07-devops/deployment.md) · [deploy-guide](07-devops/deploy-guide.md) · [rollback](07-devops/rollback.md) · [release-process](07-devops/release-process.md)
- [infra-architecture](07-devops/infra-architecture.md) · [terraform](07-devops/terraform.md) · [containers](07-devops/containers.md) · [kubernetes](07-devops/kubernetes.md)
- [monitoring](07-devops/monitoring.md) · [slo-sla](07-devops/slo-sla.md) · [runbook](07-devops/runbook.md) · [on-call](07-devops/on-call.md)
- [postmortem-template](07-devops/postmortem-template.md) · [backup-restore](07-devops/backup-restore.md) · [capacity-planning](07-devops/capacity-planning.md)

## 08 · User docs

- [user-guide](08-user-docs/user-guide.md) · [admin-guide](08-user-docs/admin-guide.md)
- [cli-guide](08-user-docs/cli-guide.md) · [desktop-app-guide](08-user-docs/desktop-app-guide.md)
- [api-docs](08-user-docs/api-docs.md) · [sdk-guide](08-user-docs/sdk-guide.md)
- [faq](08-user-docs/faq.md) · [troubleshooting](08-user-docs/troubleshooting.md) · [support-playbook](08-user-docs/support-playbook.md)
- [customer-onboarding](08-user-docs/customer-onboarding.md) · [migration-guide](08-user-docs/migration-guide.md)
- [deprecation-policy](08-user-docs/deprecation-policy.md) · [status-page-policy](08-user-docs/status-page-policy.md)

## OpenAPI

- [openapi/openapi.yaml](../openapi/openapi.yaml)

## Reports

- [_reports/documentation-generation-report](_reports/documentation-generation-report.md)
- [_reports/project-analysis-report](_reports/project-analysis-report.md)
- [_reports/documentation-coverage-matrix](_reports/documentation-coverage-matrix.md)
- [_reports/open-questions](_reports/open-questions.md)

## Archived

- [_archived_documentation/2026-05-04/ARCHIVE_INDEX](_archived_documentation/2026-05-04/ARCHIVE_INDEX.md)

---

## Reading orders

**New developer.** README → CONTRIBUTING → 03-architecture/architecture → 03-architecture/tech-stack → 03-architecture/backend-architecture → 03-architecture/frontend-architecture → 06-testing/test-strategy → 07-devops/dev-setup.

**Product manager.** 00-product/vision → 00-product/personas → 00-product/use-cases → 00-product/roadmap → 00-product/pricing-billing.

**AI agent.** AGENTS.md → 01-ai-agent/ai-context → 01-ai-agent/ai-rules → 01-ai-agent/agent-permissions → 01-ai-agent/human-review-policy → 02-design/design.md.

**Security reviewer.** 04-security/security-architecture → 04-security/threat-model → 04-security/audit-logging → 04-security/owasp-asvs-checklist → 04-security/incident-response.

**DevOps engineer.** 07-devops/dev-setup → 07-devops/deployment → 07-devops/runbook → 07-devops/monitoring → 07-devops/disaster-recovery.

**Customer support.** 08-user-docs/user-guide → 08-user-docs/admin-guide → 08-user-docs/troubleshooting → 08-user-docs/support-playbook → 08-user-docs/faq.
