# Test strategy

## Layers

1. **Unit (xUnit, `tests/RadioPad.Api.Tests`)** — validation engine, FHIR
   serialization, provider adapters, PHI policy, KMS adapters, and utility
   services.
2. **Integration** — `WebApplicationFactory<Program>` spins up the API
   in-memory against SQLite; exercises controllers with tenant/user headers
   or tenant-scoped SCIM bearer auth.
3. **Golden cases** — every approved rulebook ships with case files under
   `rulebooks/_tests/<rulebook_id>/`; CI validates every `rulebooks/*.yaml`
   and runs every matching golden suite.
4. **Frontend typecheck** — `pnpm --filter @radiopad/frontend typecheck`.
   Visual regression is enforced via the design lock (manual review against
   `docs/02-design/design.md`) until a Storybook snapshot suite is added.
5. **End-to-end (planned)** — Playwright running against the static export
   served by the desktop binary.

## What "approved rulebook" means

A rulebook may only be promoted to `Approved` when:

- It deserialises cleanly through `RulebookSpec.FromYaml`.
- All golden cases pass locally and in CI.
- A reviewer explicitly approves via the API (`POST /api/rulebooks/{id}/approve`).

## Coverage targets

| Area | Target |
| ---- | ------ |
| `RadioPad.Validation` | ≥ 90% line coverage |
| `RadioPad.Application` (gateway, reporting service, FHIR) | ≥ 85% |
| Controllers | smoke-tested through integration suite |
| Frontend logic | typecheck + future Vitest cases for `lib/api.ts` |

## Forbidden in tests

- Real PHI in fixtures — use the synthetic strings in existing tests.
- Hitting external AI providers — use `MockAiAdapter` in tests.
- Mutating the `AuditEvents` table — verify chains by reading only.
