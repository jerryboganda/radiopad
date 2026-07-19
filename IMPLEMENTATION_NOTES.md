# IMPLEMENTATION_NOTES.md — On-Device Dictation & Reporting Engine

> Living decision/version log mandated by `RadioPad_Dictation_Engine_ClaudeCode_Brief.md`
> §0.5/§10. Records pinned model versions, the MedASR deployment decision, safety-policy
> changes, and open questions. Updated every phase.

**Started:** 2026-07-18 · **Owner:** dictation-engine build · **Status:** Phases 0–3 substantially
delivered; remaining work is externally blocked (model artifacts, Rust build) or a noted follow-up.
Last desktop release: **v0.1.78** (auto-updater).

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
  trigger before the caret + tab-cycles fields). Complete.
- **F4** measurement-sanity + findings/impression consistency (deterministic).
- **F5** auto-comparison statement (deterministic; inserts into Comparison).
- **F7a/F7b** org + per-user correction dictionaries (backend + management UI).
- **F8** one-command Sign & Send · RIS-driven report priority.
- **F9** patient-friendly summary (pre-existing, verified).
- **F10** reports/hour KPI (+ AnalyticsService's first tests); template-usage pre-existing.
- **F12** free-text custom rewrite, **hard-guarded by §5.3** so it cannot introduce an un-dictated
  measurement/number/date; violations surfaced as an amber "Requires review" flag before Accept.
- **P0.3 (partial)** hold-to-talk PTT (alongside tap-toggle) + in-app rebindable dictation hotkey
  (all surfaces). System-wide/unfocused Rust rebind + streaming on-device decode still pending.
- **Phase 3 gating** — `RegulatedFeatures` gate (OFF by default, fail-safe) + admin
  "Regulated AI features" surface. See §5 below (now implemented, not just planned).

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

### Still blocked / deferred
- **Local MedGemma runtime** — pin is verified (§2), but actually running it needs the
  **llama-server binary bundled in CI**.
- **True streaming/chunked on-device decode** (P0.3 remainder) — the PTT/hotkey half is done.

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
| D5 | **All phases 0→3, commit per phase, minimal pausing** | Phase 3 regulated features ship **OFF by default**. |
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

## 5. ⚠️ Regulatory review required (Phase 3 — ships OFF by default) — **GATE IMPLEMENTED**

MedASR and MedGemma are Google **"developer models requiring validation," NOT cleared medical
devices.** The following may constitute clinical decision support / a medical-device function
(UKCA/MHRA, CE, FDA depending on market) and **require a regulatory/clinical-validation pathway
before any clinical use.** The **gate is now built**: `RadioPad.Application.Governance.RegulatedFeatures`
(enum + catalog + `IsEnabled`/`Describe`) reads `Tenant.FeatureFlagsJson` under the `regulated.`
prefix — absent/false/malformed → **OFF** (fail safe) — and the tenant **Settings → Regulated AI
features** admin panel surfaces the "Regulatory review required" note + per-feature toggles. Each
capability ships OFF by default, and its runtime behaviour must consult `IsEnabled` before acting:

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

1. Exact `transformers` minimum release for MedASR (and whether a pinned GitHub commit is needed). **OPEN.**
2. MedASR ONNX/CT2 export fidelity vs the Python-sidecar fallback (D3) — prototype outcome. **OPEN** (blocks P0.2).
3. ~~Exact MedGemma Q4_K_M GGUF artifact (URL + SHA-256 + size) to pin.~~ **RESOLVED** — pinned in §2.
4. CPU-only latency on the 2-core target for MedASR streaming + MedGemma formatting (benchmark). **OPEN.**
5. **llama-server binary bundling in CI** — required for the local MedGemma path to actually run. **OPEN.**

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
