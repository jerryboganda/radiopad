# RadioPad CLI

**Status:** Current  ·  **Owner:** Engineering  ·  **Last Updated:** 2026-05-19

The CLI ships as a .NET 8 global tool (`radiopad`). It links the same Domain
and Validation assemblies as the API so that rulebook lint runs offline.

## Install (once toolchains are on PATH)

```powershell
dotnet pack cli/RadioPad.Cli -c Release
dotnet tool install -g --add-source ./cli/RadioPad.Cli/bin/Release RadioPad.Cli
```

Or run from source:

```powershell
dotnet run --project cli/RadioPad.Cli -- <command>
```

## Global flags

- `--headless` — non-interactive mode. Commands never prompt and exit
  non-zero (`3`) on missing config / unauthenticated state. Suitable for CI.

## Exit codes

| Code | Meaning |
| --- | --- |
| `0` | Success |
| `1` | Generic failure |
| `2` | Invalid input / missing argument |
| `3` | Headless mode could not satisfy the request |
| `4` | Local PHI policy guard refused (defence-in-depth; server still authoritative) |

## Commands

### `radiopad login [--device-flow]`

Stores the tenant slug + user email + backend URL in
`~/.radiopad/config.json`. With `--device-flow`, the CLI runs the RFC 8628
OAuth 2.0 Device Authorization Grant against `/api/auth/device/authorize`
and `/api/auth/device/token`, persisting the bearer token alongside the
identity. Tokens are never echoed to stdout.

```powershell
radiopad login --tenant dev --user radiologist@radiopad.local
radiopad login --tenant prod --user me@hospital.org --device-flow
```

### `radiopad daemon start | stop | status | restart`

Manages the local `radiopad-api` backend service. Honours `--bind <addr>`
and `--port <port>` (default `127.0.0.1:7457`). The CLI passes a full
`http://<addr>:<port>` URL through `RADIOPAD_BIND` so the API's local-bind
safety default is preserved. The PID + start time are tracked in
`~/.radiopad/daemon.pid`. `stop` waits 5 s for graceful shutdown then
hard-kills.

The CLI locates the API binary alongside its own executable (or via
`RADIOPAD_API_PATH`).

### `radiopad rulebook validate <file>`

Lints a rulebook YAML file against `RulebookSpec`. Exits non-zero on failure.

### `radiopad rulebook test <file> --cases <dir>`

Runs JSON golden cases against the local validator. Cases are strict: the
command fails when an expected `expectFlagged` id is missing or when the
validator emits an unexpected rule id for the same case.

### `radiopad generate --report <id> [--mode <mode>] [--rulebook <id>] [--provider <id>] [--output <file|->] [--format <json|text>]`

Calls `POST /api/reports/{id}/ai`. Supported modes:
`draft | impression | cleanup | concise | formal | patient_friendly | referring_summary`.
With `--output -` (or omitted) the result is printed to stdout. With
`--format text` only the rendered narrative is written. The CLI runs a
client-side PHI guard before the request — when the report is flagged
`containsPhi` and the chosen provider is not `PhiApproved` /
`LocalOnly`, the CLI exits `4` with `phi-policy-blocked` (the server
still enforces authoritatively).

### `radiopad audit export | verify | sync`

- `export` dumps recent audit events as JSON.
- `verify` recomputes the SHA-256 chain locally oldest-to-newest and reports breaks.
- `sync [--out <ndjson>] [--from <iso>] [--to <iso>]` pulls new events
  to a local NDJSON file (default `~/.radiopad/audit-events.ndjson`)
  for offline / SIEM forwarding. The watermark is persisted in
  `~/.radiopad/audit-sync.state` so subsequent runs are incremental.

### `radiopad provider list | test | register`

`register` upserts a provider via `POST /api/providers`:

```powershell
radiopad provider register `
  --type openai `
  --name "OpenAI Prod" `
  --model gpt-4o-mini `
  --api-key-ref env:OPENAI_API_KEY
```

Supported `--type` values: `azure-openai`, `aws-bedrock`, `google-vertex`
(`gcp-vertex`, `vertex-ai`, and `google-vertex-ai` are accepted aliases),
`openai` (`openai-direct` alias), `openai-compatible`, `anthropic`, `mock`,
`ollama`, `ollama-chat`, `vllm`, `llama-cpp`, `gemini-cli`, and
`codex-cli`. The `--api-key-ref`
value MUST be of the form `env:NAME` whenever a key is supplied; direct
cloud providers require it. OpenAI-compatible local shims and CLI providers
may leave it blank because credentials live in the endpoint or vendor CLI.
Newly registered providers default to compliance class `Sandbox`; promote to
`PhiApproved` / `LocalOnly` from the admin UI only after the BAA / DPA is on
file.

### `radiopad templates list | export <id> <file> | import <file>`

CRUD report templates against `/api/templates`. `export` matches by
`templateId` or GUID and writes JSON or YAML based on the file
extension. `import` accepts either format and POSTs the upsert.

### Validation packs (Iter-35)

`radiopad packs list [--rulebook <id>] | import --rulebook <id> --version <semver> [--name <text>] <dir> | export <pack-id> <file> | run <pack-id>`

Manages the versioned clinical validation packs surfaced at
`/api/validation-packs`. `import` reads every `*.json` golden case in the
target directory (the same on-disk format as
`rulebooks/_tests/<rulebook_id>/`) and POSTs them as a new Draft pack;
duplicate `(rulebookId, version)` is rejected with HTTP 409
`kind:"validation_packs"`. `run` executes the pack and returns a
`passed/total` summary (exit code 0 on full pass, non-zero otherwise).
Approval / deprecation are admin actions performed via the API or the
`/admin/validation-packs` page.

```powershell
radiopad packs import --rulebook chest_ct_v1 --version 1.0.0 --name "Chest CT GA" .\rulebooks\_tests\chest_ct_v1
radiopad packs list --rulebook chest_ct_v1
radiopad packs run <pack-id>
```

### `radiopad plugin verify <path> --sha256 <hex> [--signature <sig>]`

PRD DESK-009. Mirrors the desktop sandbox check: SHA-256 (constant-time)
plus an optional Ed25519 detached signature against the public key in
`RADIOPAD_PLUGIN_PUBKEY` (PEM or 32-byte hex). Use it in CI before
distributing a plugin or model artifact.

```powershell
$env:RADIOPAD_PLUGIN_PUBKEY = (Get-Content radiopad-plugin.pub.pem -Raw)
radiopad plugin verify .\plugin.bin --sha256 e3b0c44298fc1c149afbf4c8996fb924... --signature (Get-Content plugin.bin.sig.b64 -Raw)
```

Exit code 0 on success, 1 on any verification failure. Signatures may be
supplied as base64 or hex. See [desktop/PLUGIN_TRUST.md](../../desktop/PLUGIN_TRUST.md)
for key generation and rotation.

### `radiopad bundle export-invoices --out <zip> [--from] [--to]`

PRD BILL-001..006. Downloads the tenant's invoice ZIP from
`GET /api/billing/invoices/export?format=zip` and saves it locally. Date
filters are inclusive `yyyy-MM-dd` boundaries.

```powershell
radiopad bundle export-invoices --from 2026-01-01 --to 2026-03-31 --out invoices-q1.zip
```

The endpoint streams the response body straight to disk; no invoice data
is logged to stdout.

## Environment variables

| Name | Purpose |
| --- | --- |
| `RADIOPAD_INGEST_BEARER` | Bearer secret for `radiopad ingest …` |
| `RADIOPAD_PLUGIN_PUBKEY` | Public key (PEM/hex) for `radiopad plugin verify` |
| `RADIOPAD_API_PATH` | Override path to the `radiopad-api` binary used by `radiopad daemon start` |

## CLI-AI providers

**Iter-36 AI-012.** Two first-class adapters shell out to local AI CLIs. Each provider runs as a subprocess of the API host (or `radiopad daemon`), receives the composed prompt on **stdin**, and returns stdout. Compliance class defaults to `Sandbox` because the local binary may call a vendor cloud. Adapter-level policy now refuses PHI and secret-shaped prompts before process launch; the AI gateway still enforces tenant policy authoritatively.

| Adapter id | Default binary | Override env var | Underlying CLI |
| --- | --- | --- | --- |
| `gemini-cli` | `gemini` | `RADIOPAD_GEMINI_BIN` | Google Gemini CLI |
| `codex-cli` | `codex` | `RADIOPAD_CODEX_BIN` | OpenAI Codex CLI |

Shared environment knobs:

| Name | Purpose |
| --- | --- |
| `RADIOPAD_CLI_PROVIDER_TIMEOUT_MS` | Per-process timeout in milliseconds. Default `60000`. Exceeding it kills the process tree and surfaces a `ProviderTransportException`. |
| `RADIOPAD_CLI_PROVIDER_ALLOWED_PATHS` | **Required in Production** for CLI providers; optional in development. Semicolon-separated absolute paths or bare command names. Unlisted binaries throw `ProviderPolicyException("cli_binary_not_allowed")`; missing production allowlists throw `cli_binary_allowlist_required`. |
| `RADIOPAD_CLI_PROVIDER_ENV_ALLOWLIST` | Optional comma/semicolon-separated env vars to pass into CLI subprocesses in addition to OS basics (`PATH`, home/config/temp locations). Keep this minimal and provider-specific. |
| `RADIOPAD_CODEX_CLI_ENABLED` | Must be `1` before `codex-cli` will execute. Without it the adapter fails closed with `runtime_not_enabled`. |

Security boundary: arguments are passed via `ProcessStartInfo.ArgumentList` (never the legacy concatenated `Arguments` string), so prompts cannot escape into a shell. The subprocess runs from a neutral temp working directory with a scrubbed environment. Prompts containing NUL or other C0 control characters, PHI, or secret-like material are refused before launch. `codex-cli` runs `codex exec --sandbox read-only -` and never uses full-auto mode.

## Design lock

Console output uses plain ASCII — **no emoji icons** (per the design lock).
Colour, when added, will follow the same semantic families used in the UI:
red for blockers, amber for warnings, blue for info, green for OK.
