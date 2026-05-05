namespace RadioPad.Domain.ValueObjects;

/// <summary>Outcome of a single rulebook check applied to a report.</summary>
public sealed record ValidationFinding(
    string RuleId,
    string Severity,
    string Message,
    string? Section = null,
    string? Snippet = null);

public sealed record ValidationResult(
    bool BlockerPresent,
    IReadOnlyList<ValidationFinding> Findings)
{
    public static ValidationResult Empty { get; } = new(false, Array.Empty<ValidationFinding>());
}

public sealed record AiResult(
    string Text,
    string Provider,
    string Model,
    int LatencyMs,
    int InputTokens,
    int OutputTokens,
    string PromptVersion);
