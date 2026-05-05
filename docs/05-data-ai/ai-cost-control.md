# AI Cost Control

**Status:** Draft  ·  **Owner:** AI + Ops  ·  **Last Updated:** 2026-05-04

## Levers

- **Provider choice.** Local Ollama is free at the margin; remote providers cost per token.
- **Prompt size.** RAG context capped at top-k chunks; instruction prompt audited regularly.
- **Output cap.** Bounded `max_tokens` per prompt category.
- **Cache.** None today; result caching is forbidden because input variations are clinically significant.

## Per-tenant budget

- Phase 2: tenants can set a daily token budget per provider.
- Soft limit: warn at 80%, block at 100% (returns 429 `kind: "budget_exceeded"`).
- Audit `BudgetExceeded` event (planned) when the block fires.

## Rate limits

- API group `[EnableRateLimiting("ai")]` = 60 req/min/tenant.
- Per-IP cap planned Phase 2.

## Token accounting

- Adapter reports `tokensIn` / `tokensOut`.
- Nightly rollup job aggregates per tenant + provider.
- Operators can see usage via `GET /api/usage` (Phase 2) and `radiopad usage` CLI.

## Pricing pass-through

- Hosted SKU: include AI quota in the tier; overage at provider list price + margin.
- On-prem: customer pays the provider directly; we surface usage but do not bill.

## Cost-aware operations

- Default provider is the customer's choice; not forcibly the cheapest.
- Streaming responses (Phase 2) reduce wall time but not token cost.
- Future: pre-summary step (cheap model) → main draft (expensive model) only when needed.
