# AI provider catalog

RadioPad routes every AI request through the AI gateway, which enforces tenant policy before invoking a provider adapter. This page lists the adapters that ship out of the box.

> Adding a new provider requires implementing `IAiProviderAdapter` in `RadioPad.Application/Services/Adapters` and registering it in `Program.cs`. Secrets are referenced by `ApiKeySecretRef` (e.g. `env:ANTHROPIC_API_KEY`) — never persisted in the database.

## Adapters

### `mock`

- **Use:** local development, integration tests, CI.
- **Compliance class:** `LocalOnly` (PHI permitted) or `Sandbox` (PHI blocked) — set per provider config.
- **Endpoint:** none — the adapter echoes deterministic text.
- **Secrets:** none.

### `anthropic`

- **Use:** Claude 3.5 / Claude 4 family for impression generation and cleanup.
- **Compliance class:** typically `PhiApproved` once a BAA is in place; otherwise `DeIdentifiedOnly`.
- **Endpoint:** `https://api.anthropic.com/v1/messages`.
- **Secrets:** `env:ANTHROPIC_API_KEY`.
- **Notes:** the gateway forwards `model`, `system`, and `messages`. Streaming is not used today.

### `ollama`

- **Use:** on-prem GPU deployments. Default for tenants with `RequirePhiApprovedProvider = true` and no cloud BAA.
- **Compliance class:** `LocalOnly`.
- **Endpoint:** `http://localhost:11434/api/generate` (override per tenant).
- **Secrets:** none.
- **Notes:** good fits for `llama3.1`, `qwen2.5`, and other models tuned for medical text. Resource footprint is the operator's responsibility.

## Compliance classes

The gateway permits PHI (`AiCompletionRequest.ContainsPhi == true`) only for providers in `PhiApproved` or `LocalOnly`. Disabled and `Blocked` providers are rejected unconditionally. Every rejection writes an `AuditAction.ProviderBlocked` event to the append-only log.

## Adding a new provider

1. Implement `IAiProviderAdapter` and register in `Program.cs`.
2. Add an entry to the tenant via `POST /api/providers` or the Providers UI.
3. Set `ApiKeySecretRef` to an `env:NAME` reference.
4. Document the new adapter on this page.
5. Add an integration test that proves PHI policy is honoured for the new compliance class.
