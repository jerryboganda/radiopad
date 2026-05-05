# Definition of Done

**Status:** Current  ·  **Owner:** Engineering  ·  **Last Updated:** 2026-05-04

A change is **done** when **all** of the following are true. Anything less is "in progress".

- [ ] Code reviewed and approved by at least one other engineer.
- [ ] If touching a [human-review file](../01-ai-agent/human-review-policy.md), an additional reviewer signed off (security, clinical, or product as appropriate).
- [ ] All affected tests (unit, integration, golden, eval) pass locally and in CI.
- [ ] No new ESLint or analyzer warnings.
- [ ] No new TypeScript or C# compile warnings introduced by the change.
- [ ] Documentation updated when behaviour changes:
  - `PROGRESS.md` for roadmap items.
  - `CHANGELOG.md` `[Unreleased]`.
  - `docs/` page(s) for the area.
- [ ] No PHI or secrets in code, fixtures, logs, or commit messages.
- [ ] If migration: paired with a forward-compatible plan and integration test.
- [ ] If feature flag: documented default and how to flip it.
- [ ] `dotnet build` and `dotnet test` green for backend.
- [ ] `pnpm typecheck` green for frontend.
- [ ] Audit log behaviour preserved (append-only; no UPDATE/DELETE on `AuditEvents`).
- [ ] PHI policy preserved (no provider compliance class downgrade without an ADR).
- [ ] UI uses only locked tokens & component classes.
- [ ] PR description references PROGRESS / issue / RFC if applicable.

If this list does not fit on one screen, you have shipped too many things in one PR.
