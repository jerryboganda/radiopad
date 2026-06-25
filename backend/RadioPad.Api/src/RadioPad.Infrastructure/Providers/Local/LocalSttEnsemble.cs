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
    private const string PrimaryEngineId = SherpaParakeetSttClient.EngineName; // "parakeet"
    private const string SingleProvider = SherpaParakeetSttClient.ProviderName; // "local"
    private const string EnsembleProvider = "local_ensemble";

    private readonly IReadOnlyList<ILocalSttEngine> _engines;
    private readonly ILogger<LocalSttEnsemble> _log;

    public LocalSttEnsemble(IEnumerable<ILocalSttEngine> engines, ILogger<LocalSttEnsemble> log)
    {
        _engines = engines.ToList();
        _log = log;
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

        var a = ok.FirstOrDefault(h => h.EngineId == PrimaryEngineId) ?? ok[0];
        var b = ok.First(h => !ReferenceEquals(h, a));
        var reconciled = SttReconciler.Reconcile(a, b, BuildOptions());

        _log.LogInformation(
            "Ensemble reconciled {Engines}: {Flagged}/{Total} spans flagged for review",
            $"{a.EngineId}+{b.EngineId}", reconciled.FlaggedCount, reconciled.Spans.Count);

        return new TranscriptionResult(
            reconciled.Text, EnsembleProvider, $"{a.EngineId}+{b.EngineId}",
            sw.ElapsedMilliseconds, reconciled.Spans);
    }

    private static ILocalSttEngine PickPrimary(IReadOnlyList<ILocalSttEngine> engines)
        => engines.FirstOrDefault(e => e.EngineId == PrimaryEngineId) ?? engines[0];

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

    // Parakeet (transducer) is over-confident vs Whisper's token-prob, so scale it
    // down for a fair calibrated vote. A starting prior until reliability tables
    // are built from a held-out de-identified dictation set.
    private static ReconcileOptions BuildOptions()
        => new() { EngineScale = new Dictionary<string, double> { [PrimaryEngineId] = 0.9 } };
}
