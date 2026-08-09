# 6-Gram LM UI Transparency Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Enhance the MedASR card UI in `OnDeviceModels.tsx` to explicitly disclose and itemize the 6-gram LM model bundle contents so it is 100% obvious in the UI.

**Architecture:** Update `OnDeviceModels.tsx` to add a 6-gram LM badge, an itemized bundle contents disclosure list, and explicit button labeling.

**Tech Stack:** React 19, TypeScript, Next.js, Lucide Icons.

## Global Constraints
- Target File: `frontend/components/models/OnDeviceModels.tsx`
- Framework: Next.js + React

---

### Task 1: Update OnDeviceModels Card UI for 6-Gram LM Disclosure

**Files:**
- Modify: `frontend/components/models/OnDeviceModels.tsx`

- [ ] **Step 1: Inspect OnDeviceModels ModelCard rendering**

View lines 230-310 of `frontend/components/models/OnDeviceModels.tsx`.

- [ ] **Step 2: Add 6-Gram LM Badge and Bundle Itemization**

In `frontend/components/models/OnDeviceModels.tsx`:
Add badge `6-Gram LM Included` when `model.id === 'medasr-ctc-en-int8'`.
Add itemized bundle contents list for `medasr-ctc-en-int8`:
- 🧠 Acoustic Model: Conformer-CTC ~105M (`model.int8.onnx` ~154 MB)
- 🎯 Language Model: 6-Gram LM Beam Search (`lm_6gram.fst` ~52.4 MB)
- 🔤 Vocabulary: Token table (`tokens.txt` ~4.7 KB)

- [ ] **Step 3: Update Download Button Labeling**

When `model.id === 'medasr-ctc-en-int8'`, update button text to:
`Download MedASR + 6-Gram LM Bundle (~206 MB)`

- [ ] **Step 4: Verify typecheck & lint**

Run: `pnpm --filter @radiopad/frontend typecheck`
Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add frontend/components/models/OnDeviceModels.tsx
git commit -m "feat(ui): add explicit 6-gram LM badge and bundle itemization to MedASR card"
```
