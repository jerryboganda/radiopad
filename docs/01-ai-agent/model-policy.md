# Model Policy

**Status:** Current  ·  **Owner:** Engineering + Security  ·  **Last Updated:** 2026-05-19

## Allowed providers (v0.1)

Compliance classes below record the intended posture for each provider. They no longer gate PHI routing — see *Privacy constraints*.

| Provider | Adapter | Compliance class | Use case |
| --- | --- | --- | --- |
| **Mock** | `mock` | `Sandbox` or `LocalOnly` by row | Default for dev, tests, demos. |
| **Anthropic Claude** | `anthropic` | `DeIdentifiedOnly` / `Sandbox` by default, `PhiApproved` only after BAA | Drafting impressions / recommendations on de-identified text. |
| **OpenAI direct** | `openai` | `Sandbox` by default, `PhiApproved` only after BAA/ZDR review | Cloud drafting on non-PHI or approved PHI tenants. |
| **OpenAI-compatible endpoint** | `openai-compatible` | remote `Sandbox`; tenant-owned local endpoints may be `LocalOnly` | BYO OpenAI API compatible models such as OpenRouter, Groq, Together, Mistral, NVIDIA NIM, vLLM, or local shims. Private endpoints require `LocalOnly`; PHI requires local-only or an explicit reviewed allow flag. |
| **Azure OpenAI** | `azure-openai` | `PhiApproved` after Microsoft BAA and region review | Preferred cloud path for PHI tenants with Microsoft compliance posture. |
| **AWS Bedrock** | `aws-bedrock` | `PhiApproved` after AWS BAA and model-scope review | Tenant-approved Bedrock models. |
| **Google Vertex AI** | `google-vertex` | `PhiApproved` after Google Cloud BAA and project/region review | Vertex publisher models. |
| **Ollama / vLLM / llama.cpp (local)** | `ollama`, `ollama-chat`, `vllm`, `llama-cpp` | `LocalOnly` | On-prem models for PHI-bearing input. |
| **Gemini CLI** | `gemini-cli` | `Sandbox` | Local CLI subprocess for non-PHI/de-identified workflows. PHI and secret-like prompts are refused before launch. |
| **Codex CLI** | `codex-cli` | `Sandbox`, fail-closed | Local CLI subprocess for non-PHI/de-identified workflows. Requires explicit runtime opt-in; PHI and secret-like prompts are refused before launch. |

New providers must:

1. Implement `IAiProviderAdapter`.
2. Land an entry in [../03-architecture/provider-catalog.md](../03-architecture/provider-catalog.md).
3. Carry a `ProviderComplianceClass` value justified in writing.
4. Be reviewed by Security + Engineering.

## Use cases per model

- **Impression drafting** — any compliant provider; prefer `LocalOnly`, Azure OpenAI, Bedrock, or Vertex AI for PHI-bearing input when the tenant has the matching BAA/DPA posture.
- **Technique normalisation** — Mock or local adapters; small models usually suffice.
- **Recommendation drafting** — remote providers only for de-identified text unless explicitly `PhiApproved`; local providers otherwise.
- **Provider experimentation** — CLI and sandbox providers are suitable for non-PHI comparison and evaluation only.

## Cost controls

- Rate limit `[EnableRateLimiting("ai")]` = 60 requests / minute / tenant.
- Audit `AiResponse` records token count and latency for cost analysis.
- Budget alerts (planned): per-tenant monthly spend cap.

## Privacy constraints

- PHI requests are not restricted by compliance class. The gate that required `PhiApproved` or `LocalOnly` was removed on 2026-07-20 by operator decision, so PHI may reach any enabled provider, including third-party and browser-automation adapters with no BAA. The compliance classes in the table above are informational metadata describing intended posture, not an enforced routing control; only `Blocked` still refuses traffic. What remains is the audit trail: `containsPhi` is computed per request and recorded on the audit and usage rows.
- CLI providers default to `Sandbox` because a local binary may call a vendor cloud; the current adapters also refuse PHI and secret-shaped prompts at adapter level.
- Provider responses never persist outside the tenant boundary.
- API keys live in env vars referenced by `ApiKeySecretRef = "env:<NAME>"`.

## Fallback strategy

- If a remote provider fails (`5xx` or timeout), the gateway logs an `AiResponse` with the error and surfaces a 502 to the client. The client does **not** silently fall back to a different compliance class — explicit re-selection is required.
- For local provider availability, the desktop client may show "Ollama not reachable" and offer a Mock fallback for non-PHI input only.
