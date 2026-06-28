using System.Diagnostics;
using Microsoft.Extensions.Logging;
using RadioPad.Application.Abstractions;
using RadioPad.Application.Stt;

namespace RadioPad.Infrastructure.Providers.Local;

/// <summary>
/// The manual cross-check pass. Re-runs the retained dictation audio through every
/// available on-device ASR engine (Parakeet + the platform speech engines when
/// present), reconciles them against the live draft via the N-way ROVER voter
/// (<see cref="SttReconciler.ReconcileMany"/>), and emits an original→corrected
/// list for the editor. The live draft is the backbone (so corrections are framed
/// as edits to it) and also votes, at a deliberately modest confidence so a
/// cross-engine agreement can outvote it. The optional LLM medical-accuracy pass
/// (and its opt-in UBAG route) are appended by later phases. CPU/system-RAM only.
/// </summary>
public sealed class CrossCheckService : ICrossCheckService
{
    private const string LiveEngineId = "live";
    private const double LiveConfidence = 0.5; // the draft is under review, not authoritative

    private readonly IReadOnlyList<ILocalSttEngine> _engines;
    private readonly ILogger<CrossCheckService> _log;

    public CrossCheckService(IEnumerable<ILocalSttEngine> engines, ILogger<CrossCheckService> log)
    {
        _engines = engines.ToList();
        _log = log;
    }

    public bool Available => _engines.Any(e => e.Available);

    public async Task<CrossCheckResult> RunAsync(
        byte[] wavAudio, string liveTranscript, CrossCheckOptions options, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();

        // Backbone = the live draft (whitespace tokens, modest confidence) — also
        // what the correction offsets are anchored to.
        var reference = BuildLiveHypothesis(liveTranscript);
        var hyps = new List<EngineTranscript> { reference };
        var engineIds = new List<string> { LiveEngineId };

        var available = _engines.Where(e => e.Available).ToList();
        var outcomes = await Task.WhenAll(available.Select(e => RunSafe(e, wavAudio, ct)));
        foreach (var h in outcomes)
        {
            if (h is not null && h.Tokens.Count > 0)
            {
                hyps.Add(h);
                engineIds.Add(h.EngineId);
            }
        }

        var reconciled = SttReconciler.ReconcileMany(hyps, BuildOptions());
        var corrections = CrossCheckDiff
            .BuildCorrections(liveTranscript, reconciled, options.SectionKey)
            .ToList();

        // Phase 4/5 — LLM medical-accuracy pass (+ optional UBAG) appended here.

        sw.Stop();
        _log.LogInformation(
            "Cross-check {Engines}: {N} corrections in {Ms} ms",
            string.Join("+", engineIds), corrections.Count, sw.ElapsedMilliseconds);

        return new CrossCheckResult(
            reconciled.Text, corrections, string.Join("+", engineIds), sw.ElapsedMilliseconds);
    }

    private static EngineTranscript BuildLiveHypothesis(string liveTranscript)
    {
        var tokens = CrossCheckDiff.Tokenize(liveTranscript)
            .Select(t => new SttToken(t.Text, LiveConfidence))
            .ToList();
        return new EngineTranscript(LiveEngineId, tokens);
    }

    private async Task<EngineTranscript?> RunSafe(ILocalSttEngine engine, byte[] bytes, CancellationToken ct)
    {
        try
        {
            return await engine.RecognizeAsync(bytes, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _log.LogWarning(ex, "cross-check engine {Engine} failed", engine.EngineId);
            return null;
        }
    }

    // Keep the transducer (Parakeet) scaled like the live ensemble so its
    // over-confidence doesn't dominate the vote.
    private static ReconcileOptions BuildOptions()
        => new()
        {
            EngineScale = new Dictionary<string, double>
            {
                [SherpaParakeetSttClient.EngineName] = 0.9,
            },
        };
}
