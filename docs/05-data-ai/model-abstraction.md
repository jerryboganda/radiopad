# Model Abstraction

**Status:** Current  ·  **Owner:** Engineering  ·  **Last Updated:** 2026-05-19

## Layers

```
ReportsController (POST /api/reports/{id}/ai)
   │
   ▼
AiGateway        ← PHI policy + audit + provider selection
   │
   ▼
IAiProviderAdapter ← interface
   │
   ├── MockAiAdapter / AnthropicAiAdapter
   ├── OpenAiCompatibleProvider / cloud providers
   ├── Local providers (Ollama / vLLM / llama.cpp)
   └── CLI / SDK providers (Copilot / Gemini / Codex)
```

The gateway is the **only** place that performs the PHI policy check. The adapters are dumb: they accept a request DTO and return a response DTO.

## Provider compliance class (the routing key)

```
Blocked            // never used
Sandbox            // dev / non-PHI scratchpad
DeIdentifiedOnly   // de-identified text only
PhiApproved        // BAA-backed remote provider
LocalOnly          // local network, never leaves the tenant
```

`EnforcePhiPolicy` allows a request with `containsPhi: true` only when the provider's class is `PhiApproved` or `LocalOnly`. Otherwise it audits `ProviderBlocked` and throws `ProviderPolicyException`.

## Selection logic

- The user picks a provider for the request (UI dropdown, default per modality).
- The gateway loads the provider row, resolves `ApiKeySecretRef` via `ProviderSecretResolver`, and dispatches to the adapter.
- No silent fallback — if the chosen provider fails, the user must re-select.

## Adapter contract

```csharp
public interface IAiProviderAdapter
{
   string Id { get; }                    // "mock" | "openai-compatible" | "gemini-cli"
   Task<AiResult> CompleteAsync(AiCompletionRequest request, CancellationToken ct);
}
```

`AiCompletionRequest` carries the provider row, system prompt, user prompt, prompt version, and `ContainsPhi` flag. `AiResult` carries text + token counts + latency.

Adapters that can perform a safe prompt-free readiness check may also implement `IAiProviderHealthProbe`. Health probes must never send report text, PHI, or secret material.

## Adding a provider

1. Implement `IAiProviderAdapter`.
2. Register in DI.
3. Add a row in the provider catalog ([provider-catalog.md](../03-architecture/provider-catalog.md)) with the appropriate compliance class.
4. Author at least one happy-path or fail-closed test using a mocked adapter/transport; do not hit live external providers in CI.
5. Document in [model-policy.md](../01-ai-agent/model-policy.md).
