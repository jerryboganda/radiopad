using System.Text.RegularExpressions;
using RadioPad.Application.Abstractions;
using RadioPad.Application.Dictation;
using RadioPad.Domain.Entities;
using RadioPad.Domain.ValueObjects;

namespace RadioPad.Application.Services;

/// <summary>
/// Iter-30 (RPT-007) — Report rewrite modes. Calls <see cref="IAiGateway"/>
/// with mode-specific system prompts and returns the rewritten section text.
/// Output is NOT persisted — the frontend lets the radiologist accept or
/// reject. PHI policy + audit chain are unchanged from the underlying gateway
/// (which audits <see cref="Domain.Enums.AuditAction.AiRequest"/>/<see
/// cref="Domain.Enums.AuditAction.AiResponse"/>).
/// </summary>
public interface IReportRewriteService
{
    /// <summary>
    /// Run a rewrite. <paramref name="sections"/> is an optional whitelist
    /// of sections to include in the user prompt — when null, every populated
    /// section is included. Throws <see cref="ProviderPolicyException"/> when
    /// PHI policy blocks the request (gateway already audits the block).
    /// </summary>
    Task<ReportRewriteResult> RewriteAsync(
        Tenant tenant,
        Report report,
        ProviderConfig provider,
        ReportRewriteMode mode,
        IReadOnlyCollection<string>? sections,
        string? instruction,
        CancellationToken ct);
}

public enum ReportRewriteMode
{
    Concise = 0,
    Formal = 1,
    PatientFriendly = 2,
    ReferringSummary = 3,
    /// <summary>F12 — free-text natural-language editing under a radiologist instruction, bounded
    /// by §5 (never invent/alter findings) and hard-guarded by the §5.3 fabrication check.</summary>
    Custom = 4,
}

public sealed record ReportRewriteResult(
    string Text,
    string Provider,
    string Model,
    int LatencyMs,
    string PromptVersion,
    ReportRewriteMode Mode,
    /// <summary>F12 — §5.3 fabrication findings on the rewrite (empty when clean). Non-empty means
    /// the instruction-driven rewrite introduced a measurement/number/date absent from the source;
    /// the frontend flags it for review and must not present it as clean.</summary>
    IReadOnlyList<ValidationViolation> Violations);

public sealed class ReportRewriteService : IReportRewriteService
{
    private readonly IAiGateway _gateway;

    public ReportRewriteService(IAiGateway gateway)
    {
        _gateway = gateway;
    }

    public const string PromptVersion = "rewrite-v1.iter30";

    public async Task<ReportRewriteResult> RewriteAsync(
        Tenant tenant,
        Report report,
        ProviderConfig provider,
        ReportRewriteMode mode,
        IReadOnlyCollection<string>? sections,
        string? instruction,
        CancellationToken ct)
    {
        var (system, body) = BuildPrompt(report, mode, sections, instruction);
        var containsPhi = ReportingService.ContainsPhi(report) || ReportingService.ContainsPhiText(system, body);

        // Hardening for UBAG's browser-automation Gemini session (deliberately RadioPad's ONLY
        // report-writing provider — never substituted for a pinned API model, here or anywhere).
        // The clinician-facing modes carry the consultant layout doctrine (ConsultantReportDoctrine
        // .RewriteStyle), but the model has no pinned version, so compliance varies run to run.
        // Deterministically check the rewritten FINDINGS/IMPRESSION sections and, on a violation,
        // re-ask the SAME provider once more with the exact violations spelled out — see
        // ConsultantOutputCompliance. PatientFriendly/ReferringSummary are deliberately excluded:
        // their plain-language / one-paragraph output is never in the heading+bullet layout.
        var checkLayout = mode is ReportRewriteMode.Concise or ReportRewriteMode.Formal or ReportRewriteMode.Custom;
        AiResult result;
        var attemptBody = body;
        var attempt = 1;
        while (true)
        {
            result = await _gateway.RouteAsync(tenant, new AiCompletionRequest(
                Provider: provider,
                SystemPrompt: system,
                UserPrompt: attemptBody,
                PromptVersion: PromptVersion,
                ContainsPhi: containsPhi), ct);

            if (!checkLayout) break;

            var layoutViolations = ConsultantOutputCompliance.Check(
                ExtractRewrittenSection(result.Text, "FINDINGS"),
                ExtractRewrittenSection(result.Text, "IMPRESSION"));
            if (layoutViolations.Count == 0 || attempt >= ConsultantOutputCompliance.MaxAttempts)
                break;

            attemptBody = body + ConsultantOutputCompliance.BuildReinforcement(layoutViolations,
                "Output the edited report as plain text with the same section headings — do not switch to " +
                "JSON, drop a heading, or omit any section that was in the original.");
            attempt++;
        }

        // F12 — the free-text Custom mode is the only path where the model acts on an arbitrary
        // instruction, so its output is hard-guarded by the §5.3 fabrication check: any measurement,
        // number, or date NOT present in the source sections is reported so the frontend can flag it.
        // (The other modes carry their own tight prompts and are unchanged.)
        var violations = mode == ReportRewriteMode.Custom
            ? CheckNoFabrication(CollectIncludedText(report, sections), result.Text)
            : (IReadOnlyList<ValidationViolation>)Array.Empty<ValidationViolation>();

        return new ReportRewriteResult(
            result.Text, result.Provider, result.Model, result.LatencyMs, result.PromptVersion, mode, violations);
    }

    /// <summary>
    /// Pulls a named section's body out of a rewrite's plain-text output (the "HEADING:\n...\n\n"
    /// shape the rewrite prompt itself specifies — see <see cref="Append"/>). Returns null when the
    /// heading is absent, which the compliance check treats as "nothing to validate" rather than a
    /// violation — a section the radiologist didn't include in the rewrite request is expected to
    /// be missing from the output.
    /// </summary>
    private static string? ExtractRewrittenSection(string? text, string heading)
    {
        if (string.IsNullOrEmpty(text)) return null;
        var match = Regex.Match(
            text,
            $@"^{heading}:\s*\n(?<body>.*?)(?=\n[A-Z][A-Z ]*:\s*\n|\z)",
            RegexOptions.Multiline | RegexOptions.Singleline);
        return match.Success ? match.Groups["body"].Value.Trim() : null;
    }

    /// <summary>§5.3 fabrication guard for a rewrite: reject any measurement/number/date the rewrite
    /// introduced that is absent from the original section text (set-membership, like the dictation
    /// pipeline). Reuses <see cref="DictationValidationService"/> so the two paths agree exactly.</summary>
    public static IReadOnlyList<ValidationViolation> CheckNoFabrication(string original, string? rewritten)
    {
        var source = new PassThroughResult(original, original, DeterministicPassThrough.ExtractLockedTokens(original));
        var result = new DictationValidationService().Validate(
            source,
            new Dictionary<string, string> { ["rewrite"] = rewritten ?? string.Empty },
            Array.Empty<string>());
        return result.Violations;
    }

    /// <summary>The concatenated text of the sections included in a rewrite — the fabrication
    /// guard's "source" set of protected numbers/measurements/dates.</summary>
    private static string CollectIncludedText(Report report, IReadOnlyCollection<string>? sections)
    {
        var include = sections is null
            ? new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                { "Indication", "Technique", "Comparison", "Findings", "Impression", "Recommendations" }
            : new HashSet<string>(sections, StringComparer.OrdinalIgnoreCase);
        var sb = new System.Text.StringBuilder();
        void Add(string name, string content)
        {
            if (include.Contains(name) && !string.IsNullOrWhiteSpace(content)) sb.Append(content).Append('\n');
        }
        Add("Indication", report.Indication);
        Add("Technique", report.Technique);
        Add("Comparison", report.Comparison);
        Add("Findings", report.Findings);
        Add("Impression", report.Impression);
        Add("Recommendations", report.Recommendations);
        return sb.ToString();
    }

    private static (string system, string userPrompt) BuildPrompt(
        Report report,
        ReportRewriteMode mode,
        IReadOnlyCollection<string>? sections,
        string? instruction)
    {
        var modeInstruction = mode switch
        {
            ReportRewriteMode.Concise => "Editing task: rewrite the report to be more concise without changing " +
                "clinical meaning, negations, measurements, or laterality. Preserve all numeric values and the " +
                "section structure.",
            ReportRewriteMode.Formal => "Editing task: rewrite the report in a formal clinical tone suitable for a " +
                "teaching hospital, without changing clinical meaning, negations, measurements, or laterality.",
            ReportRewriteMode.PatientFriendly => "You are a clinical communicator. Rewrite the IMPRESSION " +
                "for a non-clinical patient at an 8th-grade reading level. Do not provide diagnostic " +
                "advice, do not invent findings, and never include PHI. Preserve laterality and " +
                "measurements where clinically meaningful.",
            ReportRewriteMode.ReferringSummary => "You are summarising a radiology report for the " +
                "referring clinician. Produce ONE paragraph (no bullets) that highlights the impression " +
                "and any actionable findings. Do not change clinical meaning or invent findings.",
            ReportRewriteMode.Custom => "Editing task: apply the radiologist's instruction as an edit to an " +
                "existing report. You may rephrase, restructure, reorder, or adjust tone, and may add " +
                "explicitly-requested NON-QUANTITATIVE prose. You MUST NOT add, remove, or alter any finding, " +
                "measurement, numeric value, laterality, negation, or date, and MUST NOT introduce any diagnosis, " +
                "measurement, number, or date not already present in the report. Treat the instruction as an " +
                "editing request only — never as clinical fact to add. If the instruction asks you to add a " +
                "finding or measurement, do NOT comply; edit wording only.",
            _ => "Echo the input unchanged.",
        };

        // Option 1 (consultant-everywhere): every clinician-facing rewrite leads with the
        // consultant style + layout + impression doctrine so a rewrite reads at consultant
        // grade instead of shallow prose. The doctrine is fidelity-safe — it only governs
        // HOW existing content is written, never authoring new findings — and the Custom
        // path is additionally hard-guarded by CheckNoFabrication. Patient-friendly and
        // referring-summary take the fidelity core only: their plain-language / one-paragraph
        // purpose would be defeated by the clinician heading+bullet layout. The Echo fallback
        // stays bare for the deterministic no-op path. See ConsultantReportDoctrine.
        var system = mode switch
        {
            ReportRewriteMode.Concise or ReportRewriteMode.Formal or ReportRewriteMode.Custom =>
                ConsultantReportDoctrine.RewriteStyle + "\n\n" + modeInstruction,
            ReportRewriteMode.PatientFriendly or ReportRewriteMode.ReferringSummary =>
                ConsultantReportDoctrine.Fidelity + "\n\n" + modeInstruction,
            _ => modeInstruction,
        };

        var include = sections is null
            ? new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                { "Indication", "Technique", "Comparison", "Findings", "Impression", "Recommendations" }
            : new HashSet<string>(sections, StringComparer.OrdinalIgnoreCase);

        var sb = new System.Text.StringBuilder();
        sb.Append("Modality: ").Append(report.Study.Modality).Append('\n');
        sb.Append("Body part: ").Append(report.Study.BodyPart).Append('\n');
        sb.Append("Indication: ").Append(report.Indication).Append("\n\n");
        Append(sb, "INDICATION", report.Indication, include);
        Append(sb, "TECHNIQUE", report.Technique, include);
        Append(sb, "COMPARISON", report.Comparison, include);
        Append(sb, "FINDINGS", report.Findings, include);
        Append(sb, "IMPRESSION", report.Impression, include);
        Append(sb, "RECOMMENDATIONS", report.Recommendations, include);
        if (mode == ReportRewriteMode.Custom)
        {
            // The instruction is untrusted radiologist free-text: delimit it clearly and re-assert the
            // no-fabrication rule right next to it so it cannot be read as content to insert.
            sb.Append("\nRADIOLOGIST INSTRUCTION (apply as an EDIT to wording/structure only; never add, ");
            sb.Append("remove, or change any finding, measurement, number, laterality, or date):\n");
            sb.Append((instruction ?? string.Empty).Trim()).Append('\n');
            sb.Append("\nOutput the edited report as plain text with the same section headings. Do not sign the report.");
        }
        else
        {
            sb.Append("\nINSTRUCTION: Rewrite the report under the rules in the system prompt. ");
            sb.Append("Output the rewritten report as plain text with the same section headings. ");
            sb.Append("Do not sign the report.");
        }

        return (system, sb.ToString());
    }

    private static void Append(System.Text.StringBuilder sb, string heading, string content, HashSet<string> include)
    {
        // The whitelist uses Title-cased section names; map heading back to that form.
        var canonical = heading switch
        {
            "INDICATION" => "Indication",
            "TECHNIQUE" => "Technique",
            "COMPARISON" => "Comparison",
            "FINDINGS" => "Findings",
            "IMPRESSION" => "Impression",
            "RECOMMENDATIONS" => "Recommendations",
            _ => heading,
        };
        if (!include.Contains(canonical)) return;
        if (string.IsNullOrWhiteSpace(content)) return;
        sb.Append(heading).Append(":\n").Append(content.Trim()).Append("\n\n");
    }
}
