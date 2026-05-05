# Agent Permissions

**Status:** Current  ·  **Owner:** Engineering  ·  **Last Updated:** 2026-05-04

## Allowed actions

- Read any file in the workspace except where explicitly restricted.
- Edit files under `frontend/`, `backend/`, `desktop/`, `mobile/`, `cli/`, `rulebooks/`, `templates/`, `docs/`, `.github/`, `.cursor/`.
- Run local commands: `dotnet build`, `dotnet test`, `pnpm typecheck`, `pnpm build`, `pnpm dev`, `dotnet run --project cli/RadioPad.Cli -- ...`, `radiopad audit verify`.
- Open issues / PRs (when authorised by the human operator).
- Create new ADRs under `docs/03-architecture/adr/`.
- Add new rulebooks (status `draft`) and golden cases.

## Restricted actions (require explicit human confirmation)

- Editing `backend/RadioPad.Api/src/RadioPad.Application/Services/AiGateway.cs`.
- Editing `backend/RadioPad.Api/src/RadioPad.Validation/Engine/ReportValidator.cs`.
- Editing `backend/RadioPad.Api/src/RadioPad.Application/Services/FhirDiagnosticReportSerializer.cs`.
- Editing `backend/RadioPad.Api/src/RadioPad.Infrastructure/Persistence/RadioPadDbContext.cs` or any EF migration.
- Flipping a rulebook's `status` to `approved`.
- Editing `frontend/app/globals.css` or `docs/02-design/design.md` (must be paired).
- Pushing to `main`; tagging releases.
- Modifying anything under `src/`, `daemon/`, or `*.legacy.*` (read-only history).

## Forbidden actions

- Auto-signing reports.
- Bypassing or weakening `EnforcePhiPolicy`.
- UPDATE/DELETE on `AuditEvents`.
- Disabling tests, lint, or typecheck checks.
- Force-push to any branch.
- Committing secrets, PHI, or real patient data.
- `git reset --hard` on a shared branch.
- Running migrations against production from a developer machine.

## Safe-command policy

- Prefer reversible, local-only commands.
- For destructive commands (`rm -rf`, `git push --force`, DB drops), stop and ask the human operator.
- For network commands that hit external providers, confirm the provider is the Mock provider unless the human has explicitly enabled real-provider testing.
