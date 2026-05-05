using System.Text.RegularExpressions;
using RadioPad.Domain.Entities;

namespace RadioPad.Application.Services;

/// <summary>
/// PRD AI-007 — deterministic, fully-managed-from-admin-UI unsupported-claim
/// detector. We do NOT call a second LLM (to avoid extra cost, latency, and
/// audit complexity). Instead we tokenise the impression sentence-by-sentence
/// and require that a configurable fraction of its content tokens already
/// appears in either the Findings text, the StudyContext, or the tenant's
/// allow-list. Sentences below that threshold get a finding with rule id
/// <c>ai:unsupported_claim</c>. Severity, threshold, allow-list, and the
/// global on/off switch all live in <see cref="TenantSettings"/>.
/// </summary>
public sealed class HallucinationDetector
{
    private static readonly Regex SentenceSplit =
        new(@"(?<=[\.!\?])\s+", RegexOptions.Compiled);

    // Tokens that should never count as 'support' (stopwords, fillers).
    private static readonly HashSet<string> StopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "a","an","the","and","or","of","in","on","to","for","with","without",
        "is","are","was","were","be","been","no","not","this","that","these","those",
        "patient","study","report","impression","findings","there","also","may","note",
    };

    /// <summary>
    /// Returns one finding per impression sentence whose support fraction is
    /// below <see cref="TenantSettings.HallucinationMinSupport"/>. Empty when
    /// disabled or when nothing fails.
    /// </summary>
    public IReadOnlyList<UnsupportedClaim> Detect(Report report, TenantSettings settings)
    {
        if (!settings.HallucinationDetectionEnabled) return Array.Empty<UnsupportedClaim>();
        if (string.IsNullOrWhiteSpace(report.Impression)) return Array.Empty<UnsupportedClaim>();

        var supportText = string.Join(' ',
            report.Findings,
            report.Indication,
            report.Comparison,
            report.Study.Indication,
            report.Study.Comparison,
            report.Study.PriorReportSummary,
            settings.HallucinationAllowList);
        var supportTokens = Tokenise(supportText);

        var findings = new List<UnsupportedClaim>();
        var sentences = SentenceSplit.Split(report.Impression).Where(s => !string.IsNullOrWhiteSpace(s));
        foreach (var raw in sentences)
        {
            var tokens = Tokenise(raw);
            if (tokens.Count == 0) continue;
            var supported = tokens.Count(t => supportTokens.Contains(t));
            var fraction = (double)supported / tokens.Count;
            if (fraction < settings.HallucinationMinSupport)
            {
                findings.Add(new UnsupportedClaim(
                    Sentence: raw.Trim(),
                    SupportFraction: Math.Round(fraction, 3),
                    Severity: settings.HallucinationSeverity));
            }
        }
        return findings;
    }

    private static HashSet<string> Tokenise(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        return Regex.Matches(s, @"[A-Za-z0-9][A-Za-z0-9\-]+")
            .Select(m => m.Value.ToLowerInvariant())
            .Where(t => t.Length > 2 && !StopWords.Contains(t))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }
}

/// <param name="Sentence">Offending impression sentence.</param>
/// <param name="SupportFraction">0..1 fraction of content tokens supported.</param>
/// <param name="Severity">String severity name from <c>TenantSettings</c> (Info/Warning/Blocker).</param>
public sealed record UnsupportedClaim(string Sentence, double SupportFraction, string Severity);

/// <summary>
/// PRD STD-001/002 — terminology adapter integration point. The default
/// implementation is a no-op so RadioPad ships with no licensed data files.
/// Tenants with a RadLex / ACR RADS entitlement can plug in a custom adapter
/// via DI.
/// </summary>
public interface ITerminologyAdapter
{
    /// <summary>Maps a free-text term to a canonical id (e.g. RID12345). Null when unknown.</summary>
    Task<string?> MapAsync(string term, CancellationToken ct);
}

public sealed class NoOpTerminologyAdapter : ITerminologyAdapter
{
    public Task<string?> MapAsync(string term, CancellationToken ct) => Task.FromResult<string?>(null);
}
