# RadioPad — Phone Companion (pairing → smooth dictation) — SESSION HANDOFF

> **Status: rounds 1-4 SHIPPED & LIVE; rounds 5-6 (mic-permission fix + stuck-transcribing
> fix + Keyboard-voice mode) COMMITTED, desktop release PENDING (see §10) (2026-07-13).**
> A fresh session can start from this file. Persistent memory
> `radiopad-companion-qr-login` (auto-loads) covers the same ground.

Repo root: `E:\RadioPad MEGA Folder\RADIOPAD` · GitHub: `jerryboganda/radiopad` (branch `main`)

---

## 1. What this session did (4 rounds, all on the phone companion)

The phone companion pairs to a live desktop report session and acts as a wireless
dictation mic. This session took it from "pairing fails" to "smooth continuous
dictation". Latest shipped: **desktop v0.1.68**, APK `radiopad-mobile-apk/v0.1.68/app-debug.apk`.

| Round | What | Version |
|---|---|---|
| 1 | **QR-login** — fixed "Pairing failed" (phone was unauthenticated → 401) | v0.1.65 |
| 2 | **Toggle mic + native STT + real-time desktop preview + radiologist controls** | v0.1.66 |
| 3 | **"Check for updates" in the phone app** | v0.1.67 |
| 4 | **Smooth dictation via LAN WebRTC** (replaced the choppy native STT) — the headline | v0.1.68 |

---

## 2. Round 1 — QR-login (v0.1.65)

Root cause of "Pairing failed": `POST /api/companion/pair` is tenant-scoped, but the
phone had no way to authenticate, so it 401'd; the UI mapped that to the generic
"Pairing failed." Fix: the desktop QR carries a **short-lived (2h), revocable
companion bearer** so scanning authenticates + pairs in one step.

- Backend `CompanionController.Create` mints an `rp_` bearer (`RadioPadBearerTokens.Mint`,
  2h) + records a revocable `AuthSession` (`Method="companion"`); returns `companionToken`
  + `tenantSlug`/`userEmail`. `End()` revokes them. Files: `backend/.../Controllers/CompanionController.cs`.
- **CORS gotcha (also fixed):** Android Capacitor origin is `https://localhost`
  (`capacitor.config.ts androidScheme:'https'`) — it was MISSING from `Program.cs` CORS
  `WithOrigins` (only `capacitor://localhost`/iOS + tauri were there). Added `https://localhost`.
- Frontend: `lib/companionPairing.ts` (QR JSON codec `{k:'rp-companion',v,b,c,t,tn,u}`),
  `lib/companionScan.ts` (native ML Kit scanner + webcam BarcodeDetector/jsQR + paste fallback),
  `CompanionHostPanel` QR encodes the full payload. `/companion` **exempted from `AuthGate`**
  in `AppShell.tsx` (phone is signed-out until it scans).
- Deps added: `@capacitor-mlkit/barcode-scanning` + `jsqr` (frontend + mobile).

## 3. Round 2 — controls + real-time (v0.1.66) — MOSTLY SUPERSEDED by Round 4

- New companion remote commands (still live): `jump_findings`, `jump_impression`,
  `new_line`, `undo`, `generate_impression` (dispatches `radiopad:generate-impression`,
  `ReportClient` listens → `runAi('impression')`). In `CompanionCommand` (lib/companion.ts)
  + desktop `CompanionHostPanel.handleCommand`.
- `lib/editor/interimDecoration.ts` — ProseMirror WIDGET decoration for an at-caret preview
  (never enters the saved doc/undo). GOTCHA: widget `key` MUST vary with text or it freezes.
  Still used by the LOCAL dictation path; the companion no longer produces per-word interims
  (Round 4 is phrase-based).

## 4. Round 3 — "Check for updates" on the phone (v0.1.67)

- `MobileUpdateCheck` component (mobile surface only) + `lib/mobileUpdate.ts`. Checks the
  **backend** `GET /api/mobile/latest` (public, `IMemoryCache` 30-min, `MobileController`;
  allowlisted in `RadioPadBearerMiddleware.IsPublicApi`) — NOT GitHub directly (anon GitHub API
  is 60/hr per IP, dies behind hospital NAT; release-asset downloads send no CORS).
- App version baked from `desktop/src-tauri/tauri.conf.json` via `build-surface.mjs` →
  `NEXT_PUBLIC_APP_VERSION`.
- `.github/workflows/attach-android-release.yml` attaches the debug APK to each release as
  `RadioPad-companion-android.apk` (stable name → backend returns its URL). `mobile-bundle.yml`
  **caches `~/.android/debug.keystore`** (stable signing key → in-place updates). **One-time
  uninstall** needed to migrate an old install onto the stable key.

## 5. Round 4 — SMOOTH DICTATION via LAN WebRTC (v0.1.68) — the important part

**Root cause of choppiness:** the phone used Android's native
`@capacitor-community/speech-recognition`, which endpoints every ~3–4s and restarts with an
audio-dropping gap. Fundamentally unfit for continuous dictation. REMOVED.

**Operator chose (via AskUserQuestion): "same-Wi-Fi only, audio never touches the cloud".**

**Architecture:** phone = pure mic; desktop = STT.
- Phone captures audio continuously + segments at speech pauses (RMS VAD; MediaRecorder cycled
  per phrase so each webm is self-contained/decodable): `frontend/lib/companionAudioCapture.ts`.
- Each segment streams to the desktop over a **direct LAN WebRTC data channel**
  (`frontend/lib/companionRtc.ts`; 8-byte-header chunk framing in `companionAudioFrames.ts`).
  **`iceServers: []`** ⇒ host candidates only ⇒ connects ONLY on the same Wi-Fi (by design).
  Audio never touches the cloud; only SDP/ICE signaling rides the relay as
  `rtc_offer`/`rtc_answer`/`rtc_ice`/`rtc_bye` (forwarded verbatim — the relay passes any JSON
  with a `type` field, ≤256KB, `CompanionRelayEndpoint.cs`). **Desktop is the OFFERER** (on
  `peer_joined`); phone answers.
- Desktop transcribes each segment with the SAME engine as local dictation (`blobToWav16kMono`
  + `api.reports.transcribe` → loopback Parakeet sidecar), strictly in seq order
  (`frontend/lib/companionAudioReceiver.ts` FIFO), then `insertAtCursor`. Text appears
  per-phrase (real-time word-by-word is gone — accepted tradeoff for smoothness).
- `companionSpeech.ts` gutted → `ensureMicPermission()` only (keeps the speech-recognition
  plugin ONLY for its RECORD_AUDIO manifest entry; `getUserMedia` does the capture).
- Desktop CSP got `webrtc 'allow'` + `media-src ... mediastream:` (`tauri.conf.json`).

**KEY seq gotcha:** the send seq is owned by the **RTC peer** (per-connection), NOT the capture
session. The phone recreates its peer on EVERY offer so its seq resets in lockstep with the
desktop receiver's per-connection `nextSeq` — otherwise mic-toggle/reconnect desyncs them and
phrases silently drop.

**Adversarial-review workflow found + fixed 12 real bugs** (seq desync ×2, phone ignoring
`peer_left` → hot mic, mic-permission await race, receiver not cancelling an in-flight
transcription on reset, AudioContext suspended, no-focused-section drop, stale peer reuse on
retry, …). New/changed files: `companionRtc.ts`, `companionAudioCapture.ts`,
`companionAudioReceiver.ts`, `companionAudioFrames.ts`, `companionSpeech.ts`,
`app/(mobile)/companion/page.tsx`, `components/companion/CompanionHostPanel.tsx`,
`lib/companion.ts`, `desktop/src-tauri/tauri.conf.json`.

Also fixed a CI race: `attach-android-release.yml` now triggers on BOTH desktop-bundle AND
mobile-bundle completing, checks readiness + exits quietly if not ready (idempotent) — because
mobile-bundle can finish after desktop-bundle.

---

## 6. What's LIVE now (all verified)

- **Desktop v0.1.68** — GitHub release, signed MSI/AppImage/deb, `latest.json`→0.1.68 (auto-update).
- **APK** — `E:\RadioPad MEGA Folder\radiopad-mobile-apk\v0.1.68\app-debug.apk` (18.5 MB, debug),
  also attached to the v0.1.68 release as `RadioPad-companion-android.apk` (publicly downloadable).
- **Backend deployed** on the VPS: mint/revoke companion tokens, CORS `https://localhost`,
  `GET /api/mobile/latest` → `{version:"0.1.68", apkUrl:…}`.
- Tests: frontend typecheck + 252 tests + `build:{mobile,desktop}`; backend build + 815 tests.
  New unit tests: `companionAudioFrames` (framing round-trip), `companionAudioReceiver` (FIFO +
  reset), `companionPairing`, `interimDecoration`, `mobileUpdate`.

## 7. Deploy / build / verify playbook (unchanged from repo)

- **Backend:** commit+push `main` → `web-deploy-images` CI builds api image; then
  `ssh root@185.252.233.186 "/opt/radiopad/_deploy-images.sh [run_id]"`. (Round 4 had NO backend
  change; Rounds 1+3 did.)
- **Desktop release:** `pnpm release:desktop` (bumps tauri.conf+Cargo, tags `vX.Y.Z`, pushes).
  NEEDS A CLEAN TREE — move the two untracked items aside first (they otherwise block it):
  `UI UX SCREENS/` and `docs/HANDOFF-*.md`. Then restore them after.
- **APK:** `gh run download <mobile-bundle run id> -n radiopad-android-debug -D <dir>`.
  NOTE: `gh run download` and `gh release upload` OFTEN TIME OUT here — just retry.
- **VPS:** `root@185.252.233.186`, `/opt/radiopad`. To refresh the `/api/mobile/latest` 30-min
  cache immediately: `docker restart radiopad-api`.

## 8. Local dev / test tips proven this session

- Browser-testable without 2 devices: WebRTC LAN loopback (two RTCPeerConnections, `iceServers:[]`,
  localhost) + the framing round-trip — see the harness pattern; it PASSED (byte-exact).
- Serve a built surface: `python -m http.server 3000` in `frontend/out-mobile` (use port **3000**
  or **127.0.0.1:3000** — the backend CORS allow-list only includes those dev origins, not 3050/3100).
- `next dev` = full desktop surface (companion host code present); `/companion` renders the phone UI.
- Windows quirk: a static server serving `out-mobile` can LOCK the dir (EPERM on next build) via the
  `.agent-browser` sandboxed Chrome — kill those chrome processes + the server before rebuilding.

---

## 9. OUTSTANDING — the ONE thing left: on-device 2-device test of WebRTC dictation

Everything upstream of the physical phone↔desktop Wi-Fi link is verified. The following can ONLY
be tested on two real devices on the same Wi-Fi, and is UNVERIFIED:

1. **Actual WebRTC interop** Capacitor Android WebView ↔ Tauri WebView2 (Windows) over the LAN.
2. **Mic permission** — `getUserMedia(audio)` in the Capacitor Android WebView (`ensureMicPermission`
   requests RECORD_AUDIO via the speech plugin first; then `getUserMedia`). If it throws
   `NotAllowedError`, the permission path needs work.
3. **Codec** — Android `MediaRecorder` webm/opus → desktop WebView2 `decodeAudioData` (`blobToWav16kMono`)
   → Parakeet. Some Android WebViews only expose `audio/mp4`; `decodeAudioData` should still handle it.

**How to test:** both devices on the SAME Wi-Fi → desktop opens a report → "Pair phone" → phone
scans QR → desktop should show **"Paired over Wi-Fi"** → tap the phone mic, speak → text lands
per-phrase on the desktop.

**If it fails, likely causes (from the implementation plan):**
- Desktop stuck on "Connecting over Wi-Fi…" / shows "same Wi-Fi" + Retry → LAN link not forming:
  AP client isolation (guest/hospital wifi), different subnets, or **mDNS `.local` ICE candidates**
  not resolving between the two WebViews. Do NOT add STUN/TURN (would break the same-Wi-Fi guarantee)
  without operator sign-off.
- No text but link is "connected" → mic permission denied, or codec (webm→decodeAudioData) failing,
  or the STT sidecar not ready. Check the desktop for a "transcribing…" indicator + any error banner.
- Add diagnostics: log `pc.iceConnectionState` + gathered candidate types on both ends;
  surface `getUserMedia` errors on the phone.

If the operator reports the LAN link never connects on their network, the fallback (rejected this
session for PHI reasons) is "audio via the cloud relay" — revisit only with operator approval.
(UPDATE: the Keyboard-voice mode from §10 is now the sanctioned fallback for hostile LANs —
text over the relay, no audio off-device.)

---

## 10. Round 5+6 (2026-07-13, second session) — on-device test results + fixes

The §9 two-device test RAN. Results and what was done:

### Round 5 — mic permission (commit a891379, in v0.1.68+APK rebuild)
Phone showed "Could not start the microphone." ROOT CAUSE (from Capacitor 6.2.1 source):
WebView `getUserMedia(audio)` → `BridgeWebChromeClient.onPermissionRequest` requests BOTH
`RECORD_AUDIO` AND `MODIFY_AUDIO_SETTINGS`, denies audio if EITHER missing; undeclared
runtime permissions auto-deny with no dialog; nothing declared `MODIFY_AUDIO_SETTINGS`.
FIX: `mobile/scripts/inject-android-permissions.mjs` injects both into the CI-generated
manifest (wired into mobile-bundle.yml with grep assertion + local `pnpm sync`);
`describeCaptureError()` surfaces real getUserMedia errors on the phone. VERIFIED: binary
manifest of the built APK contains both permissions.

### Round 6 — stuck "Transcribing…" + Keyboard-voice mode (this round)
After the mic fix, pairing + capture + LAN link all worked but the desktop stuck at
"Transcribing…" forever, no text. ROOT CAUSE (5-agent investigation, confirmed in source):
`api.reports.transcribe` → `requestFormTo` is a BARE fetch — no timeout/abort anywhere; the
single-consumer FIFO (companionAudioReceiver) has ONE await; a cold ONNX engine load (or a
backlogged single-threaded sidecar, incl. zombie requests from prior sessions) blocks that
await forever → busy latched → queue frozen. (Model MISSING is a fast 503, not a hang;
sidecar down is a fast TypeError — the hang is specifically cold-load/backlog.)

FIXES (desktop, needs a desktop release to reach the fleet):
- Abortable transcribe: AbortSignal threaded through `api.reports.transcribe`/`requestForm`/
  `requestFormTo`; 60s deadline + abort-on-unpair (`transcribeAbortRef` in stopRtc); 20s
  decode deadline (`raceTimeout`, falls back to raw webm — the sidecar DOES accept webm);
  failed phrase is SKIPPED and the queue continues; receiver passes real error messages.
- Warm-up mapping like DictationOverlay: 503/`stt_unavailable` → "model may still be
  downloading"; TypeError → "engine still starting"; passes `getSttMode()` like local dictation.
- Readiness probe on RTC connect (`api.localModels.list()`): no blob-capable STT engine
  available → actionable banner immediately (Downloading state gets its own message).
  NOTE: Edge Web Speech CANNOT transcribe blobs (live-mic API only — also NOT available in
  WebView2/Android WebView at all, confirmed by research); blob-capable = Parakeet + SAPI,
  and the sidecar already honors the user's persisted primary engine.
- "Still transcribing — engine may be loading…" escalation after 8s; success clears stale
  error banners; `dictation` finals got the same no-focus fallback (findings → first section).

NEW: **Keyboard-voice mode on the phone** (the "instant realtime + Gboard" ask):
- Mode toggle on the live screen: "Wi-Fi mic" (unchanged) vs "Keyboard voice".
- Keyboard voice = textarea; radiologist uses the PHONE KEYBOARD's mic (Gboard voice
  typing — Google-grade, real-time, free). Text streams as live interims over the RELAY
  (`sendDictation` — desktop ghost-preview at caret) and auto-commits (formatDictation)
  after a 1.6s pause / Insert button / blur. `lib/companionTypeDictation.ts` (unit-tested:
  throttle, idle-commit, committed-prefix race guard, preview hygiene). ptt_start/stop on
  focus/blur drive the desktop "Listening" indicator.
- WORKS AGAINST DESKTOP v0.1.68 AS-IS (desktop dictation handler exists since v0.1.66) and
  works even when the LAN link can't form (AP isolation fallback, no audio off-device).

### Adversarial review round (16/17 findings confirmed → 8 distinct fixes applied)
committed-prefix guard (exact-match restore + one-shot consumption → window-based,
strips equal), teardown type-state leak (dead Insert + silent word loss after re-pair →
commit-then-reset), ptt_start/stop stranding desktop "Listening" (unconditional stop on
mode switch + aborted-startup path), readiness-probe staleness guard + setError(null) on
unpair, slow-hint keyed per phrase (phraseSeq — React batching kept busy true across
queued phrases), interim/final one resolved fallback target, stale-session guard before
registering transcribeAbortRef, IME-composition deferral of idle-commit
(deferIdleCommit + onCompositionStart/End). One finding rejected; one skipped by choice
(desktop "Listening" copy says "speak into your phone" — accurate for Gboard too).

### Deploy state (2026-07-13 evening) — FINAL: fleet is on v0.1.70
- The parallel RC-redesign session LANDED + pushed (0743571..d951ac8) before this ship,
  so the tree went clean; verification (275 tests + both surface builds) ran WITH the
  redesign in the tree.
- Companion fixes = commit 9b1e3ed. Two releases went out back-to-back: **v0.1.69**
  (c7586e4, companion fixes + RC redesign) then **v0.1.70** (cbb3f74; adds the parallel
  session's RC-08 GREEN listening-state CSS for the companion mic — CSS/mockups only,
  did NOT touch any companion .ts/.tsx, my logic verified intact). **v0.1.70 is the
  fleet's latest** — desktop auto-updates to it; `/api/mobile/latest`=0.1.70.
- APKs verified (RECORD_AUDIO + MODIFY_AUDIO_SETTINGS present in binary manifest) at
  `radiopad-mobile-apk/v0.1.69/` and `radiopad-mobile-apk/v0.1.70/app-debug.apk`
  (operator should sideload v0.1.70 — newest, has all fixes + green dot).
- **CI: attach-android-release FIXED (dee0c2e..756e664, verified in CI).** It had never
  auto-attached the APK — three stacked silent bugs: (1) release is a DRAFT when the
  triggers fire (tauri-updater publishes it later; a draft 404s on `gh release view`) →
  bounded wait-for-published poll; (2) `gh run list --commit/--branch` empty on the
  runner → raw REST API `head_sha` lookup; (3) **no `actions/checkout` → gh had no repo
  context → every `gh release`/`gh run download` failed silently → `env: GH_REPO`**.
  Added `workflow_dispatch(tag)` for manual recovery; verified by re-attaching v0.1.69's
  APK via dispatch. Both v0.1.69 + v0.1.70 now carry `RadioPad-companion-android.apk`.
- Backend restarted (`docker restart radiopad-api`) to refresh the 30-min
  `/api/mobile/latest` cache → returns 0.1.70 + correct apkUrl. No backend CODE change.
