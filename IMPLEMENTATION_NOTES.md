# IMPLEMENTATION_NOTES.md — On-Device Dictation & Reporting Engine

> Living decision/version log mandated by `RadioPad_Dictation_Engine_ClaudeCode_Brief.md`
> §0.5/§10. Records pinned model versions, the MedASR deployment decision, safety-policy
> changes, and open questions. Updated every phase.

**Started:** 2026-07-18 · **Owner:** dictation-engine build · **Status:** Phase 0 in progress

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
- **Runtime:** bundled **llama-server** sidecar (llama.cpp) with a **Q4_K_M GGUF** (~2.5–2.8 GB),
  driven by the existing `LlamaCppProvider` (`/completion`) adapter. Community Q4_K_M GGUFs exist on
  HF (`unsloth/medgemma-1.5-4b-it-GGUF`, `mradermacher/...`) and Ollama.
- **⏳ OPEN:** confirm the exact GGUF artifact (URL + SHA-256 + size) to pin in `LocalModelCatalog`
  before shipping. Record here.
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

## 5. ⚠️ Regulatory review required (Phase 3 — ships OFF by default)

MedASR and MedGemma are Google **"developer models requiring validation," NOT cleared medical
devices.** The following may constitute clinical decision support / a medical-device function
(UKCA/MHRA, CE, FDA depending on market) and **require a regulatory/clinical-validation pathway
before any clinical use.** Each ships behind an explicit OFF-by-default feature flag, fully audited,
UI-labelled as assistive, and requiring explicit radiologist confirmation:

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

1. Exact `transformers` minimum release for MedASR (and whether a pinned GitHub commit is needed).
2. MedASR ONNX/CT2 export fidelity vs the Python-sidecar fallback (D3) — prototype outcome.
3. Exact MedGemma Q4_K_M GGUF artifact (URL + SHA-256 + size) to pin.
4. CPU-only latency on the 2-core target for MedASR streaming + MedGemma formatting (benchmark).

## 8. Change log

- **2026-07-18** — File created. Phase 0 started. Decisions D1–D6 locked; model specs verified.
