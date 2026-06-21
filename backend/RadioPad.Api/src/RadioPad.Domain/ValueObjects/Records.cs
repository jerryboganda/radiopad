namespace RadioPad.Domain.ValueObjects;

/// <summary>Outcome of a single rulebook check applied to a report.</summary>
/// <remarks>
/// Iter-0b (PRD AI-021/028, §16.3/16.5, RPT-026) — <see cref="StartIndex"/> /
/// <see cref="EndIndex"/> are optional char-offset span anchors into the named
/// <see cref="Section"/> text. They carry source-span provenance so the
/// "Why this suggestion?" panel and the span-attested hallucination guard can
/// highlight the exact text a finding refers to. Null when a finding is not
/// span-anchored.
/// </remarks>
public sealed record ValidationFinding(
    string RuleId,
    string Severity,
    string Message,
    string? Section = null,
    string? Snippet = null,
    int? StartIndex = null,
    int? EndIndex = null);

public sealed record ValidationResult(
    bool BlockerPresent,
    IReadOnlyList<ValidationFinding> Findings,
    int QualityScore)
{
    public static ValidationResult Empty { get; } = new(false, Array.Empty<ValidationFinding>(), 100);
}

public sealed record AiResult(
    string Text,
    string Provider,
    string Model,
    int LatencyMs,
    int InputTokens,
    int OutputTokens,
    string PromptVersion);

/// <summary>
/// PRD §18.2 — result of a single model-drift regression check for one
/// provider + rulebook combination. Immutable value object returned by
/// <c>ModelDriftDetectionService</c> and surfaced via the admin drift API.
/// </summary>
public sealed record DriftCheckResult(
    string ProviderId,
    string RulebookId,
    int BaselineQualityScore,
    int CurrentQualityScore,
    int ScoreDelta,
    List<string> NewBlockerRules,
    List<string> ResolvedRules,
    DateTimeOffset CheckedAt,
    bool DriftDetected);
