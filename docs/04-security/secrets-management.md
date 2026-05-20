# Secrets Management

**Status:** Current  ·  **Owner:** Security + Ops  ·  **Last Updated:** 2026-05-19

## Required secrets

| Secret | Env var | Purpose | Rotation |
| --- | --- | --- | --- |
| Anthropic API key | `ANTHROPIC_API_KEY` | AI gateway routing | 90 days |
| OpenAI API key (optional) | `OPENAI_API_KEY` | AI gateway routing | 90 days |
| Database password | embedded in `ConnectionStrings__RadioPad` (legacy `RADIOPAD_DB` accepted) | EF Core | 180 days |
| RadioPad bearer signing secret | `RADIOPAD_AUTH_SECRET` | Auth | 180 days |
| OIDC client secret (Phase 3) | `RADIOPAD_OIDC_CLIENT_SECRET` | Auth | 365 days |
| Webhook signing key (Phase 2) | `RADIOPAD_WEBHOOK_SECRET` | Outbound webhooks | 365 days |

## Storage

- **Local dev:** `.env` file, gitignored. Use `.env.example` as the template.
- **CI:** GitHub Actions secrets; never echoed.
- **Hosted:** Cloud-secret-manager (AWS Secrets Manager / GCP Secret Manager / Azure Key Vault) injected as env vars.
- **On-prem:** Customer's secret manager or `/etc/radiopad/secrets/*.env` with `chmod 600`.

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

## Local dev handling

- `.env` is per-developer; never commit.
- `cp .env.example .env` and fill in only the providers you actually test.
- Use the **Mock provider** for the default flow — no secret required.

## CI/CD handling

- Secrets are scoped to specific workflows (e.g. `ConnectionStrings__RadioPad` / `RADIOPAD_DB` only on integration jobs).
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
