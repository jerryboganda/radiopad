# Google MedASR 6-Gram Language Model & Beam Search Integration Design

## 1. Context & Motivation
Google's MedASR paper (*"MedASR: A 105M-parameter Conformer-CTC Model for Medical Dictation"*) demonstrates that pairing the 105M-parameter acoustic model with a **6-gram SentencePiece Language Model (LM)** evaluated using **beam search decoding (beam size = 8)** drops Word Error Rate (WER) to ~4.4–4.6% across medical specialties compared to greedy decoding or general-purpose models.

This design document specifies adding the 6-gram LM artifact and modified beam search decoding configuration to RadioPad's on-device STT engine (`SherpaMedAsrSttClient.cs`), making it fully provisioned, verified, and manageable from the **AI Models** UI (`LocalModelsController.cs`).

---

## 2. Pinned Catalog & Provisioning Specification (`LocalSttModels.cs` & `SttModelProvisioner.cs`)

### 2.1 Pinned Artifacts
The `medasr-ctc-en-int8` model bundle catalog entry in `LocalSttModels.cs` is expanded from 2 raw files to 3 files:
1. `model.int8.onnx` (~154 MB) — Conformer-CTC acoustic model weights.
2. `tokens.txt` (~4.7 KB) — Vocabulary token table.
3. `lm_6gram.fst` / `lm_6gram.onnx` / `bpe.vocab` (~50-80 MB) — 6-gram SentencePiece language model.

```csharp
public static readonly FileSpec MedAsr6GramLm = new(
    Name: MedAsrModelName,
    FileName: "lm_6gram.fst",
    Url: "https://huggingface.co/csukuangfj/sherpa-onnx-medasr-ctc-en-int8-2025-12-25/resolve/main/lm_6gram.fst",
    SizeBytes: 52428800L,
    Sha256: "");
```

### 2.2 Provisioner Updates
- `LocalSttModels.IsMedAsrComplete(string dir)` returns `true` only when `model.int8.onnx`, `tokens.txt`, and the 6-gram LM file are verified present.
- `SttModelProvisioner.EnsureMedAsrAsync` provisions `MedAsrModel`, `MedAsrTokens`, and `MedAsr6GramLm` into `%LOCALAPPDATA%\com.radiopad.desktop\models\medasr-ctc-en-int8\`.

---

## 3. On-Device STT Engine Specification (`SherpaMedAsrSttClient.cs`)

### 3.1 Beam Search & LM Recognizer Tuning
In `SherpaMedAsrSttClient.cs`, `GetRecognizer()` is updated to configure:
- `config.DecodingMethod`: `"modified_beam_search"`
- `config.MaxActivePaths`: `8` (beam size = 8 as recommended by Google)
- `config.LmConfig.Model`: Path to resolved `lm_6gram.fst`
- `config.LmConfig.Scale`: `0.5f`

```csharp
var (model, tokens, lm) = LocalSttModels.ResolveMedAsrFiles(_modelDir);
config.ModelConfig.MedAsr.Model = model!;
config.ModelConfig.Tokens = tokens!;
config.DecodingMethod = LocalSttModels.ResolveDecodingMethod(); // "modified_beam_search"
config.MaxActivePaths = 8; // Beam size = 8
if (lm is not null)
{
    config.LmConfig.Model = lm;
    config.LmConfig.Scale = 0.5f;
}
```

---

## 4. AI Models UI & Management API (`LocalModelsController.cs`)
- Update `LocalModelCatalog.cs` entry for `medasr-ctc-en-int8` to report updated combined size (~210 MB) and description noting the 6-gram LM beam search.
- The UI modal at `/api/local-models` maintains complete management (Download, Force Re-download, Delete, Test, Primary selection).
