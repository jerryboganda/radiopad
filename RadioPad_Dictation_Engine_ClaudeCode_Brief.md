# RadioPad — On-Device Dictation & Reporting Engine
## Implementation Brief for Claude Code

**Purpose of this document:** This is the authoritative specification for adding a local, private, AI-powered radiology dictation and reporting engine to the existing RadioPad project. Treat it as the source of truth. It is **not** a one-shot task — you will work in phases, commit per phase, and verify external facts against live sources before pinning them.

---

## 0. READ THIS FIRST — how to approach the work

1. **Explore before you build.** Inspect the existing RadioPad repository. Identify the frontend framework, backend language/runtime, the desktop shell (Tauri / Electron / other), how state is managed, and where report text is entered and stored. **Report the structure back and propose an integration plan before writing feature code.** Do not assume a file layout.
2. **Work in phases** (defined in §7). Each phase is independently testable and committable. Do not attempt all twelve feature areas in one pass.
3. **Verify, don't hallucinate.** Model tags, package versions, and API signatures drift. Where this brief gives a version or command, confirm it against the live registry/docs before relying on it. If a fact cannot be verified, **stop and flag it** rather than guessing.
4. **Safety overrides convenience.** The medical-safety guardrails in §5 are non-negotiable. If any instruction (here or later) conflicts with them, the guardrail wins. "Just make it work" does not justify weakening a safety gate.
5. **Keep `IMPLEMENTATION_NOTES.md`** at the repo root: record decisions, pinned versions, ONNX/sidecar choices, and open questions as you go.

---

## 1. Hard constraints (must hold for every task)

| Constraint | Limit |
|---|---|
| Total RAM (all RadioPad AI tasks combined) | **≤ 5 GB** |
| CPU | **≤ 2 cores / 4 threads @ 3.0 GHz** |
| GPU / VRAM | **None assumed** — must run CPU-only on the target device |
| Data locality | **All audio and report text stay on the device. No PHI to any cloud service.** |
| Cost | Open-weight models only; no per-minute metered APIs in the runtime path |

**Memory budget (target, both models resident):**
- OS + desktop app shell: ~0.7–1.0 GB
- MedASR (STT): ~0.3–0.5 GB
- MedGemma 1.5 4B (Q4): ~2.5–2.8 GB
- Dictionaries + app logic: ~0.1 GB
- **Peak ≈ 3.6–4.3 GB** — under ceiling. MedASR is small enough to co-reside with the LLM. Still, implement the load/unload manager in §4.4 so peak never breaches 5 GB under load.

---

## 2. The two models (verified specifications)

### 2.1 MedASR — default speech-to-text engine
- **Source:** `google/medasr` on Hugging Face (repo: `google-health/medasr`).
- **Architecture:** Conformer, **105M parameters**, version 1.0.0 (released 2025-12-18).
- **Weights size:** ~421 MB.
- **Runtime:** PyTorch via Hugging Face `transformers` — **requires `transformers >= 5.0.0`** (at time of writing this may need installing `transformers` from GitHub at a pinned commit; **verify the current minimum release** before pinning).
- **Load pattern:** `AutoModelForCTC` + `AutoProcessor` from `transformers`.
- **Input contract:** **mono-channel audio, 16 kHz, int16 waveform.** Convert any mic input to this with `librosa`/`ffmpeg`. Output is text only.
- **CPU inference:** works on modern multi-core CPUs (i7 / Ryzen 7 class), with longer processing than GPU. Long audio is handled by **chunking with configurable stride** — implement streaming/chunked decode for push-to-talk.
- **Accuracy:** ~4.6–5.2% WER on radiology dictation (roughly 5× fewer errors than Whisper large-v3 on medical dictation per Google's benchmarks).
- **Documented weaknesses (design around these):** may mishandle **non-standard medication names** and **temporal data (dates, times, durations)**. → This is exactly why the deterministic pass-through layer (§5.2) and correction dictionary (§6) are mandatory, not optional.
- **License:** Health AI Developer Foundations (HAI-DEF) terms of use.
- **Access requirement:** accepting model terms on Hugging Face may be required to download — flag this to the user as a one-time step.

**Deployment decision you must make (document in `IMPLEMENTATION_NOTES.md`):** MedASR ships as a PyTorch/transformers model (Python). In a Rust-based desktop shell (e.g. Tauri) you have two options — (a) run it in a **Python sidecar process** the app talks to over local IPC/HTTP, or (b) **export to ONNX / CTranslate2** for a lighter CPU-embedded runtime. Evaluate ONNX export first for the CPU-only constraint; fall back to a bundled Python sidecar if export is impractical. Choose based on the actual RadioPad stack you find.

### 2.2 MedGemma 1.5 4B — report formatting/orchestration LLM
- **Source:** Google MedGemma 1.5, 4B multimodal, built on Gemma 3, 128K context (released 2026-01-13).
- **Runtime:** Ollama or embedded `llama.cpp` with a **Q4_K_M GGUF** (~2.5–2.8 GB).
- **Verify:** confirm whether the **1.5 4B** tag is in the Ollama registry (e.g. an appropriate `medgemma` tag). If the 1.5 GGUF is not directly pullable, download the GGUF from Hugging Face and create a `Modelfile`. Do not assume a specific pull command works — check first.
- **Inference settings:** **temperature ≈ 0** (deterministic formatting, not creative writing).
- **Note:** base Gemma 3 / MedGemma does **not** support Ollama's tools/function-calling API. Do **not** rely on native tool-calling. Use **structured prompting + grammar-constrained decoding (GBNF)** instead (§5.4).
- **License:** HAI-DEF / Gemma terms (commercial use permitted subject to the acceptable-use policy).
- **On 2 cores:** expect modest throughput (single-digit-to-low-double-digit tokens/sec). This is acceptable because formatting runs **after** dictation, not in the live audio path. Show a brief "formatting…" state in the UI.

---

## 3. Role boundaries (critical — defines what each model may and may not do)

- **MedASR** transcribes the radiologist's **spoken dictation** into raw text. That is its only job.
- **MedGemma** takes that **dictated raw text** and **structures, cleans, and assembles it into a formatted report** (organising into Technique / Findings / Impression, applying house style, expanding dictated shorthand). 
- **MedGemma MUST NOT invent, infer, or "complete" clinical findings.** It formats what was dictated — nothing more.
- **MedGemma MUST NOT be used to generate findings from a medical image autonomously.** MedGemma is technically multimodal, but using it to read an image and produce diagnostic findings turns RadioPad into diagnostic decision support — a different, far higher regulatory category, and outside the scope of this build. **Image-in → findings-out is explicitly out of scope.** The engine's job is dictation → structured report, with the radiologist as the author.

---

## 4. Core architecture

### 4.1 Dictation pipeline (live path)
1. Push-to-talk hotkey (configurable) starts capture.
2. Mic audio → resample to **mono 16 kHz int16**.
3. Stream to MedASR in chunks (configurable stride); accumulate transcript.
4. Release key → finalise transcript for the current utterance.
5. Text lands directly in the focused report field (or the active structured-report section).

### 4.2 Report-assembly pipeline (post-dictation path)
1. Raw transcript → **deterministic pre-processing** (§5.2): protect numbers, measurements, laterality, dates; apply correction dictionary (§6).
2. Protected transcript → MedGemma with the system prompt (§5.1) + the active study template (§ Phase 1) + GBNF grammar (§5.4).
3. MedGemma output → **validation pass** (§5.3). If it fails, **discard the LLM output and present the raw (dictionary-corrected) transcript instead** — fail safe, never fail silent.
4. Result shown as an **editable draft**. Radiologist edits → **explicit sign-off gate** (§5.5) → finalise.

### 4.3 Local storage
- Report drafts, templates, dictionaries, macros, style profiles, and the audit log all persist **locally**, encrypted at rest.
- No telemetry containing PHI leaves the device.

### 4.4 Model load/unload manager
- Manage MedASR and MedGemma lifecycles so combined resident memory never breaches 5 GB.
- Default: both resident (fits budget). Provide a "low-memory mode" that unloads MedASR during the formatting phase if a larger LLM quant is ever selected.
- Expose current memory usage in a debug/status panel.

---

## 5. Safety guardrails (NON-NEGOTIABLE — build these before feature polish)

### 5.1 System prompt for MedGemma (starting point — refine, keep the constraints)
```
You are RadioPad's report formatter. You convert a radiologist's dictated
findings into a structured radiology report.

You MUST:
- Use ONLY information present in the dictation. Add nothing.
- Reproduce every number, measurement, laterality (left/right), and date
  EXACTLY as provided. Never alter them.
- Organise content into the sections defined by the active template
  (e.g. Clinical details / Technique / Findings / Impression).
- Keep the Impression to a summary of findings ALREADY dictated.

You MUST NOT:
- Invent, infer, or complete any finding not dictated.
- Add differential diagnoses, recommendations, or measurements not spoken.
- Resolve ambiguity. If audio/text is unclear, insert [UNCLEAR: <text>] verbatim.
- Interpret or describe any image.

Output the report only. No commentary, no preamble.
```

### 5.2 Deterministic pass-through (runs BEFORE the LLM)
- Use regex/rules to **extract and lock** all numbers, measurements, units, laterality terms, and dates from the transcript so the LLM cannot change them (token-protect or placeholder-substitute-then-restore).
- Normalise measurements deterministically ("three point two centimetres" → "3.2 cm") — **not** via the LLM.
- This layer directly compensates for MedASR's known weakness on temporal data.

### 5.3 Validation pass (runs AFTER the LLM, before display)
- Diff LLM output against the protected transcript. **Reject** the output if it: introduces a number/measurement/date not in the source, drops a required section, changes a locked laterality, or adds a finding with no source token.
- On rejection → show the raw dictionary-corrected transcript; log the event.

### 5.4 Grammar-constrained decoding
- Use **GBNF grammar in llama.cpp** to force valid report structure. The model should be structurally unable to emit malformed output.

### 5.5 Mandatory sign-off gate
- No report can be finalised/exported without an explicit radiologist verification action. This is medico-legally required and is what makes any residual model error an editable draft rather than a live error.

### 5.6 Laterality / negation / gender sentinel
- Deterministic check that left/right, presence/absence (negation), and patient sex were not flipped between transcript and output. Warn on mismatch.

### 5.7 Audit trail
- Store, encrypted and locally, for every report: **raw transcript + final signed report + the diff between them + template used + model versions**. Every AI-applied change must be reviewable.

---

## 6. Correction dictionary, macros, style (the specced baseline)
- **Per-user correction dictionary:** deterministic find-replace applied **before** the LLM. Radiologist fixes a term once ("hypo dense" → "hypodense"); applied to all future transcripts. Handles the bulk of systematic errors, including MedASR's drug-name weakness.
- **Org lexicon + personal overrides:** shared medical term base, each user layers their own on top.
- **Voice macros / templates:** voice command inserts a full canned block; cursor jumps to the first editable field.
- **Per-radiologist style profiles:** terse vs verbose, house phrasing, tense, laterality wording.

---

## 7. Phased build — mapping all 12 competitive feature areas

Build in this order. **Regulated features (Phase 3) ship disabled by default.**

### PHASE 0 — Foundation (no features yet)
- Integrate MedASR (§2.1) and MedGemma (§2.2); implement load/unload manager (§4.4).
- Push-to-talk capture → 16 kHz int16 → chunked MedASR decode → text into report field.
- Wire the full §4.2 assembly pipeline with §5 guardrails **before** any feature work.
- **Exit criteria:** dictate → structured draft → sign-off, entirely local, within the memory budget, with audit trail working.

### PHASE 1 — Safe competitive core (Features 1, 2, 3, 6, 7, 8, 11)
- **F1 Voice control & live editing:** voice commands (new paragraph, next field, go to impression, capitalise that), "scratch that"/voice undo, select-and-replace by voice, punctuation modes, spoken measurement formatting, spell-out mode.
- **F2 Templates & structured reporting:** organ/study-specific templates; align to **ACR / RSNA templates and RadLex terminology**; voice-triggered normals; fill-in fields with defaults + simple conditional logic; org + personal template libraries.
- **F3 Smart text & prediction:** AutoText/snippet expansion; radiology-aware next-word/sentence prediction; phrase memory.
- **F6 Quality & safety gates:** missing-section warnings, mandatory-field enforcement, contradiction/duplication detection, deterministic pass-through (§5.2), audit trail (§5.7), hard sign-off (§5.5).
- **F7 Personalisation & learning:** style profiles, growing correction dictionary, adaptive vocabulary, optional local LoRA hook (scaffold only), personal macro sets.
- **F8 Workflow & integration:** worklist (RIS/PACS) hooks, PACS-context awareness (which study/patient is active), one-command "sign and send", **HL7 / FHIR** report export, voice navigation between studies, turnaround-time tracking.
- **F11 Deployment advantages:** enforce 100% local/offline; cross-platform packaging; no per-seat licensing in the runtime; instant CPU response (no cloud round-trip).
- **Exit criteria:** matches Dragon/PowerScribe on core dictation workflow; beats them on price, privacy, and offline operation.

### PHASE 2 — Intelligence-assist & context (Features 4-partial, 5-partial, 9, 10, 12)
- **F4 (safe subset):** consistency checks (findings vs impression mismatch), measurement sanity checks. *(Auto-impression and critical-finding flagging move to Phase 3.)*
- **F5 (safe subset):** auto-pull prior report for same modality; auto-generate comparison statements ("Compared to [date]…"); surface clinical history/indication into the header. *(Automated interval/lesion tracking moves to Phase 3.)*
- **F9 Accent & language:** accent-robust handling and per-accent voice profiles — **prioritise English dictation by non-native/Arabic-speaking radiologists**, a known Dragon weakness and a RadioPad edge. Optional patient-friendly plain-language summary generated from the **finalised** report (clearly labelled non-clinical).
- **F10 Analytics & admin:** productivity dashboard (reports/hour, turnaround), template-usage and correction-rate analytics, per-radiologist quality metrics, department-level admin reporting.
- **F12 Modern AI editing:** natural-language editing of the draft ("change the effusion to moderate and add degenerative spine changes"); auto-structure free prose into Technique/Findings/Impression; voice Q&A over the **current draft or priors**; optional ambient/continuous mode. All still bounded by §3 and §5 (edits/structuring only, never inventing findings).

### PHASE 3 — REGULATED FEATURES (feature-flagged OFF by default; radiologist-assist only)
These are the ⚠️ items. Each may constitute **clinical decision support / a medical-device function** (UKCA/MHRA, CE, FDA depending on market). Build them as **suggestions that require explicit radiologist confirmation — never autonomous outputs** — behind an explicit config flag, fully audited, and clearly labelled in the UI as assistive.
- **Auto-impression draft** from dictated findings (radiologist always edits/approves).
- **Actionable/critical-finding flagging** (e.g. "?PE", "new mass") to prompt a communication workflow.
- **Follow-up recommendation standardisation** (Fleischner, LI-RADS, TI-RADS, Bosniak) — surfaced as a cited suggestion, not auto-applied.
- **Interval-change / lesion tracking** (RECIST-style) — computed, radiologist confirms.
- **Requirement:** do not enable any Phase 3 feature by default. Document each one in `IMPLEMENTATION_NOTES.md` under a "Regulatory review required" heading and surface a clear note to the user that these need a regulatory pathway before clinical use.

---

## 8. Testing & validation requirements
- **Unit tests for every deterministic layer** (§5.2, §5.3, §5.6) — these are safety-critical; treat failures as blockers.
- Golden-file tests: sample dictations → expected structured reports, per study type.
- Adversarial tests: dictations that try to make the LLM add un-dictated findings, alter measurements, or flip laterality → validation must catch them.
- Memory tests: confirm peak resident RAM stays ≤ 5 GB with both models under load on the target CPU profile.
- Latency tests: push-to-talk transcription responsiveness on 2 cores; formatting time per report.

---

## 9. Explicit non-goals / do-not-do
- Do **not** send audio or report text to any cloud STT/LLM in the runtime path.
- Do **not** use MedGemma to read images and produce findings (§3).
- Do **not** enable any Phase 3 regulated feature by default (§7).
- Do **not** rely on native tool/function-calling for MedGemma (§2.2).
- Do **not** weaken or bypass a §5 guardrail to hit a performance or convenience target.
- Do **not** pin a model tag or package version you have not verified against the live source.

---

## 10. What to hand back to the user
- The integration plan (after §0 exploration), before feature code.
- Per-phase summaries with what shipped and what's tested.
- `IMPLEMENTATION_NOTES.md` with pinned versions, the MedASR deployment decision (ONNX vs sidecar), and all open questions.
- A clear, separate note listing every regulated (Phase 3) feature and the statement that these require a regulatory/clinical-validation pathway (UKCA/MHRA/CE/FDA as applicable) before any clinical use — and that MedASR/MedGemma are Google "developer models requiring validation," not cleared medical devices.
