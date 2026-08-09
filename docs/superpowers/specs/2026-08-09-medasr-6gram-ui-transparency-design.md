# Google MedASR 6-Gram Language Model UI Transparency Design

## 1. Context & Motivation
The MedASR on-device STT model bundle (`medasr-ctc-en-int8`) includes 3 raw files:
1. `model.int8.onnx` (~154 MB) - Conformer-CTC acoustic model
2. `lm_6gram.fst` (~52.4 MB) - 6-gram SentencePiece Language Model
3. `tokens.txt` (~4.7 KB) - Token vocabulary

Users inspecting `/settings/models` see a single model card displaying `197 MB` or `206 MB`. To ensure 100% transparency that the 6-gram Language Model is included, downloaded, and active, the frontend model card in `OnDeviceModels.tsx` is updated to explicitly display the 6-gram LM badge and itemize the bundle contents.

---

## 2. Design Specification (`OnDeviceModels.tsx`)

### 2.1 Badges
For `medasr-ctc-en-int8`, render a dedicated badge:
- `6-Gram LM Included`

### 2.2 Bundle Contents Disclosure List
Render a sub-section inside the MedASR card:
- 🧠 **Acoustic Model**: Conformer-CTC ~105M (`model.int8.onnx` ~154 MB)
- 🎯 **Language Model**: 6-Gram LM Beam Search (`lm_6gram.fst` ~52.4 MB)
- 🔤 **Vocabulary**: Token table (`tokens.txt` ~4.7 KB)

### 2.3 Download Action Button
Label the action button:
- **"Download MedASR + 6-Gram LM Bundle (~206 MB)"**
