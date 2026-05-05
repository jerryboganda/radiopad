# AI Audit Log

**Status:** Current  ·  **Owner:** Engineering + Security  ·  **Last Updated:** 2026-05-04

> Companion to [audit-logging.md](../04-security/audit-logging.md), specific to AI events.

## Events

| Event | When | `DetailsJson` keys |
| --- | --- | --- |
| `AiRequest` (0) | Before calling provider | `reportId`, `provider`, `phiClass`, `promptId`, `tokensIn` (best estimate) |
| `AiResponse` (1) | After provider returns | `reportId`, `provider`, `latencyMs`, `tokensIn`, `tokensOut`, `outcome` ∈ `ok / error / blocked` |
| `ProviderBlocked` (5) | PHI policy block | `reportId`, `provider`, `phiClass`, `reason: "phi_to_non_compliant"` |
| `PolicyViolation` (9) | Future detected violation | varies |

## Forbidden fields

- Prompt text, completion text, full report sections.
- Patient identifiers.
- Provider API keys.

## Token accounting

- `tokensIn` / `tokensOut` are reported by the adapter.
- Aggregated nightly per tenant for billing (Phase 3).

## Latency

- `latencyMs` measured at the adapter call site.
- Used for SLO calculation per [../03-architecture/observability.md](../03-architecture/observability.md).

## Compliance class tagging

- Every AI event includes the `phiClass` of the request: `"phi"` or `"deid"`.
- Combined with the provider compliance class, this lets auditors verify the PHI policy held.

## Verifying

- The chain hash protects every AI event the same as any other event.
- `radiopad audit verify` covers the AI events automatically.
- Operators can filter `radiopad audit export` by action enum to produce AI-only logs for review.
