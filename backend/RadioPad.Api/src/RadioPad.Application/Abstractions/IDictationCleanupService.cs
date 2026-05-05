using RadioPad.Domain.Entities;

namespace RadioPad.Application.Abstractions;

/// <summary>
/// Iter-31 AI-001 — runs the dictation-cleanup AI flow for a single report.
/// The implementation routes through <see cref="IAiGateway"/> so PHI policy,
/// usage ledger, and audit logging are enforced uniformly with every other
/// AI flow. The output is a structured set of cleaned report sections that
/// the editor can adopt one section at a time.
/// </summary>
public interface IDictationCleanupService
{
    Task<DictationCleanupResult> CleanupAsync(
        Tenant tenant,
        User user,
        Report report,
        string rawDictation,
        CancellationToken ct);
}

/// <summary>
/// Per-section cleaned narrative produced by <see cref="IDictationCleanupService"/>.
/// Sections that the model could not extract come back as the empty string
/// (never null) so the UI can apply or skip them deterministically.
/// </summary>
public sealed record DictationCleanupResult(
    string Indication,
    string Technique,
    string Findings,
    string Impression,
    string Recommendations,
    string Provider,
    string Model,
    int LatencyMs,
    string PromptVersion);
