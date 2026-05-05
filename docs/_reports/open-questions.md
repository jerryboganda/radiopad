# Open Questions

**Status:** Living  ·  **Owner:** Engineering + Product  ·  **Last Updated:** 2026-05-04

> Decisions to make. Each item should resolve into an ADR, doc change, or scope update.

## Product / Business

- [ ] Final pricing tier dollar amounts (currently placeholder structure in `docs/00-product/pricing-billing.md`).
- [ ] Trial duration (14 vs 30 days).
- [ ] Hosted SKU regions for v1.0.0 launch.
- [ ] Co-sign workflow scope for residents (Phase 3 RBAC).
- [ ] Mobile editing scope post-v0.x (currently read/acknowledge only).

## Architecture

- [ ] Background job system: Hangfire vs Quartz vs IHostedService-only.
- [ ] Event surface: in-process bus vs message broker (Phase 2 webhooks first).
- [ ] Search upgrade trigger: Postgres `tsvector` vs external (Meilisearch / OpenSearch).
- [ ] Multi-region replication strategy (logical vs physical).
- [ ] Webhook signature scheme finalisation.

## Security / Compliance

- [ ] First independent pen-test vendor + window.
- [ ] SOC 2 Type I scope and target window.
- [ ] DPIA finalisation per region / customer template.
- [ ] DB role policy (least-privilege; `AuditEvents` UPDATE/DELETE deny).
- [ ] CSP + HSTS configuration shipped via reverse-proxy reference config.

## DevOps

- [ ] Cloud choice for hosted SKU (AWS / GCP / Azure).
- [ ] Container registry choice (GHCR / cloud-native).
- [ ] Logging stack (Loki / managed / customer SIEM).
- [ ] Observability stack (managed Prometheus vs self-hosted).
- [ ] Status page provider.

## AI

- [ ] Streaming responses cutover and prompt schema versioning impact.
- [ ] RAG vector store (`pgvector` vs dedicated).
- [ ] Hallucination evaluator: which judge model + cadence.
- [ ] Prompt store: in-code constants vs DB-backed (with safety review on edit).

## UX

- [ ] Light → dark mode policy beyond v0.x (currently locked light only).
- [ ] Mobile layout for the audit page (read-only; minimal).
- [ ] Keyboard shortcut surface beyond global focus.

## Documentation

- [ ] Decide whether to delete legacy `docs/` root files or keep as historical record (currently kept + indexed).
- [ ] Generate the docs site (Docusaurus / VitePress / static) once content stabilises.

## Tracking

- Items here become ADRs (architecture / security) or PROGRESS entries (delivery) once owners commit. Items closed should be removed from this list with the resolution noted.
