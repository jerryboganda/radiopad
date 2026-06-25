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

    /// <summary>A single downloadable model file (no archive extraction).</summary>
    public sealed record FileSpec(string Name, string FileName, string Url, long SizeBytes, string Sha256);

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

    /// <summary>Resolve the Whisper GGML model file under its model dir, or null when absent.</summary>
    public static string? ResolveWhisperBin()
    {
        var dir = ResolveModelDir(WhisperModelName);
        if (dir is null || !Directory.Exists(dir)) return null;
        return Directory.GetFiles(dir, "*.bin", SearchOption.AllDirectories).FirstOrDefault();
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
