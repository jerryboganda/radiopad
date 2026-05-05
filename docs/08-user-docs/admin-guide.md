# Admin Guide (Informatics)

**Status:** Current  ·  **Owner:** Product  ·  **Last Updated:** 2026-05-04

For informatics admins who configure tenants, providers, rulebooks, and templates.

## Tenant context

- Every page operates inside a single tenant.
- The active tenant slug is shown in the topbar.
- Switching tenants re-authenticates against your IdP (Phase 3) or updates your local config (v0.1).

## Providers

- **List:** `Providers` page. Shows name, adapter, compliance class.
- **Add:** click **New provider**, choose an adapter, set the compliance class, and reference the API key by env var name (`env:NAME`). The plain key is **never** stored.
- **Compliance classes:**
  - `Sandbox` — dev/test only.
  - `DeIdentifiedOnly` — must not receive PHI.
  - `PhiApproved` — BAA-backed; PHI permitted.
  - `LocalOnly` — local network (e.g. on-prem Ollama); PHI permitted.

## Rulebooks

- **List:** `Rulebooks` page.
- **Save / edit:** YAML form; the editor calls `POST /api/rulebooks/save` and validates the schema.
- **Approve:** click **Approve** on a draft to mark `status: approved`. This is a **human-review action** and writes a `RulebookApproved` audit event.
- **Deprecate:** when a rulebook is superseded, click **Deprecate**. Writes `RulebookDeprecated`.
- Golden cases live under `rulebooks/_tests/<id>/`. The CLI runs them in CI.

## Templates

- Modality + body part → sections JSON. Used by radiologists to start a report.
- Edits are versioned by `ReportTemplate` rows.

## Users (Phase 3)

- Roles: Owner / Admin / Radiologist / Resident / Auditor. See [authorization-rbac.md](../03-architecture/authorization-rbac.md).
- Invite via the IdP (we don't manage passwords).

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
