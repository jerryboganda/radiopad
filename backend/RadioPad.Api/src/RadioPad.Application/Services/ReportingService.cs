using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using RadioPad.Application.Abstractions;
using RadioPad.Domain.Entities;
using RadioPad.Domain.Enums;
using RadioPad.Domain.ValueObjects;
using RadioPad.Validation.Engine;
using RadioPad.Validation.Rulebook;

namespace RadioPad.Application.Services;

/// <summary>
/// Orchestrates the report drafting lifecycle: draft generation,
/// findings → impression, validation, acknowledgement, and export.
/// </summary>
public class ReportingService
{
    private readonly IAiGateway _gateway;
    private readonly IRulebookStore _rulebooks;
    private readonly IAuditLog _audit;
    private readonly ReportValidator _validator;
    private readonly ILogger<ReportingService> _log;
    private readonly IProviderRouter? _router;
    private readonly HallucinationDetector? _hallucination;
    private readonly IPromptOverrideStore? _overrides;

    public ReportingService(
        IAiGateway gateway,
        IRulebookStore rulebooks,
        IAuditLog audit,
        ReportValidator validator,
        ILogger<ReportingService> log,
        IProviderRouter? router = null,
        HallucinationDetector? hallucination = null,
        IPromptOverrideStore? overrides = null)
    {
        _gateway = gateway;
        _rulebooks = rulebooks;
        _audit = audit;
        _validator = validator;
        _log = log;
        _router = router;
        _hallucination = hallucination;
        _overrides = overrides;
    }

    public const string PromptVersion = "v1.2026.05";

    /// <summary>
    /// PRD modes (RPT-005, RPT-006, RPT-007, AI-001, AI-002).
    /// `impression` and `draft` are the P0 modes; the others are rewrites.
    /// </summary>
    public static readonly string[] SupportedModes =
    {
        "impression", "cleanup", "draft", "concise", "formal", "patient_friendly", "referring_summary",
    };

    public Task<AiResult> GenerateImpressionAsync(
        Tenant tenant, User user, Report report, ProviderConfig provider, CancellationToken ct) =>
        RunAsync(tenant, user, report, provider, "impression", ct);

    /// <summary>
    /// PRD AI-010 — auto-routes to the cheapest matching provider via
    /// <see cref="IProviderRouter"/>. Throws when no provider matches.
    /// </summary>
    public async Task<(AiResult result, ProviderConfig provider)> RunAutoAsync(
        Tenant tenant, User user, Report report, string mode, CancellationToken ct)
    {
        if (_router is null)
            throw new InvalidOperationException("Auto-routing requested but no IProviderRouter is configured.");
        var provider = await _router.SelectAsync(tenant, ContainsPhi(report), ct)
            ?? throw new ProviderPolicyException(
                "No enabled provider matches the tenant's PHI / compliance requirements.");
        var result = await RunAsync(tenant, user, report, provider, mode, ct);
        return (result, provider);
    }

    /// <summary>
    /// Multi-mode AI dispatcher. Resolves the mode-specific prompt block from the active rulebook,
    /// falls back to a documented default, and routes through <see cref="IAiGateway"/> so PHI policy
    /// + usage ledger are always enforced.
    /// </summary>
    public async Task<AiResult> RunAsync(
        Tenant tenant, User user, Report report, ProviderConfig provider, string mode, CancellationToken ct)
    {
        if (!SupportedModes.Contains(mode, StringComparer.OrdinalIgnoreCase))
            throw new ArgumentException($"Unknown AI mode '{mode}'. Supported: {string.Join(", ", SupportedModes)}.");

        // PRD RB-010: a non-Approved rulebook may not be referenced from a
        // production AI run unless the tenant explicitly opts in to sandbox.
        var rulebookEntity = await ResolveRulebookEntityAsync(tenant, report, ct);
        if (rulebookEntity is { Status: not RulebookStatus.Approved } && !tenant.AllowSandboxRulebooks)
        {
            throw new RulebookGovernanceException(
                $"Rulebook '{rulebookEntity.RulebookId}@{rulebookEntity.Version}' is in status " +
                $"'{rulebookEntity.Status}' and the tenant does not allow sandbox rulebooks.");
        }

        var rulebook = rulebookEntity is null ? null : RulebookSpec.FromYaml(rulebookEntity.SourceYaml);
        // Iter-31 AI-009 — tenant-level prompt overrides take precedence over
        // the rulebook's own prompt_blocks.
        IReadOnlyDictionary<string, string> overrides = _overrides is null || rulebookEntity is null
            ? new Dictionary<string, string>()
            : await _overrides.LoadAsync(tenant.Id, rulebookEntity.RulebookId, ct);
        var (system, instructions, userBody) = BuildPromptForMode(report, rulebook, mode, overrides);

        var containsPhi = ContainsPhi(report) || ContainsPhiText(system, userBody);
        return await _gateway.RouteAsync(tenant, new AiCompletionRequest(
            Provider: provider,
            SystemPrompt: system,
            UserPrompt: userBody,
            PromptVersion: PromptVersion,
            ContainsPhi: containsPhi)
        {
            // Iter-0b (RB-009 / AI-012) — bind rulebook provenance to the audit + usage trail.
            RulebookId = rulebookEntity?.Id,
            RulebookVersion = rulebookEntity?.Version,
        }, ct);
    }

    internal static (string system, string instructions, string userPrompt) BuildPromptForMode(
        Report report, RulebookSpec? rulebook, string mode,
        IReadOnlyDictionary<string, string>? overrides = null)
    {
        // Iter-31 AI-009 — overrides win over rulebook blocks; rulebook wins
        // over the hard-coded clinical default.
        string? Resolve(string key) =>
            (overrides is not null && overrides.TryGetValue(key, out var ov) && !string.IsNullOrWhiteSpace(ov))
                ? ov
                : rulebook?.PromptBlocks.GetValueOrDefault(key);

        var system = Resolve("system")
            ?? "You are assisting a board-certified radiologist drafting a structured radiology report. " +
               "Never invent findings. Always preserve laterality and measurements. " +
               "Output only the requested section. Do not sign the report.";

        // Mode-specific instructions: prefer the rulebook's named prompt block, fall back to a
        // hard-coded clinically conservative default. Keep these instructions stable across
        // releases — golden cases depend on the wording.
        var (defaultInstr, body) = mode.ToLowerInvariant() switch
        {
            "impression" => (
                "Generate a concise impression (max 5 bullets) faithful to the Findings section. Do not introduce new findings.",
                $"FINDINGS:\n{report.Findings}"),
            "cleanup" => (
                "Rewrite the dictated text into clean grammar without changing clinical meaning. Preserve all numbers, laterality, and negations exactly.",
                $"DICTATION:\n{report.Findings}"),
            "draft" => (
                "Generate a structured draft report from the dictation and metadata, with sections: Indication, Technique, Comparison, Findings, Impression. " +
                "Do not invent findings beyond what is dictated.",
                $"Modality: {report.Study.Modality}\nBody part: {report.Study.BodyPart}\nContrast: {report.Study.Contrast}\nIndication: {report.Indication}\nComparison: {report.Study.Comparison}\nDICTATION:\n{report.Findings}"),
            "concise" => (
                "Rewrite the impression to be more concise. Do not change clinical meaning, negations, or measurements.",
                $"IMPRESSION:\n{report.Impression}"),
            "formal" => (
                "Rewrite the report in a formal clinical tone. Do not change clinical meaning.",
                $"FINDINGS:\n{report.Findings}\n\nIMPRESSION:\n{report.Impression}"),
            "patient_friendly" => (
                "Rewrite the impression in patient-friendly language at an 8th-grade reading level. Do not provide diagnostic advice. " +
                "Do not include PHI or recommendations beyond the report.",
                $"IMPRESSION:\n{report.Impression}"),
            "referring_summary" => (
                "Write a one-paragraph summary for the referring physician highlighting the impression and any actionable findings.",
                $"FINDINGS:\n{report.Findings}\n\nIMPRESSION:\n{report.Impression}"),
            _ => ("Echo the input unchanged.", report.Findings),
        };

        // Allow rulebook (and tenant override) to override per-mode
        // instruction by named prompt block (mode key).
        var instructions = Resolve(mode) ?? defaultInstr;

        // Iter-36 — patient demographics replace the former study-context Indication
        // field in the prompt header; the clinical indication of record is now the
        // report-body Indication section (report.Indication).
        var age = report.Study.Age is int a ? a.ToString() : "Unknown";
        var gender = string.IsNullOrWhiteSpace(report.Study.Gender) ? "Unknown" : report.Study.Gender;
        var contrast = string.IsNullOrWhiteSpace(report.Study.Contrast) ? "Unspecified" : report.Study.Contrast;

        var userPrompt = $"""
            Modality: {report.Study.Modality}
            Body part: {report.Study.BodyPart}
            Contrast: {contrast}
            Patient: age {age}, gender {gender}
            Indication: {report.Indication}

            {body}

            INSTRUCTION:
            {instructions}
            """;

        return (system, instructions, userPrompt);
    }

    /// <summary>
    /// Iter-36 — pick the Approved report template + rulebook that match a
    /// (modality, body part[, contrast]) selection key. This is the single
    /// mechanism behind both automatic scaffolding (template) and prompt selection
    /// (rulebook).
    /// <para>
    /// Hybrid contrast model: contrast influences TEMPLATE selection only. Among the
    /// Approved templates matching (modality, body part), the contrast preference is
    /// 3-tier: (1) exact contrast match, (2) contrast-agnostic template (empty
    /// Contrast), (3) any match ignoring contrast. This guarantees a selection always
    /// resolves a template when one exists for the region, and is fully
    /// backward-compatible (empty Contrast everywhere = pre-contrast behaviour).
    /// Within each tier the existing tie-break holds: Normal-variant first, then
    /// most-recently-updated. Rulebooks remain keyed on (modality, body part),
    /// tie-broken by highest version, and adapt contrast prose via prompt blocks.
    /// </para>
    /// Returns nulls when no Approved match exists or the key is incomplete. Pure +
    /// side-effect free for unit testing — callers load tenant-scoped candidates and
    /// apply the result.
    /// </summary>
    public static (ReportTemplate? template, Rulebook? rulebook) ResolveBindings(
        IEnumerable<ReportTemplate> templates,
        IEnumerable<Rulebook> rulebooks,
        string? modality, string? bodyPart, string? contrast = null)
    {
        var m = (modality ?? "").Trim();
        var bp = (bodyPart ?? "").Trim();
        var c = (contrast ?? "").Trim();
        if (m.Length == 0 || bp.Length == 0) return (null, null);

        // Materialize the (modality, body part) candidates once so the three
        // contrast tiers reuse a single, deterministically-ordered list.
        var matches = templates
            .Where(t => t.Status == TemplateStatus.Approved
                && string.Equals(t.Modality, m, StringComparison.OrdinalIgnoreCase)
                && string.Equals(t.BodyPart, bp, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(t => t.Variant == TemplateVariant.Normal)
            .ThenByDescending(t => t.UpdatedAt)
            .ToList();

        var template =
            (c.Length > 0
                ? matches.FirstOrDefault(t => string.Equals(t.Contrast, c, StringComparison.OrdinalIgnoreCase))
                : null)                                                   // tier 1: exact contrast
            ?? matches.FirstOrDefault(t => string.IsNullOrEmpty(t.Contrast)) // tier 2: contrast-agnostic
            ?? matches.FirstOrDefault();                                  // tier 3: ignore contrast

        var rulebook = rulebooks
            .Where(r => r.Status == RulebookStatus.Approved
                && AppliesTo(r.AppliesToModalities, m)
                && AppliesTo(r.AppliesToBodyParts, bp))
            .OrderByDescending(r => r.Version, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();

        return (template, rulebook);
    }

    /// <summary>True when a comma-separated <c>applies_to</c> list contains <paramref name="value"/> (case-insensitive).</summary>
    private static bool AppliesTo(string? csv, string value) =>
        !string.IsNullOrWhiteSpace(csv) && csv
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Any(x => string.Equals(x, value, StringComparison.OrdinalIgnoreCase));

    public async Task<ValidationResult> ValidateAsync(Tenant tenant, Report report, CancellationToken ct)
    {
        var rulebook = await ResolveRulebookAsync(tenant, report, ct);
        if (rulebook is null) return ValidationResult.Empty;
        return _validator.Validate(report, rulebook);
    }

    /// <summary>
    /// Validation with tenant lexicon awareness (PRD STD-006).
    /// </summary>
    public async Task<ValidationResult> ValidateAsync(
        Tenant tenant,
        Report report,
        IReadOnlyCollection<TenantLexicon>? lexicon,
        CancellationToken ct)
    {
        var rulebook = await ResolveRulebookAsync(tenant, report, ct);
        if (rulebook is null) return ValidationResult.Empty;
        return _validator.Validate(report, rulebook, lexicon);
    }

    /// <summary>
    /// Validation that also runs the deterministic hallucination detector
    /// (PRD AI-007). The detector is configured per-tenant via
    /// <see cref="TenantSettings"/> and is administered from the Settings UI.
    /// Findings are merged into the same <see cref="ValidationResult"/>.
    /// </summary>
    public async Task<ValidationResult> ValidateAsync(
        Tenant tenant,
        Report report,
        IReadOnlyCollection<TenantLexicon>? lexicon,
        TenantSettings? settings,
        CancellationToken ct)
    {
        var rulebook = await ResolveRulebookAsync(tenant, report, ct);
        // STD-006: tenant lexicon must be evaluated even when no rulebook is
        // bound to the report — fall back to an empty rulebook spec so the
        // validator still walks the lexicon entries.
        var spec = rulebook ?? new RadioPad.Validation.Rulebook.RulebookSpec();
        var baseResult = _validator.Validate(report, spec, lexicon);

        if (_hallucination is null || settings is null) return ApplyStrictness(baseResult, settings);
        var claims = _hallucination.Detect(report, settings);
        if (claims.Count == 0) return ApplyStrictness(baseResult, settings);

        var extra = new List<ValidationFinding>(baseResult.Findings);
        foreach (var c in claims)
        {
            // Iter-0b (AI-028 / §16.5 / RPT-026) — anchor the unsupported-claim
            // finding to the exact char span of the offending sentence in the
            // Impression so the "Why this suggestion?" panel + span-attested
            // guardrail can highlight it. Null span when the sentence cannot be located.
            var idx = string.IsNullOrEmpty(c.Sentence)
                ? -1
                : (report.Impression ?? string.Empty).IndexOf(c.Sentence, StringComparison.Ordinal);
            extra.Add(new ValidationFinding(
                RuleId: "ai:unsupported_claim",
                Severity: c.Severity,
                Message: $"Impression sentence is not clearly supported by Findings/StudyContext (support={c.SupportFraction:P0}).",
                Section: "Impression",
                Snippet: c.Sentence,
                StartIndex: idx >= 0 ? idx : null,
                EndIndex: idx >= 0 ? idx + c.Sentence.Length : null));
        }
        var blocker = extra.Any(f => string.Equals(f.Severity, nameof(Domain.Enums.ValidationSeverity.Blocker), StringComparison.OrdinalIgnoreCase));
        var blockerCount = extra.Count(f => string.Equals(f.Severity, nameof(Domain.Enums.ValidationSeverity.Blocker), StringComparison.OrdinalIgnoreCase));
        var warningCount = extra.Count(f => string.Equals(f.Severity, nameof(Domain.Enums.ValidationSeverity.Warning), StringComparison.OrdinalIgnoreCase));
        var infoCount = extra.Count(f => string.Equals(f.Severity, nameof(Domain.Enums.ValidationSeverity.Info), StringComparison.OrdinalIgnoreCase));
        var qualityScore = Math.Max(0, 100 - (blockerCount * 25) - (warningCount * 5) - (infoCount * 1));
        return ApplyStrictness(new ValidationResult(blocker, extra, qualityScore), settings);
    }

    /// <summary>
    /// Iter-31 AI-007 / RPT-012 — apply tenant-level strictness toggles to a
    /// validation result. When <see cref="TenantSettings.WarnAsBlocker"/> is
    /// true, every <c>Warning</c> finding is promoted to <c>Blocker</c>.
    /// Default behaviour (settings null or both toggles off) is identity.
    /// </summary>
    private static ValidationResult ApplyStrictness(ValidationResult result, TenantSettings? settings)
    {
        if (settings is null || !settings.WarnAsBlocker) return result;
        if (result.Findings.Count == 0) return result;
        var promoted = new List<ValidationFinding>(result.Findings.Count);
        foreach (var f in result.Findings)
        {
            promoted.Add(string.Equals(f.Severity, nameof(Domain.Enums.ValidationSeverity.Warning), StringComparison.OrdinalIgnoreCase)
                ? f with { Severity = nameof(Domain.Enums.ValidationSeverity.Blocker) }
                : f);
        }
        var blocker = promoted.Any(f => string.Equals(f.Severity, nameof(Domain.Enums.ValidationSeverity.Blocker), StringComparison.OrdinalIgnoreCase));
        var blockerCount = promoted.Count(f => string.Equals(f.Severity, nameof(Domain.Enums.ValidationSeverity.Blocker), StringComparison.OrdinalIgnoreCase));
        var warningCount = promoted.Count(f => string.Equals(f.Severity, nameof(Domain.Enums.ValidationSeverity.Warning), StringComparison.OrdinalIgnoreCase));
        var infoCount = promoted.Count(f => string.Equals(f.Severity, nameof(Domain.Enums.ValidationSeverity.Info), StringComparison.OrdinalIgnoreCase));
        var qualityScore = Math.Max(0, 100 - (blockerCount * 25) - (warningCount * 5) - (infoCount * 1));
        return new ValidationResult(blocker, promoted, qualityScore);
    }

    /// <summary>
    /// Iter-31 AI-008 — produce up to three suggested follow-up phrases for
    /// a report's recommendations section. Pulls the <c>follow_up</c> prompt
    /// block from the active rulebook (or override) and routes through the
    /// AI gateway. Returns an empty list when no provider is wired.
    /// </summary>
    public async Task<IReadOnlyList<string>> SuggestFollowUpAsync(
        Tenant tenant, User user, Report report, CancellationToken ct)
    {
        if (_router is null) return Array.Empty<string>();
        var provider = await _router.SelectAsync(tenant, ContainsPhi(report), ct);
        if (provider is null) return Array.Empty<string>();

        var rulebookEntity = await ResolveRulebookEntityAsync(tenant, report, ct);
        var rulebook = rulebookEntity is null ? null : RulebookSpec.FromYaml(rulebookEntity.SourceYaml);
        IReadOnlyDictionary<string, string> overrides = _overrides is null || rulebookEntity is null
            ? new Dictionary<string, string>()
            : await _overrides.LoadAsync(tenant.Id, rulebookEntity.RulebookId, ct);

        var instructions = (overrides.TryGetValue("follow_up", out var ov) && !string.IsNullOrWhiteSpace(ov))
            ? ov
            : rulebook?.PromptBlocks.GetValueOrDefault("follow_up")
            ?? "Suggest up to three short, evidence-based follow-up recommendation phrases for this radiology report. " +
               "One phrase per line, no numbering, no extra commentary. " +
               "Do not invent diagnoses; tie each suggestion to a finding already present.";

        var system = rulebook?.PromptBlocks.GetValueOrDefault("system")
            ?? "You are a clinically conservative radiology AI assistant. Output only the requested suggestions.";

        var userPrompt = $"""
            Modality: {report.Study.Modality}
            Body part: {report.Study.BodyPart}

            FINDINGS:
            {report.Findings}

            IMPRESSION:
            {report.Impression}

            INSTRUCTION:
            {instructions}
            """;

        var result = await _gateway.RouteAsync(tenant, new AiCompletionRequest(
            Provider: provider,
            SystemPrompt: system,
            UserPrompt: userPrompt,
            PromptVersion: PromptVersion,
            ContainsPhi: ContainsPhi(report))
        {
            RulebookId = rulebookEntity?.Id,
            RulebookVersion = rulebookEntity?.Version,
        }, ct);

        var lines = (result.Text ?? string.Empty)
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(l => l.Length > 0)
            .Take(3)
            .ToArray();

        // Iter-32 AI-008 — when the rulebook curates an `approved_followups`
        // allow-list, every suggestion must match a phrase on the list
        // (case- and whitespace-insensitive). Suggestions that fail the gate
        // are dropped and audited by hash only so no unapproved prose leaks
        // back into the user-facing workflow or support logs. Empty allow-list
        // disables the filter (back-compat).
        var allow = rulebook?.Style.ApprovedFollowups;
        if (allow is { Count: > 0 })
        {
            static string Norm(string s) => System.Text.RegularExpressions.Regex
                .Replace((s ?? "").Trim().ToLowerInvariant(), @"\s+", " ").TrimEnd('.', ';');
            var allowed = new HashSet<string>(allow.Select(Norm), StringComparer.Ordinal);
            var accepted = new List<string>(lines.Length);
            foreach (var line in lines)
            {
                if (allowed.Contains(Norm(line)))
                {
                    accepted.Add(line);
                    continue;
                }

                await _audit.AppendAsync(new AuditEvent
                {
                    TenantId = tenant.Id,
                    UserId = user.Id,
                    ReportId = report.Id,
                    Action = AuditAction.PolicyViolation,
                    DetailsJson = JsonSerializer.Serialize(new
                    {
                        policy = "approved_followups",
                        reason = "not_on_allowlist",
                        suggestionHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(line))),
                    }),
                }, ct);
            }
            lines = accepted.ToArray();
        }
        return lines;
    }

    public async Task AcknowledgeAsync(Tenant tenant, User user, Report report, CancellationToken ct)
    {
        report.Status = ReportStatus.Acknowledged;
        report.UpdatedAt = DateTimeOffset.UtcNow;
        await _audit.AppendAsync(new AuditEvent
        {
            TenantId = tenant.Id,
            UserId = user.Id,
            ReportId = report.Id,
            Action = AuditAction.ReportAcknowledged,
            DetailsJson = "{}",
        }, ct);
    }

    private async Task<RulebookSpec?> ResolveRulebookAsync(Tenant tenant, Report report, CancellationToken ct)
    {
        var rb = await ResolveRulebookEntityAsync(tenant, report, ct);
        return rb is null ? null : RulebookSpec.FromYaml(rb.SourceYaml);
    }

    /// <summary>
    /// PRD RB-010: returns the resolved <see cref="Rulebook"/> entity if any,
    /// so callers can inspect <c>Status</c> for production-vs-sandbox gating.
    ///
    /// Iter-31 RB-007 — when the report carries a <see cref="Report.DepartmentTag"/>,
    /// prefer a rulebook row whose <see cref="Rulebook.DepartmentTag"/> matches
    /// (case-insensitive). Falls back to the tenant-wide row by id when no
    /// department match exists. The lookup never crosses tenant boundaries.
    /// </summary>
    private async Task<Rulebook?> ResolveRulebookEntityAsync(Tenant tenant, Report report, CancellationToken ct)
    {
        if (report.RulebookId is null) return null;
        var list = await _rulebooks.ListAsync(tenant.Id, ct);
        var pinned = list.FirstOrDefault(r => r.Id == report.RulebookId.Value);
        if (pinned is null) return null;
        if (string.IsNullOrWhiteSpace(report.DepartmentTag)) return pinned;

        // Prefer a department-scoped sibling with the same RulebookId.
        var match = list.FirstOrDefault(r =>
            r.RulebookId == pinned.RulebookId
            && !string.IsNullOrEmpty(r.DepartmentTag)
            && string.Equals(r.DepartmentTag, report.DepartmentTag, StringComparison.OrdinalIgnoreCase));
        return match ?? pinned;
    }

    /// <summary>
    /// Heuristic PHI detector. Conservative: returns true if the patient
    /// reference is present, or if the dictation contains common PHI shapes
    /// (DOB, MRN-like ids, full names with prefixes). Real deployments must
    /// replace this with a clinically validated PHI detection service.
    /// </summary>
    public static bool ContainsPhi(Report r)
    {
        if (!string.IsNullOrWhiteSpace(r.Study.PatientReference)) return true;
        return ContainsPhiText(
            r.Study.AccessionNumber,
            r.Study.Comparison,
            r.Study.PriorReportSummary,
            r.Indication,
            r.Technique,
            r.Comparison,
            r.Findings,
            r.Impression,
            r.Recommendations,
            r.ServiceRequestRef,
            r.DepartmentTag);
    }

    public static bool ContainsPhiText(params string?[] values)
    {
        var blob = string.Join(' ', values.Where(v => !string.IsNullOrWhiteSpace(v)));
        if (blob.Length == 0) return false;
        if (System.Text.RegularExpressions.Regex.IsMatch(blob, @"\bMRN[:#\s-]*[A-Z0-9]{4,}\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase)) return true;
        if (System.Text.RegularExpressions.Regex.IsMatch(blob, @"\bDOB[:\s]*\d{1,2}/\d{1,2}/\d{2,4}\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase)) return true;
        if (System.Text.RegularExpressions.Regex.IsMatch(blob, @"\b\d{1,2}/\d{1,2}/\d{2,4}\b")) return true;
        if (System.Text.RegularExpressions.Regex.IsMatch(blob, @"\b(Mr|Mrs|Ms|Dr)\.\s+[A-Z][a-z]+\b")) return true;
        if (System.Text.RegularExpressions.Regex.IsMatch(blob, @"\b(patient\s+name|name)[:\s]+[A-Z][a-z]+\s+[A-Z][a-z]+\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase)) return true;
        return false;
    }
}
