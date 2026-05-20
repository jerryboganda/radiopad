# Admin Guide (Informatics)

**Status:** Current  Â·  **Owner:** Product  Â·  **Last Updated:** 2026-05-04

For informatics admins who configure tenants, providers, rulebooks, and templates.

## Tenant context

- Every page operates inside a single tenant.
- The active tenant slug is shown in the topbar.
- Switching tenants re-authenticates against your IdP or requires a new passwordless/device-flow sign-in. Local tenant/user tuple sign-in is dev/test-only and is disabled in hosted deployments.


## Roles and access

Current tenant roles are:

- `Radiologist` — clinical report drafting, validation, signing, export, and approved validation-pack runs.
- `ReportingAdmin` — reporting operations, providers, rulebooks/templates, lexicon/prompts, validation-pack runs, and MCP operations where allowed.
- `MedicalDirector` — clinical governance, approvals, signing/addenda, audit/security review, billing governance, and user/session controls.
- `ComplianceReviewer` — audit/security review, SIEM/security checks, and session revocation workflows.
- `ItAdmin` — operational administration for providers, users/devices, security/KMS, validation packs, MCP, Copilot, and billing where allowed.
- `BillingAdmin` — billing and marketplace payment operations plus provider OAuth token-vault operations where allowed.

Custom database-backed roles are deferred. See [authorization-rbac.md](../03-architecture/authorization-rbac.md) for the current permission matrix.

## Providers

- **List:** `Providers` page. Shows name, adapter, compliance class.
- **Add:** click **New provider**, choose an adapter, set the compliance class, and reference the API key by env var name (`env:NAME`). The plain key is **never** stored.
- **Compliance classes:**
  - `Sandbox` - dev/test only.
  - `DeIdentifiedOnly` - must not receive PHI.
  - `PhiApproved` - BAA-backed; PHI permitted.
  - `LocalOnly` - local network (e.g. on-prem Ollama); PHI permitted.

## Rulebooks

- **List:** `Rulebooks` page.
- **Save / edit:** YAML form; the editor calls `POST /api/rulebooks/save` and validates the schema.
- **Approve:** click **Approve** on a draft to mark `status: approved`. This is a **human-review action** and writes a `RulebookApproved` audit event.
- **Deprecate:** when a rulebook is superseded, click **Deprecate**. Writes `RulebookDeprecated`.
- Golden cases live under `rulebooks/_tests/<id>/`. The CLI runs them in CI.

## Templates

- Modality + body part -> sections JSON. Used by radiologists to start a report.
- Edits are versioned by `ReportTemplate` rows.

## Users and sessions

- Invite and lifecycle users through the tenant IdP/SCIM where available; RadioPad does not manage passwords.
- Production login is OIDC Authorization Code + PKCE with magic-link fallback.
- Web sessions use the `rp_session` HttpOnly/SameSite cookie for current bearer-backed flows; desktop/mobile keep session material in OS secure storage.
- Session revocation is available through user session-epoch revocation for Medical Director, Compliance Reviewer, and IT Admin roles. Treat it as a sensitive action requiring step-up MFA in production policy.
- Break-glass access is not available in this batch.

## Audit

- `Audit` page lists tenant-scoped events.
- Filter by action; copy a request id; verify offline with `radiopad audit verify`.

## Operational health

- The dashboard shows the latest `audit verify` status (planned).
- Use the CLI: `radiopad daemon status` for a quick health check.

## Common questions

- **Why was an AI request blocked?** The provider's compliance class doesn't match the PHI flag. Either pick a different provider or de-identify.
- **Why didn't a finding fire?** The rulebook `status` may be `draft`, or the rule predicate didn't match. Check the rulebook YAML and the golden case.
- **A report won't acknowledge.** It must be `Validated` first; resolve any Blocker findings.
