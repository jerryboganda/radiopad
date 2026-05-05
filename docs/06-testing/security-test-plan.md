# Security Test Plan

**Status:** Draft  ·  **Owner:** Security + Engineering  ·  **Last Updated:** 2026-05-04

## Layers

| Layer | Activity | Cadence |
| --- | --- | --- |
| Unit | Negative tests for authn, authz, PHI policy. | Per PR |
| Integration | Cross-tenant isolation, audit chain integrity, provider-blocked path. | Per PR |
| Static analysis | Roslyn analyzers + ESLint with security rules. | Per PR |
| Dependency scan | `dotnet list package --vulnerable`, `pnpm audit`. | Per PR + weekly |
| Secret scan | GitHub built-in + `gitleaks` (planned). | Per PR |
| Container scan | Trivy on API image (planned). | Per PR + weekly |
| DAST | OWASP ZAP against staging (planned). | Weekly |
| Pen test | External vendor. | Annually + before MAJOR |

## Specific security tests

- **Authn:** missing `X-RadioPad-Tenant` → 400; bogus tenant → 404.
- **Tenant isolation:** GET / PATCH a known foreign id from tenant A → 404 with `kind: not_found`.
- **PHI policy:** mark request `containsPhi: true` against a Sandbox provider → 403 `kind: provider_policy` + `ProviderBlocked` audit row written.
- **Audit chain:** corrupt one row's `IntegrityChain` in a test DB → `radiopad audit verify` exits non-zero pointing at the row.
- **Replay:** PATCH the same report twice → version sequence increments correctly; no audit row collision.
- **Rate limit:** AI endpoint group `[EnableRateLimiting("ai")]` returns 429 after configured burst.
- **Secret leak:** assert provider keys never appear in JSON responses or logs (snapshot test).

## Failure-mode acceptance

- Any 5xx from a security test is a release blocker.
- Any cross-tenant 200 is a SEV-1 incident.

## Output artefacts

- CI uploads SCA + container scan reports.
- Pen-test reports stored in the security tracker (private).
