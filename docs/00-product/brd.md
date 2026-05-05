# Business Requirements Document (BRD)

**Status:** Draft  ·  **Owner:** Product  ·  **Last Updated:** 2026-05-04

## Business objectives

1. Establish RadioPad as a credible, BAA-friendly alternative to closed RIS reporting modules.
2. Reach 10 paying tenants and 100 active radiologists by end of Phase 3.
3. Maintain ≥ 99.5% monthly availability for hosted offering.
4. Keep gross margin ≥ 70% on hosted SKU.

## Stakeholders

| Stakeholder | Interest |
| --- | --- |
| Radiologist | Faster, safer reporting. |
| Radiology informatics admin | Configurable rulebooks, audit, FHIR export. |
| IT / security officer | PHI routing controls, append-only audit, on-prem option. |
| CFO / practice owner | ROI on reporting time saved. |
| Compliance / privacy officer | HIPAA / GDPR alignment, DPIA. |

## Revenue model

- **Hosted SaaS** — per-radiologist seat (monthly or annual).
- **On-prem** — annual license + support; required for PHI-sensitive customers using local AI providers.
- **Marketplace** (future) — institutional rulebook packs and template bundles.

## Market assumptions

- Decision-maker: radiology department chair or informatics lead.
- Buying cycle: 60–120 days; security review is the longest stage.
- Price benchmark: ~25% of an attending radiologist's monthly salary saved per FTE = budget headroom.

## Operational requirements

- 24×5 support during business hours; on-call rotation for hosted SKU.
- Quarterly clinical-content updates (rulebooks, templates).
- Monthly security patch cadence; emergency patches as needed.

## Business risks

- Vendor outage at AI provider degrades UX (mitigated by fallback chain and Mock provider).
- Regulatory changes around AI in clinical software (mitigated by human-in-the-loop sign-off and on-prem option).
- Open-source competitor for the rulebook engine (mitigated by clinical content depth and integrations).

## Success metrics

See [kpi-metrics.md](kpi-metrics.md).
