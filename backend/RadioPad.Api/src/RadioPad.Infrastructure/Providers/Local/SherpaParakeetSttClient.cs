using System.Diagnostics;
using Microsoft.Extensions.Logging;
using RadioPad.Application.Abstractions;
using RadioPad.Application.Stt;
using RadioPad.Infrastructure.Audio;
using SherpaOnnx;

namespace RadioPad.Infrastructure.Providers.Local;

/// <summary>
/// Phase 1 (local STT) — fully on-device speech-to-text via sherpa-onnx running
/// an NVIDIA Parakeet-TDT transducer (INT8 ONNX) in-process on the CPU. This is
/// the free, offline replacement for the cloud (UBAG/Gemini) transcription path:
/// audio never leaves the workstation.
///
/// <para><b>Activation.</b> The engine is dormant unless
/// <c>RADIOPAD_LOCAL_STT_ENABLED</c> is truthy AND the model is present, so web /
/// server builds (which set neither) transparently keep the UBAG flow. The
/// desktop bundle sets the flag and ships the model + ffmpeg, at which point
/// <see cref="Available"/> flips true and <see cref="ITranscriptionService"/>
/// routes here instead of the cloud.</para>
///
/// <para><b>Model layout.</b> A sherpa-onnx Parakeet bundle directory containing
/// <c>encoder*.onnx</c>, <c>decoder*.onnx</c>, <c>joiner*.onnx</c> (INT8 variants
/// preferred) and <c>tokens.txt</c>. Resolved from <c>RADIOPAD_STT_MODEL_DIR</c>
/// or, by default, <c>%LOCALAPPDATA%\com.radiopad.desktop\models\&lt;model&gt;</c>
/// (downloaded on first run by the desktop shell — never bundled in the MSI).</para>
///
/// <para>The native recognizer is loaded once (lazily) and reused. sherpa-onnx
/// offline streams are not guaranteed thread-safe, so decode is serialized behind
/// a lock — fine for the single-user desktop sidecar.</para>
/// </summary>
public sealed class SherpaParakeetSttClient : ILocalSttClient, ILocalSttEngine, IDisposable
{
    /// <summary>Provider name recorded on the result + audit (never PHI).</summary>
    public const string ProviderName = "local";

    /// <summary>Engine id used in ensemble hypotheses + calibration tables.</summary>
    public const string EngineName = "parakeet";

    // Transducers do not expose a usable per-word confidence through the sherpa
    // C# result, so emit a fixed prior; the reconciler then flags disagreements
    // (it cannot trust a missing confidence signal) — the safe default.
    private const double ParakeetConfidence = 0.85;

    private const int SampleRate = 16000;
    private const int FeatureDim = 80;

    private readonly IAudioDecoder _decoder;
    private readonly ILogger<SherpaParakeetSttClient> _log;
    private readonly bool _enabled;
    private readonly string _modelName;
    private readonly string? _modelDir;
    private readonly object _gate = new();

    private string? _encoder;
    private string? _decoderModel;
    private string? _joiner;
    private string? _tokens;
    private OfflineRecognizer? _recognizer;
    private volatile bool _loadFailed;

    public SherpaParakeetSttClient(IAudioDecoder decoder, ILogger<SherpaParakeetSttClient> log)
    {
        _decoder = decoder;
        _log = log;

        var flag = Environment.GetEnvironmentVariable("RADIOPAD_LOCAL_STT_ENABLED");
        _enabled = string.Equals(flag, "1", StringComparison.Ordinal)
            || string.Equals(flag, "true", StringComparison.OrdinalIgnoreCase);

        _modelName = Environment.GetEnvironmentVariable("RADIOPAD_STT_MODEL") is { Length: > 0 } m
            ? m.Trim()
            : LocalSttModels.DefaultModelName;

        // Resolved lazily (see EnsureFilesResolved) so a model that finishes
        // downloading AFTER startup activates without a process restart.
        _modelDir = LocalSttModels.ResolveModelDir(_modelName);
    }

    /// <summary>
    /// True only when the engine is enabled, the decoder can run, and a complete
    /// model bundle is present and not already known-bad. <see cref="ITranscriptionService"/>
    /// checks this before routing — false means "use the cloud path".
    /// </summary>
    public bool Available
    {
        get
        {
            if (!_enabled || _loadFailed || !_decoder.Available || _modelDir is null)
                return false;
            EnsureFilesResolved();
            return _encoder is not null && _decoderModel is not null
                && _joiner is not null && _tokens is not null;
        }
    }

    public async Task<TranscriptionResult> TranscribeAsync(Stream audio, string contentType, CancellationToken ct, string? mode = null)
    {
        // mode is ignored here — this is the single-engine client; the ensemble
        // orchestrator (the registered ILocalSttClient) is what honors it.
        if (!Available)
            throw new InvalidOperationException("local STT engine is not available");

        var sw = Stopwatch.StartNew();
        var samples = await _decoder.DecodeAsync(audio, contentType, ct);
        ct.ThrowIfCancellationRequested();
        var text = RecognizeText(samples);
        sw.Stop();

        return new TranscriptionResult(
            Text: text,
            Provider: ProviderName,
            Model: _modelName,
            LatencyMs: sw.ElapsedMilliseconds);
    }

    public string EngineId => EngineName;

    public async Task<EngineTranscript> RecognizeAsync(byte[] wavBytes, CancellationToken ct)
    {
        if (!Available)
            throw new InvalidOperationException("local STT engine is not available");

        using var ms = new MemoryStream(wavBytes, writable: false);
        var samples = await _decoder.DecodeAsync(ms, "audio/wav", ct);
        ct.ThrowIfCancellationRequested();
        var text = RecognizeText(samples);
        return new EngineTranscript(EngineName, SttText.Tokenize(text, ParakeetConfidence));
    }

    /// <summary>Run the sherpa recognizer on decoded samples (serialized per recognizer).</summary>
    private string RecognizeText(float[] samples)
    {
        try
        {
            var recognizer = GetRecognizer();
            lock (_gate)
            {
                using var stream = recognizer.CreateStream();
                stream.AcceptWaveform(SampleRate, samples);
                recognizer.Decode(stream);
                return (stream.Result.Text ?? string.Empty).Trim();
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Mark the engine bad so subsequent calls fall back to the cloud path
            // instead of repeatedly hard-failing on a broken native load.
            _loadFailed = true;
            _log.LogError(ex, "local STT decode failed; disabling on-device engine for this session");
            throw;
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
            config.ModelConfig.Transducer.Encoder = _encoder!;
            config.ModelConfig.Transducer.Decoder = _decoderModel!;
            config.ModelConfig.Transducer.Joiner = _joiner!;
            config.ModelConfig.Tokens = _tokens!;
            config.ModelConfig.NumThreads = ResolveThreads();
            config.ModelConfig.Provider = "cpu";
            config.ModelConfig.Debug = 0;
            // greedy_search keeps Phase 1 robust; Phase 2 switches to
            // modified_beam_search + a radiology hotwords ContextGraph.
            config.DecodingMethod = "greedy_search";

            _log.LogInformation(
                "Loading on-device STT model '{Model}' from {Dir} ({Threads} threads)",
                _modelName, _modelDir, config.ModelConfig.NumThreads);

            _recognizer = new OfflineRecognizer(config);
            return _recognizer;
        }
    }

    private static int ResolveThreads()
    {
        if (int.TryParse(Environment.GetEnvironmentVariable("RADIOPAD_STT_THREADS"), out var n) && n > 0)
            return n;
        // Leave headroom on the clinical workstation; cap so a many-core box
        // doesn't oversubscribe on a single utterance.
        return Math.Clamp(Environment.ProcessorCount - 1, 1, 4);
    }

    /// <summary>
    /// Lazily resolve the model files from <see cref="_modelDir"/>, re-checking
    /// until the bundle is present (so a first-run download that completes after
    /// startup is picked up on the next dictation — no restart needed). Cheap
    /// once resolved; the directory scan only runs while still incomplete.
    /// </summary>
    private void EnsureFilesResolved()
    {
        if (_encoder is not null && _decoderModel is not null
            && _joiner is not null && _tokens is not null)
            return;

        lock (_gate)
        {
            if (_modelDir is null) return;
            var (enc, dec, join, tok) = LocalSttModels.ResolveFiles(_modelDir);
            _encoder = enc;
            _decoderModel = dec;
            _joiner = join;
            _tokens = tok;
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _recognizer?.Dispose();
            _recognizer = null;
        }
    }
}
