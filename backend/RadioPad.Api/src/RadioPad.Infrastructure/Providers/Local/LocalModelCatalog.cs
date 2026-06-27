namespace RadioPad.Infrastructure.Providers.Local;

/// <summary>The class of local model — drives grouping in the manager UI.</summary>
public enum ModelKind { Stt, Tts, Orchestrator }

/// <summary>How a model is packaged on its download URL.</summary>
public enum ModelArchiveKind { TarBz2, RawFile }

/// <summary>
/// How an entry is provisioned + run. Distinguishes the hosted-file engines
/// (Parakeet/Whisper — we download a model bundle) from the platform speech
/// engines that have no hosted artifact:
/// <list type="bullet">
/// <item><see cref="HostedFile"/> — we download/verify/extract a model file from
/// <see cref="LocalModelDescriptor.DownloadUrl"/> (the original behavior).</item>
/// <item><see cref="WindowsBuiltIn"/> — System.Speech / SAPI: ships with Windows,
/// nothing to download; "downloaded" == the recognizer is installed.</item>
/// <item><see cref="WindowsLanguagePack"/> — WinRT speech: on-device only when a
/// Windows speech/language pack is installed; "download" opens Windows settings.</item>
/// <item><see cref="BrowserWebSpeech"/> — Edge Web Speech API: runs in the WebView,
/// not the sidecar; availability is probed by the frontend.</item>
/// </list>
/// </summary>
public enum ModelProvisioning { HostedFile, WindowsBuiltIn, WindowsLanguagePack, BrowserWebSpeech }

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
    bool Placeholder,
    // How this entry is provisioned + run. Defaults to HostedFile so the existing
    // Parakeet/Whisper descriptors are unchanged; the platform speech engines set
    // it explicitly. When not HostedFile, DownloadUrl/Sha256/FileName/ArchiveKind
    // are ignored (there is no hosted artifact).
    ModelProvisioning Provisioning = ModelProvisioning.HostedFile,
    // Free-text note shown on the card (e.g. an online/PHI warning). Optional.
    string? Note = null);

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
    // ── Platform speech engine ids + catalog ids (single source of truth) ──────
    // These engines have no hosted model artifact, so their catalog id is just a
    // stable handle (NOT a models/<id> folder name). The backend engines key off
    // the engine id; the Edge engine is frontend-only (no backend ILocalSttEngine).
    public const string WindowsSapiEngine = "windows_sapi";
    public const string WindowsSapiId = "windows-sapi";
    public const string WindowsWinRtEngine = "windows_winrt";
    public const string WindowsWinRtId = "windows-winrt";
    public const string EdgeWebSpeechEngine = "edge_webspeech";
    public const string EdgeWebSpeechId = "edge-webspeech";

    public IReadOnlyList<LocalModelDescriptor> All { get; } = Build();

    public LocalModelDescriptor? ById(string id) =>
        All.FirstOrDefault(m => string.Equals(m.Id, id, StringComparison.Ordinal));

    private static IReadOnlyList<LocalModelDescriptor> Build()
    {
        var parakeet = LocalSttModels.Parakeet;
        var whisper = LocalSttModels.Whisper;
        var smallEn = LocalSttModels.WhisperSmallEn;
        var medical = LocalSttModels.MedicalWhisper;
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

            // Medical-domain Whisper — the 3rd cross-check engine, run on CPU/RAM via
            // whisper.cpp. Default artifact is the verified full (non-distilled)
            // large-v3 q5_0 — a higher-accuracy, decorrelated voice vs the distilled
            // turbo live model. Provisioned ON DEMAND (a downloadable manager card,
            // not in the first-run set). To upgrade to a true medical fine-tune,
            // convert Na0s/Medical-Whisper-Large-v3 to q5_0 ggml into this model's
            // dir (engine loads any *.bin there) — validate on radiology audio first.
            new(
                Id: medical.Name,
                DisplayName: "Whisper large-v3 (full) — medical cross-check speech-to-text",
                Kind: ModelKind.Stt,
                Engine: MedicalWhisperSttClient.EngineName, // "whisper_medical"
                DownloadUrl: medical.Url,
                Sha256: medical.Sha256,
                SizeBytes: medical.SizeBytes,
                License: "MIT",
                ArchiveKind: ModelArchiveKind.RawFile,
                FileName: medical.FileName,
                Placeholder: false),

            // ── Platform speech engines (no hosted artifact) ───────────────────
            // System.Speech / SAPI — ships with Windows, 100% on-device (PHI-safe),
            // CPU-only. The robust default dictation engine on the desktop.
            new(
                Id: WindowsSapiId,
                DisplayName: "Windows Speech (on-device) — primary speech-to-text",
                Kind: ModelKind.Stt,
                Engine: WindowsSapiEngine,
                DownloadUrl: "",
                Sha256: "",
                SizeBytes: 0,
                License: "Windows",
                ArchiveKind: ModelArchiveKind.RawFile,
                FileName: null,
                Placeholder: false,
                Provisioning: ModelProvisioning.WindowsBuiltIn,
                Note: "Built into Windows. Runs fully on-device — audio never leaves the workstation."),

            // Windows speech language/accuracy settings. NOTE: WinRT
            // Windows.Media.SpeechRecognition only recognizes the LIVE microphone
            // (no supported recorded-WAV path) and would force a Windows-only build
            // of the server, so it is not a separate batch engine. Instead this card
            // is a helper for the same on-device Windows recognizer (SAPI): it opens
            // Windows speech settings so the user can add languages / improve
            // recognition, and Test exercises the on-device recognizer. Engine is the
            // SAPI engine so availability + Test are real (no dead 503 card).
            new(
                Id: WindowsWinRtId,
                DisplayName: "Windows Speech — languages & accuracy",
                Kind: ModelKind.Stt,
                Engine: WindowsSapiEngine,
                DownloadUrl: "",
                Sha256: "",
                SizeBytes: 0,
                License: "Windows",
                ArchiveKind: ModelArchiveKind.RawFile,
                FileName: null,
                Placeholder: false,
                Provisioning: ModelProvisioning.WindowsLanguagePack,
                Note: "Opens Windows speech settings to add languages and improve the on-device Windows recognizer. Runs fully on-device."),

            // Edge Web Speech API — highly accurate; runs in the desktop WebView2
            // (Edge), backed by Microsoft's online speech service. Online: audio
            // leaves the device, so it carries a PHI warning. Recognition happens in
            // the frontend, so there is no backend ILocalSttEngine for it.
            new(
                Id: EdgeWebSpeechId,
                DisplayName: "Microsoft Edge Speech (online) — speech-to-text",
                Kind: ModelKind.Stt,
                Engine: EdgeWebSpeechEngine,
                DownloadUrl: "",
                Sha256: "",
                SizeBytes: 0,
                License: "Microsoft Edge",
                ArchiveKind: ModelArchiveKind.RawFile,
                FileName: null,
                Placeholder: false,
                Provisioning: ModelProvisioning.BrowserWebSpeech,
                Note: "Highly accurate, but processed online by Microsoft — audio leaves the device. Avoid for PHI dictation."),

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
