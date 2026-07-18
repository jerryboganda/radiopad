---
name: ef-migration-helper
description: Use when adding or changing EF Core entities/migrations in the RadioPad backend. Encodes the exact add-migration incantation (DbContext in Infrastructure, startup project in Api), the naming + snapshot rules, and the two-provider (SQLite dev/tests, Postgres prod) constraints — migrations apply automatically at startup, so a bad one breaks boot, not just a test.
---

# EF Core migrations (RadioPad)

Migrations are **hand-managed** and applied automatically at startup via `db.Database.MigrateAsync()` (`Program.cs`). A malformed or snapshot-desynced migration breaks production boot — treat them carefully.

## Project layout gotcha

The `RadioPadDbContext` lives in **`RadioPad.Infrastructure`**, but the design-time / startup project is **`RadioPad.Api`**. Always pass both:

```bash
dotnet ef migrations add <PascalCaseName> \
  --project        backend/RadioPad.Api/src/RadioPad.Infrastructure \
  --startup-project backend/RadioPad.Api/src/RadioPad.Api
```

## Rules

- **Naming**: match the existing `timestamp_PascalCase` convention already in `Infrastructure/Migrations/` (there are ~45 migrations + `RadioPadDbContextModelSnapshot.cs`).
- **Always commit the regenerated `RadioPadDbContextModelSnapshot.cs`** alongside the new migration — an out-of-date snapshot silently corrupts the next migration.
- **Never edit an already-applied migration.** Add a new one to correct course.
- **Two providers**: prod uses Npgsql (`UseNpgsql`), dev/tests use SQLite (`UseSqlite`). Keep migrations provider-neutral — avoid provider-specific SQL/column types. `DevSeed.cs` runs `MigrateAsync` with an `EnsureCreated` fallback, so the SQLite test path must stay migratable.
- After adding, verify with a **single targeted test** or `dotnet ef migrations list` — do not run the whole suite locally (CI is the gate).

## Removing the last (unapplied) migration

```bash
dotnet ef migrations remove \
  --project        backend/RadioPad.Api/src/RadioPad.Infrastructure \
  --startup-project backend/RadioPad.Api/src/RadioPad.Api
```
Only when it has **not** shipped/been applied anywhere.
