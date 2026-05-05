# Model Abstraction

**Status:** Current  ·  **Owner:** Engineering  ·  **Last Updated:** 2026-05-04

## Layers

```
ReportsController (POST /api/reports/{id}/ai)
   │
   ▼
AiGateway        ← PHI policy + audit + provider selection
   │
   ▼
IProviderAdapter ← interface
   │
   ├── MockProviderAdapter
   ├── AnthropicProviderAdapter
   └── OllamaProviderAdapter
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
- The gateway loads the provider row, resolves `ApiKeySecretRef` via `EnvSecretResolver`, and dispatches to the adapter.
- No silent fallback — if the chosen provider fails, the user must re-select.

## Adapter contract

```csharp
public interface IProviderAdapter
{
    string Name { get; }                    // "mock" | "anthropic" | "ollama"
    Task<AiResult> GenerateAsync(AiRequest request, CancellationToken ct);
}
```

`AiRequest` carries the de-identified payload and a routing tag. `AiResult` carries text + token counts + latency.

## Adding a provider

1. Implement `IProviderAdapter`.
2. Register in DI.
3. Add a row in the provider catalog ([provider-catalog.md](../03-architecture/provider-catalog.md)) with the appropriate compliance class.
4. Author at least one happy-path integration test using `WebApplicationFactory<Program>` and the new adapter mocked or a Sandbox endpoint.
5. Document in [model-policy.md](../01-ai-agent/model-policy.md).
