# Auth Architecture

**Status:** Current  ·  **Owner:** Engineering + Security  ·  **Last Updated:** 2026-05-17

## Current — verified tenant context

RadioPad now resolves tenant context from a server-verified request identity. Production-like traffic must use one of:

- `Authorization: Bearer rp_<opaque>` minted by a proof-based flow. Current `rp_` bearers are HMAC-bound to tenant slug, user email, session epoch, and issued-at time; they expire after 12 hours and are invalidated by session-epoch revocation, lockout, or deprovisioning.
- Valid OIDC JWTs accepted by `OidcBearerMiddleware` and promoted into the same verified context.

`X-RadioPad-Tenant` and `X-RadioPad-User` are no longer authoritative in production-like mode. They are accepted only when explicit dev/test headers are enabled and are otherwise lookup hints for current `rp_` bearer validation. SCIM and ingest remain separate tenant-bearer integration surfaces.

The `/api/auth/signin` tuple exchange is dev/test-only. Hosted deployments must use OIDC, SAML, WebAuthn, magic-link delivery, or another proof-based flow rather than minting a bearer from public identifiers.

## Enterprise identity foundation

RadioPad has an additive backend identity foundation for enterprise migration:

- `GlobalUser` stores cross-tenant account metadata only. It is not an authorization principal.
- `ExternalIdentity` links stable provider subjects (`provider + issuer + subject`) to a global account. Email is a snapshot, not the trust key.
- `TenantMembership` bridges a global account into the current tenant-scoped `User` row. Existing `User.Role`, `User.IsActive`, `LockedUntil`, and `SessionEpoch` remain authoritative for request authorization.
- `AuthSession` records hashed issued bearer sessions for inventory and future revocation workflows. Raw bearer tokens are never stored.

This slice does not add public endpoints or change `/api/tenant/me`; existing controllers still resolve `(Tenant, User)` through verified request identity.

## Production login decision — OIDC Authorization Code + PKCE

The production login model is generic OIDC Authorization Code + PKCE. It must not be coupled to one IdP brand; tenant configuration supplies issuer, client-id/client-secret (when needed), redirect URIs, and claim mappings. Okta, Azure AD / Entra ID, Google Workspace, Auth0, and Keycloak are expected configuration profiles, not separate RadioPad protocols.

Target web flow:

1. Browser starts OIDC Authorization Code + PKCE.
2. Backend validates issuer metadata, state, nonce, code verifier, token response, and tenant/user claims.
3. Backend maps the stable provider subject through `ExternalIdentity` and `TenantMembership` to an active tenant-scoped `User`.
4. Web clients receive a server-managed session cookie: `HttpOnly; Secure; SameSite=Lax` (or stricter where compatible), 12-hour maximum lifetime, refresh-on-use only when policy allows.
5. Desktop/mobile continue to store native access material in OS secure storage or the platform keychain; they must not use browser `localStorage` for bearer/session secrets.

Current implementation note: the backend accepts validated OIDC JWTs through `OidcBearerMiddleware` and also exposes the generic OIDC Authorization Code + PKCE web entry points `GET /api/auth/oidc/authorize` and `GET /api/auth/oidc/callback`. Successful OIDC, SAML, magic-link, WebAuthn, device, and dev/test sign-ins still mint `rp_` opaque bearers, record hashed `AuthSession` inventory rows, and set/clear the `rp_session` HttpOnly/SameSite cookie via `/api/auth/session`, `/api/auth/sessions`, `/api/auth/sessions/{sessionId}/revoke`, and `/api/auth/logout`.

## Magic-link fallback

Magic-link sign-in remains the production fallback when an IdP is unavailable or a tenant chooses passwordless bootstrap. The current backend implements:

- `POST /api/auth/magic-link/request` — request a single-use link; response does not reveal whether the tenant/user exists.
- `POST /api/auth/magic-link/consume` — consume the token, mint a 12-hour `rp_` bearer, and append the `rp_session` cookie.

For web production, the fallback uses the same bearer-backed cookie session shape as other current flows. The full generic OIDC Code + PKCE callback names still need to be synchronized after backend implementation.

## MFA and step-up

- Baseline MFA is enforced upstream by the IdP where OIDC is used.
- RadioPad must require step-up MFA freshness for sensitive actions (for example session revocation, user lock/unlock, provider secret/token changes, KMS verification, billing changes, rulebook/template approvals, report signing/addenda, and audit export/verification).
- Current backend has TOTP enrollment/verification and OIDC MFA claim toggles, but endpoint-level step-up freshness enforcement is not complete. Treat this as a production gap until each sensitive route checks recent MFA/`acr`/`amr` evidence.
- Break-glass access is deferred one batch; do not document an operational break-glass account as available yet.

## Password policy

- Not applicable post-Phase-3 — passwords are owned by the IdP.

## Account recovery

- Owned by the IdP. RadioPad never stores passwords or secrets material.

## Token and session model

- Current API bearer: 12-hour `rp_` opaque bearer, HMAC-bound to tenant/user/session epoch/issued-at. Issued bearers are recorded in `AuthSession` by one-way token hash for inventory; validation enforces HMAC, expiry, lockout/deprovisioning, and `User.SessionEpoch`. `RADIOPAD_AUTH_SECRET` is required outside Development/Testing.
- Current OIDC bearer: validated JWTs can be promoted into the same verified context when OIDC env configuration is enabled.
- Current web session surface: bearer-backed `rp_session` cookie (`HttpOnly`, `SameSite=Lax`, `Secure` outside development/testing), current-session inspection, session inventory, per-session revoke, and logout endpoints.
- Remaining production web target: generic OIDC Code + PKCE callback/code exchange feeding the same cookie session model.
- Native target: desktop/mobile keep using secure OS storage for bearer/session material.
- Deferred: DB-backed custom roles, break-glass access, and row-level revocation beyond `User.SessionEpoch`.

## CLI auth

- `radiopad login` should use the device flow or browser IdP flow and store the resulting bearer in the OS keychain.
- Dev/test mode may still use tenant/user headers for local automation.

## Data-store migration

Production deployments for this batch target VPS-hosted PostgreSQL rather than SQLite/local-only storage. Auth-sensitive tables (`GlobalUser`, `ExternalIdentity`, `TenantMembership`, `AuthSession`, MFA, WebAuthn, magic-link and device-flow rows) must participate in the same PostgreSQL backup, encryption-at-rest, migration, and restore runbooks as PHI-bearing tenant data.
