namespace RadioPad.Application.Dictation;

/// <summary>Why the §5.3 validation pass rejected the formatter output.</summary>
public enum ValidationRejectReason
{
    AddedNumber,
    AddedMeasurement,
    AddedDate,
    MissingRequiredSection,
}

/// <summary>A single §5.3 validation failure.</summary>
public sealed record ValidationViolation(ValidationRejectReason Reason, string Detail);

/// <summary>Outcome of the §5.3 validation pass.</summary>
/// <param name="Accepted">True when the formatter output is safe to show as an editable draft.</param>
/// <param name="Violations">Empty when accepted; otherwise the reasons it was rejected.</param>
/// <param name="FallbackText">The dictionary-corrected raw transcript to show INSTEAD of the LLM
/// output when rejected (brief §5.3 — fail safe, never fail silent).</param>
public sealed record DictationValidationResult(
    bool Accepted,
    IReadOnlyList<ValidationViolation> Violations,
    string FallbackText);

/// <summary>
/// Brief §5.3 — runs AFTER the LLM formatter, before display. Diffs the formatter output against the
/// deterministically-protected transcript and REJECTS the output if it introduces a number,
/// measurement or date absent from the source, or drops a required section. On rejection the caller
/// must show <see cref="DictationValidationResult.FallbackText"/> (the dictionary-corrected raw
/// transcript) rather than the LLM output — fail safe, never fail silent.
///
/// Set membership (not multiset) is used deliberately: a measurement dictated once but legitimately
/// echoed into both Findings and Impression must NOT be rejected, while a value that appears nowhere
/// in the source is always caught.
/// </summary>
public sealed class DictationValidationService
{
    public DictationValidationResult Validate(
        PassThroughResult passThrough,
        IReadOnlyDictionary<string, string> formattedSections,
        IReadOnlyCollection<string> requiredSections)
    {
        var sourceTokens = DeterministicPassThrough.ExtractLockedTokens(passThrough.CorrectedTranscript);

        var outputText = string.Join("\n", (formattedSections ?? new Dictionary<string, string>()).Values);
        var outputTokens = DeterministicPassThrough.ExtractLockedTokens(outputText);

        var srcMeasurements = Set(sourceTokens, LockedTokenKind.Measurement, StringComparer.OrdinalIgnoreCase);
        var srcDates = Set(sourceTokens, LockedTokenKind.Date, StringComparer.OrdinalIgnoreCase);
        var srcNumbers = Set(sourceTokens, LockedTokenKind.Number, StringComparer.Ordinal);

        var violations = new List<ValidationViolation>();
        var reported = new HashSet<string>(StringComparer.Ordinal);

        foreach (var t in outputTokens)
        {
            switch (t.Kind)
            {
                case LockedTokenKind.Measurement when !srcMeasurements.Contains(t.Text) && reported.Add("M:" + t.Text):
                    violations.Add(new ValidationViolation(ValidationRejectReason.AddedMeasurement,
                        $"measurement '{t.Text}' not present in the dictation"));
                    break;
                case LockedTokenKind.Date when !srcDates.Contains(t.Text) && reported.Add("D:" + t.Text):
                    violations.Add(new ValidationViolation(ValidationRejectReason.AddedDate,
                        $"date '{t.Text}' not present in the dictation"));
                    break;
                case LockedTokenKind.Number when !srcNumbers.Contains(t.Text) && reported.Add("N:" + t.Text):
                    violations.Add(new ValidationViolation(ValidationRejectReason.AddedNumber,
                        $"number '{t.Text}' not present in the dictation"));
                    break;
            }
        }

        foreach (var key in requiredSections ?? Array.Empty<string>())
        {
            if (formattedSections is null
                || !formattedSections.TryGetValue(key, out var value)
                || string.IsNullOrWhiteSpace(value))
            {
                violations.Add(new ValidationViolation(ValidationRejectReason.MissingRequiredSection,
                    $"required section '{key}' is missing or empty"));
            }
        }

        return new DictationValidationResult(violations.Count == 0, violations, passThrough.CorrectedTranscript);
    }

    private static HashSet<string> Set(IReadOnlyList<LockedToken> tokens, LockedTokenKind kind, StringComparer cmp)
        => tokens.Where(t => t.Kind == kind).Select(t => t.Text).ToHashSet(cmp);
}
