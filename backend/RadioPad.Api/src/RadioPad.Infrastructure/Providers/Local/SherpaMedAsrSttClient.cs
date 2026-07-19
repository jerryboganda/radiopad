using System.Diagnostics;
using Microsoft.Extensions.Logging;
using RadioPad.Application.Abstractions;
using RadioPad.Application.Stt;
using RadioPad.Infrastructure.Audio;
using SherpaOnnx;

namespace RadioPad.Infrastructure.Providers.Local;

/// <summary>
/// Brief §2.1 / decision D2 — the DEFAULT primary on-device STT: Google MedASR (Conformer-CTC,
/// radiology-tuned, ~4.6% WER) running fully offline via sherpa-onnx on the CPU. Same in-process,
/// audio-never-leaves-the-workstation contract as <see cref="SherpaParakeetSttClient"/>; the only
/// differences are the model (a two-file sherpa-onnx CTC bundle, <c>model.int8.onnx</c> +
/// <c>tokens.txt</c>) and the recognizer config (<c>OfflineModelConfig.MedAsr</c> instead of the
/// transducer). Parakeet remains as the user-promotable fallback.
///
/// <para><b>Activation.</b> Dormant unless <c>RADIOPAD_LOCAL_STT_ENABLED</c> is truthy AND the
/// bundle is present under <c>%LOCALAPPDATA%\com.radiopad.desktop\models\medasr-ctc-en-int8</c>
/// (provisioned on first run — never in the MSI). The bundle is a public, ungated sherpa-onnx export
/// (no HF token / licence-click at download time).</para>
/// </summary>
public sealed class SherpaMedAsrSttClient : ILocalSttClient, ILocalSttEngine, IDisposable
{
    /// <summary>Provider name recorded on the result + audit (never PHI).</summary>
    public const string ProviderName = "local";

    /// <summary>Engine id used in ensemble hypotheses + calibration tables.</summary>
    public const string EngineName = "medasr";

    // Offline CTC does not expose a usable per-token confidence through the sherpa C# result, so
    // emit a fixed prior; the ROVER reconciler then flags disagreements rather than trusting a
    // missing signal — the safe default, mirroring the Parakeet client.
    private const double MedAsrConfidence = 0.85;

    private const int SampleRate = 16000;
    private const int FeatureDim = 80;

    private readonly IAudioDecoder _decoder;
    private readonly ILogger<SherpaMedAsrSttClient> _log;
    private readonly bool _enabled;
    private readonly string? _modelDir;
    private readonly object _gate = new();

    private string? _model;
    private string? _tokens;
    private OfflineRecognizer? _recognizer;
    private volatile bool _loadFailed;
    private volatile string? _lastError;

    public SherpaMedAsrSttClient(IAudioDecoder decoder, ILogger<SherpaMedAsrSttClient> log)
    {
        _decoder = decoder;
        _log = log;

        var flag = Environment.GetEnvironmentVariable("RADIOPAD_LOCAL_STT_ENABLED");
        _enabled = string.Equals(flag, "1", StringComparison.Ordinal)
            || string.Equals(flag, "true", StringComparison.OrdinalIgnoreCase);

        // Resolved lazily (see EnsureFilesResolved) so a model that finishes downloading AFTER
        // startup activates without a process restart.
        _modelDir = LocalSttModels.ResolveModelDir(LocalSttModels.MedAsrModelName);
    }

    /// <summary>
    /// True only when the engine is enabled, the decoder can run, and the MedASR CTC bundle is
    /// present and not already known-bad. The routing layer checks this before using MedASR — false
    /// means "fall back to Parakeet / the cloud path".
    /// </summary>
    public bool Available
    {
        get
        {
            if (!_enabled || _loadFailed || !_decoder.Available || _modelDir is null)
                return false;
            EnsureFilesResolved();
            return _model is not null && _tokens is not null;
        }
    }

    public async Task<TranscriptionResult> TranscribeAsync(Stream audio, string contentType, CancellationToken ct, string? mode = null)
    {
        if (!Available)
            throw new InvalidOperationException("MedASR on-device engine is not available");

        var sw = Stopwatch.StartNew();
        var samples = await _decoder.DecodeAsync(audio, contentType, ct);
        ct.ThrowIfCancellationRequested();
        var text = RecognizeText(samples);
        sw.Stop();

        return new TranscriptionResult(
            Text: text,
            Provider: ProviderName,
            Model: LocalSttModels.MedAsrModelName,
            LatencyMs: sw.ElapsedMilliseconds);
    }

    public string EngineId => EngineName;

    public string? LastError => _lastError;

    public async Task<EngineTranscript> RecognizeAsync(byte[] wavBytes, CancellationToken ct)
    {
        if (!Available)
            throw new InvalidOperationException("MedASR on-device engine is not available");

        using var ms = new MemoryStream(wavBytes, writable: false);
        var samples = await _decoder.DecodeAsync(ms, "audio/wav", ct);
        ct.ThrowIfCancellationRequested();
        var text = RecognizeText(samples);
        return new EngineTranscript(EngineName, SttText.Tokenize(text, MedAsrConfidence));
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
                // MedASR emits its own markup ({period} markers, [FINDINGS] tags) rather than plain
                // punctuation — translate it here, at the engine boundary, so every consumer (§5.2,
                // the formatter, the ROVER ensemble, the raw-transcript fallback) sees plain prose.
                return MedAsrTranscriptNormalizer.Normalize(stream.Result.Text);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _loadFailed = true;
            _lastError = $"{ex.GetType().Name}: {ex.Message}";
            _log.LogError(ex, "MedASR decode failed; disabling the on-device MedASR engine for this session");
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
            // sherpa-onnx has first-class MedASR support: a single CTC model path.
            config.ModelConfig.MedAsr.Model = _model!;
            config.ModelConfig.Tokens = _tokens!;
            config.ModelConfig.NumThreads = LocalSttModels.ResolveThreads();
            config.ModelConfig.Provider = LocalSttModels.ResolveProvider();
            config.ModelConfig.Debug = 0;

            _log.LogInformation(
                "Loading MedASR CTC model from {Dir} ({Threads} threads, {Provider})",
                _modelDir, config.ModelConfig.NumThreads, config.ModelConfig.Provider);

            _recognizer = new OfflineRecognizer(config);
            return _recognizer;
        }
    }

    private void EnsureFilesResolved()
    {
        if (_model is not null && _tokens is not null) return;
        lock (_gate)
        {
            if (_modelDir is null) return;
            var (model, tokens) = LocalSttModels.ResolveMedAsrFiles(_modelDir);
            _model = model;
            _tokens = tokens;
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
