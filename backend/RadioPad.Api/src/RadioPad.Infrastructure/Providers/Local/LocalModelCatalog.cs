namespace RadioPad.Infrastructure.Providers.Local;

/// <summary>The class of local model — drives grouping in the manager UI.</summary>
public enum ModelKind { Stt, Tts, Orchestrator }

/// <summary>How a model is packaged on its download URL.</summary>
public enum ModelArchiveKind { TarBz2, RawFile }

/// <summary>
/// A single manageable on-device model. Generalizes the pinned STT specs in
/// <see cref="LocalSttModels"/> so TTS + an orchestrator brain can be added later
/// by appending a descriptor (and wiring its engine) — the controller/provisioner
/// stay kind-agnostic. <see cref="Id"/> MUST equal the model-dir name the engine
/// resolves via <see cref="LocalSttModels.ResolveModelDir"/> or status/delete will
/// look in the wrong folder.
/// </summary>
public sealed record LocalModelDescriptor(
    string Id,
    string DisplayName,
    ModelKind Kind,
    string Engine,
    string DownloadUrl,
    string Sha256,
    long SizeBytes,
    string License,
    ModelArchiveKind ArchiveKind,
    string? FileName,
    bool Placeholder);

/// <summary>The set of local models the desktop build can manage.</summary>
public interface ILocalModelCatalog
{
    IReadOnlyList<LocalModelDescriptor> All { get; }
    LocalModelDescriptor? ById(string id);
}

/// <summary>
/// Static catalog seeded from the pinned <see cref="LocalSttModels"/> specs (the
/// SHA-256 / URL / size are NOT re-pinned here — they reference the single source
/// of truth) plus roadmap placeholders for the future TTS + orchestrator kinds.
/// </summary>
public sealed class LocalModelCatalog : ILocalModelCatalog
{
    public IReadOnlyList<LocalModelDescriptor> All { get; } = Build();

    public LocalModelDescriptor? ById(string id) =>
        All.FirstOrDefault(m => string.Equals(m.Id, id, StringComparison.Ordinal));

    private static IReadOnlyList<LocalModelDescriptor> Build()
    {
        var parakeet = LocalSttModels.Parakeet;
        var whisper = LocalSttModels.Whisper;
        var smallEn = LocalSttModels.WhisperSmallEn;
        return new List<LocalModelDescriptor>
        {
            new(
                Id: parakeet.Name,
                DisplayName: "Parakeet TDT 0.6B v3 — primary speech-to-text",
                Kind: ModelKind.Stt,
                Engine: SherpaParakeetSttClient.EngineName, // "parakeet"
                DownloadUrl: parakeet.Url,
                Sha256: parakeet.Sha256,
                SizeBytes: parakeet.SizeBytes,
                License: "CC-BY-4.0",
                ArchiveKind: ModelArchiveKind.TarBz2,
                FileName: null,
                Placeholder: false),
            new(
                Id: whisper.Name,
                DisplayName: "Whisper large-v3-turbo — cross-check speech-to-text",
                Kind: ModelKind.Stt,
                Engine: WhisperNetSttClient.EngineName, // "whisper"
                DownloadUrl: whisper.Url,
                Sha256: whisper.Sha256,
                SizeBytes: whisper.SizeBytes,
                License: "MIT",
                ArchiveKind: ModelArchiveKind.RawFile,
                FileName: whisper.FileName,
                Placeholder: false),
            new(
                Id: smallEn.Name,
                DisplayName: "Whisper small.en — fast English speech-to-text",
                Kind: ModelKind.Stt,
                Engine: WhisperNetSttClient.EngineName, // "whisper"
                DownloadUrl: smallEn.Url,
                Sha256: smallEn.Sha256,
                SizeBytes: smallEn.SizeBytes,
                License: "MIT",
                ArchiveKind: ModelArchiveKind.RawFile,
                FileName: smallEn.FileName,
                Placeholder: false),

            // ── Roadmap placeholders (no engine/URL yet). The manager renders these
            // as disabled "coming soon" cards. When the engine lands: flip Placeholder
            // to false and fill the URL/SHA/size — nothing else in the pipeline changes.
            new(
                Id: "tts-coming-soon",
                DisplayName: "On-device text-to-speech",
                Kind: ModelKind.Tts,
                Engine: "tts",
                DownloadUrl: "",
                Sha256: "",
                SizeBytes: 0,
                License: "",
                ArchiveKind: ModelArchiveKind.RawFile,
                FileName: null,
                Placeholder: true),
            new(
                Id: "orchestrator-coming-soon",
                DisplayName: "On-device orchestrator brain",
                Kind: ModelKind.Orchestrator,
                Engine: "orchestrator",
                DownloadUrl: "",
                Sha256: "",
                SizeBytes: 0,
                License: "",
                ArchiveKind: ModelArchiveKind.RawFile,
                FileName: null,
                Placeholder: true),
        };
    }
}
