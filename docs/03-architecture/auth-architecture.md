# Auth Architecture

**Status:** Current  ·  **Owner:** Engineering + Security  ·  **Last Updated:** 2026-05-04

## v0.1 — header-based dev tenant

Authentication in v0.1 is intentionally simple to keep clinical-safety primitives front-and-centre:

- `X-RadioPad-Tenant: <slug>` identifies the tenant.
- `X-RadioPad-User: <email>` identifies the user.
- The backend resolves both lazily (creating dev rows if missing) via `TenantedController.ResolveContextAsync`.
- Suitable for: local development, integration tests, on-prem reading rooms with a trusted reverse proxy enforcing identity upstream.
- **Not** suitable for hosted multi-tenant deployments without an upstream identity gateway.

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

- v0.1: none. Phase 3: short-lived access token (15 min) + refresh token; both rotated on each use.

## CLI auth

- `radiopad login` (Phase 3) opens a browser to the IdP, performs PKCE, stores the refresh token in the OS keychain.
- v0.1: `radiopad login` writes `X-RadioPad-Tenant` and `X-RadioPad-User` into a local config file.
