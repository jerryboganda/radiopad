# Copilot security and PHI threat model

## Assets

- RadioPad tenant data, clinical report context, PHI, audit chain, and user identity.
- GitHub App/OAuth client secrets and user access tokens.
- Local Copilot CLI credentials stored by the official GitHub CLI/keychain path.

## Non-negotiable controls

- Tenant filtering flows through `TenantedController.ResolveContextAsync`.
- Admin endpoints require RBAC and append audit rows.
- Prompt/context logging is forced off; usage/session rows store hashes and metadata only.
- Secret config is represented as references (`env:`, `vault:`, `kms:`, cloud KMS prefixes), never raw secret bytes in API responses. Those references are also covered by the existing AES-256-GCM column encryptor so database reads do not reveal vault paths, environment variable names, or token-reference naming patterns.
- Chat fails closed unless LocalCli runtime, account entitlement, quota, context filtering, and PHI gates all pass.

## Primary threats and mitigations

| Threat | Mitigation |
| --- | --- |
| User token exposure to WebView or IPC | No token-returning API/IPC exists; the Tauri CLI bridge reports only status/login metadata and executes fixed commands. |
| Admin token impersonation | Explicitly unsupported; no endpoint accepts a shared admin token to act as a user. |
| PHI egress to Copilot | Context preview blocks clinical/PHI-like context and the backend rejects sessions with PHI-like message/context before any CLI process starts. |
| Undocumented endpoint drift | Architecture permits only official SDK/CLI/REST public docs. |
| Plaintext CLI credentials | Settings include `requireOsKeychainForCli`; CLI status surfaces environment-token override warnings and never reads raw credential files. |
| Prompt or output retention | Session/message records persist hashes and status only; AI output is returned to the caller and rendered with `.ai-mark` until reviewed. |
| Quota bypass or runaway CLI processes | Backend checks request/concurrency quotas before spawn and uses fixed `ProcessStartInfo.ArgumentList`; Tauri uses fixed `gh` subcommands with timeouts. |
| Audit tampering | New Copilot actions append through existing `IAuditLog`; no update/delete audit path added. |

## Preview limitations

GitHub Copilot SDK and REST management/usage/content-exclusion APIs are preview surfaces and may change. RadioPad treats preview availability as capability input, not entitlement to bypass GitHub policy, seat assignment, SSO, rate limits, or organization controls.
