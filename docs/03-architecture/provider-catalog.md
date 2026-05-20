# AI Provider Catalog

**Status:** Current  ·  **Owner:** Engineering + Security  ·  **Last Updated:** 2026-05-19

RadioPad routes every AI request through `IAiGateway`. The gateway enforces tenant policy before invoking a provider adapter, writes append-only audit rows, records usage, and blocks PHI unless the configured provider row is `PhiApproved` or `LocalOnly`.

Secrets are always referenced as `ApiKeySecretRef = "env:<NAME>"`. Literal API keys are rejected by `POST /api/providers` and must never be stored in the database, frontend state, logs, fixtures, or docs. Hosted provider endpoint overrides are allowlisted by adapter before any bearer/API-key header is attached; use `openai-compatible` for reviewed BYO endpoints.

## Adapter IDs

| Adapter id | Kind | Default compliance | Configuration | Notes |
| --- | --- | --- | --- | --- |
| `mock` | In-process | `Sandbox` or `LocalOnly` by row | no endpoint, no secret | Deterministic dev/test adapter. |
| `anthropic` | HTTPS | `DeIdentifiedOnly` / `Sandbox` until BAA | `env:ANTHROPIC_API_KEY` | Anthropic Messages API. PHI requires explicit `PhiApproved` review. |
| `openai` | HTTPS | `Sandbox` | `env:OPENAI_API_KEY` | Direct OpenAI adapter. Endpoint overrides are restricted to `api.openai.com`; PHI requires BAA/ZDR/abuse-monitoring review before `PhiApproved`. |
| `openai-compatible` | HTTPS/local HTTP | remote `Sandbox`, local `LocalOnly` when the endpoint is actually tenant-controlled | endpoint URL, optional `env:<NAME>` key | Generic `/v1/chat/completions` adapter for OpenRouter, Groq, Together, Mistral, NVIDIA NIM, vLLM, local shims, and similar endpoints. Private-network endpoints require `LocalOnly`; PHI requires `LocalOnly` or an explicit reviewed allow flag. |
| `azure-openai` | HTTPS | `PhiApproved` when tenant has Microsoft BAA | Azure deployment endpoint + `env:<NAME>` key | Recommended cloud path for PHI tenants after region/BAA review. Endpoints must be Azure OpenAI / Cognitive Services hosts. |
| `aws-bedrock` | HTTPS | `PhiApproved` when tenant has AWS BAA | region/model + AWS env refs | Use only Bedrock models in the tenant-approved compliance scope. Endpoints must be Bedrock AWS hosts. |
| `google-vertex` | HTTPS | `PhiApproved` when tenant has Google Cloud BAA | project/location/model + service account env ref | Vertex publisher models only. Endpoints must be Google AI Platform hosts. |
| `ollama` | Local HTTP | `LocalOnly` | `http://127.0.0.1:11434` | Legacy `/api/generate` adapter. |
| `ollama-chat` | Local HTTP | `LocalOnly` | `http://127.0.0.1:11434` | Preferred Ollama chat adapter. |
| `vllm` | Local OpenAI-compatible HTTP | `LocalOnly` | `http://127.0.0.1:8000` | Local/on-prem vLLM endpoint. |
| `llama-cpp` | Local HTTP | `LocalOnly` | `http://127.0.0.1:8080` | Local llama.cpp server. |
| `github-copilot-sdk` | Official SDK transport, fail-closed | `Sandbox` | no secret until official backend-safe transport is installed | Provider id exists so policy can be modeled. Runtime returns `runtime_not_configured` until a reviewed official SDK transport is enabled. PHI routing is always refused. |
| `github-copilot-cli` | CLI subprocess | `Sandbox` | `RADIOPAD_COPILOT_BIN` (default `copilot`) | Prompt is supplied through Copilot CLI's stdin option stream. PHI and secret-like prompts are refused before launch. |
| `gemini-cli` | CLI subprocess | `Sandbox` | `RADIOPAD_GEMINI_BIN` (default `gemini`) | Prompt is piped on stdin in headless mode with `--output-format json`. JSON stdout is parsed when present; PHI and secret-like prompts are refused before launch. |
| `codex-cli` | CLI subprocess, fail-closed | `Sandbox` | `RADIOPAD_CODEX_BIN` (default `codex`), `RADIOPAD_CODEX_CLI_ENABLED=1` | Prompt is piped on stdin via `codex exec --sandbox read-only -`. The adapter never opts into full-auto mode; PHI and secret-like prompts are refused before launch. |

Production CLI providers require `RADIOPAD_CLI_PROVIDER_ALLOWED_PATHS`; empty/unset allowlists are accepted only in development. Server-side GitHub Copilot CLI execution in production also requires `RADIOPAD_COPILOT_SERVER_CLI_ENABLED=1`.

## Health Probes

`POST /api/providers/{id}/health` is prompt-free and never sends clinical content.

- Local providers probe their metadata endpoints (`/api/tags`, `/v1/models`, or `/health`).
- `openai-compatible` probes `GET /v1/models` without bearer auth and blocks unsafe endpoint targets before the request.
- CLI providers verify the configured binary without passing a prompt.
- `github-copilot-sdk` reports unavailable until an official backend-safe SDK transport is installed and reviewed.

## Adding A Provider

1. Implement `IAiProviderAdapter`; implement `IAiProviderHealthProbe` when a safe prompt-free readiness check exists.
2. Register the adapter in `Program.cs`.
3. Add the adapter id to the Providers UI, CLI registration allowlist, OpenAPI schema, and this catalog.
4. Add tests for happy path, missing configuration, transport failure, and PHI policy block.
5. Update `docs/01-ai-agent/model-policy.md` and `docs/09-regulatory/vendor-risk-register.md` with the compliance posture.
