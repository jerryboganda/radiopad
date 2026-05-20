# RadioPad GitHub Copilot integration architecture

**Status:** Current  ·  **Owner:** Engineering + Security  ·  **Last Updated:** 2026-05-19

RadioPad integrates Copilot only through official GitHub-supported surfaces:

- GitHub Copilot SDK public preview for app-owned Copilot experiences, where auth stays backend-side or in an official local credential store.
- GitHub Copilot CLI authentication for local signed-in-user desktop mode. The CLI uses GitHub OAuth/device auth and stores credentials in the OS keychain when available; plaintext fallback is treated as an admin policy risk.
- GitHub REST API Copilot public-preview endpoints for org/user management, usage metrics, and content exclusion where GitHub documents the scope and preview contract.

## Implemented production slice

The checked-in implementation supports the official local GitHub CLI path while keeping SDK/GitHub App modes unavailable until a backend-safe SDK transport is reviewed:

1. ASP.NET Core owns `/api/copilot/admin/*` and `/api/copilot/*`.
2. Tenant-scoped settings, feature flags, user account snapshots, entitlement snapshots, quotas, session/message metadata, diagnostics, and metadata-only usage events are persisted.
3. Admins can save mode/runtime policy and write-only secret references. Secret bytes are not accepted or returned.
4. Users can link the official local GitHub CLI account through a token-free Tauri bridge. RadioPad records only login/status metadata; credentials remain in GitHub CLI/OS keychain storage.
5. Session entrypoints preview context, block PHI/secrets/excluded files, enforce entitlements and quotas, then route through `IAiGateway` using a tenant-owned `github-copilot-cli` provider. The provider adapter is the only component that may invoke the local `copilot` binary.
6. SDK/OAuth enterprise-managed and BYO labels are represented for policy/admin setup, but requests remain unavailable unless a future reviewed backend SDK transport is added.

The WebView receives status and masked configuration only. Raw GitHub tokens are never stored in frontend state, localStorage/sessionStorage, IPC, logs, or audit details.

## AI provider catalog integration

RadioPad exposes two Copilot-related provider ids in the provider registry:

- `github-copilot-sdk` — policy-mode provider id for a future official backend-safe GitHub Copilot SDK transport. It is registered as an `IAiProviderAdapter`, defaults to `Sandbox`, returns `runtime_not_configured` by default, and refuses PHI even if a provider row is accidentally marked `PhiApproved`.
- `github-copilot-cli` — CLI subprocess provider. It supplies prompts through Copilot CLI's stdin option stream to the configured GitHub Copilot CLI binary and is treated as `Sandbox` by default because the binary may call vendor cloud APIs.

The SDK provider exists to make admin setup, OpenAPI, and policy modeling explicit without pretending an unsupported private Copilot chat endpoint exists. Enabling a real SDK transport requires a separate security review and must keep token exchange/storage backend-side.

## Unsupported and blocked

- IDE token scraping, private Copilot endpoints, and classic PAT assumptions.
- Shared admin token impersonation of users.
- Frontend Copilot SDK usage requiring user tokens in browser JavaScript.
- PHI prompt/code routing to Copilot.
- Broad Tauri shell/filesystem/network permissions.

## Runtime selection

`CopilotIntegrationSettings` supports disabled, enterprise-managed, BYO account, local CLI, and BYOK labels. `LocalCli` is the implemented runtime: it requires tenant enablement, `CliRuntimeEnabled`, a linked local CLI account, entitlement success, quota availability, context/PHI filtering, and an enabled `github-copilot-cli` row in the provider registry. Enterprise-managed/BYO SDK modes remain represented but return runtime/configuration errors until a reviewed official SDK transport is installed.

## Capability matrix

| Desired capability | Official surface | RadioPad stance |
| --- | --- | --- |
| Copilot chat sessions and streaming | Copilot SDK public preview session APIs and streaming events; Copilot CLI supports local signed-in-user prompts | Local CLI sessions are implemented as non-streaming fixed-argument calls; `github-copilot-sdk` fails closed until a reviewed backend-safe SDK transport is installed. |
| User OAuth / GitHub App tokens | Copilot SDK supports user tokens from OAuth/GitHub Apps; classic `ghp_` tokens are not supported by SDK auth docs | OAuth authorization URL generation is surfaced, but token exchange/storage must terminate in backend/vault secret handling before SDK mode is enabled. |
| Local signed-in-user desktop mode | Copilot CLI OAuth/device flow with OS keychain storage where available | Implemented via fixed Tauri commands for status/login/logout and backend session broker calls; no token bytes cross IPC/API. |
| Environment-token auth | Copilot SDK/CLI support `COPILOT_GITHUB_TOKEN`, `GH_TOKEN`, and `GITHUB_TOKEN` | Disabled by default for production desktop because environment tokens silently override keychain credentials. |
| BYOK/custom provider | Copilot SDK BYOK public preview | Treated as a custom-provider mode, not GitHub Copilot Enterprise entitlement. |
| Seat and billing management | GitHub REST Copilot user-management public preview endpoints | Backend admin diagnostics/sync only; no desktop secret or frontend token access. |
| Usage metrics | GitHub REST Copilot usage-metrics report endpoints | Import only where enterprise/org policy enables metrics and token permissions allow it. |
| Content exclusions | GitHub REST content-exclusion public preview endpoints | Admin-only sync; GitHub API preview limitations are shown instead of hidden. |
| Full enterprise policy management | Partly REST-visible; many settings remain GitHub-admin UI managed | RadioPad mirrors and explains supported state; unsupported controls are documented, not faked. |

## Session and quota behavior

- Context preview removes empty, binary, lockfile/media, secret-bearing, and clinical/PHI-like items before a prompt can run.
- The prompt body and Copilot output are not stored. `CopilotMessage` and `CopilotUsageEvent` persist hashes, status, runtime, durations, and block kinds.
- Default quotas apply when no tenant policies are saved: 100 tenant requests/hour with five concurrent sessions, and 20 user requests/hour with one concurrent session.
- Admin quota policies can override tenant/user request windows and concurrency for the `chat` feature.
