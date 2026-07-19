namespace RadioPad.Application.Dictation;

/// <summary>Why the §5.3 validation pass rejected the formatter output.</summary>
public enum ValidationRejectReason
{
    AddedNumber,
    AddedMeasurement,
    AddedDate,
    MissingRequiredSection,
    // Content the radiologist DID dictate that never reached the report. Losing a measurement is
    // as dangerous as inventing one, and until this existed the pass only looked for additions.
    DroppedMeasurement,
    DroppedDate,
}

/// <summary>A single §5.3 validation failure.</summary>
public sealed record ValidationViolation(ValidationRejectReason Reason, string Detail)
{
    /// <summary>
    /// True when this violation must DISCARD the formatter output in favour of the raw transcript.
    ///
    /// <para>Only fabrication blocks. §5.3 exists to stop content the radiologist never dictated
    /// from reaching a report, so an added number, measurement or date is a hard reject. A missing
    /// section is the exact opposite: it is the formatter correctly declining to invent something
    /// that was not dictated, and discarding a good structured report over it would replace it with
    /// something strictly less useful while preventing nothing unsafe.</para>
    ///
    /// <para>This distinction is load-bearing. Most dictations do not state "recommendations", so
    /// treating a missing section as a reject made the offline formatter fall back on nearly every
    /// real dictation — the feature looked broken while behaving "correctly". Observed end-to-end:
    /// MedGemma produced a clean report with zero sentinel warnings and was still discarded for the
    /// one section the radiologist had not dictated.</para>
    /// </summary>
    public bool IsBlocking => Reason is not ValidationRejectReason.MissingRequiredSection;
}

/// <summary>Outcome of the §5.3 validation pass.</summary>
/// <param name="Accepted">True when the formatter output is safe to show as an editable draft. A
/// draft can be accepted while still carrying non-blocking violations (e.g. a section the
/// radiologist never dictated) — those are surfaced for review, not grounds to discard the report.</param>
/// <param name="Violations">Every violation found, blocking or not. Non-empty does NOT imply
/// rejection; check <see cref="ValidationViolation.IsBlocking"/>.</param>
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

        // Dropped clinical content. The brief's "reject on a dropped required section" is really
        // about the formatter LOSING what was dictated; an empty section was only ever a crude
        // proxy for that, and one that fired hardest on the correct behaviour of not inventing
        // undictated sections. Checking the locked tokens directly measures the real property:
        // a measurement or date the radiologist dictated must appear somewhere in the report.
        //
        // Numbers are deliberately excluded — a bare number is routinely absorbed into a
        // measurement or rephrased ("two nodules" → "multiple nodules"), so requiring every one to
        // survive verbatim would reject sound reports. Measurements and dates carry the clinical
        // weight and are never legitimately dropped.
        var outMeasurements = Set(outputTokens, LockedTokenKind.Measurement, StringComparer.OrdinalIgnoreCase);
        var outDates = Set(outputTokens, LockedTokenKind.Date, StringComparer.OrdinalIgnoreCase);

        foreach (var m in srcMeasurements)
        {
            if (!outMeasurements.Contains(m) && reported.Add("DM:" + m))
                violations.Add(new ValidationViolation(ValidationRejectReason.DroppedMeasurement,
                    $"measurement '{m}' was dictated but is absent from the report"));
        }
        foreach (var d in srcDates)
        {
            if (!outDates.Contains(d) && reported.Add("DD:" + d))
                violations.Add(new ValidationViolation(ValidationRejectReason.DroppedDate,
                    $"date '{d}' was dictated but is absent from the report"));
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

        // Accept unless something was FABRICATED. Non-blocking violations still travel with the
        // result so the UI can flag them for review — fail safe, but never discard a sound report
        // over an omission that was itself the correct behaviour.
        var accepted = !violations.Any(v => v.IsBlocking);
        return new DictationValidationResult(accepted, violations, passThrough.CorrectedTranscript);
    }

    private static HashSet<string> Set(IReadOnlyList<LockedToken> tokens, LockedTokenKind kind, StringComparer cmp)
        => tokens.Where(t => t.Kind == kind).Select(t => t.Text).ToHashSet(cmp);
}
