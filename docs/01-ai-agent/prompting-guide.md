# Prompting Guide

**Status:** Current  ·  **Owner:** Engineering  ·  **Last Updated:** 2026-05-04

How a human should ask an AI agent for work in this repo.

## Good prompts

- "Add a `GET /api/reports/{id}/versions` endpoint returning the most-recent 50 `ReportVersion` rows newest-first; update `openapi/openapi.yaml`, `docs/03-architecture/api-reference.md`, and add an integration test under `tests/Integration/`."
- "In `frontend/app/reports/[id]/page.tsx`, group the validation panel findings by severity using only the locked `.finding.blocker/.warning/.info` classes and per-bucket headers."
- "Bump the chest-CT rulebook from `1.0.0` to `1.1.0` adding rule `chest_ct.lat.001`; include a golden case under `rulebooks/_tests/chest_ct_v1/`."

What makes them good:

- They name the file or surface area precisely.
- They state the constraint (locked classes, append-only audit, etc.).
- They list the docs/tests that must change.

## Bad prompts

- "Make it nicer." → ambiguous; will violate the design lock.
- "Refactor the backend." → too broad; will touch reviewed files.
- "Add AI auto-sign." → forbidden by the safety boundaries.

## Required context for any non-trivial prompt

1. **Goal.** One sentence.
2. **Files / modules.** Explicit paths.
3. **Constraints.** Locked tokens, PHI policy, audit append-only, tenant isolation.
4. **Tests.** Which test project, which fixtures.
5. **Docs.** Which canonical doc(s) to update.
6. **Definition of done.** Acceptance criteria.

## How to specify files

- Use repo-relative paths: `backend/RadioPad.Api/src/RadioPad.Api/Controllers/ReportsController.cs`.
- Quote symbol names in backticks: `RouteAsync`, `ResolveContextAsync`.
- Use line ranges only when you've actually opened the file: `Lines 120-145`.

## How to request tests

- Name the test project (`RadioPad.Api.Tests`) and the file (`AiGatewayPolicyTests.cs`).
- State the assertion shape: "Single audit event with `AuditAction.ProviderBlocked`."

## How to request documentation updates

- Name the canonical document, e.g. "Update `docs/03-architecture/api-reference.md` and `openapi/openapi.yaml`."
- For UI work, also: "Update `docs/02-design/design.md` if a new token/class was introduced."
