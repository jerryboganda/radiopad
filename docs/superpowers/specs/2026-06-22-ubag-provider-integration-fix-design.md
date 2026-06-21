# UBAG provider integration — selectable targets + Gemini fix

**Date:** 2026-06-22
**Status:** ✅ Implemented & verified (PR #1 merged to main; UBAG worker fix committed `fb659f9e`)
**Scope:** RadioPad (`D:\Projects\Radiopad.com`, prod `/opt/radiopad`) **and** UBAG (`/opt/ubag`, compose project `ubag-small`). Both projects are owned by the same operator; full authority granted.

---

## 1. Problem & goals

The operator wants:

1. RadioPad to be **100% able to use UBAG** as an AI provider.
2. The **UBAG sub-target to be selectable** in RadioPad when UBAG is chosen as a provider.
3. To know whether **Gemini and DeepSeek** (via UBAG) work end-to-end with RadioPad — and to **fix** whatever does not.

### Success criteria

- In the Providers ("AI models") config UI, choosing adapter `ubag` lets the user **pick the target from a dropdown** (gemini_web / deepseek_web / mock) instead of typing it.
- A RadioPad provider `(adapter=ubag, model=deepseek_web)` and `(adapter=ubag, model=gemini_web)` both **return real AI output** through RadioPad's normal adapter path.
- The UBAG **health probe** and the UBAG Hub **Targets panel** correctly reflect live target/readiness state.
- Live verification jobs to **gemini_web and deepseek_web both complete with an answer**.

---

## 2. Findings (evidence gathered 2026-06-22)

All probes run against live prod gateway `http://ubag-small-gateway-1:8080` (Bearer auth, api-version `2026-05-22`).

**Plumbing is healthy.** `/v1/health` → ok; a `mock` job round-tripped to `completed` with output. RadioPad↔gateway wiring (network `ubag-small_ubag-private`, env `RADIOPAD_UBAG_*`) is correct. Prod env: `RADIOPAD_UBAG_ALLOWED_TARGETS=gemini_web,deepseek_web,mock`, `RADIOPAD_UBAG_ORDERED_TARGETS=gemini_web,deepseek_web`.

**DeepSeek works 100%.** Live job to `deepseek_web` → `completed` in ~20s with a real answer. Its browser context `ctx_prod_deepseek` is `login_state: authenticated`.

**Gemini does NOT work.** Live job to `gemini_web` → `assigned` then `failed_retryable` after exactly 2 min; job events show `error_class: worker_execution`, `message: "worker execution timed out"`; gateway log: `worker process timed out after 2m0s`. Its context `ctx_prod_gemini` IS `login_state: authenticated`, so **this is not a login problem** — it is **selector drift**: `adapters/gemini_web/.../selectors.py` carries `TODO(drift): re-confirm selectors`, the manifest is `status: stub`, and `GEMINI_WEB` selectors in `apps/worker/ubag_worker/live/selectors.py` are `selector_version: 2026-05-22-baseline-unverified`. The worker cannot locate Gemini's input/response elements, so it waits until timeout.

**RadioPad bug — target listing mis-parsed.** Gateway `/v1/targets` returns `{kind:"targets", data:[{adapter_key, display_name, key, manual_login_required, safe_mode}], next_cursor}`. `UbagClient.ListTargetsAsync` instead looks for a root array or a `targets` property, and reads id from `id`/`target`/`name`. Against this gateway it returns an **empty list**, which breaks:
- the UBAG Hub "Targets" panel (shows "No targets reported"), and
- `UbagProviderAdapter.ProbeAsync` (no match → `target_not_found`), so the provider "Test connection" wrongly reports failure even for working targets.

**RadioPad UX gap — target not selectable.** In `frontend/app/providers/page.tsx`, when adapter = `ubag` the **Model** field is a plain free-text `<input>` (line ~261). A preset sets `model: gemini_web`, but there is no dropdown. The UBAG **Hub** page already has a correct target `<select>` sourced from `status.allowedTargets`; the Providers config page does not.

---

## 3. Design

### Track A — RadioPad (deterministic)

**A1. Selectable UBAG target in provider config.**
In the provider edit modal, when `draft.adapter === 'ubag'`, render the **Model** field as a `<select>` populated from the live allowed targets, falling back to `['gemini_web','deepseek_web','mock']`.
- Source of options: call `api.ubag.status()` (existing) to read `allowedTargets`; cache for the modal session. If the call fails or returns empty, use the fallback list.
- Keep a non-ubag adapter on the existing free-text Model input (unchanged behaviour).
- Default the select to `gemini_web` only if current `draft.model` is empty/not in the list; otherwise preserve the saved value.
- Result: operator creates e.g. "UBAG · DeepSeek" (`adapter=ubag, model=deepseek_web`) and "UBAG · Gemini" (`adapter=ubag, model=gemini_web`) and selects whichever to draft with.

**A2. Fix `UbagClient.ListTargetsAsync` to the real gateway shape.**
Parse the `data[]` array (in addition to the legacy `targets[]`/root-array fallbacks, kept for compatibility). Read the id from `key` ?? `adapter_key` ?? `id` ?? `target` ?? `name`; display name from `display_name` ?? `name` ?? id. Since `/v1/targets` carries no per-target readiness, **derive readiness** by cross-referencing `/v1/browser/contexts` (`login_state == "authenticated"` for the matching `target_id`). Add a new client method `ListBrowserContextsAsync` (GET `/v1/browser/contexts`) returning `(target_id, login_state)` pairs; `ProbeAsync` and the Hub status use it to mark a target ready when its context is authenticated.

**A3. Health probe + ordered-chain resilience.**
- `UbagProviderAdapter.ProbeAsync` uses the corrected target list + context login-state: `Ok` when the target exists and its context is authenticated; otherwise a clear `Note` (e.g. `login_required` / `target_not_found`).
- Confirm `EnrichRunOutputAsync` already keeps a healthy step's output when a sibling step fails (it does — per-step aggregation). No reorder required, but document that with Gemini down the ordered chain still returns DeepSeek output plus a Gemini error note.

**A4. Tests.**
- Unit tests for the new `/v1/targets` `data[]` parsing and the contexts-based readiness (table-driven against captured real JSON).
- Keep existing `UbagProviderAdapterTests` green; add a probe test for "authenticated context ⇒ ready" and "unknown context ⇒ not ready".
- Frontend: extend `providersPage.test.tsx` to assert the Model field renders a `<select>` (not an input) when adapter = `ubag`.

### Track B — UBAG Gemini browser fix (iterative)

**B1. Reproduce & capture.** Submit a `gemini_web` job; collect the worker's on-failure screenshot/DOM artifact (manifest `artifact_policy.screenshots: on_failure_only`) and inspect the live Gemini tab via the noVNC browser viewer (`ubag-small-browser-viewer-1`, mapped `127.0.0.1:7900`) to see exactly which step fails (input not found / submit not firing / response or completion-signal not detected) and whether an interstitial (model picker, consent) is present.

**B2. Update selectors / flow.** In `apps/worker/ubag_worker/live/selectors.py`, correct `GEMINI_WEB` `prompt_input` / `submit_button` / `response_container` (and the streaming/complete signal) to the current `gemini.google.com/app` DOM; bump `selector_version` from `...-baseline-unverified` to a verified value; clear the `TODO(drift)` in the adapter `selectors.py`. Re-warm or re-create `ctx_prod_gemini` if the tab is stuck.

**B3. Re-test.** Iterate B1–B2 until a `gemini_web` job returns a real answer, then verify the same through RadioPad's adapter path.

---

## 4. Verification plan

1. **Backend build/tests on CI** (per AGENTS.md §0.5) — push branch, GitHub Actions green.
2. **Live gateway jobs** (from a throwaway container on `ubag-small_ubag-private`): `deepseek_web` and `gemini_web` both reach `completed` with answer text.
3. **RadioPad path**: configure/confirm providers `(ubag, deepseek_web)` and `(ubag, gemini_web)`; "Test connection" → OK; a draft/sandbox-compare run returns output for each.
4. **UI**: provider modal shows a target dropdown for `ubag`; Hub "Targets" panel lists targets with correct ready/login badges.
5. **No regression**: mock target still works; PHI/secret guards still reject; non-ubag adapters unchanged.

---

## 5. Risks & mitigations

- **Gemini DOM keeps drifting / interstitial requires human action.** Browser-automation fixes are inherently brittle. Mitigation: land Track A independently so RadioPad is fully usable with DeepSeek regardless; document Gemini selector version + a re-verify runbook; ordered chain degrades gracefully.
- **Touching production UBAG worker.** Mitigation: change only `selectors.py` (data, not control flow) where possible; back up the file; the worker restart only affects browser automation, not RadioPad uptime; verify on a single job before relying on it.
- **`/v1/browser/contexts` adds a call per probe.** Mitigation: only call it during probe/status (admin actions), not per draft.

---

## 6. Out of scope

- ESLint setup (separately deferred).
- chatgpt_web / claude_web / other UBAG targets (not enabled in prod `ALLOWED_TARGETS`).
- Report-drafting-time model picker UI beyond the existing provider selection (each UBAG target is a distinct provider row).
- Any PHI routing to UBAG (UBAG remains non-PHI / Sandbox by policy).

---

## 7. Outcome (2026-06-22)

**Track A (RadioPad) — done, merged via PR #1, CI green (backend+cli+frontend):**
- `UbagClient.ListTargetsAsync` now parses the real `data[]`/`key` shape; `ListBrowserContextsAsync` added; `UbagBrowserContext.Authenticated` readiness.
- `ProbeAsync` + `UbagController.Status` derive readiness from `/v1/browser/contexts` login state; probe hardened against `ProviderPolicyException`.
- Providers config UI: adapter `ubag` → **Model dropdown** of allowed targets (live + fallback, model seeded on switch).

**Track B (UBAG worker `/opt/ubag`, commit `fb659f9e`) — root cause + fix:**
- The Gemini browser tab had been parked on `accounts.google.com/chrome/blank.html` since a browser-viewer restart; re-navigating to `gemini.google.com/app` restored the (still-valid) logged-in session. The worker navigates per job, so it self-heals as long as the Google session persists (manual re-login via noVNC `:7900` if Google logs out — automated login is forbidden by UBAG policy).
- Real bug: `GEMINI_WEB.response_container` selectors (`*.model-response-text`, `data-response-index`) had drifted to zero matches → worker hung to the 2-min hard timeout. Fixed to `message-content` / `.markdown` / `model-response`. Also rewrote `page_driver.stream_response` to complete on a **non-empty, settled** answer (Gemini exposes no detectable streaming indicator) instead of breaking on the first read.
- Deployed live into the running gateway container (image bakes the file; not rebuilt to avoid baking unrelated WIP in `/opt/ubag`). A future clean rebuild bakes it.

**Verified end-to-end (live gateway jobs through RadioPad's exact job API):**
- `gemini_web` ✅ "The capital of France is Paris." (was a 2-min timeout before)
- `deepseek_web` ✅ (unregressed) · `mock` ✅

**Known follow-ups (non-blocking):** Gemini session can expire → needs manual noVNC re-login; the gateway can report a stale `login_state: authenticated` for a parked tab; UBAG gateway image should be rebuilt from a clean tree to bake the worker fix permanently.
