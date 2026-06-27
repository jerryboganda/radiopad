using Microsoft.Extensions.Logging;
using RadioPad.Application.Abstractions;
using RadioPad.Application.Stt;
using RadioPad.Domain.Entities;

namespace RadioPad.Application.Services;

/// <summary>
/// LLM medical-accuracy pass for the cross-check. Asks the model to act as a
/// board-certified radiologist QA — flagging only clear errors in laterality,
/// negation, anatomy, measurements, and drug/term spelling — and to emit a strict
/// JSON array of original→corrected edits (never inventing findings). Routes
/// through <see cref="IAiGateway"/> so PHI policy + audit apply; the optional
/// <c>forcedProvider</c> is how the opt-in UBAG route is honored.
/// </summary>
public sealed class CrossCheckReviewService : ICrossCheckReviewService
{
    private const string PromptVersion = "v1.crosscheck_medical_review";

    private readonly IAiGateway _gateway;
    private readonly IProviderRouter _router;
    private readonly ILogger<CrossCheckReviewService> _log;

    public CrossCheckReviewService(
        IAiGateway gateway, IProviderRouter router, ILogger<CrossCheckReviewService> log)
    {
        _gateway = gateway;
        _router = router;
        _log = log;
    }

    public async Task<CrossCheckReviewResult> ReviewAsync(
        Tenant tenant, Report report, string text, string? sectionKey,
        ProviderConfig? forcedProvider, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(text))
            return new CrossCheckReviewResult(Array.Empty<CrossCheckCorrection>(), "none", "none", 0);

        var containsPhi = ReportingService.ContainsPhi(report);
        var provider = forcedProvider
            ?? await _router.SelectAsync(tenant, containsPhi, ct)
            ?? throw new ProviderPolicyException(
                "No enabled provider matches the tenant's PHI / compliance requirements.");

        const string system =
            "You are a board-certified radiologist performing a careful QA pass on a " +
            "dictated report. Correct ONLY clear errors in laterality (left/right), " +
            "negation (no/without), anatomy, measurements/units, and drug or term " +
            "spelling. Never invent findings, never rephrase for style, never change " +
            "clinical meaning beyond fixing an obvious error. Output ONLY the JSON " +
            "array described — no preface, no trailing prose.";

        var userPrompt = $$"""
            Modality: {{report.Study.Modality}}
            Body part: {{report.Study.BodyPart}}

            REPORT TEXT:
            {{text}}

            Return a JSON array of corrections (empty array if none). Each item:
            {
              "original": "<exact substring from the text>",
              "corrected": "<the corrected wording>",
              "reason": "<short why>",
              "category": "laterality|negation|anatomy|measurement|drug_term|spelling",
              "severity": "safety|warning|info"
            }
            Use "safety" for laterality, negation, and measurement errors.
            """;

        var result = await _gateway.RouteAsync(tenant, new AiCompletionRequest(
            Provider: provider,
            SystemPrompt: system,
            UserPrompt: userPrompt,
            PromptVersion: PromptVersion,
            ContainsPhi: containsPhi), ct);

        var items = CrossCheckDiff.ParseLlmCorrections(result.Text);
        var corrections = CrossCheckDiff.AnchorLlmCorrections(text, items, sectionKey);

        _log.LogInformation(
            "Cross-check medical review via {Provider}: {N} corrections in {Ms} ms",
            result.Provider, corrections.Count, result.LatencyMs);

        return new CrossCheckReviewResult(corrections, result.Provider, result.Model, result.LatencyMs);
    }
}
