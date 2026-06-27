using RadioPad.Application.Stt;
using RadioPad.Domain.Entities;

namespace RadioPad.Application.Abstractions;

/// <summary>
/// The LLM medical-accuracy pass of the cross-check: reviews already-transcribed
/// text (laterality, negation, anatomy, measurements, drug/term spelling) and
/// returns suggested original→corrected edits. Routes through <see cref="IAiGateway"/>
/// so PHI policy + audit apply. Lives hosted-side (it needs a tenant + provider);
/// the on-device ASR pass is separate.
/// </summary>
public interface ICrossCheckReviewService
{
    /// <param name="forcedProvider">
    /// When non-null (e.g. the user opted into UBAG), this provider is used instead
    /// of the router's choice. The caller is responsible for it satisfying policy.
    /// </param>
    Task<CrossCheckReviewResult> ReviewAsync(
        Tenant tenant,
        Report report,
        string text,
        string? sectionKey,
        ProviderConfig? forcedProvider,
        CancellationToken ct);
}
