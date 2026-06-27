using System.Text.Json;

namespace RadioPad.Infrastructure.Providers.Local;

/// <summary>
/// Per-workstation on-device STT preferences — currently just which downloaded
/// model is the PRIMARY dictation engine. Persisted as a small JSON file next to
/// the model store so the desktop sidecar honors the operator's choice across
/// restarts. Read cheaply per request; written by the local-models controller.
/// </summary>
public interface ILocalSttSettings
{
    /// <summary>The model id chosen as primary (defaults to Parakeet).</summary>
    string PrimaryModelId { get; }

    /// <summary>Engine that should lead transcription: "parakeet" or "whisper".</summary>
    string PrimaryEngineId { get; }

    /// <summary>
    /// The whisper model the whisper engine should load — the primary when it is a
    /// whisper model, else the turbo model (used as the ensemble cross-check engine).
    /// </summary>
    string ActiveWhisperModelId { get; }

    bool IsPrimary(string modelId);
    void SetPrimary(string modelId);
}

/// <summary>File-backed <see cref="ILocalSttSettings"/> (JSON at
/// <see cref="LocalSttModels.ResolveSettingsPath"/>). Singleton in the sidecar.</summary>
public sealed class LocalSttSettings : ILocalSttSettings
{
    private sealed record Prefs(string? PrimaryModelId);

    private readonly object _gate = new();
    private readonly string? _path;
    private string _primaryModelId;

    public LocalSttSettings() : this(LocalSttModels.ResolveSettingsPath()) { }

    /// <summary>Path-injectable for tests.</summary>
    public LocalSttSettings(string? path)
    {
        _path = path;
        _primaryModelId = Load(path) ?? LocalSttModels.DefaultModelName;
    }

    private static string? Load(string? path)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;
        try
        {
            var prefs = JsonSerializer.Deserialize<Prefs>(File.ReadAllText(path));
            return string.IsNullOrWhiteSpace(prefs?.PrimaryModelId) ? null : prefs!.PrimaryModelId;
        }
        catch { return null; }
    }

    public string PrimaryModelId
    {
        get { lock (_gate) { return _primaryModelId; } }
    }

    public string PrimaryEngineId =>
        LocalSttModels.IsWhisperModel(PrimaryModelId)
            ? WhisperNetSttClient.EngineName
            : SherpaParakeetSttClient.EngineName;

    public string ActiveWhisperModelId
    {
        get
        {
            var id = PrimaryModelId;
            return LocalSttModels.IsWhisperModel(id) ? id : LocalSttModels.WhisperModelName;
        }
    }

    public bool IsPrimary(string modelId) =>
        string.Equals(modelId, PrimaryModelId, StringComparison.Ordinal);

    public void SetPrimary(string modelId)
    {
        lock (_gate)
        {
            _primaryModelId = modelId;
            if (string.IsNullOrEmpty(_path)) return;
            try
            {
                var dir = Path.GetDirectoryName(_path);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                File.WriteAllText(_path, JsonSerializer.Serialize(new Prefs(modelId)));
            }
            catch { /* best-effort persist; the in-memory value still applies this session */ }
        }
    }
}

/// <summary>
/// Default (in-memory, Parakeet-primary) settings used when an engine/orchestrator
/// is constructed without DI — e.g. the unit/smoke tests that build engines
/// directly. Keeps the historical behavior (Parakeet primary, turbo whisper).
/// </summary>
internal sealed class DefaultLocalSttSettings : ILocalSttSettings
{
    public static readonly DefaultLocalSttSettings Instance = new();
    public string PrimaryModelId => LocalSttModels.DefaultModelName;
    public string PrimaryEngineId => SherpaParakeetSttClient.EngineName;
    public string ActiveWhisperModelId => LocalSttModels.WhisperModelName;
    public bool IsPrimary(string modelId) =>
        string.Equals(modelId, LocalSttModels.DefaultModelName, StringComparison.Ordinal);
    public void SetPrimary(string modelId) { }
}
