# Troubleshooting

**Status:** Current  ·  **Owner:** Support  ·  **Last Updated:** 2026-05-04

> If support contacts you about an error, ask for the **request id** from the banner — every error response includes one.

## Frontend

### "Failed to fetch" / connection refused
- Verify the backend is running on `http://127.0.0.1:7457` (`/api/health` returns 200).
- Verify `next.config.ts` rewrite is intact (`/api/*` → backend).
- Browser cache: hard reload.

### "Tenant not found"
- Header `X-RadioPad-Tenant` not set or wrong slug.
- v0.1 dev: ensure `dev` tenant seed ran (`DevSeed`).

### Page renders without styles
- `globals.css` import missing in `frontend/app/layout.tsx`.
- Browser blocking the stylesheet (CSP, ad blocker).

## Backend

### `dotnet ef database update` fails
- Connection string wrong; check `RADIOPAD_DB`.
- DB not reachable from the API container.

### 5xx with `kind: "internal"`
- Check logs for the request id; full stack is logged once at error level.
- Look for an unhandled `null` or unexpected DB state.

### 403 with `kind: "provider_policy"`
- Expected when PHI is sent to a non-compliant provider.
- Audit log will contain a `ProviderBlocked` event with the same request id.

### 429
- AI rate limit (60/min/tenant). Wait or reduce request volume.

### Audit verify fails
- Identify the offending event id from the CLI output.
- Stop writes to the affected tenant.
- Follow the [audit chain mismatch runbook](../07-devops/runbook.md).

## CLI

### `radiopad rulebook test` fails
- Compare the failing case file to the rulebook semantics.
- Run `radiopad rulebook validate <yaml>` first to catch schema errors.

### `radiopad provider test` returns 502
- Provider unreachable; check the API key env var; check egress allowlist.

## Desktop

### Global shortcut doesn't focus
- Another app may own the shortcut; check OS keybindings.
- Tauri capability `globalShortcut` must be granted in `default.json`.

### Clipboard cleared "too fast"
- That's `secure_copy` doing its job (TTL wipe). Paste promptly.
- TTL is intentional; not configurable.

## Mobile

### Login keeps prompting
- Confirm the IdP (Phase 3) returns the tenant claim.
- v0.1: ensure the dev config has both tenant slug and user email.
