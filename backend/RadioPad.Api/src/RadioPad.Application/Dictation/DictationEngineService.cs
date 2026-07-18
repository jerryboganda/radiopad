namespace RadioPad.Application.Dictation;

/// <summary>The structured report sections returned by a formatter (cloud or local MedGemma).</summary>
public sealed record FormatterOutput(
    IReadOnlyDictionary<string, string> Sections,
    string Provider,
    string Model,
    int LatencyMs);

/// <summary>Context handed to a formatter for a single dictation-format run.</summary>
public sealed record DictationFormatContext(
    string Modality,
    string BodyPart,
    string Indication,
    IReadOnlyList<string> SectionKeys,
    string? Grammar);

/// <summary>
/// Abstraction over the report formatter. The production adapter routes through the existing
/// <c>AiGateway</c> (cloud, default) or a local MedGemma <c>LocalOnly</c> provider (optional,
/// offline); the deterministic safety layers wrap whichever one runs.
/// </summary>
public interface IDictationFormatter
{
    Task<FormatterOutput> FormatAsync(string protectedTranscript, DictationFormatContext context, CancellationToken ct);
}

/// <summary>The editable draft produced by the §4.2 pipeline for radiologist review + sign-off.</summary>
public sealed record DictationDraft(
    string RawTranscript,
    string CorrectedTranscript,
    IReadOnlyDictionary<string, string> DraftSections,
    bool Accepted,
    bool UsedFallback,
    IReadOnlyList<ValidationViolation> Violations,
    IReadOnlyList<SentinelWarning> SentinelWarnings,
    string Provider,
    string Model,
    int LatencyMs)
{
    /// <summary>True when the draft must wear the <c>.ai-mark</c> "Requires review" flag — always,
    /// because it is AI-generated, and emphatically when the sentinel or validation flagged it.</summary>
    public bool RequiresReview => true;
}

/// <summary>
/// Brief §4.2 — orchestrates the report-assembly pipeline: §5.2 deterministic pass-through →
/// formatter → §5.3 validation-diff → §5.6 sentinel. On validation rejection it fails safe to the
/// dictionary-corrected raw transcript (never fails silent). Formatter-agnostic: the same pipeline
/// wraps the cloud formatter (default) and the optional local MedGemma formatter. It NEVER signs a
/// report — the output is always an editable draft gated by the §5.5 sign-off flow.
/// </summary>
public sealed class DictationEngineService
{
    private readonly DeterministicPassThrough _passThrough;
    private readonly DictationValidationService _validation;
    private readonly LateralityNegationSentinel _sentinel;

    public DictationEngineService(
        DeterministicPassThrough passThrough,
        DictationValidationService validation,
        LateralityNegationSentinel sentinel)
    {
        _passThrough = passThrough;
        _validation = validation;
        _sentinel = sentinel;
    }

    public async Task<DictationDraft> RunAsync(
        string rawTranscript,
        DictationFormatContext context,
        IReadOnlyList<CorrectionRule> corrections,
        string? patientSex,
        IDictationFormatter formatter,
        CancellationToken ct)
    {
        // 1) Deterministic pass-through (§5.2) — correction dictionary + spoken-number normalization
        //    + token lock. The corrected transcript is what the formatter sees AND the fallback.
        var pass = _passThrough.Process(rawTranscript, corrections);

        // 2) Formatter — cloud (default) or local MedGemma (optional). Never signs; format only.
        var formatted = await formatter.FormatAsync(pass.CorrectedTranscript, context, ct);

        // 3) Validation-diff (§5.3) — reject fabricated numbers/measurements/dates or dropped sections.
        var validation = _validation.Validate(pass, formatted.Sections, context.SectionKeys);

        // 4) Sentinel (§5.6) — warn on laterality / negation / gender mismatch (does not reject).
        var sentinel = _sentinel.Check(pass.CorrectedTranscript, formatted.Sections, patientSex);

        // 5) Fail safe: on rejection show the dictionary-corrected raw transcript, never the LLM output.
        IReadOnlyDictionary<string, string> draftSections = validation.Accepted
            ? formatted.Sections
            : new Dictionary<string, string> { ["findings"] = pass.CorrectedTranscript };

        return new DictationDraft(
            RawTranscript: pass.RawTranscript,
            CorrectedTranscript: pass.CorrectedTranscript,
            DraftSections: draftSections,
            Accepted: validation.Accepted,
            UsedFallback: !validation.Accepted,
            Violations: validation.Violations,
            SentinelWarnings: sentinel.Warnings,
            Provider: formatted.Provider,
            Model: formatted.Model,
            LatencyMs: formatted.LatencyMs);
    }
}
