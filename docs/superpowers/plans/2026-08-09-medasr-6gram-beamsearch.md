# MedASR 6-Gram Language Model & Beam Search Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Integrate Google's 6-gram SentencePiece Language Model and modified beam search decoding (beam size 8) into the on-device MedASR STT engine, making it provisioned and fully manageable via the AI Models UI.

**Architecture:** Extend `LocalSttModels` and `SttModelProvisioner` to handle the 6-gram LM artifact alongside `model.int8.onnx` and `tokens.txt`. Configure `SherpaMedAsrSttClient` recognizer to attach the LM and set modified beam search with beam size 8.

**Tech Stack:** C#, ASP.NET Core 8, sherpa-onnx, xUnit.

## Global Constraints
- Target Framework: `net8.0`
- Target Project: `RadioPad.Infrastructure` & `RadioPad.Api`
- Model Directory: `%LOCALAPPDATA%\com.radiopad.desktop\models\medasr-ctc-en-int8\`

---

### Task 1: Update Model Catalog and Provisioner for 6-Gram LM Artifact

**Files:**
- Modify: `backend/RadioPad.Api/src/RadioPad.Infrastructure/Providers/Local/LocalSttModels.cs`
- Modify: `backend/RadioPad.Api/src/RadioPad.Infrastructure/Providers/Local/SttModelProvisioner.cs`
- Modify: `backend/RadioPad.Api/src/RadioPad.Infrastructure/Providers/Local/LocalModelCatalog.cs`
- Test: `backend/RadioPad.Api/tests/RadioPad.Api.Tests/Stt/SttModelProvisionerTests.cs`

**Interfaces:**
- Consumes: `LocalSttModels.FileSpec`
- Produces: `LocalSttModels.ResolveMedAsrFiles(dir)` returning `(model, tokens, lm)`

- [ ] **Step 1: Write failing unit test for 3-file MedASR resolution**

In `backend/RadioPad.Api/tests/RadioPad.Api.Tests/Stt/SttModelProvisionerTests.cs`:
```csharp
[Fact]
public void ResolveMedAsrFiles_Returns_Model_Tokens_And_Lm()
{
    var dir = Path.Combine(Path.GetTempPath(), "test-medasr-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(dir);
    try
    {
        File.WriteAllText(Path.Combine(dir, "model.int8.onnx"), "dummy");
        File.WriteAllText(Path.Combine(dir, "tokens.txt"), "dummy");
        File.WriteAllText(Path.Combine(dir, "lm_6gram.fst"), "dummy");

        var (m, t, lm) = LocalSttModels.ResolveMedAsrFiles(dir);
        Assert.NotNull(m);
        Assert.NotNull(t);
        Assert.NotNull(lm);
    }
    finally
    {
        if (Directory.Exists(dir)) Directory.Delete(dir, true);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test backend/RadioPad.Api/tests/RadioPad.Api.Tests/RadioPad.Api.Tests.csproj --filter "ResolveMedAsrFiles_Returns_Model_Tokens_And_Lm"`
Expected: FAIL (method currently returns tuple of 2 elements).

- [ ] **Step 3: Update LocalSttModels and SttModelProvisioner**

In `LocalSttModels.cs`:
1. Add `MedAsr6GramLm` `FileSpec`:
```csharp
public static readonly FileSpec MedAsr6GramLm = new(
    Name: MedAsrModelName,
    FileName: "lm_6gram.fst",
    Url: "https://huggingface.co/csukuangfj/sherpa-onnx-medasr-ctc-en-int8-2025-12-25/resolve/main/lm_6gram.fst",
    SizeBytes: 52428800L,
    Sha256: "");
```
2. Update `ResolveMedAsrFiles`:
```csharp
public static (string? model, string? tokens, string? lm) ResolveMedAsrFiles(string? dir)
{
    if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir))
        return (null, null, null);
    string? Pick(string fileName)
    {
        var f = Directory.GetFiles(dir, fileName, SearchOption.AllDirectories).FirstOrDefault();
        return f;
    }
    var m = Pick(MedAsrModel.FileName);
    var t = Pick(MedAsrTokens.FileName);
    var lm = Pick(MedAsr6GramLm.FileName) ?? Pick("bpe.vocab") ?? Directory.GetFiles(dir, "*.fst", SearchOption.AllDirectories).FirstOrDefault();
    return (m, t, lm);
}
```
3. Update `IsMedAsrComplete`:
```csharp
public static bool IsMedAsrComplete(string? dir)
{
    var (m, t, lm) = ResolveMedAsrFiles(dir);
    return m is not null && t is not null;
}
```

In `SttModelProvisioner.cs`:
Update `EnsureMedAsrAsync` to include `EnsureFileInDirAsync(LocalSttModels.MedAsr6GramLm, dir, ct)`.

In `LocalModelCatalog.cs`:
Update MedASR descriptor `SizeBytes` to `206535219L` and update `Note` to explicitly mention 6-gram LM beam search.

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test backend/RadioPad.Api/tests/RadioPad.Api.Tests/RadioPad.Api.Tests.csproj --filter "ResolveMedAsrFiles_Returns_Model_Tokens_And_Lm"`
Expected: PASS

- [ ] **Step 5: Commit changes**

```bash
git add backend/RadioPad.Api/src/RadioPad.Infrastructure/Providers/Local/
git add backend/RadioPad.Api/tests/RadioPad.Api.Tests/Stt/
git commit -m "feat(stt): add MedASR 6-gram LM artifact resolution and provisioning"
```

---

### Task 2: Configure SherpaMedAsrSttClient for Modified Beam Search & 6-Gram LM

**Files:**
- Modify: `backend/RadioPad.Api/src/RadioPad.Infrastructure/Providers/Local/SherpaMedAsrSttClient.cs`
- Modify: `backend/RadioPad.Api/src/RadioPad.Infrastructure/Providers/Local/LocalSttModels.cs`
- Test: `backend/RadioPad.Api/tests/RadioPad.Api.Tests/Stt/SherpaMedAsrSttClientTests.cs`

**Interfaces:**
- Consumes: `LocalSttModels.ResolveMedAsrFiles`
- Produces: `SherpaMedAsrSttClient` recognizer with `MaxActivePaths = 8` and attached LM

- [ ] **Step 1: Write test verifying recognizer configuration parameters**

In `backend/RadioPad.Api/tests/RadioPad.Api.Tests/Stt/SherpaMedAsrSttClientTests.cs`:
```csharp
[Fact]
public void ResolveThreads_And_BeamSize_Return_Expected_Defaults()
{
    Assert.Equal(8, LocalSttModels.ResolveMaxActivePaths());
    Assert.Equal("modified_beam_search", LocalSttModels.ResolveDecodingMethod());
}
```

- [ ] **Step 2: Run test to verify default beam size**

Run: `dotnet test backend/RadioPad.Api/tests/RadioPad.Api.Tests/RadioPad.Api.Tests.csproj --filter "ResolveThreads_And_BeamSize_Return_Expected_Defaults"`

- [ ] **Step 3: Update SherpaMedAsrSttClient.cs recognizer configuration**

In `SherpaMedAsrSttClient.cs`:
Update `EnsureFilesResolved()` and `GetRecognizer()`:
```csharp
private string? _model;
private string? _tokens;
private string? _lm;

private void EnsureFilesResolved()
{
    if (_model is not null && _tokens is not null) return;
    lock (_gate)
    {
        if (_modelDir is null) return;
        var (model, tokens, lm) = LocalSttModels.ResolveMedAsrFiles(_modelDir);
        _model = model;
        _tokens = tokens;
        _lm = lm;
    }
}

private OfflineRecognizer GetRecognizer()
{
    if (_recognizer is not null) return _recognizer;
    lock (_gate)
    {
        if (_recognizer is not null) return _recognizer;

        var config = new OfflineRecognizerConfig();
        config.FeatConfig.SampleRate = SampleRate;
        config.FeatConfig.FeatureDim = FeatureDim;
        config.ModelConfig.MedAsr.Model = _model!;
        config.ModelConfig.Tokens = _tokens!;
        config.ModelConfig.NumThreads = LocalSttModels.ResolveThreads();
        config.ModelConfig.Provider = LocalSttModels.ResolveProvider();
        config.DecodingMethod = LocalSttModels.ResolveDecodingMethod();
        config.MaxActivePaths = LocalSttModels.ResolveMaxActivePaths(); // 8

        if (!string.IsNullOrEmpty(_lm) && File.Exists(_lm))
        {
            config.LmConfig.Model = _lm;
            config.LmConfig.Scale = 0.5f;
        }

        _log.LogInformation(
            "Loading MedASR CTC model with 6-gram LM from {Dir} ({Threads} threads, beam size {BeamSize}, method {Method})",
            _modelDir, config.ModelConfig.NumThreads, config.MaxActivePaths, config.DecodingMethod);

        _recognizer = new OfflineRecognizer(config);
        return _recognizer;
    }
}
```

- [ ] **Step 4: Run full STT test suite to verify no regressions**

Run: `dotnet test backend/RadioPad.Api/tests/RadioPad.Api.Tests/RadioPad.Api.Tests.csproj --filter "FullyQualifiedName~Stt"`
Expected: ALL PASS

- [ ] **Step 5: Commit changes**

```bash
git add backend/RadioPad.Api/src/RadioPad.Infrastructure/Providers/Local/
git add backend/RadioPad.Api/tests/RadioPad.Api.Tests/
git commit -m "feat(stt): configure MedASR with modified beam search (beam=8) and 6-gram LM"
```
