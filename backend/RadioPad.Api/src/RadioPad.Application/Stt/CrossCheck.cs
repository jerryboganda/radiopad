using System.Text.Json;
using System.Text.RegularExpressions;

namespace RadioPad.Application.Stt;

/// <summary>One raw correction item parsed from the LLM medical-accuracy pass.</summary>
public sealed record LlmCorrectionItem(
    string? Original, string? Corrected, string? Reason, string? Category, string? Severity);

/// <summary>
/// One AI correction surfaced by the cross-check pass: a wording change the
/// radiologist accepts or rejects, with enough provenance to explain it and
/// enough anchoring (char range + the original text) for the editor to highlight
/// it even if the section was edited since the pass started.
/// </summary>
public sealed record CrossCheckCorrection(
    string Id,
    string? SectionKey,
    string OriginalText,
    string CorrectedText,
    int StartOffset,
    int EndOffset,
    string Reason,
    string Category,
    string Source,
    double Confidence,
    string Severity);

/// <summary>The cross-check outcome: reconciled transcript + the correction list.</summary>
public sealed record CrossCheckResult(
    string Transcript,
    IReadOnlyList<CrossCheckCorrection> Corrections,
    string EngineIds,
    long LatencyMs);

/// <summary>Outcome of the hosted LLM medical-accuracy review pass.</summary>
public sealed record CrossCheckReviewResult(
    IReadOnlyList<CrossCheckCorrection> Corrections,
    string Provider,
    string Model,
    long LatencyMs);

/// <summary>Per-run options for a cross-check.</summary>
public sealed record CrossCheckOptions
{
    /// <summary>Route the LLM medical-accuracy pass through the UBAG cloud gateway (opt-in).</summary>
    public bool UseUbag { get; init; }

    /// <summary>Report section the audio was dictated into (echoed onto corrections).</summary>
    public string? SectionKey { get; init; }

    /// <summary>Tenant id for AI-gateway routing of the LLM pass.</summary>
    public string? Tenant { get; init; }
}

/// <summary>Lifecycle of an async cross-check job.</summary>
public enum CrossCheckState { Queued, Running, Completed, Failed }

/// <summary>An async cross-check job tracked by <see cref="Abstractions.ICrossCheckJobStore"/>.</summary>
public sealed class CrossCheckJob
{
    public required string Id { get; init; }
    public CrossCheckState State { get; set; } = CrossCheckState.Queued;
    /// <summary>Human-readable stage for the processing badge (e.g. "re-running engines").</summary>
    public string Stage { get; set; } = "queued";
    public CrossCheckResult? Result { get; set; }
    public string? Error { get; set; }
    public DateTimeOffset CreatedAt { get; init; }
}

/// <summary>
/// Pure helper: turn a reconciled N-way result into the editor's correction list,
/// anchoring each change to a char range in the live-draft string it was built
/// from. Deterministic + engine-free so it is unit tested in isolation.
/// </summary>
public static class CrossCheckDiff
{
    private static readonly Regex Word = new(@"\S+", RegexOptions.Compiled);

    /// <summary>Whitespace tokens of <paramref name="text"/> with their char ranges.</summary>
    public static IReadOnlyList<(string Text, int Start, int End)> Tokenize(string text)
    {
        var list = new List<(string, int, int)>();
        foreach (Match m in Word.Matches(text ?? string.Empty))
            list.Add((m.Value, m.Index, m.Index + m.Length));
        return list;
    }

    /// <summary>
    /// Build corrections from a reconciled result whose backbone (first hypothesis)
    /// was the whitespace tokens of <paramref name="liveTranscript"/>. Emits one
    /// correction per changed slot (original→corrected) and per inserted word.
    /// </summary>
    public static IReadOnlyList<CrossCheckCorrection> BuildCorrections(
        string liveTranscript, ReconciledResult reconciled, string? sectionKey = null)
    {
        var tokens = Tokenize(liveTranscript);
        int endOfText = liveTranscript?.Length ?? 0;
        var result = new List<CrossCheckCorrection>();
        int backboneIdx = 0;
        int seq = 0;

        foreach (var span in reconciled.Spans)
        {
            if (span.OriginalText == string.Empty)
            {
                // Inserted word (no backbone token consumed) — a zero-width anchor.
                int pos = backboneIdx < tokens.Count ? tokens[backboneIdx].Start : endOfText;
                result.Add(Make(ref seq, sectionKey, string.Empty, span, pos, pos));
                continue;
            }

            if (span.OriginalText != null) // changed backbone word
            {
                var (start, end) = backboneIdx < tokens.Count
                    ? (tokens[backboneIdx].Start, tokens[backboneIdx].End)
                    : (endOfText, endOfText);
                result.Add(Make(ref seq, sectionKey, span.OriginalText, span, start, end));
            }
            backboneIdx++; // unchanged or changed, a backbone token was consumed
        }

        return result;
    }

    /// <summary>
    /// Parse the LLM medical-accuracy pass output — a JSON array of
    /// {original, corrected, reason, category, severity} (or an object wrapping a
    /// "corrections" array). Tolerates ```json fences and free text (→ empty).
    /// </summary>
    public static IReadOnlyList<LlmCorrectionItem> ParseLlmCorrections(string? body)
    {
        var list = new List<LlmCorrectionItem>();
        if (string.IsNullOrWhiteSpace(body)) return list;

        var trimmed = body.Trim();
        if (trimmed.StartsWith("```"))
        {
            var nl = trimmed.IndexOf('\n');
            if (nl >= 0) trimmed = trimmed[(nl + 1)..];
            if (trimmed.EndsWith("```")) trimmed = trimmed[..^3];
            trimmed = trimmed.Trim();
        }

        try
        {
            using var doc = JsonDocument.Parse(trimmed);
            var root = doc.RootElement;
            JsonElement arr;
            if (root.ValueKind == JsonValueKind.Array)
                arr = root;
            else if (root.ValueKind == JsonValueKind.Object
                     && root.TryGetProperty("corrections", out var c)
                     && c.ValueKind == JsonValueKind.Array)
                arr = c;
            else
                return list;

            foreach (var el in arr.EnumerateArray())
            {
                if (el.ValueKind != JsonValueKind.Object) continue;
                list.Add(new LlmCorrectionItem(
                    Original: Str(el, "original"),
                    Corrected: Str(el, "corrected"),
                    Reason: Str(el, "reason"),
                    Category: Str(el, "category"),
                    Severity: Str(el, "severity")));
            }
        }
        catch (JsonException)
        {
            // Model returned free text — treat as "no corrections".
        }
        return list;
    }

    /// <summary>
    /// Anchor LLM corrections to char ranges by locating each item's original text
    /// in <paramref name="text"/> (first occurrence, case-insensitive). Items whose
    /// original can't be found are dropped (the client re-anchors what it can).
    /// </summary>
    public static IReadOnlyList<CrossCheckCorrection> AnchorLlmCorrections(
        string text, IEnumerable<LlmCorrectionItem> items, string? sectionKey, int startSeq = 1000)
    {
        var result = new List<CrossCheckCorrection>();
        int seq = startSeq;
        foreach (var it in items)
        {
            if (string.IsNullOrWhiteSpace(it.Original) || string.IsNullOrWhiteSpace(it.Corrected)) continue;
            int idx = (text ?? string.Empty).IndexOf(it.Original, StringComparison.OrdinalIgnoreCase);
            if (idx < 0) continue;
            var severity = NormalizeSeverity(it.Severity);
            result.Add(new CrossCheckCorrection(
                Id: $"llm-{seq++}",
                SectionKey: sectionKey,
                OriginalText: it.Original!,
                CorrectedText: it.Corrected!,
                StartOffset: idx,
                EndOffset: idx + it.Original!.Length,
                Reason: string.IsNullOrWhiteSpace(it.Reason) ? "medical accuracy" : it.Reason!,
                Category: string.IsNullOrWhiteSpace(it.Category) ? "medical" : it.Category!,
                Source: "llm",
                Confidence: 0d,
                Severity: severity));
        }
        return result;
    }

    private static string NormalizeSeverity(string? s) => (s ?? string.Empty).Trim().ToLowerInvariant() switch
    {
        "safety" or "critical" or "high" => "safety",
        "info" or "low" => "info",
        _ => "warning",
    };

    private static string? Str(JsonElement el, string name) =>
        el.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static CrossCheckCorrection Make(
        ref int seq, string? sectionKey, string original, ReconciledSpan span, int start, int end)
    {
        bool inserted = original.Length == 0;
        bool safety = span.Reason == "safety";
        string severity = safety ? "safety" : span.Flagged ? "warning" : "info";
        string category = safety ? "safety"
            : inserted ? "insertion"
            : "asr_disagreement";
        double confidence = span.Votes is { Count: > 0 }
            ? span.Votes.Max(v => v.Confidence)
            : 0d;
        return new CrossCheckCorrection(
            Id: $"xc-{seq++}",
            SectionKey: sectionKey,
            OriginalText: original,
            CorrectedText: span.Text,
            StartOffset: start,
            EndOffset: end,
            Reason: span.Reason ?? (inserted ? "inserted by agreement" : "engine cross-check"),
            Category: category,
            Source: span.Source,
            Confidence: confidence,
            Severity: severity);
    }
}
