# Secrets Management

**Status:** Current  Â·  **Owner:** Security + Ops  Â·  **Last Updated:** 2026-05-05

## Required secrets

| Secret | Env var | Purpose | Rotation |
| --- | --- | --- | --- |
| Anthropic API key | `ANTHROPIC_API_KEY` | AI gateway routing | 90 days |
| OpenAI API key (optional) | `OPENAI_API_KEY` | AI gateway routing | 90 days |
| VPS Postgres database password | embedded in `ConnectionStrings__RadioPad` / `POSTGRES_PASSWORD` / `RADIOPAD_DB` | EF Core production data store, including auth/session tables | 180 days |
| RadioPad token signing secret | `RADIOPAD_AUTH_SECRET` | Built-in bearer token signing; required outside Development/Testing | 180 days |
| Column encryption key reference | `RADIOPAD_COLUMN_KEY_REF` | KMS/KEK reference or env-backed wrapping key reference | 180 days or per KMS policy |
| Wrapped column data key | `RADIOPAD_COLUMN_KEY_WRAPPED` | Base64 wrapped DEK for column-level encryption; required in Production | 180 days or per KMS policy |
| OIDC authority / audience / client | `RADIOPAD_OIDC_AUTHORITY`, `RADIOPAD_OIDC_AUDIENCE`, `RADIOPAD_OIDC_CLIENT_ID`, `RADIOPAD_OIDC_REDIRECT_URI`, `RADIOPAD_OIDC_SCOPE` | OIDC JWT validation and Authorization Code + PKCE login | Per IdP policy |
| OIDC claim controls | `RADIOPAD_OIDC_TENANT_CLAIM`, `RADIOPAD_OIDC_EMAIL_CLAIM`, `RADIOPAD_OIDC_REQUIRE_MFA`, `RADIOPAD_OIDC_PRESET` | Tenant/user claim mapping and MFA requirement | On IdP integration changes |
| OIDC client secret, when confidential client is used | `RADIOPAD_OIDC_CLIENT_SECRET` | Generic OIDC Authorization Code + PKCE backend exchange | 365 days or IdP policy |
| Magic-link SMTP password | `RADIOPAD_SMTP_PASS` | Passwordless fallback email delivery | 180 days |
| Webhook signing key (Phase 2) | `RADIOPAD_WEBHOOK_SECRET` | Outbound webhooks | 365 days |

## Storage

- **Local dev:** `.env` file, gitignored. Use `.env.example` as the template.
- **CI:** GitHub Actions secrets; never echoed.
- **Hosted:** Cloud-secret-manager (AWS Secrets Manager / GCP Secret Manager / Azure Key Vault) injected as env vars.
- **VPS production:** provider secret store or OS-level secret manager; inject `RADIOPAD_DB`, OIDC, SMTP, Stripe, KMS, and webhook secrets as environment variables readable only by the RadioPad service account. Do not bake secrets into compose files or images.
- **On-prem:** Customer's secret manager or `/etc/radiopad/secrets/*.env` with `chmod 600`.
- **VPS compose:** `/opt/radiopad/.secrets.env` with `chmod 600`; see `deploy/vps/README.md`. Do not commit this file or copy it into the source tree.

## In-code reference

Provider rows store `ApiKeySecretRef = "env:<NAME>"`. The provider API rejects non-`env:` secret refs, and `ProviderSecretResolver` treats literal values or unsupported schemes as unresolved instead of secret material. PACS vendor adapters use the same `env:<NAME>` convention through `PacsSecretResolver`. The plain value never appears in:

- The DB.
- Logs (the resolver hands the value straight to the adapter and discards locals).
- JSON responses.
- Test fixtures.

## Rotation

1. Provision the new secret in the secret manager.
2. Deploy the new env var alongside the old.
3. Update the `ApiKeySecretRef` to the new env-var name (DB write, audited).
4. Redeploy without the old env var.
5. Verify with a `radiopad provider test`.

For auth secrets:

- Rotate `RADIOPAD_AUTH_SECRET` during a maintenance window because all current `rp_` bearers become invalid.
- Rotate OIDC client secrets at the IdP first, deploy the new secret, then validate Authorization Code + PKCE login and magic-link fallback.
- Rotate SMTP credentials by sending a test magic link; production responses must not expose `devLink`.
- Postgres password rotation must include application restart/reload and a readiness check against `/api/health/ready`.

For `RADIOPAD_AUTH_SECRET`, rotate during a maintenance window because existing
RadioPad-issued bearer tokens will be invalidated. For
`RADIOPAD_COLUMN_KEY_REF` / `RADIOPAD_COLUMN_KEY_WRAPPED`, rotate only with a
tested rewrap/backfill plan so existing encrypted columns remain decryptable.
Production must provide these env vars; the deterministic development fallback
is not acceptable for hosted, VPS, or on-prem production.

## Local dev handling

- `.env` is per-developer; never commit.
- `cp .env.example .env` and fill in only the providers you actually test.
- Use the **Mock provider** for the default flow - no secret required.

## CI/CD handling

- Secrets are scoped to specific workflows (e.g. `ConnectionStrings__RadioPad` only on integration jobs).
- Prefer `ConnectionStrings__RadioPad` for app database configuration; legacy
  docs may mention `RADIOPAD_DB`, but the ASP.NET Core app consumes the named
  connection string.
- Secret values are never `echo`'d in logs.
- PR pipelines do not have access to production secrets.

## Secret-exposure findings

| Date | Surface | Outcome |
| --- | --- | --- |
| (none recorded) | | |

If a secret is exposed:

1. Revoke immediately at the issuer.
2. Rotate per the runbook above.
3. Open a SEV-1 incident and follow [incident-response.md](incident-response.md).
4. Add a regression: the test corpus / pre-commit hook that would have caught it.
