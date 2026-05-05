# Integration Architecture

**Status:** Current  ·  **Owner:** Engineering  ·  **Last Updated:** 2026-05-04

## Outbound integrations

| Provider | Purpose | Auth | Data exchanged | Failure modes | Retry |
| --- | --- | --- | --- | --- | --- |
| Anthropic Claude | AI impression / recommendation drafting | API key (`env:ANTHROPIC_API_KEY`) | De-identified report text | 429 / 5xx → 502 to client | None automatic (explicit re-ask) |
| Ollama (local) | Local AI for PHI-bearing input | none (loopback) | Report text incl. PHI | Connection refused → 502 to client | None automatic |
| Mock | Deterministic responses for dev/test | none | Synthetic | n/a | n/a |
| EHR / RIS (export) | One-way FHIR `DiagnosticReport` text | Customer-provided endpoint (Phase 2) | Final report text + JSON | Endpoint down → user-driven retry | Manual |

## Inbound integrations

| Integration | Source | Auth | Purpose |
| --- | --- | --- | --- |
| OIDC IdP (Phase 3) | Okta / Azure AD / Google | Standard OIDC PKCE | User authentication |
| RIS (Phase 2) | Hospital RIS | OAuth2 / mTLS | Pull `ServiceRequest` (study orders) |

## Integration policy

- Every integration is configurable per tenant.
- Provider credentials live behind `ApiKeySecretRef = "env:<NAME>"` — never in the DB plaintext.
- Outbound calls record an audit event (`AiRequest` / `AiResponse` / `ProviderBlocked` / `ReportExported`).
- Customer endpoints are validated with a non-PHI test request before being marked active.

## Failure handling

- A provider 5xx response surfaces as HTTP 502 from RadioPad to the client. The client never sees the provider's body.
- Timeouts: 15 s for AI providers, 60 s for local Ollama on first warm-up.
- Circuit breaker (planned, Phase 2): open after 5 consecutive failures within 60 s.
