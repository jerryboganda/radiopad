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

    /// <summary>Engine that should lead transcription (e.g. "parakeet").</summary>
    string PrimaryEngineId { get; }

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
        _primaryModelId = Load(path) ?? DefaultPrimaryModelId;
    }

    /// <summary>
    /// The out-of-box primary on a fresh install: Windows on-device speech (SAPI)
    /// when a Windows recognizer is actually installed — it is PHI-safe and needs no
    /// download — else the downloadable **MedASR** model (decision D2: MedASR is the
    /// default primary sherpa engine; Parakeet is the user-promotable fallback). The
    /// recognizer check matters on Windows 11, where the legacy speech recognizer is
    /// often absent; defaulting to SAPI only when present keeps the "Primary" badge
    /// honest. Either way the ensemble's primary-pick falls back to the first available
    /// engine, so this is always safe (e.g. while MedASR is still downloading).
    /// </summary>
    internal static string DefaultPrimaryModelId =>
        LocalSttModels.IsEnabled() && WindowsSapiSttClient.IsRecognizerInstalled()
            ? LocalModelCatalog.WindowsSapiId
            : LocalSttModels.MedAsrModelName;

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

    public string PrimaryEngineId => MapEngine(PrimaryModelId);

    /// <summary>Map a primary model id to the engine id that should lead. The two
    /// Windows engines + the Edge Web Speech engine map to their own ids; everything
    /// else (incl. Parakeet) maps to Parakeet. Edge is frontend-routed and has no
    /// backend engine, so when it is primary the ensemble's primary-pick simply
    /// falls back to the first available engine for any (rare) sidecar call.</summary>
    internal static string MapEngine(string modelId) => modelId switch
    {
        LocalModelCatalog.WindowsSapiId => LocalModelCatalog.WindowsSapiEngine,
        // The "languages & accuracy" helper card drives the same on-device Windows
        // recognizer (SAPI), so it maps to the SAPI engine rather than a separate one.
        LocalModelCatalog.WindowsWinRtId => LocalModelCatalog.WindowsSapiEngine,
        LocalModelCatalog.EdgeWebSpeechId => LocalModelCatalog.EdgeWebSpeechEngine,
        LocalSttModels.MedAsrModelName => SherpaMedAsrSttClient.EngineName,
        _ => SherpaParakeetSttClient.EngineName,
    };

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
/// directly. Keeps the historical behavior (Parakeet primary).
/// </summary>
internal sealed class DefaultLocalSttSettings : ILocalSttSettings
{
    public static readonly DefaultLocalSttSettings Instance = new();
    public string PrimaryModelId => LocalSttModels.DefaultModelName;
    public string PrimaryEngineId => SherpaParakeetSttClient.EngineName;
    public bool IsPrimary(string modelId) =>
        string.Equals(modelId, LocalSttModels.DefaultModelName, StringComparison.Ordinal);
    public void SetPrimary(string modelId) { }
}
