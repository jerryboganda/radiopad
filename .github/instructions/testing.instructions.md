---
applyTo: "**"
---

# Testing instructions

- Backend: xUnit + plain `Assert`. No FluentAssertions / Moq unless added with explicit approval.
- Integration tests use `WebApplicationFactory<Program>` against an in-memory SQLite. Tenant slug = `it`, radiologist email = `it-radiologist@radiopad.local`, mock provider compliance = `LocalOnly`.
- Every behaviour change in `RadioPad.Validation`, `RadioPad.Application`, or `RadioPad.Domain` must ship with a test in the matching `*.Tests` project.
- Approved rulebooks (`status: approved`) require at least one passing golden case under `rulebooks/_tests/<rulebook_id>/`. Golden cases are JSON fixtures with `report` + `expectFlagged`.
- Frontend: prefer `pnpm typecheck` and component-level tests over snapshot tests. Never assert against live Open Design markup.
- AI gateway tests must cover: PHI + non-compliant provider ⇒ `ProviderPolicyException` *and* an `AuditAction.ProviderBlocked` audit row.
- Never include PHI or real patient data in fixtures — use the synthetic datasets under `rulebooks/_tests/`.
- CI must validate every `rulebooks/*.yaml` file and run every matching golden suite under `rulebooks/_tests/*`.
