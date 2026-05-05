# Regression Test Plan

**Status:** Current  ·  **Owner:** Engineering + QA  ·  **Last Updated:** 2026-05-04

## Goal

Every fixed bug and every shipped feature has a test that would have caught it.

## Composition

- **Unit + integration tests** form the spine; a regression for most bugs adds one or two of these.
- **Rulebook golden cases** under `rulebooks/_tests/<rulebook_id>/` regression-test rulebook semantics.
- **Prompt evals** under `evals/<prompt-id>/` regression-test AI quality and safety.
- **E2E smoke** runs nightly against staging (planned).

## Workflow on a bug fix

1. Reproduce in the most local layer possible (unit > integration > E2E).
2. Add the failing test that catches it.
3. Fix.
4. Confirm test now passes; full suite still passes.
5. Reference the test path in the PR description.

## Workflow on a feature

1. Author tests at the appropriate layer (often integration for backend, E2E for UI flows).
2. Implement.
3. Both new tests and existing suite pass.

## Areas with strict regression policy

- Tenant isolation: any change touching `ResolveContextAsync` requires the cross-tenant test.
- PHI policy: any change touching `AiGateway.EnforcePhiPolicy` requires the policy-block test.
- Audit chain: any change touching `IAuditLog` requires the chain-hash test.
- Pagination: any change to the list endpoints checks `X-Total-Count` and clamp behaviour.

## Storage

- Tests live with their respective project.
- Golden cases / eval cases live in repo data folders.
- Bug-fix regression tests have a comment referencing the issue id.

## Suite execution time budget

- Unit + integration: < 60 s on the fastest workstation.
- Golden + evals: minutes; nightly cadence acceptable for the long tail.
- E2E: < 5 min for the smoke set.
