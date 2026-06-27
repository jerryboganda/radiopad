using System.Runtime.Versioning;
using System.Speech.AudioFormat;
using System.Speech.Recognition;
using Microsoft.Extensions.Logging;
using RadioPad.Application.Abstractions;
using RadioPad.Application.Stt;
using RadioPad.Infrastructure.Audio;

namespace RadioPad.Infrastructure.Providers.Local;

/// <summary>
/// On-device speech-to-text via the classic Windows Speech Recognition engine
/// (System.Speech / SAPI 5). It ships with Windows, runs entirely on the CPU, and
/// never sends audio off the machine — so it is PHI-safe (<c>LocalOnly</c>) and
/// needs no model download. This is the robust default dictation engine on the
/// desktop.
///
/// <para>Audio is decoded to 16 kHz mono via the shared <see cref="IAudioDecoder"/>
/// (same path as Parakeet/Whisper) and fed to SAPI as 16-bit PCM through
/// <see cref="SpeechRecognitionEngine.SetInputToAudioStream"/>, so recognition is
/// independent of the source WAV's container/format.</para>
///
/// <para>Dormant unless the local engine is enabled
/// (<c>RADIOPAD_LOCAL_STT_ENABLED</c>) AND the host is Windows with a speech
/// recognizer installed — inert on web / server / Linux. System.Speech is
/// synchronous and not thread-safe, so decode is serialized behind a lock and run
/// on a worker thread.</para>
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsSapiSttClient : ILocalSttEngine, IDisposable
{
    public const string EngineName = LocalModelCatalog.WindowsSapiEngine; // "windows_sapi"

    private const int SampleRate = WavAudioDecoder.TargetSampleRate; // 16 kHz

    private readonly IAudioDecoder _decoder;
    private readonly ILogger<WindowsSapiSttClient> _log;
    private readonly bool _enabled;
    private readonly object _gate = new();

    private SpeechRecognitionEngine? _engine;
    private volatile bool _loadFailed;
    private volatile string? _lastError;

    public WindowsSapiSttClient(IAudioDecoder decoder, ILogger<WindowsSapiSttClient> log)
    {
        _decoder = decoder;
        _log = log;
        _enabled = LocalSttModels.IsEnabled();
    }

    public string EngineId => EngineName;

    public string? LastError => _lastError;

    /// <summary>
    /// True only when enabled, running on Windows with the decoder available, a
    /// recognizer is installed, and the engine has not self-disabled on a prior
    /// failure. False ⇒ the manager shows the card as unavailable.
    /// </summary>
    public bool Available
    {
        get
        {
            if (!_enabled || _loadFailed || !_decoder.Available)
                return false;
            return IsRecognizerInstalled();
        }
    }

    /// <summary>
    /// True when a Windows desktop speech recognizer is installed AND its SAPI COM
    /// engine is registered. Returns false (never throws) when it is missing — which
    /// is common on Windows 11, where the legacy Windows Speech Recognition feature
    /// is frequently absent (the call then throws <c>REGDB_E_CLASSNOTREG</c>). Used
    /// both for <see cref="Available"/> and to pick a sensible default primary engine.
    /// </summary>
    public static bool IsRecognizerInstalled()
    {
        if (!OperatingSystem.IsWindows()) return false;
        try { return SpeechRecognitionEngine.InstalledRecognizers().Count > 0; }
        catch { return false; }
    }

    public async Task<EngineTranscript> RecognizeAsync(byte[] wavBytes, CancellationToken ct)
    {
        if (!Available)
            throw new InvalidOperationException("windows speech (SAPI) engine is not available");

        using var ms = new MemoryStream(wavBytes, writable: false);
        var samples = await _decoder.DecodeAsync(ms, "audio/wav", ct);
        ct.ThrowIfCancellationRequested();

        // SAPI is synchronous + not thread-safe — recognize on a worker thread,
        // serialized behind the engine lock.
        return await Task.Run(() => Recognize(samples, ct), ct);
    }

    private EngineTranscript Recognize(float[] samples, CancellationToken ct)
    {
        try
        {
            lock (_gate)
            {
                var engine = GetEngine();
                using var pcm = new MemoryStream(ToPcm16(samples), writable: false);
                engine.SetInputToAudioStream(
                    pcm, new SpeechAudioFormatInfo(SampleRate, AudioBitsPerSample.Sixteen, AudioChannel.Mono));

                var tokens = new List<SttToken>();
                RecognitionResult? r;
                // Recognize successive phrases until end-of-stream (returns null).
                while (!ct.IsCancellationRequested && (r = engine.Recognize()) is not null)
                {
                    foreach (var w in r.Words)
                    {
                        var text = w.Text?.Trim();
                        if (!string.IsNullOrEmpty(text))
                            tokens.Add(new SttToken(text, Math.Clamp((double)w.Confidence, 0d, 1d)));
                    }
                }
                ct.ThrowIfCancellationRequested();
                return new EngineTranscript(EngineName, tokens);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _loadFailed = true;
            _lastError = $"{ex.GetType().Name}: {ex.Message}";
            _log.LogError(ex, "windows SAPI recognize failed; disabling engine for this session");
            throw;
        }
    }

    private SpeechRecognitionEngine GetEngine()
    {
        if (_engine is not null) return _engine;
        var engine = new SpeechRecognitionEngine();
        engine.LoadGrammar(new DictationGrammar());
        _log.LogInformation(
            "Loaded Windows SAPI dictation recognizer '{Recognizer}'", engine.RecognizerInfo?.Name);
        _engine = engine;
        return _engine;
    }

    /// <summary>Convert normalized float samples (-1..1) to little-endian 16-bit PCM.</summary>
    private static byte[] ToPcm16(float[] samples)
    {
        var bytes = new byte[samples.Length * 2];
        for (int i = 0; i < samples.Length; i++)
        {
            var s = (short)(Math.Clamp(samples[i], -1f, 1f) * short.MaxValue);
            bytes[i * 2] = (byte)(s & 0xFF);
            bytes[i * 2 + 1] = (byte)((s >> 8) & 0xFF);
        }
        return bytes;
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _engine?.Dispose();
            _engine = null;
        }
    }
}
