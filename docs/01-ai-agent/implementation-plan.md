# Implementation Plan Template

**Status:** Current  ·  **Owner:** Engineering  ·  **Last Updated:** 2026-05-04

Use this template at the start of any non-trivial change.

## 1. Goal

State the user-visible outcome in one sentence.

## 2. Background

What's true today? Link the canonical doc(s) describing the current behaviour.

## 3. Proposal

What changes? Diagram or pseudocode if useful.

## 4. Affected surfaces

| Surface | File(s) | Type of change |
| --- | --- | --- |
| Backend | `backend/...` | new endpoint / migration / refactor |
| Frontend | `frontend/...` | new page / component / styling |
| CLI | `cli/...` | new command |
| Docs | `docs/...` | spec update |

## 5. Data model & migrations

- New tables / columns / indexes.
- Backwards-compat plan.
- Migration name.

## 6. API contract

- New / changed endpoints.
- Request / response shapes.
- Error codes.
- Pagination / rate-limit / auth.

## 7. Tests

- Unit tests (`<project>.Tests`).
- Integration tests (`Integration/...`).
- Rulebook golden cases (if clinical).

## 8. Documentation

- [ ] `docs/03-architecture/api-reference.md`
- [ ] `openapi/openapi.yaml`
- [ ] `docs/02-design/design.md` (if UI tokens added)
- [ ] `CHANGELOG.md` under `[Unreleased]`
- [ ] `PROGRESS.md` iteration entry

## 9. Risks & rollback

- Risks (clinical, security, perf, UX).
- Rollback plan.

## 10. Open questions

- Add to [../_reports/open-questions.md](../_reports/open-questions.md) if not decided.
