# Vendor Risk Register

**Status:** Draft  ·  **Owner:** Regulatory + Security  ·  **Last Updated:** 2026-05-20  ·  **Iteration:** 48

This register inventories every external vendor / processor that may touch RadioPad customer data, alongside the compliance posture and the safeguards that gate them. It is the canonical source for HIPAA Subprocessor disclosures and GDPR Article 28 records.

The PHI policy gate (`AiGateway.EnforcePhiPolicy`) **never** routes PHI to a vendor whose `ProviderComplianceClass` is anything other than `PhiApproved` or `LocalOnly`. See [eu-aiact-gdpr-profile.md](eu-aiact-gdpr-profile.md) for the EU posture and [baa-template.md](baa-template.md) for the BAA wording.

## Compliance class legend

| Class | Meaning |
| --- | --- |
| `PhiApproved` | BAA / DPA on file. PHI may be sent. |
| `LocalOnly` | Runs inside the customer's own infrastructure. No data leaves their boundary. PHI may be processed. |
| `DeIdentifiedOnly` | No BAA, but contractually limited to de-identified data. PHI is blocked. |
| `Blocked` | Disabled. Any attempt audits `ProviderBlocked`. |

## AI providers

| Vendor | Adapter (iter 31) | Region(s) | Class | BAA / DPA | Data retention | Transfer mechanism (EU) | Notes |
| --- | --- | --- | --- | --- | --- | --- | --- |
| Azure OpenAI | `AzureOpenAiProvider` | tenant-pinned (e.g. `swedencentral`, `eastus2`) | `PhiApproved` (with Microsoft BAA) | yes (Microsoft Online Services BAA) | Configurable; ZDR available with approval | EU-region pinning + Microsoft SCCs | Recommended default for US PHI tenants. |
| AWS Bedrock | `AwsBedrockProvider` | tenant-pinned (e.g. `eu-central-1`, `us-east-1`) | `PhiApproved` (with AWS BAA) | yes (AWS BAA) | No retention by Bedrock; provider-model dependent | AWS SCCs | Use Anthropic Claude / Meta Llama models that AWS confirms in scope. |
| Google Vertex AI | `GoogleVertexAiProvider` | tenant-pinned (e.g. `europe-west4`, `us-central1`) | `PhiApproved` (with Google BAA) | yes (Google Cloud BAA) | No prompt logging on configured projects | Google SCCs | Vertex publisher models only; user-managed encryption keys when SEC-003 lands. |
| OpenAI direct | `OpenAiDirectProvider` | global | `PhiApproved` only after BAA + ZDR + abuse-monitoring exemption | yes (OpenAI BAA) | ZDR + Modified Abuse Monitoring required for PHI | OpenAI SCCs | Without BAA + ZDR the adapter is set to `DeIdentifiedOnly`. |
| OpenAI-compatible (generic) | `OpenAiCompatibleProvider` | depends on endpoint | per-endpoint (remote defaults to `Sandbox`; admin can set `LocalOnly` for self-hosted) | per-endpoint | per-endpoint | per-endpoint | Covers DigitalOcean serverless inference, NVIDIA NIM, Cloudflare AI, Together, Groq, vLLM, Mistral, OpenRouter, Ollama, etc. Private-network endpoints require `LocalOnly`; hosted adapters validate HTTPS, host allowlists, default ports, no userinfo, and no IP literals before credentials attach. PHI requires `LocalOnly` or an explicit reviewed allow flag. |
| Gemini CLI | `GeminiCliProvider` | local binary, Google cloud possible | `Sandbox`; PHI refused by adapter | per-customer Google agreement | vendor-controlled | per-customer | Prompt is piped on stdin; JSON stdout is parsed when present; env scrub and secret/PHI refusal apply. |
| OpenAI Codex CLI | `CodexCliProvider` | local binary, OpenAI cloud possible | `Sandbox`; PHI refused by adapter | per-customer OpenAI agreement | vendor-controlled | per-customer | Fails closed unless `RADIOPAD_CODEX_CLI_ENABLED=1`; invokes `codex exec --sandbox read-only -`; no full-auto mode by default; env scrub and secret/PHI refusal apply. |

## Cloud / infrastructure

| Vendor | Purpose | Class | BAA / DPA | Region | Notes |
| --- | --- | --- | --- | --- | --- |
| Customer-controlled DB (PostgreSQL / SQLite) | Primary store | `LocalOnly` (customer infra) | n/a | customer-chosen | EF Core dev = SQLite, prod = PostgreSQL. |
| Customer-controlled object store (S3-compatible) | PDFs / DOCX exports | `LocalOnly` | per-customer | customer-chosen | RadioPad does not centrally host. |
| OS keyring (Windows Credential Manager / macOS Keychain / Linux Secret Service) | Desktop master key + device pairing token | `LocalOnly` | n/a | customer device | iter-31 `keyring` crate; no network. |

## Subprocessors with limited PHI scope

| Vendor | Purpose | Class | BAA / DPA | Notes |
| --- | --- | --- | --- | --- |
| Stripe | Billing (cards / invoices) | `DeIdentifiedOnly` (billing data, not PHI) | Stripe DPA | PHI never sent; only seat counts + plan metadata. |
| MailKit / SMTP relay (operator-chosen) | Magic-link + breach-notification emails | `DeIdentifiedOnly` | per-relay | Subject lines and bodies pass through `log_redactor` patterns; no PHI in outbound mail. |

## Review cadence

- **Quarterly** — every row reviewed by Security + Regulatory leads. Region pinning, retention, and BAA expiration are checked.
- **Per release** — any new adapter or new env-var added to `Provider.ApiKeySecretRef` triggers a register update in the same PR.
- **On compliance downgrade** — if a provider drops PhiApproved status, the gateway auto-blocks PHI to that provider; a register entry is filed within 24 h per [pms-plan.md](pms-plan.md) §2.

## Change log

| Date | Change | Iter |
| --- | --- | --- |
| 2026-05-19 | Added Iter-47 provider hardening notes for hosted endpoint allowlists and Codex read-only invocation | 47 |
| 2026-05-20 | Added Iter-48 browser-auth hardening posture: production magic links require configured SMTP/public URL and no raw dev links are exposed | 48 |
| 2026-05-19 | Added fail-closed Gemini CLI and Codex CLI provider rows; clarified OpenAI-compatible endpoint/PHI posture | 46 |
| 2026-05-04 | Initial register; 5 AI provider rows; scaffolding for KMS / SCIM follow-ups | 31 |
