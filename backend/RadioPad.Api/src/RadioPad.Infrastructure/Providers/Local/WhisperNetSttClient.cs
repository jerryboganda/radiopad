using Microsoft.Extensions.Logging;
using RadioPad.Application.Abstractions;
using RadioPad.Application.Stt;
using RadioPad.Infrastructure.Audio;
using Whisper.net;

namespace RadioPad.Infrastructure.Providers.Local;

/// <summary>
/// Phase 2 — the second on-device ASR engine: OpenAI Whisper via Whisper.net /
/// whisper.cpp, in-process. Architecturally decorrelated from the Parakeet
/// transducer (Conformer attention-decoder vs FastConformer-TDT), so the two make
/// different errors — exactly what ROVER needs to actually reduce WER.
///
/// The ACTIVE whisper model is chosen by <see cref="ILocalSttSettings"/>: when the
/// operator makes a whisper model (large-v3-turbo or small.en) primary, this engine
/// loads that model; otherwise it loads turbo for the ensemble cross-check. The
/// native factory is (re)loaded only when the selected model changes.
///
/// Dormant unless the local engine is enabled AND the selected model is present, so
/// it is inert on web/server and until a model is downloaded.
/// </summary>
public sealed class WhisperNetSttClient : ILocalSttEngine, IDisposable
{
    public const string EngineName = "whisper";

    private readonly IAudioDecoder _decoder;
    private readonly ILogger<WhisperNetSttClient> _log;
    private readonly ILocalSttSettings _settings;
    private readonly bool _enabled;
    private readonly object _gate = new();
    private string? _loadedModelId;
    private WhisperFactory? _factory;
    private volatile bool _loadFailed;
    private volatile string? _lastError;

    public WhisperNetSttClient(
        IAudioDecoder decoder,
        ILogger<WhisperNetSttClient> log,
        ILocalSttSettings? settings = null)
    {
        _decoder = decoder;
        _log = log;
        _settings = settings ?? DefaultLocalSttSettings.Instance;
        _enabled = LocalSttModels.IsEnabled();
    }

    public string EngineId => EngineName;

    public string? LastError => _lastError;

    public bool Available
    {
        get
        {
            if (!_enabled || _loadFailed) return false;
            // Available iff the currently-selected whisper model is downloaded.
            return LocalSttModels.ResolveWhisperBin(_settings.ActiveWhisperModelId) is not null;
        }
    }

    public async Task<EngineTranscript> RecognizeAsync(byte[] wavBytes, CancellationToken ct)
    {
        if (!Available)
            throw new InvalidOperationException("whisper engine is not available");

        // Decode + resample to 16 kHz mono first. Whisper.net's WAV reader only
        // accepts exactly 16 kHz, so feeding raw mic audio (44.1/48 kHz) straight
        // in throws NotSupportedWaveException. Going through the shared decoder (as
        // the Parakeet engine does) makes the engine accept any input rate.
        var samples = await _decoder.DecodeAsync(
            new MemoryStream(wavBytes, writable: false), "audio/wav", ct);

        var factory = GetFactory();
        var tokens = new List<SttToken>();
        try
        {
            // Optimized decode: multi-threaded, beam search (accuracy over the
            // default greedy), real token probabilities (so the reconciler gets a
            // usable confidence signal), and a radiology priming prompt to bias
            // domain vocabulary.
            var builder = factory.CreateBuilder()
                .WithLanguage("en")
                .WithThreads(LocalSttModels.ResolveThreads())
                .WithProbabilities();

            var beam = LocalSttModels.ResolveWhisperBeamSize();
            if (beam > 1)
                builder = builder.WithBeamSearchSamplingStrategy(c => c.WithBeamSize(beam));

            var prompt = LocalSttModels.ResolveWhisperPrompt();
            if (!string.IsNullOrWhiteSpace(prompt))
                builder = builder.WithPrompt(prompt);

            using var processor = builder.Build();
            await foreach (var seg in processor.ProcessAsync(samples, ct))
            {
                var conf = NormalizeProbability(seg.Probability);
                foreach (var t in SttText.Tokenize(seg.Text, conf))
                    tokens.Add(t);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _loadFailed = true;
            _lastError = $"{ex.GetType().Name}: {ex.Message}";
            _log.LogError(ex, "whisper recognize failed; disabling engine for this session");
            throw;
        }

        return new EngineTranscript(EngineName, tokens);
    }

    private static double NormalizeProbability(float p) => p <= 0f ? 0.5 : Math.Clamp(p, 0f, 1f);

    /// <summary>
    /// Return the native factory for the currently-selected whisper model, loading
    /// (or hot-reloading, when the operator switched models) it on demand.
    /// </summary>
    private WhisperFactory GetFactory()
    {
        lock (_gate)
        {
            var activeId = _settings.ActiveWhisperModelId;
            if (_factory is not null && string.Equals(activeId, _loadedModelId, StringComparison.Ordinal))
                return _factory;

            // First load, or the selected model changed — drop the old factory.
            _factory?.Dispose();
            _factory = null;
            _loadedModelId = null;

            var bin = LocalSttModels.ResolveWhisperBin(activeId)
                ?? throw new InvalidOperationException($"whisper model '{activeId}' is not present");

            // Stage the native whisper.cpp runtime before the first factory is
            // created — the single-file desktop sidecar has no on-disk
            // `runtimes/win-x64`, so Whisper.net's loader can't find its DLLs
            // without this. No-op on non-Windows-x64 / non-bundled builds.
            WhisperNativeLibrary.EnsureLoaded();

            _log.LogInformation("Loading Whisper model {Model} from {Bin}", activeId, bin);
            _factory = WhisperFactory.FromPath(bin);
            _loadedModelId = activeId;
            return _factory;
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _factory?.Dispose();
            _factory = null;
        }
    }
}
