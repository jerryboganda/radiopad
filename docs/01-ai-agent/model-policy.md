# Model Policy

**Status:** Current  ·  **Owner:** Engineering + Security  ·  **Last Updated:** 2026-05-04

## Allowed providers (v0.1)

| Provider | Adapter | Compliance class | Use case |
| --- | --- | --- | --- |
| **Mock** | in-process | `LocalOnly` | Default for dev, tests, demos. |
| **Anthropic Claude** | HTTPS | `Sandbox` (default) → `PhiApproved` only after BAA | Drafting impressions / recommendations on de-identified text. |
| **Ollama (local)** | HTTP `127.0.0.1:11434` | `LocalOnly` | On-prem models for PHI-bearing input. |

New providers must:

1. Implement `IAiProviderAdapter`.
2. Land an entry in [../03-architecture/provider-catalog.md](../03-architecture/provider-catalog.md).
3. Carry a `ProviderComplianceClass` value justified in writing.
4. Be reviewed by Security + Engineering.

## Use cases per model

- **Impression drafting** — any compliant provider; preferred locally for PHI-bearing input.
- **Technique normalisation** — Mock or Ollama (deterministic; small models suffice).
- **Recommendation drafting** — Anthropic on de-identified text; Ollama otherwise.

## Cost controls

- Rate limit `[EnableRateLimiting("ai")]` = 60 requests / minute / tenant.
- Audit `AiResponse` records token count and latency for cost analysis.
- Budget alerts (planned): per-tenant monthly spend cap.

## Privacy constraints

- PHI requests must use `PhiApproved` or `LocalOnly` providers.
- Provider responses never persist outside the tenant boundary.
- API keys live in env vars referenced by `ApiKeySecretRef = "env:<NAME>"`.

## Fallback strategy

- If a remote provider fails (`5xx` or timeout), the gateway logs an `AiResponse` with the error and surfaces a 502 to the client. The client does **not** silently fall back to a different compliance class — explicit re-selection is required.
- For local provider availability, the desktop client may show "Ollama not reachable" and offer a Mock fallback for non-PHI input only.
