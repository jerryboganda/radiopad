# Code Review Checklist

**Status:** Current  ·  **Owner:** Engineering  ·  **Last Updated:** 2026-05-04

## Correctness

- [ ] Behaviour matches the acceptance criteria.
- [ ] Edge cases handled (empty list, missing tenant, missing rulebook).
- [ ] Concurrency / cancellation handled (`CancellationToken ct`).

## Security

- [ ] Tenant isolation: every new query filters by `tenant.Id`.
- [ ] No PHI / secrets in logs or responses.
- [ ] PHI policy untouched or reviewed.
- [ ] Audit events emitted via `IAuditLog.AppendAsync` with stable enums.
- [ ] No new dependency without a license/maintenance note.

## Tests

- [ ] Unit + integration tests added or updated.
- [ ] Rulebook golden cases pass (if clinical).
- [ ] `dotnet test` and `pnpm typecheck` green.

## Performance

- [ ] No accidental N+1 queries.
- [ ] List endpoints honor `skip` / `take`; `X-Total-Count` returned.
- [ ] Hot paths within targets in [../00-product/nfr.md](../00-product/nfr.md).

## Accessibility

- [ ] Locked component classes preserve focus states & contrast.
- [ ] Labels for form fields.
- [ ] No color-only signalling (severity icons + text).

## Maintainability

- [ ] Code complies with [../../CONVENTIONS.md](../../CONVENTIONS.md).
- [ ] Methods small; names accurate.
- [ ] Commented sparingly; comments explain *why*, not *what*.

## Docs

- [ ] Canonical doc updated.
- [ ] `openapi/openapi.yaml` updated if API changed.
- [ ] `CHANGELOG.md` updated under `[Unreleased]`.
- [ ] `PROGRESS.md` iteration entry added.

## Human-review gates

- [ ] Touched files in [human-review-policy.md](human-review-policy.md) → human reviewer assigned.
