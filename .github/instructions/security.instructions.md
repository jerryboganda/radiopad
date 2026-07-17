---
applyTo: "**"
---

# Security instructions

- Never commit secrets. Provider API keys live behind `ApiKeySecretRef = "env:<NAME>"`; the only acceptable storage in code is the env-var name.
- Never weaken the PHI policy. `AiGateway.EnforcePhiPolicy` blocks `containsPhi: true` requests unless the provider's `ProviderComplianceClass` is `PhiApproved` or `LocalOnly`. The block must audit `AuditAction.ProviderBlocked` before rethrowing.
- Audit log is append-only and integrity-chained with SHA-256. Never patch a row, never bypass `IAuditLog.AppendAsync`.
- Tenant isolation: every query touching tenant-scoped data must include `r.TenantId == tenant.Id` (or equivalent). Add an integration test for any new tenant-scoped endpoint.
- Backend binds `127.0.0.1` by default. Remote exposure requires the operator to set `RADIOPAD_BIND` *and* a TLS reverse proxy.
- Logs and JSON responses must never contain PHI, secrets, or full report bodies. Use the `X-Request-Id` correlation header for support, not patient identifiers.
- Authentication is real and multi-modal: WebAuthn / passkeys (with server-side user verification), SAML and OIDC SSO, SCIM user provisioning, and bearer tokens with lockout. Header-based tenant/user resolution exists for **local dev only**, gated by `RADIOPAD_DEV_HEADERS` (and `RADIOPAD_REQUIRE_AUTH`). Changes to the auth stack require an ADR and human review of `auth-architecture.md` and the WebAuthn/Saml/Scim/Auth controllers.
- Dependencies: pin minor versions; apply security patches promptly to the next minor (`0.x.y`) regardless of feature cadence. (Automated SCA / dependency scanning is **not** currently wired into CI — do not represent it as running; add it deliberately if adopted.)
- Disclosure: contributors must follow [SECURITY.md](../../SECURITY.md). Never discuss unpatched vulnerabilities in public issues.
