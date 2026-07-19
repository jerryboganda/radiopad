using System.Diagnostics;
using Microsoft.Extensions.Logging;
using RadioPad.Application.Abstractions;
using RadioPad.Application.Stt;

namespace RadioPad.Infrastructure.Providers.Local;

/// <summary>
/// Phase 2 — the multi-engine orchestrator behind the <see cref="ILocalSttClient"/>
/// seam. It runs the available on-device engines on the same audio and:
/// <list type="bullet">
/// <item>with ensemble mode ON (<c>RADIOPAD_STT_ENSEMBLE</c>) and ≥2 engines
/// available, runs both in parallel and reconciles the two hypotheses via the
/// ROVER <see cref="SttReconciler"/> — cross-detecting disagreements + safety
/// tokens and surfacing them as flagged review spans;</item>
/// <item>otherwise transcribes with the single best engine.</item>
/// </list>
/// The single-engine on-device path (Phase 1) is just the n==1 case here, so the
/// orchestrator degrades gracefully (e.g. while the second model is still
/// downloading).
/// </summary>
public sealed class LocalSttEnsemble : ILocalSttClient
{
    private const string SingleProvider = SherpaParakeetSttClient.ProviderName; // "local"
    private const string EnsembleProvider = "local_ensemble";

    private readonly IReadOnlyList<ILocalSttEngine> _engines;
    private readonly ILogger<LocalSttEnsemble> _log;
    private readonly ILocalSttSettings _settings;

    public LocalSttEnsemble(
        IEnumerable<ILocalSttEngine> engines,
        ILogger<LocalSttEnsemble> log,
        ILocalSttSettings? settings = null)
    {
        _engines = engines.ToList();
        _log = log;
        _settings = settings ?? DefaultLocalSttSettings.Instance;
    }

    public bool Available => _engines.Any(e => e.Available);

    public async Task<TranscriptionResult> TranscribeAsync(Stream audio, string contentType, CancellationToken ct, string? mode = null)
    {
        var available = _engines.Where(e => e.Available).ToList();
        if (available.Count == 0)
            throw new InvalidOperationException("no on-device STT engine is available");

        byte[] bytes;
        using (var ms = new MemoryStream())
        {
            await audio.CopyToAsync(ms, ct);
            bytes = ms.ToArray();
        }

        var sw = Stopwatch.StartNew();

        // Per-request mode override (from the editor's engine picker) wins; else
        // the configured default (RADIOPAD_STT_ENSEMBLE).
        var useEnsemble = mode?.Trim().ToLowerInvariant() switch
        {
            "ensemble" => true,
            "single" => false,
            _ => LocalSttModels.IsEnsembleEnabled(),
        };

        // Single-engine path: ensemble off/not requested, or only one engine ready.
        if (!useEnsemble || available.Count < 2)
        {
            var only = await PickPrimary(available).RecognizeAsync(bytes, ct);
            sw.Stop();
            return new TranscriptionResult(only.Text, SingleProvider, only.EngineId, sw.ElapsedMilliseconds);
        }

        // Ensemble path: run two decorrelated engines in parallel, then reconcile.
        var primary = PickPrimary(available);
        var secondary = available.First(e => !ReferenceEquals(e, primary));
        var hyps = await Task.WhenAll(RunSafe(primary, bytes, ct), RunSafe(secondary, bytes, ct));
        sw.Stop();

        var ok = hyps.Where(h => h is not null).Cast<EngineTranscript>().ToList();
        if (ok.Count == 0)
            throw new InvalidOperationException("all on-device engines failed");
        if (ok.Count == 1)
            return new TranscriptionResult(ok[0].Text, SingleProvider, ok[0].EngineId, sw.ElapsedMilliseconds);

        var a = ok.FirstOrDefault(h => h.EngineId == _settings.PrimaryEngineId) ?? ok[0];
        var b = ok.First(h => !ReferenceEquals(h, a));
        var reconciled = SttReconciler.Reconcile(a, b, BuildOptions());

        _log.LogInformation(
            "Ensemble reconciled {Engines}: {Flagged}/{Total} spans flagged for review",
            $"{a.EngineId}+{b.EngineId}", reconciled.FlaggedCount, reconciled.Spans.Count);

        return new TranscriptionResult(
            reconciled.Text, EnsembleProvider, $"{a.EngineId}+{b.EngineId}",
            sw.ElapsedMilliseconds, reconciled.Spans);
    }

    /// <summary>
    /// Deliberate fallback order when the configured primary has no backend engine to run.
    ///
    /// <para>This is not hypothetical: the Edge Web Speech engine is frontend-only, so whenever it
    /// is the user's primary, EVERY sidecar transcription falls back. Before this order existed the
    /// fallback was <c>engines[0]</c> — i.e. whichever engine DI happened to register first — which
    /// silently routed dictation to Parakeet even with MedASR installed and available, defeating
    /// decision D2. Registration order is an implementation detail and must never decide which
    /// engine transcribes a radiologist's dictation.</para>
    /// </summary>
    private static readonly string[] FallbackEngineOrder =
    {
        SherpaMedAsrSttClient.EngineName,      // D2: the radiology-tuned default primary
        SherpaParakeetSttClient.EngineName,    // general-purpose alternative
        LocalModelCatalog.WindowsSapiEngine,   // always-present Windows recognizer
    };

    private ILocalSttEngine PickPrimary(IReadOnlyList<ILocalSttEngine> engines)
    {
        var configured = engines.FirstOrDefault(e => e.EngineId == _settings.PrimaryEngineId);
        if (configured is not null) return configured;

        foreach (var engineId in FallbackEngineOrder)
        {
            var match = engines.FirstOrDefault(e => e.EngineId == engineId);
            if (match is not null)
            {
                _log.LogInformation(
                    "Configured primary STT engine {Configured} has no on-device implementation; " +
                    "falling back to {Fallback}.", _settings.PrimaryEngineId, match.EngineId);
                return match;
            }
        }

        return engines[0];
    }

    private async Task<EngineTranscript?> RunSafe(ILocalSttEngine engine, byte[] bytes, CancellationToken ct)
    {
        try
        {
            return await engine.RecognizeAsync(bytes, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _log.LogWarning(ex, "on-device engine {Engine} failed in the ensemble", engine.EngineId);
            return null;
        }
    }

    // Parakeet (transducer) is over-confident vs a calibrated token-prob, so scale
    // it down for a fair calibrated vote regardless of which engine leads. A starting
    // prior until reliability tables are built from a held-out dictation set.
    private static ReconcileOptions BuildOptions()
        => new() { EngineScale = new Dictionary<string, double> { [SherpaParakeetSttClient.EngineName] = 0.9 } };
}
