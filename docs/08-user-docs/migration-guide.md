# Migration Guide

**Status:** Current  ·  **Owner:** Engineering  ·  **Last Updated:** 2026-05-04

> Per-version migration notes. When upgrading between MAJOR or MINOR versions, follow the relevant section.

## v0.1.x → v0.2.x

### What changed
- Server-side pagination on `/api/reports` (`skip`, `take`, `X-Total-Count`).
- New endpoint: `GET /api/reports/{id}/versions`.
- New endpoint: `GET /api/health/ready`.
- New CLI: `radiopad provider test`.
- `ReportVersion` now written on `PATCH` of report.

### Required actions
1. Apply DB migrations: `dotnet ef database update`.
2. Backend container image bumped to `radiopad/api:v0.2.x`.
3. Frontend `pnpm build` and redeploy static assets.
4. Update reverse proxy if it cached `Cache-Control` headers; pagination requires fresh data.
5. CLI: `dotnet tool update -g RadioPad.Cli`.

### Backward compatibility
- Old `GET /api/reports` clients receive the first 25 by default; behaviour preserved.
- `X-Total-Count` is additive; old clients ignore it.
- No breaking changes.

## v0.2.x → v0.3.x (planned)

- Streaming AI responses (SSE).
- Webhook dispatch for `report.signed` / `provider.blocked`.
- Per-tenant token budget enforcement.

Required actions will be documented when the version cuts.

## v0.x → v1.0.0 (planned)

- API stability commitment; further breaking changes require major version bumps.
- OIDC mandatory for hosted SKU.
- RBAC enforcement on all endpoints.

The v1.0.0 cutover is gated on:
- One full pen-test with no Critical / High open.
- SOC 2 Type I scope ready.
- Multi-region hosted SKU operational.

## General migration practice

- Backups verified within 24 hours of upgrade.
- Audit chain verified before and after the upgrade.
- Smoke flow run on staging before production rollout.
- Customer comms 5 business days before any breaking change.
