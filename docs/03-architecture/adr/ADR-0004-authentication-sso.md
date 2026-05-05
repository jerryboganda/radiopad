# ADR-0004: Authentication & SSO pipeline

- **Status:** Accepted — landed in iter-32 (OIDC presets, SAML 2.0 ACS, WebAuthn, lockout, session revocation)
- **Last Updated:** 2026-05-04
- **Decision-makers:** Engineering + Compliance + Security
- **Related PRD:** AUTH-001 (SSO/OIDC/SAML), AUTH-004 (MFA), AUTH-006 (emergency lockout / session revocation), AUTH-007 (device trust)
- **Related code:** `backend/RadioPad.Api/src/RadioPad.Api/Controllers/TenantedController.cs::ResolveContextAsync`

## Context

RadioPad currently runs on a development authentication shim: every request carries `X-RadioPad-Tenant` and `X-RadioPad-User` headers, and the controller resolves them to the `dev` tenant + first registered user. This is acceptable for local development and integration tests but cannot ship to clinical production:

- AUTH-001 requires email/password, magic-link, SSO/OIDC, and SAML for enterprise tenants.
- AUTH-004 requires per-tenant MFA enforcement.
- AUTH-006 requires emergency account lockout + session revocation.
- AUTH-007 requires device-trust policies for the desktop app + local daemon.

A real auth pipeline touches every controller (via the auth filter), every test fixture (via the `RadioPadAppFactory`), the Tauri shell (token storage + refresh), the Capacitor shell (deep-link OAuth), and the CLI (`radiopad login`). It also requires legal/compliance review (token TTL policy, session revocation SLA, BAA/DPA implications for the IdP).

## Decision (high-level direction; details to be ratified in a follow-up ADR)

We will adopt **OpenIddict** as the OIDC server framework (chosen over Microsoft.Identity.Web because we need to host the IdP for sandbox/edge deployments where Entra is not available, and because OpenIddict has first-class SAML interop). MFA will be delegated to the IdP (TOTP / WebAuthn). Emergency lockout will use a `RevokedSession` table + a low-TTL access token pattern (5 minutes), refresh-token rotation, and a "lock all sessions for tenant" admin action that bumps a tenant-level token version.

Concretely:

1. New entities: `Session`, `RevokedSession`, `RefreshToken` (rotated). All tenant-scoped.
2. New filter: replaces the dev header shim; resolves `(tenantId, userId, role, sessionId)` from a JWT issued by OpenIddict.
3. New endpoints under `/auth`:
   - `POST /auth/login` (email/password fallback when SSO disabled)
   - `GET /auth/oidc/{tenant}/start` and `/auth/oidc/{tenant}/callback`
   - `POST /auth/refresh`
   - `POST /auth/logout`
   - `POST /auth/admin/revoke-tenant-sessions` (PRD AUTH-006)
4. `TenantedController.ResolveContextAsync` continues to expose `(Tenant, User)` so existing controllers do not change.
5. CLI `radiopad login` uses the OAuth device-code flow.

## Why this is deferred

- Touches every controller's request pipeline → must be merged with care, ideally in a single iteration with no other surface changes.
- Requires a security review and BAA/DPA review with the customer's IdP (Okta / Entra / Auth0 / etc.).
- Requires desktop / mobile auth UX work (Tauri secure storage, Capacitor deep links).
- Out of scope for the current Ralph loop. Tracked here so iteration 13+ does not silently lose the requirement.

## Open questions for the implementing iteration

1. Which IdP do we ship pre-configured for the in-house demo? (Recommendation: OpenIddict + a local password store under `radiopad-auth-demo`.)
2. JWT vs reference tokens? (Recommendation: short-lived JWT + rotated refresh, so revocation works via session id check.)
3. SAML support: ship in v0.2 or defer to v0.3? (Recommendation: defer; SAML adds a large surface and OIDC covers ~95% of enterprise IdPs.)
4. Tauri token storage: keychain (macOS), credential manager (Windows), libsecret (Linux). Use `tauri-plugin-stronghold`.
5. Capacitor: use system browser via `@capacitor/browser` for OAuth (avoid in-app webview to satisfy IdP CSP).

## Consequences

- Until this lands, RadioPad SHALL NOT be deployed to production with PHI.
- All current header-based controllers will need to be re-exercised in tests once the JWT filter is wired.
- The integration test fixture must mint a signed JWT instead of injecting headers.

## References

- `AGENTS.md` — auth architecture overview

## Iter-32 addendum (2026-05-04, accepted)

The deferred items above are now in main. The shipped surface is:

- **OIDC presets** for Keycloak, Auth0, and Okta in [OidcProfiles.cs](../../../backend/RadioPad.Api/src/RadioPad.Api/Auth/OidcProfiles.cs). `RADIOPAD_OIDC_PRESET=keycloak|auth0|okta` populates `RADIOPAD_OIDC_TENANT_CLAIM`, `RADIOPAD_OIDC_EMAIL_CLAIM`, and `RADIOPAD_OIDC_REQUIRE_MFA` without overwriting explicit values.
- **SAML 2.0 SP** in [SamlController.cs](../../../backend/RadioPad.Api/src/RadioPad.Api/Controllers/SamlController.cs): `GET /saml/metadata` (SP descriptor) and `POST /saml/acs`. Signature verification uses `System.Security.Cryptography.Xml.SignedXml` against the IdP cert in `RADIOPAD_SAML_IDP_CERT_PEM` — we deliberately did not pull in `Sustainsys.Saml2` (kept the dependency surface minimal; revisit if multi-IdP federation is needed).
- **WebAuthn / passkeys** in [WebAuthnController.cs](../../../backend/RadioPad.Api/src/RadioPad.Api/Controllers/WebAuthnController.cs) — register-options, register, signin-options, signin. Credentials persist in `WebAuthnCredentials` (tenant-scoped, `(TenantId, CredentialIdHash)` unique). The current implementation uses an in-tree challenge / signature path; full FIDO2 attestation parsing (Fido2NetLib) is a P1 follow-up.
- **Account lockout** in [LockoutPolicy.cs](../../../backend/RadioPad.Api/src/RadioPad.Api/Auth/LockoutPolicy.cs): sliding window of 5 failures / 15 minutes → `LockedUntil = now + 15 min`, `IsActive = false`, audit `UserLockedOut`. Auto-clear on success, audit `UserUnlocked`.
- **Session revocation** via `User.SessionEpoch` folded into the bearer HMAC (`v{epoch}|{tenant}|{email}`). `POST /api/users/{id}/revoke-sessions` (Compliance / IT-Admin) increments the epoch, audits `SessionsRevoked`, and invalidates every outstanding token without an extra DB hit per request.
- **Migration:** `Auth32` adds `Users.FailedLoginCount`, `FailedLoginWindowStart`, `LockedUntil`, `SessionEpoch` and the `WebAuthnCredentials` table.
- **Open questions resolved:**
  1. Demo IdP set: Keycloak (self-host), Auth0, Okta — picked for combined enterprise coverage; Entra is a trivial Auth0/OIDC-generic mapping if a tenant requests it.
  2. JWTs vs reference tokens: kept the iter-22 short HMAC bearer (`rp_…`) and added `SessionEpoch` for revocation. Full JWT/JWKS rotation deferred until federation arrives.
  3. SAML: shipped (v0.3 target pulled forward).
  4. Tauri / Capacitor token storage: unchanged from iter-22.

## References

- `AGENTS.md` — auth architecture overview
- `docs/04-security/auth-architecture.md` — to be authored alongside the implementing iteration
- PRD §10.1 (AUTH-001..007)
