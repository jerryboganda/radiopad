# Security architecture

**Status:** Current  Â·  **Owner:** Security + Engineering  Â·  **Last Updated:** 2026-06-05

RadioPad processes Protected Health Information (PHI). Security posture is
**deny-by-default for AI egress, append-only for audit, tenant-isolated for
storage**.

## Threat model (summary)

| Asset | Threat | Mitigation |
| ----- | ------ | ---------- |
| PHI in prompts | Leakage to non-approved AI provider | `AiGateway` PHI policy enforced before HTTP egress; `ProviderPolicyException` on violation |
| Audit log | Tampering / silent edits | SHA-256 hash chain (`IntegrityChain`); only `INSERT`s allowed |
| Provider API keys | Exfiltration via API responses | Keys stripped in `ProvidersController` GET responses (`apiKeyConfigured` boolean only) |
| Cross-tenant access | Tenant A reading Tenant B's reports | Every controller filters by tenant resolved from verified request identity; raw tenant/user headers are dev/test-only |
| Rulebook tampering | Unapproved edits hitting production | Status workflow `Draft -> InReview -> Approved`; approval requires passing golden tests (planned) |
| AI hallucination | Unverified AI text becoming the signed report | `.ai-mark` UI + `aiHighlightsJson` persistence + Acknowledge step blocked when blockers > 0 |
| Account/session takeover | Stolen bearer, stale cookie, or weak recovery path | OIDC Code + PKCE production direction; current bearers are 12-hour HMAC tokens bound to session epoch; dev tuple sign-in is disabled outside explicit dev/test; step-up MFA for sensitive actions remains a production gap until enforced per endpoint |

## Provider compliance classes

Defined in `ProviderComplianceClass`:

| Class | Numeric | PHI allowed? | Notes |
| ----- | ------- | ------------ | ----- |
| `Blocked` | 0 | No | All requests rejected |
| `Sandbox` | 1 | No | Test/dev only; never PHI |
| `DeIdentifiedOnly` | 2 | No | Caller must scrub before send |
| `PhiApproved` | 3 | Yes | Vendor BAA / contractual approval |
| `LocalOnly` | 4 | Yes | Stays on device (e.g. Ollama) |

## Identity & multi-tenancy

- Tenant + user are resolved from a verified request identity. Current accepted production paths are `rp_` opaque bearers (plus tenant/user lookup hints) and validated OIDC JWTs.
- `X-RadioPad-Tenant` and `X-RadioPad-User` are accepted as authoritative only in explicit dev/test mode.
- `TenantedController.ResolveContextAsync` is the controller resolution path and rejects requests with no verified identity outside dev/test mode.
- Enterprise identity rows are additive: `GlobalUser` is metadata only, `ExternalIdentity` maps stable provider subjects, and `TenantMembership` must point to an active tenant-scoped `User` before access is granted.
- `AuthSession` stores hashed session inventory only. Raw bearer/session material must never be persisted or logged, and current revocation still honors `User.SessionEpoch`, lockout, and deprovisioning.
- Production login decision: generic OIDC Authorization Code + PKCE with
  magic-link fallback. Current web sessions can use the bearer-backed
  `rp_session` HttpOnly/SameSite cookie with current-session, session-list,
  revoke, and logout endpoints; desktop/mobile use OS secure storage. Generic
  OIDC authorize/callback/code-exchange routes are available at
  `/api/auth/oidc/authorize` and `/api/auth/oidc/callback`.
- Production sensitive actions require step-up MFA freshness. Current controls
  include IdP MFA expectations, TOTP endpoints, and OIDC MFA claim toggles, but
  route-by-route step-up enforcement is not complete.

## Secrets

- Provider API keys are stored on `ProviderConfig` and never returned by any
  read API (`GET /api/providers` returns only `apiKeyConfigured: bool`).
- Production stores keys via the platform's KMS / Secrets Manager, fronted by
  an `IProviderSecretStore` adapter (not yet shipped).
- Stripe secrets are read from environment variables only and are never
  surfaced through any read API.

### Environment variables

| Variable | Purpose | Notes |
| --- | --- | --- |
| `RADIOPAD_STRIPE_SECRET_KEY` | Stripe server-side secret used for all Stripe API calls (Checkout, Billing Portal, invoices, refunds, Connect). | **Canonical**. Legacy `STRIPE_SECRET_KEY` is accepted as a fallback for one release; remove before v0.3. |
| `RADIOPAD_STRIPE_WEBHOOK_SECRET` | HMAC secret for `/api/billing/webhook` signature verification. | **Canonical**. Legacy `STRIPE_WEBHOOK_SECRET` accepted for one release. |
| `RADIOPAD_BIND` | API bind address. Defaults to `127.0.0.1`. | Remote exposure requires a TLS reverse proxy. |
| `RADIOPAD_AUTH_SECRET` | HMAC secret used for `rp_` bearer signing. | Required outside Development/Testing; default secret is fail-closed. |
| `RADIOPAD_OIDC_AUTHORITY` / `RADIOPAD_OIDC_REQUIRE_MFA` | OIDC issuer + MFA `amr` enforcement. | Generic OIDC Code + PKCE is the production direction; route-level step-up still needs completion. |
| `RADIOPAD_IP_ALLOWLIST` | Comma-separated CIDR list. Empty = no-op. | PRD SEC-007. |

### Billing PII handling

The billing surface (`BillingController`, `MarketplaceController`,
`SubscriptionLifecycleService`) never writes raw email, Stripe customer
identifiers, payment-intent identifiers, or subscription identifiers into
the audit log. The `IBillingAudit` helper hashes each value to `sha16:<hex>`
(SHA-256 truncated to 16 hex chars) before it lands in `DetailsJson`. Raw
identifiers remain available to admins through `GET /api/billing/status`
and `GET /api/billing/invoices` because those rows live on `TenantSettings`
and on Stripe's API, not in the audit chain.

Webhook deliveries from Stripe are deduplicated through the
`StripeWebhookEvents` table, which carries a unique index on `EventId`.
Replays are accepted with `200 OK` but produce no second audit row and no
second mutation, satisfying Stripe's at-least-once delivery semantics
without breaking the append-only invariant of `AuditEvents`.

The `PlanQuotaService` gate at the AI gateway throws
`QuotaExceededException`, which the global problem-details middleware
translates to `402 { kind: "quota_exceeded", resetAt }`. The
`SuspensionGuardMiddleware` short-circuits every mutating non-billing
`/api/*` request with `402 { kind: "tenant_suspended", suspendedAt }` when
`TenantSettings.SuspendedAt` is non-null; `/api/billing/*` and
`/api/auth/*` remain reachable so operators can recover.

## Network defenses (SEC-008 / SEC-011, iter-32)

Three controls protect the public API from network-layer abuse and
exfiltration. The operator-wide IP allowlist runs before authentication;
tenant-specific allowlists and tenant/user rate partitions run after verified
identity projection so they do not trust raw client headers.

### IP allowlist (`IpAllowlistMiddleware`, SEC-008)

- Global gate from `RADIOPAD_IP_ALLOWLIST` (CSV / newline-separated CIDR
  list, IPv4 + IPv6).
- Per-tenant override in `TenantSettings.IpAllowlistJson` (JSON array of
  CIDR strings). When populated, ANDs with the global gate. Falls back to
  the legacy `TenantSettings.IpAllowlistCidr` CSV when the JSON column is
  empty.
- Loopback (`127.0.0.1`, `::1`) is **always** allowed - the API binds
  loopback by default.
- `X-Forwarded-For` is honoured **only** when
  `RADIOPAD_TRUST_FORWARDED_FOR=1`. With the toggle off, an attacker
  behind a misconfigured proxy cannot spoof the client IP. Only the
  left-most XFF entry is trusted.
- On block, writes a `PolicyViolation` audit row with
  `reason: "ip_not_allowed"` and a SHA-256-hashed client IP (raw IPs
  never enter the audit chain), and returns RFC-7807 problem+json with
  `kind: "ip_not_allowed"`.

### Rate limit (`RateLimitMiddleware`, SEC-008)

Two partitioned fixed-window limiters using
`System.Threading.RateLimiting`:

| Partition | Default limit | Override env var |
| --------- | ------------- | ---------------- |
| Per-IP | 100 req / minute | `RADIOPAD_RATE_LIMIT_IP_PER_MIN` |
| Per-tenant | 5000 req / minute | `RADIOPAD_RATE_LIMIT_TENANT_PER_MIN` |

`/api/health`, `/api/health/ready`, and loopback bypass the limiter so
liveness probes and dev productivity stay unaffected. Rejections return
RFC-7807 `{kind: "rate_limited", retryAfterSeconds}` plus a standard
`Retry-After` header.

### Anomaly detector (`AnomalyDetector`, SEC-011)

Background service that scans the last 5 minutes of audit events every
60 seconds. Iter-32 patterns emit
`AuditAction.SecurityAlert`; iter-31 patterns continue emitting
`AuditAction.AnomalyDetected` for back-compat.

| Pattern | Threshold | Window |
| ------- | --------- | ------ |
| `provider_blocked_burst_by_user` | > 50 `ProviderBlocked` per user | 5 min |
| `policy_violation_burst_by_ip` | > 20 `PolicyViolation` per client-IP-hash | 5 min |
| `user_login_failure_burst` | > 100 `UserLogin` failure rows per user | 5 min |
| `ai_request_spike` | recent `AiRequest` count â‰¥ max(20, 10Ã— per-window baseline) | 5 min recent vs 24 h baseline |

On flag the detector:

1. Appends a `SecurityAlert` audit row through `IAuditLog.AppendAsync`.
2. Logs at `Warning`.
3. Optionally POSTs JSON to `RADIOPAD_SECURITY_WEBHOOK_URL` with an
   `X-RadioPad-Signature: sha256=<hex>` header derived from
   `RADIOPAD_SECURITY_WEBHOOK_SECRET`. The legacy
   `RADIOPAD_ANOMALY_WEBHOOK_URL` is still honoured. The webhook secret is
   never echoed back in the body, response, or audit row.

### Network defense env vars

| Variable | Purpose | Default |
| -------- | ------- | ------- |
| `RADIOPAD_IP_ALLOWLIST` | Global CIDR allowlist (CSV / newline) | unset (no global gate) |
| `RADIOPAD_TRUST_FORWARDED_FOR` | Honour `X-Forwarded-For` left-most entry when `=1` | unset (off) |
| `RADIOPAD_RATE_LIMIT_IP_PER_MIN` | Per-IP fixed-window limit | `100` |
| `RADIOPAD_RATE_LIMIT_TENANT_PER_MIN` | Per-tenant fixed-window limit | `5000` |
| `RADIOPAD_SECURITY_WEBHOOK_URL` | Anomaly alert receiver | unset (audit-only) |
| `RADIOPAD_SECURITY_WEBHOOK_SECRET` | HMAC-SHA256 signing key for the alert receiver | unset (no signature) |

## Compliance mapping

| Control | HIPAA Security Rule | GDPR |
| ------- | ------------------- | ---- |
| Audit chain | §164.312(b) Audit Controls | Art. 32 |
| Access control via verified tenant/user identity + RBAC | §164.312(a)(1) | Art. 25, 32 |
| Encryption in transit | §164.312(e)(1) | Art. 32 |
| Right-to-be-forgotten (planned) | n/a | Art. 17 (with audit-log derogation) |
| PHI egress block | §164.502(a) Minimum Necessary | Art. 6, 9 |

## Customer-managed keys (CMK / SEC-003)

Iter-32 promotes PRD **SEC-003** to âœ… by shipping real cloud KMS adapters.
The `IKmsProvider` abstraction (in
`backend/RadioPad.Api/src/RadioPad.Application/Services/Kms/KmsProvider.cs`)
fronts envelope encryption: a per-tenant 32-byte data-encryption-key (DEK)
is wrapped under the tenant's master key (KEK) held by the customer's KMS,
and unwrapped on demand into a 5-minute in-memory cache
(`TenantDekCache`). The cache zeroes evicted entries and never logs key
material.

The active scheme is selected by the `keyRef` prefix:

| Scheme | KeyRef format | Notes |
| ------ | ------------- | ----- |
| `env:` | `env:<VAR_NAME>` | Dev / on-prem. Variable holds a base64-encoded 32-byte AES-256 key. |
| `local:` | `local:/abs/path/to/key.bin` | File-backed appliance KMS. Same wrap shape as `env:`. |
| `aws:` | `aws:arn:aws:kms:<region>:<account>:key/<id>` | Real `AWSSDK.KeyManagementService`. Region parsed from the ARN; credentials via the SDK default chain (IAM role / env vars / shared profile). |
| `azkv:` | `azkv:https://<vault>.vault.azure.net/keys/<name>/<version>` | Real `Azure.Security.KeyVault.Keys` + `DefaultAzureCredential`. Wraps with `RsaOaep256` (or AES-GCM when the key is symmetric). |
| `gcp:` | `gcp:projects/<p>/locations/<l>/keyRings/<r>/cryptoKeys/<k>` | Real `Google.Cloud.Kms.V1`. Application Default Credentials. |

### Tenant binding

Cloud adapters bind the tenant id into the request so a wrapped DEK from
tenant A cannot be unwrapped while masquerading as tenant B:

- AWS: `EncryptionContext = { "tenantId": "<guid>" }` on Encrypt + Decrypt.
- GCP: `additional_authenticated_data = utf8(tenantId)` on Encrypt + Decrypt.
- Azure: per-tenant key URI; AAD-bound AES-GCM is a P1 follow-up for
  symmetric keys.

`env:` and `local:` ignore the tenant id - tenant isolation there is
provided by the per-tenant master-key file/variable.

### IAM permissions required

| Provider | Required permissions |
| -------- | -------------------- |
| AWS KMS | `kms:Encrypt`, `kms:Decrypt`, `kms:DescribeKey` on the CMK ARN. |
| Azure Key Vault | `WrapKey`, `UnwrapKey`, `Get` (the **Crypto User** role; **Crypto Officer** also works). |
| Google Cloud KMS | `roles/cloudkms.cryptoKeyEncrypterDecrypter` on the cryptoKey resource. |

The `keyRef` is **opaque to the UI** - it is only echoed back through
`GET /api/tenant/settings` (active configured value), never by any other
endpoint. Wrapped key material is never returned in any response.

### Health check

`POST /api/tenant/settings/kms/verify` performs a real wrap + unwrap of a
random 32-byte probe and only stamps `TenantSettings.CmkLastVerifiedAt`
when the round-trip returns the original bytes via constant-time compare.
Failures return `422` with one of:

- `kind = "kms_unavailable"` (provider error, missing IAM, malformed `keyRef`)
- `kind = "kms_roundtrip_mismatch"` (the unwrap returned a different value)

The endpoint is restricted to `ItAdmin`, `ComplianceReviewer`, and
`MedicalDirector` roles.

## OAuth refresh-token vault (PROV-007)

Iter-35 PROV-007 introduces a per-provider OAuth refresh-token vault that
reuses the SEC-003 KMS abstraction to envelope-encrypt the token bytes:

1. `OAuthRefreshVault.SaveAsync` generates a fresh 32-byte AES-256-GCM
   data-encryption key (DEK), encrypts the plaintext refresh token under
   the DEK, then asks `IKmsResolver` to wrap the DEK under the tenant's
   KEK (`TenantSettings.CmkKeyRef`, falling back to the
   `RADIOPAD_TENANT_KEK_DEFAULT` environment variable). Only the
   ciphertext, IV, GCM tag, and wrapped DEK are persisted on
   `ProviderConfig`. The plaintext DEK is zeroed immediately after use.
2. `OAuthRefreshVault.LoadAsync` is the inverse path; it is the only code
   path that decrypts the token. It is called only from the rotation
   worker - never from any HTTP endpoint.
3. `OAuthRefreshVault.Delete` clears all four crypto columns (zeroing the
   buffers first) but preserves the rotation policy so a re-saved token
   keeps the same cadence.
4. The HTTP surface (`POST/DELETE/GET /api/providers/{id}/oauth/refresh-token[/status]`)
   is gated on `ItAdmin` / `BillingAdmin`. The status endpoint returns
   only `{ hasToken, updatedAt, expiresAt, rotationPolicy }` - never any
   ciphertext, IV, tag, or wrapped DEK. The save endpoint accepts
   plaintext over TLS, encrypts in-process, and writes an append-only
   `OAuthRefreshRotated` audit row whose `DetailsJson` records only the
   action kind (`saved` / `rotated` / `deleted`), the provider id, and
   the (already-public) timestamps. **The token bytes are never logged.**
5. `OAuthRefreshRotationService` runs as a hosted background worker on a
   15-minute cadence. It scans every `ProviderConfig` whose policy +
   expiry indicate rotation is due (`before_expiry` â‡’ within 1 h of
   `OAuthRefreshTokenExpiresAt`; `every_24h` â‡’ updated more than 24 h
   ago;
ever` â‡’ skipped). The worker delegates the upstream exchange
   to the registered `IOAuthTokenIssuer`. The default registration is
   `NoopOAuthTokenIssuer` (`CanRefresh = false`), so the worker is a
   no-op until a real adapter ships. Successful rotations audit
   `OAuthRefreshRotated` with `kind:"rotated"`; failures audit
   `ProviderBlocked` with the reason class.
6. KMS unavailability fails closed: `OAuthRefreshVault.ResolveKekRef`
   throws `KmsUnavailableException` when neither the tenant `CmkKeyRef`
   nor `RADIOPAD_TENANT_KEK_DEFAULT` is configured, and the save
   endpoint surfaces this as `503 { kind:"kms_unavailable" }`.

## Forbidden practices

- Logging raw prompt/response text outside the audit table.
- Returning provider API keys in any HTTP response.
- Bypassing `AiGateway.SendAsync` to call providers directly.
- Updating or deleting rows in `AuditEvents`.
- Storing PHI, bearer tokens, OIDC tokens, refresh tokens, or web session
  secrets in browser `localStorage`.
- Using `/api/auth/signin` in hosted/production-like deployments.

## Vendor subprocessors

Third-party processors that can receive *any* tenant data (PHI, billing
metadata, telemetry) are listed below. AI providers are configured per
tenant through `ProviderConfig` and are governed by the compliance-class
table above.

| Vendor | Surface | Data sent | PHI? | Compliance evidence |
| --- | --- | --- | --- | --- |
| Stripe | Checkout, Customer Portal, invoices, refunds, webhook deliveries (`POST /api/billing/webhook`) | Tenant id (as `client_reference_id`), billing email, plan tier metadata, invoice / subscription / customer ids. **Never report bodies, never patient identifiers.** | No | PCI-DSS Level 1 (vendor-attested), Stripe DPA. Operator runbook: [docs/06-operations/billing-stripe.md](../06-operations/billing-stripe.md). |

Stripe webhook signatures are verified with
`RADIOPAD_STRIPE_WEBHOOK_SECRET` before any DB write; a missing secret
returns `503` and a bad signature returns `400 RFC-7807`. All webhook
deliveries are deduped through the `StripeWebhookEvents` table inside the
same transaction as the side effects, and audit rows are written through
`IBillingAudit`, which hashes customer / subscription / payment-intent ids
to `sha16:<hex>` before they reach the audit chain.

## MCP signing

Iter-32 MCP-007 - every Model Context Protocol tool registered with the
backend carries a manifest JSON, its SHA-256 hash, and an optional Ed25519
detached signature. Built-in connectors that ship with RadioPad
(`mcp-connectors/*.json`) are signed by the **release key**.

### Key material

| File | Purpose |
| --- | --- |
| [`mcp-connectors/_signing/release.pub`](../../mcp-connectors/_signing/release.pub) | Base64 32-byte Ed25519 public key. Read by the backend at startup (env override: `RADIOPAD_MCP_RELEASE_PUBKEY_B64`). |
| `mcp-connectors/_signing/release.sec` | **Dev placeholder seed.** Never use in production. Production keeps the seed in an HSM and the public key alone in repo. |
| `mcp-connectors/<name>.json.sig` | Detached Ed25519 signature (b64) over the canonical bytes of `<name>.json`. Re-generated whenever the manifest changes. |

### Verification

`McpManifestVerifier.Verify(bytes, sigB64, publicKey32)` returns
`(Valid, Sha256, Error?)`. The SHA-256 is **always** computed and persisted
on `McpTool.ManifestSha256`, so a registry diff is possible even when a
signature is rejected. Failure paths:

- `missing_signature` - submitted without a `manifestSig` field
  (acceptable for tenant-authored tools; refused for built-in tools).
- `bad_signature_b64` / `bad_signature_length` - malformed b64 or non-64
  bytes.
- `bad_signature` - signature does not verify against the configured public
  key. The registry flips the row to `Status = Blocked` and audits
  `McpToolBlocked` with `reason = "bad_signature"`.

### Key rotation

1. Generate a new keypair on the HSM:
   ```powershell
   dotnet run --project scripts/McpSignTool -- gen-key mcp-connectors/_signing
   ```
2. Re-sign every shipped manifest:
   ```powershell
   foreach ($m in 'dicomweb-qido','fhir-servicerequest','pacs-recent-studies') {
       dotnet run --project scripts/McpSignTool -- sign \
         mcp-connectors/_signing/release.pub \
         mcp-connectors/_signing/release.sec \
         "mcp-connectors/$m.json"
   }
   ```
3. Commit the new `release.pub` + `*.sig` files. **Do not commit the new
   private seed in production** - keep it in the HSM only and replace
   `release.sec` in the dev placeholder directory with a no-op file.
4. Cut a release. The backend reads the public key from
   `RADIOPAD_MCP_RELEASE_PUBKEY_B64` (preferred) or the on-disk
   `release.pub`, so deployments roll forward by setting the env var.
5. Tools whose `manifestSig` no longer verifies against the new public key
   are auto-blocked at next registry refresh; admins must register the new
   manifest + signature.

### Default-deny scope policy

The verifier is independent of the runtime scope policy. Even a manifest
with a perfectly valid signature is **still** refused at invocation time if
its scope contains `shell:`, `fs:`, or
et:` and either the operator env
flag (`RADIOPAD_MCP_ALLOW_DANGEROUS=1`) or the per-tenant
`TenantSettings.AllowDangerousMcp` flag is unset. Both must be true before
any dangerous-scope tool can run; the gate is enforced by
`McpScopePolicy` in `RadioPad.Application.Services.Mcp`.
