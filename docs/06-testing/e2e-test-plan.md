# End-to-End Test Plan

**Status:** Partially implemented  ·  **Owner:** Engineering + QA  ·  **Last Updated:** 2026-07-20

## Scope

Browser-driven flows that exercise the static export against a running backend.

## What ships today (2026-07-21)

**Desktop MSI E2E** — removed 2026-07-21 (operator decision: manual release testing instead of
maintaining an unproven CI harness). It used to install the actual Windows `.msi` on every tag
push and drive the installed renderer (login incl. TOTP enrollment, report creation, on-device
MedGemma format + Apply, microphone dictation through MedASR) over CDP via
`scripts/desktop-msi-e2e.mjs`; both the job (`desktop-bundle.yml`) and the reusable workflow
(`desktop-msi-e2e.yml`) are gone. The desktop release is no longer gated on any renderer-driven
test — see `PROGRESS.md` for the removal note.

Not covered today: any automated renderer-driven verification of the desktop app. Releases are
verified manually by the operator.

## Tooling

- Web: Playwright (still planned).

## Flows to cover

1. **Authoring & sign.** Open dashboard → create report → fill sections → run validation → ask AI Mock for impression → acknowledge → export text.
2. **Tenant isolation.** Login as tenant A, navigate to a known tenant B id by URL, expect 404 banner.
3. **PHI block.** Configure a Sandbox provider, mark request as PHI, verify 403 banner with `kind: provider_policy`.
4. **Pagination.** Create > 25 reports, scroll dashboard, verify total + skip/take URL state.
5. **Rulebook approve.** Save a rulebook draft → run golden tests → approve → verify deprecation pathway works.
6. **Audit visibility.** Acknowledge a report; ensure the audit page shows the new event with the request id.
7. **Acknowledge AI marks.** Verify `.ai-mark` text loses the visual treatment after acknowledge.

## Data setup

- Seeded `it` tenant + Mock provider.
- Synthetic reports created via the API in the test setup.
- No real PHI.

## Run

```powershell
cd frontend
pnpm e2e
```

(Pending implementation; the `e2e` script will provision the API + run Playwright.)

## CI

- Nightly E2E job (planned).
- PRs run a short smoke subset (login + create + validate + ack).

## What we don't E2E

- AI provider quality (covered by [evals](../05-data-ai/prompt-evals.md)).
- Native desktop / mobile shells beyond a smoke check (their UI is identical to the web).
