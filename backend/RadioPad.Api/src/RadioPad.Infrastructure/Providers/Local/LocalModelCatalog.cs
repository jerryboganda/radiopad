namespace RadioPad.Infrastructure.Providers.Local;

/// <summary>The class of local model — drives grouping in the manager UI.</summary>
public enum ModelKind { Stt, Tts, Orchestrator }

/// <summary>How a model is packaged on its download URL. <see cref="MedAsrCtc"/> is the two-raw-file
/// sherpa-onnx CTC bundle (model.int8.onnx + tokens.txt) provisioned via
/// <c>SttModelProvisioner.EnsureMedAsrAsync</c>.</summary>
public enum ModelArchiveKind { TarBz2, RawFile, MedAsrCtc }

/// <summary>
/// How an entry is provisioned + run. Distinguishes the hosted-file engines
/// (Parakeet — we download a model bundle) from the platform speech
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
    // Parakeet descriptor is unchanged; the platform speech engines set it
    // explicitly. When not HostedFile, DownloadUrl/Sha256/FileName/ArchiveKind
    // are ignored (there is no hosted artifact).
    ModelProvisioning Provisioning = ModelProvisioning.HostedFile,
    // Free-text note shown on the card (e.g. an online/PHI warning). Optional.
    string? Note = null,
    // Withheld from the model-manager UI while the entry stays fully wired in code.
    // Operator decision (2026-07-20): the three platform speech engines (Windows
    // SAPI, the Windows languages/accuracy helper, and Edge Web Speech) are hidden
    // from radiologists for now. Their descriptors, engines, provisioning paths and
    // tests are all deliberately left intact so re-enabling one later is flipping
    // this single flag — no re-implementation. Hidden entries are filtered ONLY at
    // the API boundary (see ILocalModelCatalog.Visible); ById/All still resolve them
    // so any already-persisted selection keeps working instead of hard-failing.
    bool Hidden = false);

/// <summary>The set of local models the desktop build can manage.</summary>
public interface ILocalModelCatalog
{
    /// <summary>Every descriptor, including hidden ones. Used by the engines,
    /// provisioner and status paths — these must keep resolving hidden entries.</summary>
    IReadOnlyList<LocalModelDescriptor> All { get; }

    /// <summary>The subset the UI may show. This is what the controller returns;
    /// <see cref="All"/> stays complete so nothing in the runtime breaks.</summary>
    IReadOnlyList<LocalModelDescriptor> Visible { get; }

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

    // ── Optional on-device MedGemma report formatter (dictation brief §2.2) ────
    /// <summary>Catalog id == the models/&lt;id&gt; folder the GGUF is provisioned into.</summary>
    public const string MedGemmaId = "medgemma-1.5-4b-q4";
    /// <summary>Pinned MedGemma Q4_K_M GGUF file name (see IMPLEMENTATION_NOTES.md for the SHA-256).</summary>
    public const string MedGemmaFileName = "medgemma-1.5-4b-it-Q4_K_M.gguf";

    public IReadOnlyList<LocalModelDescriptor> All { get; } = Build();

    public IReadOnlyList<LocalModelDescriptor> Visible { get; } =
        Build().Where(m => !m.Hidden).ToList();

    /// <summary>Ids withheld from every UI. Consulted by <see cref="LocalSttSettings"/>
    /// so a workstation is never left primary-ed on an engine the radiologist can no
    /// longer see, and therefore could not switch away from.</summary>
    private static readonly HashSet<string> HiddenIds =
        Build().Where(m => m.Hidden).Select(m => m.Id).ToHashSet(StringComparer.Ordinal);

    public static bool IsHiddenId(string id) => HiddenIds.Contains(id);

    public LocalModelDescriptor? ById(string id) =>
        All.FirstOrDefault(m => string.Equals(m.Id, id, StringComparison.Ordinal));

    private static IReadOnlyList<LocalModelDescriptor> Build()
    {
        var parakeet = LocalSttModels.Parakeet;
        return new List<LocalModelDescriptor>
        {
            // MedASR (Google Conformer-CTC, radiology-tuned ~4.6% WER) — the DEFAULT primary
            // on-device engine (D2), via the public/ungated sherpa-onnx CTC bundle (two raw files:
            // model.int8.onnx + tokens.txt). Auto-provisioned on first run; runs on the same
            // sherpa-onnx CPU runtime as Parakeet. Pinned in LocalSttModels (verified, HF gated:false).
            new(
                Id: LocalSttModels.MedAsrModelName,
                DisplayName: "MedASR (radiology) — primary speech-to-text",
                Kind: ModelKind.Stt,
                Engine: SherpaMedAsrSttClient.EngineName, // "medasr"
                DownloadUrl: LocalSttModels.MedAsrModel.Url,
                Sha256: LocalSttModels.MedAsrModel.Sha256,
                SizeBytes: LocalSttModels.MedAsrModel.SizeBytes,
                License: "HAI-DEF",
                ArchiveKind: ModelArchiveKind.MedAsrCtc,
                FileName: LocalSttModels.MedAsrModel.FileName,
                Placeholder: false,
                Provisioning: ModelProvisioning.HostedFile,
                Note: "Radiology-tuned on-device speech-to-text (~160 MB). Runs fully on-device — audio never leaves the workstation."),

            new(
                Id: parakeet.Name,
                DisplayName: "Parakeet TDT 0.6B v3 — speech-to-text (alternative)",
                Kind: ModelKind.Stt,
                Engine: SherpaParakeetSttClient.EngineName, // "parakeet"
                DownloadUrl: parakeet.Url,
                Sha256: parakeet.Sha256,
                SizeBytes: parakeet.SizeBytes,
                License: "CC-BY-4.0",
                ArchiveKind: ModelArchiveKind.TarBz2,
                FileName: null,
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
                Note: "Built into Windows. Runs fully on-device — audio never leaves the workstation.",
                Hidden: true),

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
                Note: "Opens Windows speech settings to add languages and improve the on-device Windows recognizer. Runs fully on-device.",
                Hidden: true),

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
                Note: "Highly accurate, but processed online by Microsoft — audio leaves the device. Avoid for PHI dictation.",
                Hidden: true),

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
            // MedGemma 1.5 4B (Q4_K_M) — the OPTIONAL on-device report formatter (dictation brief
            // §2.2). Downloaded on demand from the model manager (NOT auto-provisioned on first run;
            // only MedASR is). Run via the LlamaCppProvider LocalOnly adapter against a llama-server
            // on loopback that RadioPad provisions and supervises itself:
            // SttModelProvisioner.EnsureMedGemmaWithRuntimeAsync fetches the pinned llama.cpp
            // runtime (LocalRuntimes.LlamaServerId) alongside this GGUF, and LlamaServerProcess
            // starts it lazily on first use. One download therefore yields a working feature — do
            // NOT reintroduce the old "bring your own llama-server" wording here or in the Note;
            // it described a state resolved in 2026-07 (IMPLEMENTATION_NOTES.md) and had already
            // outlived it once, telling users a shipped feature was inert. Pin verified against the
            // HF public API (repo gated:false, so the provisioner's anonymous SHA-256-verified
            // download works). Cloud formatting stays the default — this is the fully-offline,
            // no-PHI-to-cloud alternative.
            new(
                Id: MedGemmaId,
                DisplayName: "MedGemma 1.5 4B (Q4_K_M) — on-device report formatter",
                Kind: ModelKind.Orchestrator,
                Engine: LlamaCppProvider.AdapterId, // "llama-cpp"
                DownloadUrl: "https://huggingface.co/unsloth/medgemma-1.5-4b-it-GGUF/resolve/main/medgemma-1.5-4b-it-Q4_K_M.gguf",
                Sha256: "b31becdf4f39561800505514cce67681604fe449d04dd35c8c92fd7848c6d7bd",
                SizeBytes: 2489894976L,
                License: "HAI-DEF / Gemma",
                ArchiveKind: ModelArchiveKind.RawFile,
                FileName: MedGemmaFileName,
                Placeholder: false,
                Provisioning: ModelProvisioning.HostedFile,
                Note: "Optional offline report formatter (~2.5 GB). Formats dictated findings only — never invents findings, never reads images. Nothing leaves this workstation: it runs against a llama.cpp server RadioPad downloads with the model and starts on loopback for you. Cloud formatting remains the default until you select this model for report generation."),
        };
    }
}
