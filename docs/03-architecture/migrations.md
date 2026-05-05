# Migrations

**Status:** Current  ·  **Owner:** Engineering  ·  **Last Updated:** 2026-05-04

## Tooling

- EF Core CLI: `dotnet ef migrations add <Name> --project src/RadioPad.Infrastructure --startup-project src/RadioPad.Api`.
- Migrations live under `backend/RadioPad.Api/src/RadioPad.Infrastructure/Migrations/`.

## Process

1. Author the entity / column change in `RadioPad.Domain`.
2. Update `RadioPadDbContext` if needed.
3. Run `dotnet ef migrations add <Name>`.
4. Inspect the generated migration — never accept a destructive migration without a paired forward-compatible step.
5. Add tests:
   - Integration test exercises the new column / table.
   - Backfill script unit-tested if applicable.
6. Open a PR with the `human-review-required` label.

## Review requirements

- A reviewer from Engineering and a reviewer from Security/Ops both approve any migration that touches `AuditEvents`, `Tenants`, or columns marked PHI in [../04-security/data-classification.md](../04-security/data-classification.md).
- Migrations that drop columns or tables require an ADR.
- Migrations are never amended after merge — if a fix is needed, ship a follow-up migration.

## Rollback strategy

- Application code is rolled back via standard deploy.
- Database changes are rolled forward, not backward — every migration must include a forward-compatible plan so the previous app version continues to work.
- If a migration must be reversed, a new migration is authored that restores the prior shape; the original migration is annotated in this file.

## Seed data

- `DevSeed` runs in `Program.cs` when `ASPNETCORE_ENVIRONMENT != "Testing"`.
- Seed creates: `dev` tenant, mock provider, the five seed rulebooks/templates.
- Production deployments do not run seed; they rely on operator-driven imports via the CLI.

## Backfill policy

- Backfills run as a one-time CLI command (`radiopad backfill <name>`), not inline migrations.
- Backfills must be idempotent and chunked (≤ 1k rows / batch).
- Backfills emit progress logs and an audit event on completion.
