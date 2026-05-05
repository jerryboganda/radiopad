# OWASP ASVS Checklist

**Status:** Draft  ·  **Owner:** Security  ·  **Last Updated:** 2026-05-04

> Self-assessment against [OWASP ASVS v4.0](https://owasp.org/www-project-application-security-verification-standard/) Level 2. Items are scored:
>
> `Status: Implemented / Partial / Missing / Unknown / Not Applicable`

## V2 — Authentication

| Item | Status | Evidence | Recommendation |
| --- | --- | --- | --- |
| 2.1 Verify user identity | Partial | Header-based dev tenant in v0.1. | Move to OIDC in Phase 3. |
| 2.2 Anti-automation | Missing | No CAPTCHA / rate limit per IP. | Add per-IP token bucket Phase 2. |
| 2.5 Credential recovery | Not Applicable | Owned by IdP. | — |

## V3 — Session management

| Item | Status | Evidence | Recommendation |
| --- | --- | --- | --- |
| 3.2 Session token security | Missing | No sessions yet. | OIDC cookie + refresh rotation Phase 3. |
| 3.4 Cookie attributes | Not Applicable | — | Phase 3. |

## V4 — Access control

| Item | Status | Evidence | Recommendation |
| --- | --- | --- | --- |
| 4.1 General access control | Partial | Tenant isolation enforced; RBAC pending. | Phase 3 RBAC matrix. |
| 4.2 Operation-level access control | Partial | `ResolveContextAsync` everywhere. | Add object-level checks for resident drafts. |

## V5 — Validation, sanitization, encoding

| Item | Status | Evidence | Recommendation |
| --- | --- | --- | --- |
| 5.1 Input validation | Implemented | DTO attributes + 422. | — |
| 5.2 HTML encoding | Implemented | React escaping. | — |
| 5.3 Output encoding | Implemented | `System.Text.Json` defaults. | — |

## V6 — Cryptography

| Item | Status | Evidence | Recommendation |
| --- | --- | --- | --- |
| 6.2 Algorithm choice | Implemented | SHA-256 audit chain. | — |
| 6.3 Random values | Implemented | `Guid.NewGuid()` for ids. | — |
| 6.4 Secret management | Implemented | Env-var via `ApiKeySecretRef`. | Document key rotation policy. |

## V7 — Error handling and logging

| Item | Status | Evidence | Recommendation |
| --- | --- | --- | --- |
| 7.1 Error handling | Implemented | `GlobalExceptionMiddleware`. | — |
| 7.2 Logging | Partial | Structured logs but no central aggregation. | Phase 2 OpenTelemetry. |
| 7.3 Audit trail integrity | Implemented | Append-only + SHA-256 chain. | — |

## V8 — Data protection

| Item | Status | Evidence | Recommendation |
| --- | --- | --- | --- |
| 8.1 Data classification | Partial | [data-classification.md](data-classification.md). | Tag DB columns by class. |
| 8.3 Sensitive data in URLs | Implemented | No PHI in URLs. | — |

## V9 — Communications

| Item | Status | Evidence | Recommendation |
| --- | --- | --- | --- |
| 9.1 TLS | Implemented (deployment) | Reverse proxy terminates TLS. | Document HSTS in deploy guide. |

## V13 — API and web service

| Item | Status | Evidence | Recommendation |
| --- | --- | --- | --- |
| 13.1 Generic API | Implemented | RFC-7807 problem details. | — |
| 13.2 RESTful auth | Partial | Header-based v0.1. | Phase 3 OIDC. |

## V14 — Configuration

| Item | Status | Evidence | Recommendation |
| --- | --- | --- | --- |
| 14.1 Build & deploy | Implemented | Docker images; semver tags. | — |
| 14.2 Dependencies | Partial | `pnpm audit`, `dotnet list package`. | Add Dependabot/Renovate Phase 2. |
| 14.5 HTTP headers | Missing | No CSP/HSTS/Referrer-Policy. | Add via reverse proxy + middleware. |
