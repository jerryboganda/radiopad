# AI Coding Rules

**Status:** Current  ·  **Owner:** Engineering  ·  **Last Updated:** 2026-05-04

These rules apply to every AI coding agent (Claude Code, Cursor, Codex, Gemini, OmO, Ralph, etc.) operating in this repo.

## Hard rules

1. **Never auto-sign reports.** RadioPad is human-in-the-loop. AI text wears `.ai-mark` until acknowledged.
2. **Never weaken or bypass the PHI policy.** `AiGateway.EnforcePhiPolicy` is the cornerstone of clinical safety. The gate must audit `ProviderBlocked` before rethrowing `ProviderPolicyException`.
3. **Never modify the audit log destructively.** `AuditEvents` is append-only; SHA-256 chain. Use `IAuditLog.AppendAsync`.
4. **Never invent API contracts.** Update `openapi/openapi.yaml` and `docs/03-architecture/api-reference.md` when changing the API.
5. **Never invent rulebook semantics.** Rulebook YAML schema and approval flow live in `docs/05-clinical/rulebook-authoring.md`.
6. **Never delete user data without an explicit migration + ADR + human review.**
7. **Never commit secrets, PHI, or real patient data.** Provider keys live behind `ApiKeySecretRef = "env:<NAME>"`.

## Discipline

- Always read existing files before editing.
- Always update tests for behaviour changes.
- Always update `docs/` and `PROGRESS.md` in the same PR.
- Always respect the locked Open Design tokens & component classes.
- Always file an ADR for cross-layer architectural changes.

## Forbidden moves

- New frontend frameworks (Tailwind utility-only, MUI, Ant, Chakra, Bootstrap).
- Dark mode or alternate accent palettes.
- Emoji as functional icons.
- New backend frameworks (Express, NestJS, Dapper, etc.).
- Disabling tests, lint, or typecheck to land a PR.
- Force-pushing to `main`.
- Pushing real patient data anywhere.

## When stuck

- Search `docs/` and `PROGRESS.md`.
- Read the matching legacy file in `src/` or `daemon/` for UX hints — but do not copy implementation.
- Open an explicit *Open Question* in `docs/_reports/open-questions.md` rather than guessing.
