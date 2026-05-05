# Commit Conventions

**Status:** Current  ·  **Owner:** Engineering  ·  **Last Updated:** 2026-05-04

## Format

[Conventional Commits 1.0](https://www.conventionalcommits.org/):

```
<type>(<scope>)?: <short summary>

<optional body>

<optional footers>
```

## Types

- `feat` — user-visible feature.
- `fix` — bug fix.
- `docs` — documentation only.
- `refactor` — internal rework, no behaviour change.
- `perf` — performance improvement.
- `chore` — tooling, deps, repo plumbing.
- `test` — test-only change.
- `build` — build / packaging.
- `ci` — CI workflow change.
- `revert` — reverts a previous commit.

## Scopes (suggested)

`api`, `frontend`, `desktop`, `mobile`, `cli`, `validation`, `ai`, `audit`, `rulebook`, `template`, `provider`, `docs`.

## Examples

```
feat(api): add /api/reports/{id}/versions endpoint

Returns the version history newest-first. Used by the report editor
and CLI export.

Refs PROGRESS.md iteration 8.
```

```
fix(ai): audit ProviderBlocked before throwing

Previously the policy block threw without writing the audit row,
so the integration test relying on the row count was flaky.
```

## Body / footers

- Body explains the **why** plus relevant trade-offs.
- Footer references issues / RFCs (`Refs #123`, `Fixes #123`).
- Use `BREAKING CHANGE:` footer for any incompatible change; this triggers a MAJOR bump under SemVer.

## What never goes in a commit

- PHI, secrets, real patient data.
- Generated files outside committed paths.
- Mass auto-formatting in a feature commit (split into a separate `chore: format` commit).
