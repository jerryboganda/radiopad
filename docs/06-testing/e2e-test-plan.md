# End-to-End Test Plan

**Status:** Partially implemented  ·  **Owner:** Engineering + QA  ·  **Last Updated:** 2026-07-20

## Scope

Browser-driven flows that exercise the static export against a running backend.

## What ships today (2026-07-20)

**Desktop MSI E2E** — implemented, not planned. `desktop-bundle.yml`'s `msi-e2e` job installs the
actual Windows `.msi` on every tag push, pre-places the pinned on-device models, and
[`scripts/desktop-msi-e2e.mjs`](../../scripts/desktop-msi-e2e.mjs) (dependency-free Node 22
driving the WebView2 devtools protocol) exercises the installed renderer: UI login including the
mandatory TOTP enrollment, report creation, the dictation draft panel, a real on-device MedGemma
format that must pass the safety validator (`.ai-mark`, "Requires review", spoken-measurement
normalization asserted), Apply, and the **microphone capture path**: Chromium's fake audio
device (`--use-file-for-fake-audio-capture`) plays the MedASR bundle's own radiology dictation
sample into the overlay's real HQ mic button — getUserMedia → MediaRecorder → 16 kHz WAV →
`/api/stt/transcribe` → on-device MedASR decode — and the E2E asserts the transcript's content
words land in the focused section editor and that every transcribe response names MedASR as the
serving model. The desktop release job depends on it. Screenshots and logs are uploaded as the
`msi-e2e-evidence` artifact.

Not covered by it: the web/mobile surfaces (and the live Web-Speech "Dictate" mic, which is a
platform engine, not RadioPad code — the on-device HQ path is the covered one).

## Tooling

- Desktop: raw CDP via `scripts/desktop-msi-e2e.mjs` (see above) — no framework, no npm deps.
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
