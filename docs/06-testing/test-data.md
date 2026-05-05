# Test Data

**Status:** Current  ·  **Owner:** QA + Engineering  ·  **Last Updated:** 2026-05-04

## Principle

**Synthetic only.** No real patient data, no real-looking patient identifiers, no real MRNs, no real accession numbers from a production system.

## Sources

- `DevSeed` in the backend: a `dev` tenant, mock provider, five seed rulebooks, five seed templates.
- Integration test factories: each test builds its own minimal data.
- Rulebook golden cases: `rulebooks/_tests/<id>/case-*.json`.
- Prompt eval cases: `evals/<prompt-id>/case-*.json`.

## Naming conventions

- Patient names: `Test, Patient` / `Doe, Sample` — never names that match real persons.
- Accession numbers: prefixed `IT-` for integration; `DEV-` for dev seed.
- Dates: 2025-01-01 onward; never the current calendar date in fixtures (which would imply real-time relevance).

## Sensitive data rules

- Forbidden in all fixtures: real names, real DOBs, real MRNs, real addresses, real provider keys, real session tokens.
- If a screenshot is added to docs, every textual identifier must be replaced with placeholders before commit.
- A pre-commit hook (planned) will warn on patterns that look like real PHI.

## Refresh

- Dev seed runs whenever the dev DB is recreated (`Data Source=radiopad.dev.db` deleted).
- Integration test data is created and discarded per-test.
- Golden cases evolve with the rulebooks and prompts they cover.

## Loading

- Backend: `DevSeed` runs in `Program.cs` when `ASPNETCORE_ENVIRONMENT != "Testing"`.
- Frontend: no separate fixture loading; relies on the seeded backend.
- CLI: `radiopad seed dev` (planned) wraps the same path.
