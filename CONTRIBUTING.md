# Contributing to RadioPad

Thanks for your interest. RadioPad is a clinical-grade AI-assisted reporting platform — please read this entire file before sending a PR.

> Legacy Open Design contributing notes were archived to `CONTRIBUTING.legacy.md` and consolidated here.

## Local setup

```powershell
# Backend
cd backend/RadioPad.Api
dotnet restore && dotnet build && dotnet test

# Frontend
cd frontend
pnpm install
pnpm dev          # http://localhost:3000 — proxies /api → 127.0.0.1:7457
```

See [docs/07-devops/dev-setup.md](docs/07-devops/dev-setup.md) for the full environment matrix.

## Branching

- Trunk: `main`. Feature work goes through short-lived branches `feat/<slug>`, `fix/<slug>`, `docs/<slug>`, `chore/<slug>`, `sec/<slug>`.
- Force-pushes to `main` are forbidden.
- Rebase on `main` before opening a PR; squash-merge by default.

## Commits

Use Conventional-Commit-style prefixes:

```
feat(api): server-side report list pagination
fix(ai-gateway): audit ProviderBlocked before rethrow
docs(architecture): refresh PHI policy diagram
```

See [docs/07-devops/commit-conventions.md](docs/07-devops/commit-conventions.md).

## PR process

1. Open an issue or grab one from the roadmap before non-trivial work.
2. Keep PRs ≤400 LOC where possible.
3. Fill in the PR template (`.github/pull_request_template.md`).
4. Update [PROGRESS.md](PROGRESS.md) when a checklist item closes.
5. Update relevant `docs/` pages in the same PR if behaviour changed.

## Required checks

- `dotnet build && dotnet test` for any backend change.
- `pnpm typecheck` for any frontend change.
- Every matching rulebook golden suite green (`rulebooks/_tests/<id>/`) for clinical changes.
- No PHI/secrets in fixtures, logs, or screenshots.

## Review expectations

- Files in `backend/RadioPad.Api/src/RadioPad.Application/Services/AiGateway.cs`, `ReportValidator.cs`, `FhirDiagnosticReportSerializer.cs`, and any rulebook flipping to `status: approved` require human review.
- UI changes are reviewed against [docs/02-design/design.md](docs/02-design/design.md) — the design lock is non-negotiable.

## Code of conduct

By participating, you agree to follow [CODE_OF_CONDUCT.md](CODE_OF_CONDUCT.md).

## Security

Never disclose vulnerabilities in public issues — see [SECURITY.md](SECURITY.md).
