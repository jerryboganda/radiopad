namespace RadioPad.Infrastructure.Providers.Local;

/// <summary>
/// Pinned catalog + path resolution for the on-device STT model, shared by the
/// engine (<see cref="SherpaParakeetSttClient"/>) and the first-run provisioner
/// (<see cref="SttModelProvisioner"/>) so they always agree on the model name,
/// download source, and on-disk location.
/// </summary>
public static class LocalSttModels
{
    public const string DefaultModelName = "parakeet-tdt-0.6b-v3";

    /// <summary>
    /// NVIDIA Parakeet-TDT-0.6B-v3 INT8, packaged for sherpa-onnx (~465 MB).
    /// CC-BY-4.0 weights (attribution required in the app About/notices).
    /// <see cref="ModelSpec.Sha256"/> is the GitHub release asset digest.
    /// </summary>
    public static readonly ModelSpec Parakeet = new(
        Name: DefaultModelName,
        Url: "https://github.com/k2-fsa/sherpa-onnx/releases/download/asr-models/sherpa-onnx-nemo-parakeet-tdt-0.6b-v3-int8.tar.bz2",
        SizeBytes: 487170055L,
        Sha256: "5793d0fd397c5778d2cf2126994d58e9d56b1be7c04d13c7a15bb1b4eafb16bf");

    public sealed record ModelSpec(string Name, string Url, long SizeBytes, string Sha256);

    public const string WhisperModelName = "whisper-large-v3-turbo-q5_0";

    /// <summary>English-only whisper.cpp small model — the low-latency option.</summary>
    public const string WhisperSmallEnModelName = "whisper-small.en-q5_1";

    /// <summary>
    /// OpenAI Whisper large-v3-turbo q5_0 GGML for whisper.cpp / Whisper.net
    /// (~547 MB). MIT (weights + whisper.cpp). SHA-256 from the HuggingFace LFS
    /// pointer. The architecturally-decorrelated second engine for the ensemble.
    /// </summary>
    public static readonly FileSpec Whisper = new(
        Name: WhisperModelName,
        FileName: "ggml-large-v3-turbo-q5_0.bin",
        Url: "https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-large-v3-turbo-q5_0.bin",
        SizeBytes: 574041195L,
        Sha256: "394221709cd5ad1f40c46e6031ca61bce88931e6e088c188294c6d5a55ffa7e2");

    /// <summary>
    /// OpenAI Whisper small.en q5_1 GGML (~181 MB). MIT. English-only and much
    /// faster than turbo — the low-latency choice for the primary dictation engine.
    /// SHA-256 from the HuggingFace LFS pointer.
    /// </summary>
    public static readonly FileSpec WhisperSmallEn = new(
        Name: WhisperSmallEnModelName,
        FileName: "ggml-small.en-q5_1.bin",
        Url: "https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-small.en-q5_1.bin",
        SizeBytes: 190098681L,
        Sha256: "bfdff4894dcb76bbf647d56263ea2a96645423f1669176f4844a1bf8e478ad30");

    /// <summary>A single downloadable model file (no archive extraction).</summary>
    public sealed record FileSpec(string Name, string FileName, string Url, long SizeBytes, string Sha256);

    // ── Tuning knobs (env-overridable; safe defaults) ──────────────────────
    // All default to the *optimized* setting so a stock desktop build benefits
    // without configuration; env vars exist for per-site tuning + CI coverage.

    /// <summary>Parakeet decoding. Beam search beats greedy on WER and is the
    /// prerequisite for hotword biasing. Override: <c>RADIOPAD_STT_DECODING</c>.</summary>
    public const string DefaultDecodingMethod = "modified_beam_search";

    /// <summary>Default radiology priming prompt for Whisper (biases domain
    /// vocabulary). Override: <c>RADIOPAD_STT_WHISPER_PROMPT</c>.</summary>
    public const string DefaultWhisperPrompt =
        "Radiology report dictation. Findings: lungs, pleura, mediastinum, " +
        "cardiac silhouette. Impression:";

    public static string ResolveDecodingMethod()
        => Env("RADIOPAD_STT_DECODING") is { Length: > 0 } v ? v : DefaultDecodingMethod;

    /// <summary>
    /// Execution provider for the on-device engines. PROJECT RULE: inference runs
    /// on the CPU and SYSTEM RAM — never the GPU/VRAM by manual choice. There is
    /// deliberately NO env switch (no manual switching). A discrete GPU is used
    /// ONLY when it is auto-detected AND a GPU-capable native runtime is actually
    /// present; otherwise CPU. The shipped sherpa-onnx + Whisper runtimes are CPU
    /// builds, so this resolves to "cpu" today — the detection seam keeps any
    /// future elevation fully automatic (still no manual switch).
    /// </summary>
    public static string ResolveProvider()
        => GpuAccelerationAvailable() ? "cuda" : "cpu";

    /// <summary>
    /// Automatic, no-manual-override capability check. True ONLY when a GPU-capable
    /// native runtime is present AND a discrete GPU with dedicated VRAM is detected.
    /// The current CPU-only build ships no GPU runtime, so this is false and
    /// inference stays on CPU + system RAM per the project rule. At most an
    /// integrated Intel GPU may appear; it is never required.
    /// </summary>
    private static bool GpuAccelerationAvailable() => false;

    /// <summary>Decode threads, leaving headroom on the clinical workstation and
    /// capping so a many-core box doesn't oversubscribe on a single utterance.</summary>
    public static int ResolveThreads()
        => int.TryParse(Env("RADIOPAD_STT_THREADS"), out var n) && n > 0
            ? n
            : Math.Clamp(Environment.ProcessorCount - 1, 1, 4);

    public static int ResolveMaxActivePaths()
        => int.TryParse(Env("RADIOPAD_STT_MAX_ACTIVE_PATHS"), out var n) && n > 0 ? n : 4;

    public static int ResolveWhisperBeamSize()
        => int.TryParse(Env("RADIOPAD_STT_WHISPER_BEAM"), out var n) && n >= 1 ? n : 5;

    public static string? ResolveWhisperPrompt()
        => Env("RADIOPAD_STT_WHISPER_PROMPT") is { Length: > 0 } v ? v : DefaultWhisperPrompt;

    /// <summary>Explicit hotwords file (one phrase per line); null when unset/missing.</summary>
    public static string? ResolveHotwordsFile()
        => Env("RADIOPAD_STT_HOTWORDS_FILE") is { Length: > 0 } v && File.Exists(v) ? v : null;

    public static float ResolveHotwordsScore()
        => float.TryParse(Env("RADIOPAD_STT_HOTWORDS_SCORE"),
               System.Globalization.NumberStyles.Float,
               System.Globalization.CultureInfo.InvariantCulture, out var s) && s > 0
            ? s : 1.5f;

    /// <summary>Locate a SentencePiece BPE vocab (needed for hotword biasing on a
    /// BPE transducer). Null when the bundle doesn't ship one — hotwords then
    /// degrade gracefully to plain beam search.</summary>
    public static string? ResolveBpeVocab(string dir)
    {
        if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return null;
        foreach (var pat in new[] { "bpe.vocab", "*.bpe.vocab", "*.vocab" })
        {
            var f = Directory.GetFiles(dir, pat, SearchOption.AllDirectories).FirstOrDefault();
            if (f is not null) return f;
        }
        return null;
    }

    private static string? Env(string name)
    {
        var v = Environment.GetEnvironmentVariable(name);
        return string.IsNullOrWhiteSpace(v) ? null : v.Trim();
    }

    /// <summary>True when the desktop build has opted the on-device engine in
    /// via <c>RADIOPAD_LOCAL_STT_ENABLED</c>. Web/server builds leave it unset.</summary>
    public static bool IsEnabled()
    {
        var flag = Environment.GetEnvironmentVariable("RADIOPAD_LOCAL_STT_ENABLED");
        return string.Equals(flag, "1", StringComparison.Ordinal)
            || string.Equals(flag, "true", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The directory that holds (or will hold) the model bundle for
    /// <paramref name="modelName"/>: <c>RADIOPAD_STT_MODEL_DIR</c> if set, else
    /// <c>%LOCALAPPDATA%\com.radiopad.desktop\models\&lt;modelName&gt;</c>. Null
    /// when no local-app-data dir is resolvable.
    /// </summary>
    public static string? ResolveModelDir(string modelName)
    {
        var explicitDir = Environment.GetEnvironmentVariable("RADIOPAD_STT_MODEL_DIR");
        if (!string.IsNullOrWhiteSpace(explicitDir))
            return explicitDir.Trim();

        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrEmpty(localAppData))
            return null;
        return Path.Combine(localAppData, "com.radiopad.desktop", "models", modelName);
    }

    /// <summary>Resolve the Whisper GGML model file for <paramref name="modelName"/>
    /// (the turbo model by default), or null when absent.</summary>
    public static string? ResolveWhisperBin(string? modelName = null)
    {
        var dir = ResolveModelDir(modelName ?? WhisperModelName);
        if (dir is null || !Directory.Exists(dir)) return null;
        return Directory.GetFiles(dir, "*.bin", SearchOption.AllDirectories).FirstOrDefault();
    }

    /// <summary>True when <paramref name="modelId"/> is one of the whisper models.</summary>
    public static bool IsWhisperModel(string? modelId) =>
        modelId == WhisperModelName || modelId == WhisperSmallEnModelName;

    /// <summary>
    /// Path of the per-install on-device STT preferences file (primary-model
    /// selection). Independent of <c>RADIOPAD_STT_MODEL_DIR</c> so it survives a
    /// model-dir override. Null when no local-app-data dir is resolvable.
    /// </summary>
    public static string? ResolveSettingsPath()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrEmpty(localAppData)) return null;
        return Path.Combine(localAppData, "com.radiopad.desktop", "stt-prefs.json");
    }

    /// <summary>True when multi-engine ensemble mode is on (<c>RADIOPAD_STT_ENSEMBLE</c>).</summary>
    public static bool IsEnsembleEnabled()
    {
        var flag = Environment.GetEnvironmentVariable("RADIOPAD_STT_ENSEMBLE");
        return string.Equals(flag, "1", StringComparison.Ordinal)
            || string.Equals(flag, "true", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Resolve the transducer model files under <paramref name="dir"/> (searched
    /// recursively so the archive's own top-level folder is fine; INT8 preferred).
    /// Any element is null when its file is absent.
    /// </summary>
    public static (string? encoder, string? decoder, string? joiner, string? tokens) ResolveFiles(string dir)
    {
        if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir))
            return (null, null, null, null);

        string? Pick(string part)
        {
            var files = Directory.GetFiles(dir, $"*{part}*.onnx", SearchOption.AllDirectories);
            return files.FirstOrDefault(f => f.Contains("int8", StringComparison.OrdinalIgnoreCase))
                ?? files.FirstOrDefault();
        }

        var tokens = Directory.GetFiles(dir, "tokens.txt", SearchOption.AllDirectories).FirstOrDefault();
        return (Pick("encoder"), Pick("decoder"), Pick("joiner"), tokens);
    }

    /// <summary>True when a complete transducer bundle is present under <paramref name="dir"/>.</summary>
    public static bool IsComplete(string dir)
    {
        var (e, d, j, t) = ResolveFiles(dir);
        return e is not null && d is not null && j is not null && t is not null;
    }
}
