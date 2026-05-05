# KPI / Metrics

**Status:** Current  ·  **Owner:** Product  ·  **Last Updated:** 2026-05-04

## Activation

- **First report drafted within 24h of seat creation:** target ≥ 80% of new radiologists.
- **First signed report within 7 days:** target ≥ 60%.

## Engagement

- **Reports drafted per active radiologist per week.**
- **AI assists accepted / suggested ratio.** (How often AI text is kept after editing.)
- **Validation findings resolved before sign-off / total findings.**

## Retention

- **Monthly active radiologists / total seats.** Target ≥ 75% by month 3.
- **Tenant churn:** Target < 5% annual.

## Revenue

- **MRR by tenant tier** (small / mid / enterprise).
- **Net revenue retention.** Target ≥ 110%.

## Reliability

- **Backend availability (hosted):** Target ≥ 99.5% monthly.
- **Audit chain integrity:** Target 100% verifiable.
- **PHI policy violations:** Target 0 (any incident is a SEV-1).

## Quality

- **Rulebook golden-case pass rate on `main`:** Target 100%.
- **Reports signed without Blocker findings:** Target 100%.
- **Accessibility audit pass rate (axe-core):** Target 0 critical violations.

## AI quality

- **Hallucination rate (per [../05-data-ai/ai-quality-rubric.md](../05-data-ai/ai-quality-rubric.md)):** Target < 2% on the impression-suggestion eval set.
- **PHI block recall:** Target 100% on the synthetic PHI-bearing test corpus.
- **AI cost per signed report:** Target < $0.10 on hosted SKU.

## How metrics are produced

- Audit log → daily JSON-Lines export → analytics warehouse (planned).
- Frontend RUM (Real User Monitoring) — planned (Phase 2).
- Synthetic golden suites in CI provide rulebook + PHI-block metrics on every push.
