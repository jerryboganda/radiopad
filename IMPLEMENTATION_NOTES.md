# IMPLEMENTATION_NOTES.md — On-Device Dictation & Reporting Engine

> Living decision/version log mandated by `RadioPad_Dictation_Engine_ClaudeCode_Brief.md`
> §0.5/§10. Records pinned model versions, the MedASR deployment decision, safety-policy
> changes, and open questions. Updated every phase.

**Started:** 2026-07-18 · **Owner:** dictation-engine build · **Status:** Phases 0–3 delivered and
runtime-verified (MedASR + MedGemma both exercised end-to-end against the real sidecar); the
adversarial audit that followed is closed — every confirmed finding fixed, four refuted.
Last desktop release: **v0.1.85** (auto-updater; published with all assets + signed `latest.json`).

---

## 0. Delivered (running summary)

The safety engine and the buildable competitive features are shipped, all TDD + committed per slice:

- **§5 dictation safety engine** — §5.2 deterministic pass-through, §5.3 validation-diff (fail-safe
  fallback), §5.4 GBNF grammar, §5.6 laterality/negation/gender sentinel, §5.7 encrypted on-device
  audit; orchestrated by `DictationEngineService`; §4.4 memory manager; §4.2 draft endpoint + UI.
- **F1** spoken-measurement formatting (TS port of §5.2, idempotent) · "scratch that" voice undo ·
  spoken percent/slash punctuation.
- **F2** template "normal" values — author per-section defaults, **Use** seeds a report from them,
  Preview shows the normal body.
- **F3** device-local snippets with tab-through `${field}` selection math + manager UI + textarea
  insertion primitive **and in-editor auto-expansion** (Tiptap `SnippetExpansion`: Tab expands the
  trigger before the caret + tab-cycles fields). Complete **after the 2026-07-20 audit** — it was
  called complete earlier while the textarea primitive had no callers, the wizard's editors never
  registered the extension, and tab-through wrapped into a keyboard trap (see §9).
- **F4** measurement-sanity + findings/impression consistency (deterministic).
- **F5** auto-comparison statement (deterministic; inserts into Comparison).
- **F6** quality/safety gates on the dictation path — the tenant's rulebook now runs over the
  DRAFTED text and its findings return with the draft (Blocker→red / Warning→amber / Info→blue).
  Delivered 2026-07-20; it was the one planned feature previously recorded in neither doc, and
  grepping confirmed the dictation flow had never touched `ReportValidator`.
- **F7a/F7b** org + per-user correction dictionaries (backend + management UI).
- **F8** one-command Sign & Send · RIS-driven report priority. *(Both were overstated until
  2026-07-20: Sign & Send signed before validating, was unretryable, and skipped the permission
  gate; "RIS-driven" priority was written by no ingest path at all. Fixed — see §9.)*
- **F9** patient-friendly summary (pre-existing, verified).
- **F10** reports/hour KPI (+ AnalyticsService's first tests); template-usage pre-existing.
- **F12** free-text custom rewrite, **hard-guarded by §5.3** so it cannot introduce an un-dictated
  measurement/number/date; violations surfaced as an amber "Requires review" flag before Accept.
- **P0.3 (partial)** hold-to-talk PTT (alongside tap-toggle) + in-app rebindable dictation hotkey
  (all surfaces). System-wide/unfocused Rust rebind + streaming on-device decode still pending.
- **Phase 3 gating** — `RegulatedFeatures` gate + admin "Regulated AI features" surface. **Default
  flipped to ENABLED on 2026-07-19** by explicit operator instruction (UKCA/MHRA/CE/FDA licensing
  stated as acquired). Until 2026-07-20 the gate had **no call sites** and read the wrong entity, so
  neither the old OFF default nor the panel's regulatory claim was actually in force. See §5.

### MedASR — UNBLOCKED (browser research, 2026-07-19)
The "no sherpa bundle exists" blocker was **wrong**. A public, **ungated** sherpa-onnx-native CTC
export exists from the sherpa-onnx maintainer: `csukuangfj/sherpa-onnx-medasr-ctc-en-int8-2025-12-25`
(model.int8.onnx + tokens.txt, ~160 MB, HF API `gated:false`). And sherpa-onnx v1.13.3 (already
pinned) has first-class MedASR support (`OfflineModelConfig.MedAsr.Model`). So MedASR now runs on the
existing on-device engine — **no HF token, no license-click, no ONNX-export step**. Engine wired +
compile-verified: `SherpaMedAsrSttClient` + the verified `LocalSttModels.MedAsr*` descriptor +
`SttModelProvisioner.EnsureMedAsrAsync`. DI registration, routing/ensemble, and `LocalSttSettings`
primary selection all landed.

### MedASR — RUNTIME-VERIFIED on-device (2026-07-19)
Downloaded the real bundle (SHA-256 re-verified against the pin: `2c20f03…f4c33a`, 154,106,419 B)
and transcribed its own `test_wavs` through the actual engine. `OfflineModelConfig.MedAsr` is
correct at runtime, not merely at compile time. Covered by `MedAsrEngineSmokeTests`, gated on
`RADIOPAD_MEDASR_SMOKE_MODEL_DIR` (see `SttSmokeGate` — `RADIOPAD_STT_SMOKE_REQUIRE=1` turns a
missing model into a hard failure so the smoke job cannot false-green).

**This is what compile-checking could never have caught.** MedASR does not emit spoken punctuation
as words (Parakeet/SAPI style) nor always as punctuation — it emits **its own markup**:

```
[EXAM TYPE] CT chest PE protocol {period} [FINDINGS] {colon} ... right lower lobe {comma} ...
```

- Punctuation markers: `{period}` `{comma}` `{colon}` `{new paragraph}`
- Section tags: `[EXAM TYPE]` `[INDICATION]` `[TECHNIQUE]` `[FINDINGS]` `[IMPRESSION]` `[DIAGNOSES]`
- It **already normalizes numbers** ("54-year-old", "37.2 degrees", "98%"), so §5.2's spoken-number
  pass is largely redundant on this engine — harmless, since it is idempotent.

Untranslated these reach a signed report verbatim; worse, the frontend's spoken-punctuation pass
matches the word *inside* the braces, degrading `{period}` to `{.}`. `MedAsrTranscriptNormalizer`
translates the markup **at the engine boundary**, so §5.2, the formatter, the ROVER ensemble's token
alignment, and the raw-transcript fallback all see plain prose. It is punctuation-only and
content-preserving, hence safe ahead of the §5.2 token lock. Two deliberate choices:

- An **unrecognised marker is left verbatim**, never guessed — a visible `{foo}` is a fail-visible
  anomaly the radiologist catches; a guess would be a silent fabrication.
- **Section tags are rendered as heading lines, not treated as authoritative structure.**
  `test_wavs/5.wav` showed a tag emitted mid-sentence ("The `[DIAGNOSES]` Includes …"), so a hard
  boundary would mangle the sentence. As a heading, legitimate cases read correctly, a spurious one
  is obvious, and no words are lost either way.

The bundle's own radiology sample WAV is now provisioned alongside the model (best-effort, excluded
from `IsMedAsrComplete`): without it `SelfTestAudio` falls back to a synthesized 440 Hz tone, which
MedASR correctly transcribes as nothing — indistinguishable from a broken engine in the manager's
"Test" action.

### Still blocked / deferred — **all cleared as of 2026-07-20**
- ~~**Local MedGemma runtime** — needs the llama-server binary bundled in CI.~~ **Resolved:**
  provisioned on demand (operator's choice over bundling), pinned to llama.cpp `b10068`, started
  lazily by `LlamaServerProcess`; verified end-to-end against the real sidecar.
- ~~**True streaming/chunked on-device decode** (P0.3 remainder).~~ **Resolved and named honestly:**
  neither pinned engine exposes sherpa's streaming `OnlineRecognizer`, so what ships is chunked
  incremental decode with **display-only** previews; the authoritative transcript stays the
  whole-buffer decode. See the 2026-07-19 P0.3 entry in §8.

Nothing in this build is now externally blocked. Remaining items are benchmarks (§7) and the
commercial-model decision (§6), neither of which is engineering work.

> **Correction to an earlier note in this file:** "needs a Tauri build I can't compile-verify" was
> wrong. Rust *is* available locally; the only obstacle was the build script requiring a sidecar
> binary that CI produces, which a gitignored placeholder under `desktop/src-tauri/binaries/`
> satisfies for a local `cargo check`/`cargo test`.

---

## 1. Locked operator decisions (approved plan, 2026-07-18)

| # | Decision | Consequence |
|---|---|---|
| D1 | **Cloud AI stays primary** (Iteration 55 NOT reversed) | Local MedGemma is an **optional, selectable** offline formatter. Hosted `AiGateway`/`ReportingService` stays the default AI path. Safety layers (§5.2/§5.3/§5.6) wrap **whichever** formatter runs. |
| D2 | **MedASR = default primary STT; Parakeet = optional** | User can promote Parakeet to primary via `LocalSttSettings`. Windows SAPI unchanged. Both stay in the ROVER ensemble. |
| D3 | **MedASR deployment: ONNX-export-first, Python-sidecar fallback** | Prototype ONNX/CTranslate2 export → run on the existing ONNX Runtime path (mirrors `SherpaParakeetSttClient`). Fall back to a bundled Python/transformers sidecar only if export loses accuracy/coverage. Both live behind `ILocalSttClient`. |
| D4 | **Streaming push-to-talk from Phase 0** | New streaming/chunked decode path + hold-to-talk PTT + rebindable global hotkey. |
| D5 | **All phases 0→3, commit per phase, minimal pausing** | Phase 3 regulated features ship **OFF by default**. ⚠️ **Superseded 2026-07-19** by a later operator instruction — the default is now **ENABLED** (licensing stated as acquired); per-tenant opt-out remains. See §5. |
| D6 | **§5.7 raw transcript persisted locally + encrypted** | Reverses the current SHA-256-only privacy design, but stays **on-device + encrypted** (never leaves the machine). Additive to the existing server hash-chain. |

## 2. Pinned model specifications (verified against live sources 2026-07-18)

### MedASR — default on-device STT
- **Repo:** `google/medasr` (Hugging Face) · Google Health AI Developer Foundations (HAI-DEF).
- **Arch:** Conformer encoder, **105M params**, v1.0.0, released **2025-12-18**. Weights ~421 MB.
- **Accuracy:** ~4.6% WER on radiology dictation (vs ~10% Gemini 2.5 Pro, ~25% Whisper v3 large).
- **Load pattern (reference):** `AutoModelForCTC` + `AutoProcessor`, **`transformers >= 5.0.0`**
  (PyTorch). ⚠️ **VERIFY the exact minimum `transformers` release at build time** — may need a
  pinned GitHub commit. Record the pinned version here once resolved.
- **Input contract:** mono **16 kHz int16** waveform. (RadioPad's pipeline already standardizes on
  16 kHz mono; `wavEncode.blobToWav16kMono` already emits int16.)
- **Known weaknesses (design around):** non-standard medication names + temporal data (dates/times/
  durations) → exactly why §5.2 deterministic pass-through + §6 correction dictionary are mandatory.
- **License/access:** HAI-DEF terms of use; **accepting model terms on HF may be required to
  download** → surface as a one-time user step in the model-download UI.
- **⏳ OPEN:** ONNX/CT2 export fidelity for this brand-new Conformer is unproven → **prototype first**
  (D3). Record the outcome (ONNX vs Python sidecar) + pinned runtime here.

### MedGemma 1.5 4B — optional local report formatter
- **Source:** Google MedGemma 1.5, 4B multimodal (built on Gemma 3, 128K ctx), updated **2026-01-13**.
- **Runtime:** bundled **llama-server** sidecar (llama.cpp) with a **Q4_K_M GGUF** (~2.5 GB),
  driven by the existing `LlamaCppProvider` (`/completion`) adapter.
- **✅ PINNED (verified via HF public API 2026-07-18 — repo is `gated:false`, anonymously
  downloadable by the provisioner):**
  - Repo/file: `unsloth/medgemma-1.5-4b-it-GGUF` → `medgemma-1.5-4b-it-Q4_K_M.gguf`
  - URL: `https://huggingface.co/unsloth/medgemma-1.5-4b-it-GGUF/resolve/main/medgemma-1.5-4b-it-Q4_K_M.gguf`
  - Size: `2489894976` bytes · SHA-256 (HF LFS oid): `b31becdf4f39561800505514cce67681604fe449d04dd35c8c92fd7848c6d7bd`
  - Registered in `LocalModelCatalog` as an `Orchestrator`-kind `RawFile` descriptor
    (`medgemma-1.5-4b-q4`), download-on-demand (NOT auto-downloaded on first run).
  - Ungated mirror fallback: `mradermacher/medgemma-1.5-4b-it-GGUF` →
    `medgemma-1.5-4b-it.Q4_K_M.gguf` (size `2489894624`, sha256
    `ee5121f1b6ffda000f65bcf14b86a653f1beae2438663381f61980e3cf639454`).
- **Inference:** **temperature ≈ 0** (deterministic formatting). **No native tool/function-calling**
  → structured output enforced via **GBNF grammar** (§5.4), tolerant JSON parse as secondary net.
- **License:** HAI-DEF / Gemma terms (commercial use permitted subject to the acceptable-use policy).
- **Role boundary (§3):** formats dictated text only — MUST NOT invent findings, MUST NOT read
  images. Image-in → findings-out is explicitly out of scope.

## 3. Memory budget (§1 / §4.4) — ≤ 5 GB combined, CPU-only

| Component | Est. |
|---|---|
| OS + desktop shell | ~0.7–1.0 GB |
| MedASR (STT) | ~0.3–0.5 GB |
| MedGemma 1.5 4B (Q4) | ~2.5–2.8 GB |
| Dictionaries + app logic | ~0.1 GB |
| **Peak** | **≈ 3.6–4.3 GB** (under ceiling) |

Load/unload manager (P0.10) enforces the ceiling; "low-memory mode" unloads STT during formatting.
CPU-only is hard-enforced today (`LocalSttModels.ResolveProvider` returns `cpu`; GPU detection
stubbed false). No GPU/VRAM assumed.

## 4. Safety-policy changes recorded here (§5)

- **§5.7 (D6):** the dictation audit persists the **raw transcript + protected transcript + final
  report + diff + template + model versions** in an **encrypted, on-device, append-only** store
  (reusing the ADR-0003 hash-chain design). This is a deliberate change from the current
  privacy-by-SHA-only server design; justified because it stays on-device + encrypted and is
  required for medico-legal reviewability of every AI-applied change.
- All other §5 guardrails are **additive** and never weaken existing safety boundaries: never
  auto-sign; `.ai-mark` until reviewed; PHI only to `PhiApproved`/`LocalOnly` providers (the local
  MedGemma path is the no-PHI-to-cloud option); append-only audit via `IAuditLog.AppendAsync`;
  backend binds `127.0.0.1`; tenant isolation via `TenantedController.ResolveContextAsync`.

## 5. ⚠️ Regulated features (Phase 3) — **gate enforced; default ENABLED by operator instruction**

MedASR and MedGemma are Google **"developer models requiring validation," NOT cleared medical
devices.** The capabilities below may constitute clinical decision support / a medical-device
function (UKCA/MHRA, CE, FDA depending on market).

**Current state (2026-07-19/20).** `RadioPad.Application.Governance.RegulatedFeatures`
(enum + catalog + `IsEnabled`/`Describe`) reads **`TenantSettings.FeatureFlagsJson`** under the
`regulated.` prefix. The default is **ENABLED** — flipped on explicit operator instruction that
UKCA/MHRA/CE/FDA licensing has been acquired. An operator can still switch any capability off per
tenant from **Settings → Regulated AI features**.

Two corrections worth keeping, because the doc previously described a control that was not in force:

- The gate had **zero call sites** until 2026-07-20. It is now enforced at all three
  `ReportsController` entry points (`/ai`, `/ai/jobs`, `/followup-suggestions`), returning
  `403 kind=regulated_feature_disabled`.
- It named **`Tenant.FeatureFlagsJson`**, which does not exist; the flags live on `TenantSettings`.
  Nothing caught it because nothing called it.

Capabilities under the gate:

- Auto-impression draft from dictated findings.
- Actionable/critical-finding flagging (e.g. "?PE", "new mass") + communication workflow.
- Follow-up recommendation standardisation (Fleischner, LI-RADS, TI-RADS, Bosniak) — cited
  suggestion, not auto-applied.
- Interval-change / lesion tracking (RECIST-style) — computed, radiologist confirms.

## 6. Out of scope / flagged for the operator

- **F11 commercial model:** a 100%-local, no-metered-API runtime conflicts with the current Stripe
  per-seat subscription + per-request AI cost metering. This is a **separate business decision**, not
  part of this engineering effort. Flagged, not built.

## 7. Open questions / to resolve at build time

1. ~~Exact `transformers` minimum release for MedASR.~~ **MOOT** — no PyTorch/`transformers` runtime
   is used. MedASR runs on the already-pinned sherpa-onnx v1.13.3 via a public ONNX CTC bundle.
2. ~~MedASR ONNX/CT2 export fidelity vs the Python-sidecar fallback (D3) — blocks P0.2.~~ **MOOT** —
   no export step exists: the maintainer publishes a sherpa-onnx-native export. D3's fallback branch
   was never needed.
3. ~~Exact MedGemma Q4_K_M GGUF artifact (URL + SHA-256 + size) to pin.~~ **RESOLVED** — pinned in §2.
4. ~~CPU-only latency for MedASR + MedGemma formatting (benchmark).~~ **MEASURED for MedASR**
   (2026-07-20, `on-device-latency.yml`): the full smoke suite — model load plus two decodes of the
   bundle's radiology sample — took **53.6 s on 4 CPU-only cores** (AMD EPYC 7763). That is suite
   wall-clock, not per-utterance inference, and one runner is not a workstation; treat it as the
   order of magnitude and as a canary against a silent provider fallback, not as an SLA. The
   workflow publishes the number to its job summary weekly. **MedGemma formatting latency is now
   instrumented (2026-07-20):** `MedGemmaFormatterSmokeTests` times each §4.2 format call
   (`medgemma_format_ms=` marker) and `offline-formatter-smoke.yml` publishes the numbers to its
   job summary weekly — server already warm, so per-call pipeline latency, not model load — and
   fails on >10 min/call. A missing marker fails the job rather than publishing nothing. First
   actual numbers land on that workflow's next run; none observed yet.
5. ~~**llama-server binary bundling in CI.**~~ **RESOLVED** — provisioned on demand instead of
   bundled, pinned to `b10068`; `offline-formatter-smoke.yml` exercises the full §4.2 pipeline.

## 8. Change log

- **2026-07-18** — File created. Phase 0 started. Decisions D1–D6 locked; model specs verified.
- **2026-07-18/19** — Delivered the §5 safety engine + orchestration + memory manager + encrypted
  audit + draft UI; F1/F2/F3/F4/F5/F7a/F7b/F8/F9/F10/F12; Phase 3 gate + admin surface; P0.3
  hold-to-talk PTT + in-app rebindable hotkey. See §0 for the full list and what remains blocked.
  MedGemma GGUF pin resolved (§2). Desktop releases: **v0.1.76** (mid-session) → v0.1.77 (**failed**:
  root `pnpm-lock.yaml` was out of sync with the ESLint devDeps added to `frontend/package.json`;
  desktop-bundle installs `--frozen-lockfile` — no `latest.json` published, so nothing broken
  reached users) → **v0.1.78** (lockfile regenerated + committed, re-cut).
- **2026-07-19** — MedASR **runtime-verified on-device** (not just compiled): pinned SHA-256
  re-confirmed against a real download, and the bundle's own `test_wavs` transcribed through the
  live engine. That run exposed MedASR's `{period}`/`[FINDINGS]` output markup — a bug no
  compile-time check could reach, since it only manifests as literal markers (and `{.}`) in a signed
  report. Fixed by `MedAsrTranscriptNormalizer` at the engine boundary; see §0 for the marker
  vocabulary and the two safety choices behind it. P0.3's rebindable **system-wide** hotkey landed
  (`desktop/src-tauri/src/hotkeys.rs` + `frontend/lib/desktopHotkeys.ts`): overrides previously
  reached only in-page listeners, so a rebound chord kept firing the old accelerator while the
  window was unfocused. Also fixed the cause of a long-standing drift — `pnpm release:desktop` never
  bumped `Cargo.lock`, whose own version entry had read 0.1.29 since that release.
- **2026-07-19 (end-to-end run against the real sidecar)** — Published the .NET sidecar exactly as
  the MSI does and drove the endpoints the desktop calls. This caught two wiring bugs that every
  unit test missed, because each component was correct in isolation and only the wiring was wrong:
  (a) `LocalModelsController.IsDownloaded` had no `MedAsrCtc` case, so MedASR reported
  `downloaded=false` while reporting `available=true` — the manager offered to re-download a
  154 MB bundle already on disk and "Make primary" 409'd, meaning MedASR could never become
  primary (all of D2); (b) `LocalSttEnsemble.PickPrimary` fell back to `engines[0]` — DI
  registration order — whenever the configured primary had no backend engine, which is the normal
  case for the frontend-only Edge engine. Result: dictation ran on Parakeet (41.6 s, empty
  transcript) with MedASR installed and idle. After the fix: `model=medasr`, 14.7 s, full clean
  report. Regression tests guard the invariants (every ArchiveKind needs a completeness check; the
  fallback order is explicit) rather than the instances.
- **llama-server is now provisioned on demand** (operator's choice over bundling; cloud stays
  default per D1) and started lazily by `LlamaServerProcess`. Pinned to llama.cpp `b10068` —
  deliberately a version we bump, never "latest", since llama.cpp releases per merged commit and
  every workstation would otherwise run a different unreviewed build against PHI. Verified by real
  download + hash, and `llama-server.exe` turns out to be a 9 KB launcher stub whose implementation
  is `llama-server-impl.dll` with ~14 dlopened `ggml-cpu-*.dll` backends — so the whole archive is
  extracted. Downloading MedGemma now fetches the runtime too, so one user action yields a working
  feature instead of an inert 2.5 GB file.
- **Unplanned adversarial validation of §5.3/§5.6.** Driving the offline formatter with a
  deliberately weak model (SmolLM2-135M) produced degenerate looping output; §5.3 rejected it and
  fell back to the dictionary-corrected transcript, and §5.6 flagged the added negation cues. The
  safety layers refused a bad formatter rather than putting its output in a draft — the behaviour
  they exist for, observed rather than asserted. §5.2 was visible in the same run: "three point two
  centimeter" → "3.2 cm".
- **2026-07-19 — P0.3 complete (incremental decode).** Named honestly: both engines are sherpa-onnx
  **offline** recognizers (MedASR Conformer-CTC, Parakeet TDT) and neither exposes sherpa's
  streaming `OnlineRecognizer`, so frame-level streaming ASR is not available with the pinned
  models. What ships is chunked incremental decode — `SpeechSegmenter` cuts audio at natural pauses
  (700 ms hold, chosen to ride through the ~200-400 ms gap inside "three point two" rather than cut
  it) and each completed segment is decoded for a live preview.
  **Safety line, enforced by design and stated at both call sites: preview text is DISPLAY-ONLY.**
  A segment boundary can split a spoken measurement and decode to a number nobody said, so the
  authoritative transcript remains the whole-buffer decode taken on push-to-talk release. Desktop
  only — on-device segments are free, whereas on the web each would be a billed cloud transcription
  of audio being uploaded in full anyway.
- **2026-07-19/20 — adversarial audit closed.** 31 findings raised, ~21 still open at the start of
  the pass; every one independently re-verified against HEAD and then re-checked by a second
  reviewer instructed to **refute** it. All confirmed findings fixed; four refuted and deliberately
  untouched. Releases v0.1.80 → **v0.1.85**. Full detail in `PROGRESS.md`; the durable lesson is §9.
  Backend 972 passed / 5 skipped, frontend 464 passed. One backend test failed once in the first
  full run and did not reproduce across three subsequent clean runs — recorded as a known flake,
  **not** as resolved, because I could not identify it.

- **2026-07-20 — the three remaining §8b gaps closed end to end.** (1) **MSI E2E:**
  `desktop-bundle.yml` gained an `msi-e2e` job — installs the actual `.msi`, pre-places the pinned
  MedGemma GGUF + llama-server runtime + MedASR bundle under `%LOCALAPPDATA%\com.radiopad.desktop`
  (cache keys shared with the ubuntu smokes), then `scripts/desktop-msi-e2e.mjs` (dependency-free
  Node 22, raw CDP over `WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS`) drives the INSTALLED renderer:
  UI login including the mandatory TOTP enrollment (code computed from the secret the UI shows),
  report seeding via the token the UI's own login minted, the dictation draft panel with the
  on-device toggle, a real MedGemma format ("Passed the safety validator" required — fallback or
  error fails), `.ai-mark`/"3.2 cm"/"Requires review" assertions, and Apply. The webview talks to
  the bundled sidecar as its data backend (`RADIOPAD_BACKEND` loopback, CSP-allowed); the sidecar
  is pre-started with a bootstrap secret + throwaway SQLite DB and the shell adopts it. `release`
  now depends on `msi-e2e`, so an installer whose UI cannot complete a draft is not published.
  (2) **MedGemma latency instrumented** — §7 item 4. (3) **Flaky-test prime suspect eliminated:**
  the audit found ~20 env-mutating test classes outside any parallel-disabled collection —
  including three files sharing `STRIPE_WEBHOOK_SECRET` with only partial coverage, and
  `"OrgCreationSerial"` having **no CollectionDefinition at all** (members-only serialization) —
  all now serialized, and `EnvSerializationConventionTests` (source-scan + reflection) enforces
  the invariant on every run; verified locally that it fails on a violation and passes at HEAD.
  Deliberately NOT recorded as "flake resolved": the flake was never named.

---

## 8b. Verification gaps closed 2026-07-20 (and what is still open)

Three things had been true all session and none of them were checked:

- **Nobody had run the packaged app.** Every on-device check started a sidecar *we* launched. The
  `desktop-bundle` no-panic launch smoke now goes further: while the shell is alive it polls the
  sidecar's loopback health, asserts the model manager offers MedASR, and asserts `draft-local` does
  not answer 503 — i.e. that **Tauri** spawns a working sidecar from the **real installed binary**.
  Confirmed against a live run that the shell does stay alive on `windows-latest`, so the assertions
  execute; if it ever exits early the step emits a warning that the engines were unverified rather
  than letting a green tick imply cover it did not provide.
- **Latency was unmeasured** → see §7 item 4. MedASR measured; MedGemma still open.
- **The flaky test was never identified.** `flaky-hunt.yml` runs the suite N times and *names* what
  fails. First run: **6/6 passed, flake not reproduced** — so it is now 9 consecutive clean runs
  (3 local + 6 CI) since the single observed failure. Still **not identified**, and deliberately not
  recorded as fixed; the job runs weekly and will name it if it recurs. Prime suspect documented in
  the workflow: an env-mutating test missing `EnvironmentVariableCollection`, which produces exactly
  this once-in-several signature and has bitten this repo before.

**All three closed on 2026-07-20** (see the change-log entry): the `msi-e2e` job installs the
actual MSI and drives the installed renderer end to end over CDP (login incl. mandatory TOTP
enrollment → report → dictation draft panel → real on-device MedGemma format → Apply), and
`release` now depends on it; MedGemma latency is instrumented in `offline-formatter-smoke`; the
flaky-test prime suspect was eliminated by serializing every env-mutating test class, with
`EnvSerializationConventionTests` enforcing the invariant.

Still open, honestly: the `msi-e2e` job and the latency instrumentation are **wired but not yet
observed green** — the next tag push / weekly run is the evidence; the **microphone capture path**
(press mic → MedASR decode through the UI) is still not driven in CI — the E2E seeds dictation as
text, so audio-device injection remains uncovered; and the original flake was never **named**, so
"prime suspect eliminated" is not "flake resolved" — flaky-hunt stays as the watchdog.

## 9. The pattern this build kept producing (read before the next change)

Almost every real defect found in this build was **not a wrong function — it was a correct function
nothing reached**, with green unit tests over the pieces. A partial list from one audit:

- the on-device model manager shipped only to the `(web)` bundle, so the desktop — the only surface
  where MedASR and MedGemma run — had no way to download or select a model;
- `dictationDraftLocal` and `lib/snippetInsert.ts` each had **zero production callers**;
- the Phase 3 gate had zero call sites and named a field on the wrong entity (§5);
- `/compare-prior` answered with field names no client read, so F5 threw on every load;
- `comparison` was serialized into HL7/FHIR exports with no editor ever rendering it;
- the microphone path never applied the correction dictionary the draft panel applied;
- `Report.Priority` — documented "RIS-driven" — was written by no ingest path.

**Why it hides:** unit tests assert the unit. Nothing asserts *"a user can get here."*

**What to do about it.** Before asking whether a change is correct, ask **who calls this and on which
surface**. Concretely: grep for callers of anything added or fixed; check the route group
(`app/(desktop|web|mobile|shared)/`) against the surface that actually uses the feature, because
`scripts/build-surface.mjs` **physically stages the other groups out of `app/`** — a cross-group link
is a hard 404, not a soft one; and verify both sides of a client/server contract by **keys**, since a
typed client cannot defend against a server that answers with different field names.
`frontend/__tests__/crossSurfaceLinks.test.ts` now pins the link case automatically.

**Two process notes earned the hard way.** (1) After writing a test for a bug, **revert the fix and
confirm the test fails** — one audit finding turned out to be a false positive whose test passed
either way. (2) When a port and its original disagree, check which one is wrong before "fixing" the
code: the `applyCorrections` punctuation limitation was correct behaviour and my test was wrong, and
making the frontend correct *more* than the backend would have been the worse outcome.
