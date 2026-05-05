# CI / CD

**Status:** Current (CI only)  ·  **Owner:** Ops + Engineering  ·  **Last Updated:** 2026-05-04

## CI

GitHub Actions workflow at `.github/workflows/ci.yml`.

### Jobs

1. **Backend** — `dotnet restore`, `dotnet build`, `dotnet test`.
2. **Frontend** — `pnpm install --frozen-lockfile`, `pnpm typecheck`, `pnpm build`.
3. **Rulebook validate** — `dotnet run --project cli/RadioPad.Cli -- rulebook validate <each>`.
4. **Rulebook golden** — `dotnet run --project cli/RadioPad.Cli -- rulebook test <each>`.
5. **SCA** — `dotnet list package --vulnerable --include-transitive` and `pnpm audit --prod`.

### Triggers

- Pull request opened / pushed: all jobs.
- Push to `main`: all jobs + tag-on-pass for the next planned release line.

### Required checks (branch protection)

- All jobs above must pass.
- At least one approving review.
- `human-review-required` label cleared (if applied).

## CD

CD is **manual** in v0.x:

1. Bump version in `Directory.Build.props` and `package.json`.
2. Update `CHANGELOG.md` with the new version.
3. Tag `vX.Y.Z` on `main`.
4. Build container images (planned: GitHub Actions release workflow).
5. Push images to the configured registry.
6. Apply DB migrations (`dotnet ef database update`).
7. Roll out via `docker compose up -d` (v0.x) or Helm (Phase 2).

## Build artefacts

- `radiopad-api:<tag>` container image.
- `frontend/out/` static export (consumed by Tauri / Capacitor).
- `radiopad-cli` NuGet package (planned tool publish).

## Smoke after deploy

- `curl /api/health/ready` → 200.
- Login as integration tenant; create a draft; validate; ask Mock; export.
- `radiopad audit verify --tenant it` → exit 0.
