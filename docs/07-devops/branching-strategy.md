# Branching Strategy

**Status:** Current  ·  **Owner:** Engineering  ·  **Last Updated:** 2026-05-04

## Branches

- **`main`** — protected, always deployable, signed-tag releases originate here.
- **Topic branches** — short-lived (`feat/...`, `fix/...`, `docs/...`, `refactor/...`); rebased onto `main` and merged via PR.
- **Release branches** — `release/vX.Y` for backports only when LTS support requires it (Phase 3+). Not used in v0.x.

## Lifecycle

1. Create a topic branch from `main`.
2. Push frequently; open a PR early (draft is fine).
3. Rebase, never merge `main` into the topic.
4. Squash-merge to `main` with a Conventional Commits subject.
5. Delete the topic branch.

## Naming

- `feat/short-summary`
- `fix/short-summary`
- `docs/short-summary`
- `chore/short-summary`
- `refactor/short-summary`

## Protection rules

- `main`: required status checks (CI jobs), required review, no force-push.
- Optional (recommended): require signed commits.

## Releases & tags

- Tags `vX.Y.Z` (signed) cut from `main`.
- A tagged commit must have a paired `CHANGELOG.md` entry.
- Pre-releases `vX.Y.Z-rc.N` are permitted.

## Hotfixes

- Land a hotfix on `main` and tag a patch release.
- For a customer on a previous MAJOR (Phase 3+), a backport branch `backport/vX.Y` may be created; approval is required from Engineering Lead.
