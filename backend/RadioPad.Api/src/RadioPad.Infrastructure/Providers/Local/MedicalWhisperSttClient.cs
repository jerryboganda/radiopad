using Microsoft.Extensions.Logging;
using RadioPad.Application.Abstractions;
using RadioPad.Application.Stt;
using RadioPad.Infrastructure.Audio;
using Whisper.net;

namespace RadioPad.Infrastructure.Providers.Local;

/// <summary>
/// Phase 3 — the 3rd, decorrelated cross-check engine: a medical-domain Whisper
/// run through whisper.cpp / Whisper.net on CPU + system RAM (no GPU), pinned to
/// its OWN model (full, non-distilled large-v3 by default; swap in a converted
/// medical fine-tune by dropping its ggml in the model dir — see
/// <see cref="LocalSttModels.MedicalWhisperModelName"/>). Unlike
/// <see cref="WhisperNetSttClient"/> — which tracks the operator's *primary*
/// whisper selection — this engine is fixed to the medical model so the
/// cross-check always gets a third, independent voice distinct from both the
/// distilled-turbo live model and the Parakeet transducer (different errors =
/// what ROVER needs to reduce WER).
///
/// Dormant unless the local engine is enabled AND the medical model is present, so
/// it is inert on web/server and until the model is provisioned on demand — at
/// which point it joins the cross-check automatically with no other change.
/// </summary>
public sealed class MedicalWhisperSttClient : ILocalSttEngine, IDisposable
{
    public const string EngineName = "whisper_medical";

    private readonly IAudioDecoder _decoder;
    private readonly ILogger<MedicalWhisperSttClient> _log;
    private readonly bool _enabled;
    private readonly object _gate = new();
    private WhisperFactory? _factory;
    private volatile bool _loadFailed;
    private volatile string? _lastError;

    public MedicalWhisperSttClient(IAudioDecoder decoder, ILogger<MedicalWhisperSttClient> log)
    {
        _decoder = decoder;
        _log = log;
        _enabled = LocalSttModels.IsEnabled();
    }

    public string EngineId => EngineName;

    public string? LastError => _lastError;

    public bool Available
    {
        get
        {
            if (!_enabled || _loadFailed) return false;
            return LocalSttModels.ResolveMedicalWhisperBin() is not null;
        }
    }

    public async Task<EngineTranscript> RecognizeAsync(byte[] wavBytes, CancellationToken ct)
    {
        if (!Available)
            throw new InvalidOperationException("medical whisper engine is not available");

        var factory = GetFactory();
        try
        {
            var tokens = await WhisperDecoder.DecodeAsync(
                factory, _decoder, wavBytes, LocalSttModels.ResolveWhisperPrompt(), ct);
            return new EngineTranscript(EngineName, tokens);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _loadFailed = true;
            _lastError = $"{ex.GetType().Name}: {ex.Message}";
            _log.LogError(ex, "medical whisper recognize failed; disabling engine for this session");
            throw;
        }
    }

    private WhisperFactory GetFactory()
    {
        lock (_gate)
        {
            if (_factory is not null) return _factory;

            var bin = LocalSttModels.ResolveMedicalWhisperBin()
                ?? throw new InvalidOperationException("medical whisper model is not present");

            // Stage the native whisper.cpp runtime before the first factory is
            // created (single-file desktop sidecar has no on-disk runtimes/win-x64).
            WhisperNativeLibrary.EnsureLoaded();

            _log.LogInformation("Loading medical Whisper model from {Bin}", bin);
            _factory = WhisperFactory.FromPath(bin);
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
