using System.Text.RegularExpressions;

namespace RadioPad.Application.Stt;

/// <summary>A single recognised word + the engine's confidence in it (0..1).</summary>
public sealed record SttToken(string Text, double Confidence);

/// <summary>One engine's hypothesis for an utterance.</summary>
public sealed record EngineTranscript(string EngineId, IReadOnlyList<SttToken> Tokens)
{
    public string Text => string.Join(" ", Tokens.Select(t => t.Text));
}

/// <summary>One engine's vote for a reconciled slot (used by N-way cross-check).</summary>
public sealed record SpanVote(string EngineId, string Text, double Confidence);

/// <summary>
/// One reconciled output word. <see cref="Flagged"/> marks spans the radiologist
/// must eye-confirm (rendered as an <c>.ai-mark</c> review span in the editor).
/// <see cref="OriginalText"/> + <see cref="Votes"/> are populated by the N-way
/// cross-check (<see cref="SttReconciler.ReconcileMany"/>): OriginalText is the
/// reference/live-draft word this slot replaced (null when unchanged, "" for an
/// inserted word), enabling an original→corrected diff. They are optional so the
/// 2-way <see cref="SttReconciler.Reconcile"/> and its callers are unaffected.
/// </summary>
public sealed record ReconciledSpan(
    string Text,
    bool Flagged,
    string? Reason,
    string Source,
    string? OriginalText = null,
    IReadOnlyList<SpanVote>? Votes = null);

/// <summary>The reconciled transcript plus its per-word provenance/flags.</summary>
public sealed record ReconciledResult(string Text, IReadOnlyList<ReconciledSpan> Spans)
{
    public int FlaggedCount => Spans.Count(s => s.Flagged);
}

public sealed class ReconcileOptions
{
    /// <summary>Below this calibrated confidence a word is flagged for review.</summary>
    public double LowConfidence { get; init; } = 0.55;

    /// <summary>When two engines disagree and the calibrated-confidence gap is
    /// narrower than this, the winner is flagged (the vote isn't trustworthy).</summary>
    public double DisagreementMargin { get; init; } = 0.15;

    /// <summary>Per-engine multiplicative confidence calibration (transducers run
    /// hot relative to Whisper's token-prob). Missing engines use 1.0.</summary>
    public IReadOnlyDictionary<string, double>? EngineScale { get; init; }
}

/// <summary>
/// ROVER-style reconciliation of two ASR hypotheses into one transcript. Word-
/// aligns the hypotheses (edit-distance DP with NULL arcs — a confusion network),
/// votes each slot by CALIBRATED confidence, and FLAGS rather than silently
/// resolves any span that is a disagreement with a narrow/low-confidence vote, an
/// insertion/deletion, or a patient-safety-critical token (laterality, negation,
/// hypo/hyper, or any numeric measurement/dose). Pure + deterministic — the
/// engines that produce the hypotheses are tested separately.
/// </summary>
public static class SttReconciler
{
    public static ReconciledResult Reconcile(EngineTranscript a, EngineTranscript b, ReconcileOptions? options = null)
    {
        options ??= new ReconcileOptions();
        var spans = new List<ReconciledSpan>();

        foreach (var (ai, bi) in Align(a.Tokens, b.Tokens))
        {
            if (ai is not null && bi is not null)
            {
                var ta = a.Tokens[ai.Value];
                var tb = b.Tokens[bi.Value];
                var ca = Calib(a.EngineId, ta.Confidence, options);
                var cb = Calib(b.EngineId, tb.Confidence, options);

                if (WordsEqual(ta.Text, tb.Text))
                {
                    var safety = IsHighRisk(ta.Text);
                    var low = Math.Min(ca, cb) < options.LowConfidence;
                    spans.Add(new ReconciledSpan(
                        ta.Text, safety || low,
                        safety ? "safety" : low ? "low-confidence" : null, "both"));
                }
                else
                {
                    // ROVER vote by calibrated confidence. Accept a confident,
                    // wide-margin winner silently; flag only when the vote is
                    // narrow, both engines are low, or the token is safety-critical.
                    var winner = ca >= cb ? ta : tb;
                    var source = ca >= cb ? a.EngineId : b.EngineId;
                    var narrow = Math.Abs(ca - cb) < options.DisagreementMargin;
                    var low = Math.Max(ca, cb) < options.LowConfidence;
                    var safety = IsHighRisk(ta.Text) || IsHighRisk(tb.Text);
                    var flagged = narrow || low || safety;
                    spans.Add(new ReconciledSpan(
                        winner.Text, flagged,
                        safety ? "safety" : flagged ? "disagreement" : null,
                        source));
                }
            }
            else if (ai is not null)
            {
                var ta = a.Tokens[ai.Value];
                spans.Add(new ReconciledSpan(ta.Text, true,
                    IsHighRisk(ta.Text) ? "safety" : "insert-delete", a.EngineId));
            }
            else
            {
                var tb = b.Tokens[bi!.Value];
                spans.Add(new ReconciledSpan(tb.Text, true,
                    IsHighRisk(tb.Text) ? "safety" : "insert-delete", b.EngineId));
            }
        }

        return new ReconciledResult(string.Join(" ", spans.Select(s => s.Text)), spans);
    }

    /// <summary>
    /// N-way ROVER cross-check. Uses the FIRST hypothesis as the backbone (the
    /// live draft being verified), aligns every other hypothesis to it, and votes
    /// each backbone slot by summed CALIBRATED confidence. The winner's surface
    /// form replaces the slot and <see cref="ReconciledSpan.OriginalText"/> records
    /// the backbone word (null when unchanged, "" for an inserted word) so callers
    /// can build an original→corrected diff. Conservative: it never deletes a
    /// dictated backbone word, and only emits an inserted word when EVERY other
    /// engine agrees on it. Changed / narrow / low-confidence / patient-safety
    /// slots are flagged. One hypothesis ⇒ returned verbatim.
    /// </summary>
    public static ReconciledResult ReconcileMany(
        IReadOnlyList<EngineTranscript> hypotheses, ReconcileOptions? options = null)
    {
        if (hypotheses is null || hypotheses.Count == 0)
            return new ReconciledResult(string.Empty, Array.Empty<ReconciledSpan>());
        options ??= new ReconcileOptions();

        var reference = hypotheses[0];
        var others = hypotheses.Skip(1).ToList();
        int n = reference.Tokens.Count;

        // votesPerSlot[i] = candidate tokens aligned to reference token i (the
        // backbone vote is always first), one per engine that has a token there.
        var votesPerSlot = new List<SpanVote>[n];
        for (int i = 0; i < n; i++)
        {
            votesPerSlot[i] = new List<SpanVote>
            {
                new(reference.EngineId, reference.Tokens[i].Text,
                    Calib(reference.EngineId, reference.Tokens[i].Confidence, options)),
            };
        }

        // insertionsAtGap[g] = words other engines inserted before reference slot g.
        var insertionsAtGap = new Dictionary<int, List<SpanVote>>();

        foreach (var other in others)
        {
            int refConsumed = 0;
            foreach (var (ri, oi) in Align(reference.Tokens, other.Tokens))
            {
                if (ri is not null && oi is not null)
                {
                    var tok = other.Tokens[oi.Value];
                    votesPerSlot[ri.Value].Add(new SpanVote(
                        other.EngineId, tok.Text, Calib(other.EngineId, tok.Confidence, options)));
                    refConsumed = ri.Value + 1;
                }
                else if (ri is not null)
                {
                    refConsumed = ri.Value + 1; // other engine deleted this slot — no vote
                }
                else
                {
                    var tok = other.Tokens[oi!.Value];
                    if (!insertionsAtGap.TryGetValue(refConsumed, out var list))
                        insertionsAtGap[refConsumed] = list = new List<SpanVote>();
                    list.Add(new SpanVote(
                        other.EngineId, tok.Text, Calib(other.EngineId, tok.Confidence, options)));
                }
            }
        }

        var spans = new List<ReconciledSpan>();
        for (int i = 0; i <= n; i++)
        {
            EmitInsertions(spans, insertionsAtGap, i, others.Count);
            if (i == n) break;
            spans.Add(VoteSlot(votesPerSlot[i], options));
        }

        return new ReconciledResult(string.Join(" ", spans.Select(s => s.Text)), spans);
    }

    private static ReconciledSpan VoteSlot(List<SpanVote> votes, ReconcileOptions o)
    {
        var refWord = votes[0].Text; // backbone vote is always first
        var refNorm = Norm(refWord);

        // ROVER confidence voting: sum calibrated confidence per distinct word.
        var byWord = votes
            .GroupBy(v => Norm(v.Text))
            .Select(g => new
            {
                Norm = g.Key,
                Sum = g.Sum(v => v.Confidence),
                Best = g.OrderByDescending(v => v.Confidence).First(),
            })
            .ToList();

        var refEntry = byWord.First(w => w.Norm == refNorm);
        var top = byWord.OrderByDescending(w => w.Sum).First();
        // Prefer the backbone word on a tie — never change without a clear winner.
        var winner = top.Sum > refEntry.Sum ? top : refEntry;

        bool changed = winner.Norm != refNorm;
        bool safety = votes.Any(v => IsHighRisk(v.Text));
        bool low = winner.Best.Confidence < o.LowConfidence;
        bool narrow = changed && (winner.Sum - refEntry.Sum) < o.DisagreementMargin;
        bool flagged = safety || low || (changed && narrow);

        string? reason = safety ? "safety"
            : changed ? "disagreement"
            : low ? "low-confidence"
            : null;

        return new ReconciledSpan(
            winner.Best.Text, flagged, reason, winner.Best.EngineId,
            OriginalText: changed ? refWord : null,
            Votes: votes);
    }

    private static void EmitInsertions(
        List<ReconciledSpan> spans, Dictionary<int, List<SpanVote>> insertionsAtGap,
        int gap, int otherCount)
    {
        if (otherCount == 0 || !insertionsAtGap.TryGetValue(gap, out var inserts)) return;
        // Insert a word the radiologist didn't dictate only when EVERY other engine
        // agrees on it (conservative — avoids hallucinated additions).
        foreach (var g in inserts.GroupBy(v => Norm(v.Text)))
        {
            if (g.Count() < otherCount) continue;
            var best = g.OrderByDescending(v => v.Confidence).First();
            spans.Add(new ReconciledSpan(
                best.Text, true, IsHighRisk(best.Text) ? "safety" : "insert",
                best.EngineId, OriginalText: string.Empty, Votes: g.ToList()));
        }
    }

    /// <summary>
    /// Edit-distance alignment with backtrace. Returns aligned index pairs; a null
    /// on either side is a NULL arc (insertion / deletion).
    /// </summary>
    private static List<(int? a, int? b)> Align(IReadOnlyList<SttToken> a, IReadOnlyList<SttToken> b)
    {
        int n = a.Count, m = b.Count;
        var cost = new int[n + 1, m + 1];
        for (int i = 0; i <= n; i++) cost[i, 0] = i;
        for (int j = 0; j <= m; j++) cost[0, j] = j;

        for (int i = 1; i <= n; i++)
            for (int j = 1; j <= m; j++)
            {
                int sub = cost[i - 1, j - 1] + (WordsEqual(a[i - 1].Text, b[j - 1].Text) ? 0 : 1);
                int del = cost[i - 1, j] + 1;
                int ins = cost[i, j - 1] + 1;
                cost[i, j] = Math.Min(sub, Math.Min(del, ins));
            }

        var pairs = new List<(int?, int?)>();
        int x = n, y = m;
        while (x > 0 || y > 0)
        {
            if (x > 0 && y > 0 &&
                cost[x, y] == cost[x - 1, y - 1] + (WordsEqual(a[x - 1].Text, b[y - 1].Text) ? 0 : 1))
            {
                pairs.Add((x - 1, y - 1)); x--; y--;
            }
            else if (x > 0 && cost[x, y] == cost[x - 1, y] + 1)
            {
                pairs.Add((x - 1, null)); x--;
            }
            else
            {
                pairs.Add((null, y - 1)); y--;
            }
        }
        pairs.Reverse();
        return pairs;
    }

    private static double Calib(string engineId, double conf, ReconcileOptions o)
        => o.EngineScale is not null && o.EngineScale.TryGetValue(engineId, out var s)
            ? Math.Clamp(conf * s, 0, 1)
            : conf;

    private static string Norm(string w) =>
        new string(w.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();

    private static bool WordsEqual(string a, string b) => Norm(a) == Norm(b);

    // Patient-safety tokens ASR is worst at — always surfaced for human verify.
    private static readonly Regex LateralityNegation =
        new(@"^(left|right|bilateral|no|not|without|absent|negative|denies|known)$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex HypoHyper =
        new(@"^(hypo|hyper)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex HasDigit = new(@"\d", RegexOptions.Compiled);

    private static bool IsHighRisk(string word)
    {
        if (HasDigit.IsMatch(word)) return true; // measurements / doses
        var n = Norm(word);
        return n.Length > 0 && (LateralityNegation.IsMatch(n) || HypoHyper.IsMatch(n));
    }
}
