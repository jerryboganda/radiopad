# Product Roadmap

**Status:** Current  ·  **Owner:** Product  ·  **Last Updated:** 2026-05-04

> Strategic-level roadmap. Engineering log = [../../PROGRESS.md](../../PROGRESS.md). Strategic phases summary = [../../ROADMAP.md](../../ROADMAP.md). Per-release scope = [release-scope.md](release-scope.md).

| Phase | Theme | Highlights | Status |
| --- | --- | --- | --- |
| 0 | Architecture baseline | Strict stack, locked UI, audit chain, PHI policy, five seed rulebooks. | ✅ Shipped (v0.1.0) |
| 1 | Operational hardening | Server-side pagination, version history, readiness probe, severity grouping, CLI provider test. | 🟡 In progress |
| 2 | Clinical breadth | Cardiac MRI, mammography, paediatric chest X-ray, addendum workflow, peer-review viewer. | Planned |
| 3 | Enterprise | SSO/OIDC, RBAC matrix in admin UI, signed audit export, SOC 2 Type I readiness, hosted BAA option. | Planned |
| 4 | Intelligence | RAG over priors, fine-tuned local model bundle, voice dictation, configurable AI quality rubric. | Planned |

## Dependencies

- Phase 1 unblocks Phase 2 by providing the version-history surface that addendum + peer-review depend on.
- Phase 3 SSO/RBAC is a prerequisite for any enterprise procurement contract.
- Phase 4 RAG depends on Phase 3 audit export (for ingestion governance).

## Risks

- Provider-policy regression (clinical safety blocker).
- Design-system erosion (slows everything else).
- Vendor outage at hosted AI providers (mitigated by fallback chain and on-prem option).
- Regulatory changes around AI in medical software.
