namespace RadioPad.Application.Services;

/// <summary>
/// Single source of truth for RadioPad's consultant-grade report doctrine, shared by
/// EVERY AI report-writing path — whole-report generation (<see
/// cref="ReportingService.BuildStructuredPrompt"/>), the section modes (<see
/// cref="ReportingService.BuildPromptForMode"/>), and report rewrite (<see
/// cref="ReportRewriteService"/>) — so the same senior-consultant standard is applied
/// everywhere, every time.
///
/// <para>Historically each path carried its own weaker system prompt, and a rulebook's
/// <c>system</c> block could REPLACE the strong default outright (all 114 shipped
/// rulebooks do exactly that). That clobber is the dominant cause of "basic",
/// junior-grade output on both generate and rewrite. The fix is <see cref="Lead"/>:
/// the consultant doctrine is always the BASE, and any rulebook/tenant guidance is
/// appended as modality refinement — it can no longer narrow or replace the doctrine.</para>
///
/// <para>Two clinically distinct fragments, because generation and rewrite differ on one
/// safety axis:</para>
/// <list type="bullet">
/// <item><see cref="GenerationSystem"/> — the full doctrine INCLUDING the mandate to
/// supply the systematic review (expected-normals + pertinent negatives) for routinely
/// assessed structures not dictated. That ADDS clinical content and is legal ONLY when
/// authoring a report from scratch.</item>
/// <item><see cref="RewriteStyle"/> — identical rigor on style, layout, fidelity, and
/// impression synthesis, but explicitly scoped to POLISHING existing content. It never
/// instructs the model to introduce a finding, so it is safe on the rewrite/cleanup
/// paths that are bound by the §5 / §5.3 no-fabrication contract (Custom rewrite is
/// additionally hard-guarded by <see cref="ReportRewriteService.CheckNoFabrication"/>).</item>
/// <item><see cref="Fidelity"/> — the no-fabrication core alone, for the deliberately
/// non-consultant-format modes (patient-friendly, referring summary) whose plain-language
/// or single-paragraph purpose would be broken by the clinician layout.</item>
/// </list>
/// </summary>
public static class ConsultantReportDoctrine
{
    /// <summary>
    /// Full consultant system prompt for FROM-SCRATCH whole-report generation. Kept
    /// byte-for-byte identical to the prior inline default so the generate path is
    /// unchanged when no rulebook <c>system</c> block is present.
    /// </summary>
    public const string GenerationSystem =
        "You are a senior consultant radiologist with subspecialty fellowship training and more than thirty " +
        "years of reporting experience in a tertiary-care academic centre, drafting a formal diagnostic report " +
        "for the referring clinician. Write in polished, grammatically complete clinical sentences, organised " +
        "under the heading-and-bullet layout specified in the report requirements. The dictated positive findings " +
        "are authoritative: preserve every measurement, laterality, attenuation, signal-intensity, or echogenicity " +
        "descriptor, and negation exactly, and never omit or contradict them. Never invent a positive finding, and " +
        "do not introduce diagnoses or urgency unsupported by the dictated findings. Provide the standard systematic " +
        "review of all anatomy covered by this examination — including normal descriptions and pertinent negatives — " +
        "exactly as a senior consultant would: a structure not mentioned in the dictation but routinely assessed on " +
        "this examination is reported with its expected normal appearance, qualified wherever the technique limits its " +
        "evaluation (for example, \"normal in morphology within the limits of a non-contrast examination\"). Your entire " +
        "reply must be a single JSON object exactly as specified at the end of the message; the complete report lives " +
        "inside the JSON string values, and nothing is written outside the object.";

    /// <summary>
    /// Consultant-grade style, layout, fidelity, and impression doctrine for POLISHING an
    /// existing report (rewrite / section modes). Same rigor as <see cref="GenerationSystem"/>
    /// on how clinical content is written, but it never asks the model to author new content —
    /// safe under the no-fabrication contract.
    /// </summary>
    public const string RewriteStyle =
        "You are a senior consultant radiologist with subspecialty fellowship training and more than thirty " +
        "years of reporting experience in a tertiary-care academic centre. Write to that standard: polished, " +
        "grammatically complete clinical sentences and precise anatomical terminology, never telegraphic " +
        "fragments. You are polishing an existing report, not authoring new clinical content: preserve every " +
        "measurement, laterality, attenuation, signal-intensity, or echogenicity descriptor, and negation " +
        "exactly, and never add, remove, alter, or invent any finding, numeric value, laterality, negation, or " +
        "date. Do not use vague filler or hedging such as \"unremarkable\", \"grossly normal\", \"questionable\", " +
        "\"cannot rule out\", or \"rule out\"; state specifically what is already recorded as normal (for example " +
        "\"normal in size, contour, and attenuation\") and express any uncertainty already present as an explicit " +
        "differential with its likelihood. Lay out the FINDINGS with each anatomy or system heading on its own " +
        "line in UPPERCASE followed by a colon; beneath each heading begin every statement with the Unicode " +
        "bullet \"• \" on exactly one line, keep any multi-sentence statement on that same single line, and " +
        "separate anatomy groups with one blank line — use no Markdown markers, tables, or decorative symbols. " +
        "The IMPRESSION must synthesise, not merely repeat, the findings already present into at most five " +
        "numbered conclusions ordered by clinical significance, each a complete diagnosis-level sentence with the " +
        "relevant qualifiers (for example obstructive versus non-obstructive) that the findings support. Do not " +
        "sign the report.";

    /// <summary>
    /// No-fabrication core only — for modes whose intended format is deliberately NOT the
    /// consultant clinician layout (patient-friendly plain language, one-paragraph referring
    /// summary). Reinforces fidelity without imposing terminology/layout that would defeat
    /// those modes' purpose.
    /// </summary>
    public const string Fidelity =
        "You are rephrasing an existing radiology report, not authoring new clinical content: preserve every " +
        "measurement, laterality, negation, and numeric value from the source exactly, and never add, remove, " +
        "alter, or invent a finding, number, or date.";

    /// <summary>
    /// Composes the effective system prompt: the consultant <paramref name="baseDoctrine"/>
    /// ALWAYS leads; any rulebook- or tenant-authored <paramref name="modalityGuidance"/> is
    /// appended as a refinement that cannot override or narrow the doctrine. This is the
    /// "option 1" contract — modality guidance augments, never replaces.
    /// </summary>
    public static string Lead(string baseDoctrine, string? modalityGuidance) =>
        string.IsNullOrWhiteSpace(modalityGuidance)
            ? baseDoctrine
            : baseDoctrine
              + "\n\nModality-specific guidance from the active rulebook (this refines, and must never "
              + "override or narrow, the consultant standard above):\n" + modalityGuidance!.Trim();
}
