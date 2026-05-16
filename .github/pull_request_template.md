# Pull Request

## Summary

<!-- One paragraph: what changes and why. Link the issue or roadmap item. -->

## Type

- [ ] feat
- [ ] fix
- [ ] docs
- [ ] chore / refactor
- [ ] sec
- [ ] clinical (rulebook / template)

## RadioPad checklist

- [ ] UI uses only the locked Open Design tokens & component classes (no Tailwind / MUI / dark mode / emoji icons).
- [ ] `dotnet build && dotnet test` passes for backend changes.
- [ ] `pnpm typecheck` (and `pnpm build` if exporting) passes for frontend changes.
- [ ] Tenant isolation respected — every new query filters via `ResolveContextAsync`.
- [ ] PHI policy untouched, or change reviewed by a human under [docs/04-security/security-architecture.md](../docs/04-security/security-architecture.md).
- [ ] Audit log writes go through `IAuditLog.AppendAsync` only (append-only).
- [ ] `[PROGRESS.md](../PROGRESS.md)` updated when a checklist item closes.
- [ ] `[CHANGELOG.md](../CHANGELOG.md)` updated for any user-facing change.
- [ ] Relevant `docs/` page updated in the same PR.
- [ ] No secrets, PHI, or real patient data in code, tests, fixtures, or screenshots.
- [ ] Approved rulebooks have golden cases under `rulebooks/_tests/<id>/`.

## Reviewer notes

<!-- Anything the reviewer should focus on, screenshots, breaking-change notes, migration steps. -->
