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
| `gemini-cli` | CLI subprocess | `Sandbox` | `RADIOPAD_GEMINI_BIN` (default `gemini`) | Prompt is piped on stdin in headless mode with `--output-format json`. JSON stdout is parsed when present; PHI and secret-like prompts are refused before launch. |
| `codex-cli` | CLI subprocess, fail-closed | `Sandbox` | `RADIOPAD_CODEX_BIN` (default `codex`), `RADIOPAD_CODEX_CLI_ENABLED=1` | Prompt is piped on stdin via `codex exec --sandbox read-only -`. The adapter never opts into full-auto mode; PHI and secret-like prompts are refused before launch. |
| `ubag` | HTTPS automation gateway | `PhiApproved` | `RADIOPAD_UBAG_BASE_URL`, optional server-only auth env ref | Routes report prompts to production UBAG (PHI gate removed 2026-06-27, operator decision — only de-identified text is sent). `ProviderConfig.Model` selects `chatgpt_web`, `gemini_web`, `deepseek_web`, or `mock`; default preset uses `gemini_web`. Secret-shaped prompts are still refused. |

Production CLI providers require `RADIOPAD_CLI_PROVIDER_ALLOWED_PATHS`; empty/unset allowlists are accepted only in development.

## Health Probes

`POST /api/providers/{id}/health` is prompt-free and never sends clinical content.

- Local providers probe their metadata endpoints (`/api/tags`, `/v1/models`, or `/health`).
- `openai-compatible` probes `GET /v1/models` without bearer auth and blocks unsafe endpoint targets before the request.
- CLI providers verify the configured binary without passing a prompt.
- `ubag` prefers UBAG `GET /v1/ready` (full readiness: job store, executor, artifact store, webhook outbox) plus target readiness without sending a prompt; it falls back to `/v1/health` only when the gateway answers 404/405 (older gateways). An **absent** `/v1/browser/contexts` row is treated as "ready, no explicit login signal" — NOT a failure: the vps-local executor drives working targets without registering contexts, so the probe reports the listed target ready and only an explicit context row can downgrade it to `login_required` (fix 2026-07-19; previously surfaced a bogus `context_not_found` that showed working primaries as "Unavailable").

## UBAG Guardrails

UBAG is for governed browser AI automation only. RadioPad's backend is the only
component that may call UBAG; the frontend never stores UBAG auth material.

- `RADIOPAD_UBAG_BASE_URL`: in production, the internal address on the shared `platform` docker network (`http://ubag-vps-gateway-1:8080`). The public `https://ubag.polytronx.com` sits behind operator Basic-auth and must **not** be used by RadioPad.
- Default API version: `2026-05-22`.
- `RADIOPAD_UBAG_ALLOWED_TARGETS`: default-deny allowlist. Unset means the conservative default list `chatgpt_web,gemini_web,deepseek_web,mock` (everything else is denied); set the env var to override explicitly. Production pins `gemini_web,deepseek_web,chatgpt_web,mock`.
- The ordered workflow is configurable via `RADIOPAD_UBAG_ORDERED_TARGETS`; the code default is `gemini_web,deepseek_web` (`chatgpt_web` is not included by default).
- The UBAG adapter (report-text path) is `PhiApproved` (operator decision 2026-06-27): the AiGateway compliance class gates it and only de-identified report text is routed, so the adapter itself no longer rejects PHI. Only the Hub endpoints (`POST /api/ubag/jobs` etc.) apply PHI/secret heuristics to raw operator prompts; secret-shaped prompts are rejected everywhere.
- Operator login, CAPTCHA, 2FA, consent, cookie, and credential handling remain manual inside UBAG Browser Sessions.
- **Auto-discovery / sync:** `UbagProviderDiscoveryService` materialises a provider row for **every allowed catalog target** the gateway lists (bounded by `RADIOPAD_UBAG_ALLOWED_TARGETS`), regardless of whether a login signal is available — so all UBAG web models (e.g. `chatgpt_web`) appear in the picker automatically without operator config. A freshly discovered row defaults `Enabled=ON` and relies on failure-based alerting; only an **explicit** logged-out signal from `/v1/browser/contexts` starts it disabled (fix 2026-07-19; the old code required an explicit `authenticated` signal that vps-local gateways never emit, so non-pinned targets like ChatGPT never surfaced). Curated primaries `gemini_web`/`deepseek_web` remain DevSeed-owned.

## Adding A Provider

1. Implement `IAiProviderAdapter`; implement `IAiProviderHealthProbe` when a safe prompt-free readiness check exists.
2. Register the adapter in `Program.cs`.
3. Add the adapter id to the Providers UI, CLI registration allowlist, OpenAPI schema, and this catalog.
4. Add tests for happy path, missing configuration, transport failure, and PHI policy block.
5. Update `docs/01-ai-agent/model-policy.md` and `docs/09-regulatory/vendor-risk-register.md` with the compliance posture.
