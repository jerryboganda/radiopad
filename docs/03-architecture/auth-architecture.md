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

## Phase 3 — OIDC / SSO

The target authentication model:

- **OIDC providers:** Okta, Azure AD, Google Workspace.
- Flow: PKCE Authorization Code → backend exchange → JWT bearer with tenant + user claims.
- Session cookie: HttpOnly, Secure, SameSite=Lax, 12-hour lifetime, refresh-on-use.
- Frontend uses `next-auth` or equivalent.
- Backend validates the JWT and enriches `ResolveContextAsync` from claims (`tid` → tenant, `sub` → user).

## MFA

- Enforced upstream by the IdP (we do not implement MFA in RadioPad).
- Tenants may require MFA via IdP policy; RadioPad checks an `acr` claim ≥ `mfa` (Phase 3).

## Password policy

- Not applicable post-Phase-3 — passwords are owned by the IdP.

## Account recovery

- Owned by the IdP. RadioPad never stores passwords or secrets material.

## Token model

- Current: 12-hour `rp_` opaque bearer, HMAC-bound to tenant/user/session epoch/issued-at. Issued bearers are also recorded in `AuthSession` by one-way token hash for inventory; validation still enforces the HMAC, expiry, lockout/deprovisioning, and `User.SessionEpoch`. `RADIOPAD_AUTH_SECRET` is required outside Development/Testing.
- Future: session-backed `rp_v3` or JWT/JWKS with refresh-token rotation and row-level revocation enforcement.

## CLI auth

- `radiopad login` should use the device flow or browser IdP flow and store the resulting bearer in the OS keychain.
- Dev/test mode may still use tenant/user headers for local automation.
