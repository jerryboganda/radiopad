using RadioPad.Domain.Entities;
using RadioPad.Domain.ValueObjects;

namespace RadioPad.Application.Abstractions;

/// <summary>Adapter for an AI provider. Each registered provider
/// (Anthropic, Azure OpenAI, Ollama, mock) implements this interface; the
/// AI gateway selects the active adapter at request time using the tenant
/// provider registry and PHI policy.</summary>
public interface IAiProviderAdapter
{
    /// <summary>Adapter id, e.g. "anthropic". Must match <see cref="ProviderConfig.Adapter"/>.</summary>
    string Id { get; }

    Task<AiResult> CompleteAsync(AiCompletionRequest request, CancellationToken cancellationToken);
}

/// <summary>
/// Optional capability for provider adapters that can perform a safe,
/// prompt-free readiness probe. Implementations must never send clinical
/// text or secret material during a probe.
/// </summary>
public interface IAiProviderHealthProbe
{
    Task<AiProviderHealthResult> ProbeAsync(ProviderConfig provider, CancellationToken cancellationToken);
}

public sealed record AiProviderHealthResult(
    bool Ok,
    string? Error = null,
    string? Note = null,
    int? Status = null,
    string? Runtime = null);

public sealed record AiCompletionRequest(
    ProviderConfig Provider,
    string SystemPrompt,
    string UserPrompt,
    string PromptVersion,
    bool ContainsPhi);

public interface IAiGateway
{
    Task<AiResult> RouteAsync(
        Tenant tenant,
        AiCompletionRequest request,
        CancellationToken cancellationToken);
}

public interface IAuditLog
{
    Task AppendAsync(AuditEvent evt, CancellationToken cancellationToken);
    Task<IReadOnlyList<AuditEvent>> QueryAsync(
        Guid tenantId, DateTimeOffset? from, DateTimeOffset? to, int take = 200,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Re-computes the SHA-256 chain across every <see cref="AuditEvent"/> for a
    /// tenant in CreatedAt order. Returns the first event id whose stored
    /// <c>IntegrityChain</c> diverges from the recomputed value, or <c>null</c>
    /// when the chain is intact. Used by <c>GET /api/audit/verify</c>
    /// (PRD §13.2 audit-completeness, AUTH-006 tamper-evident logging).
    /// </summary>
    Task<AuditChainVerification> VerifyChainAsync(Guid tenantId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Result of a tenant audit-chain integrity check.
/// </summary>
/// <param name="EventCount">Total events in scope.</param>
/// <param name="Intact">True when no tampering detected.</param>
/// <param name="FirstBrokenEventId">First event id where the recomputed hash diverged, or null if intact.</param>
/// <param name="LastVerifiedAt">CreatedAt of the last event that was successfully verified.</param>
public record AuditChainVerification(
    int EventCount,
    bool Intact,
    Guid? FirstBrokenEventId,
    DateTimeOffset? LastVerifiedAt);

public interface IRulebookStore
{
    Task<Rulebook?> GetAsync(Guid tenantId, string rulebookId, CancellationToken ct);
    Task<IReadOnlyList<Rulebook>> ListAsync(Guid tenantId, CancellationToken ct);
    Task SaveAsync(Rulebook rulebook, CancellationToken ct);
}

/// <summary>
/// PRD AI-010 — selects the cheapest enabled <see cref="ProviderConfig"/>
/// whose <see cref="ProviderComplianceClass"/> satisfies the tenant + PHI
/// constraints. Returns null when no provider matches.
/// </summary>
public interface IProviderRouter
{
    Task<ProviderConfig?> SelectAsync(
        Tenant tenant,
        bool containsPhi,
        CancellationToken ct);
}

/// <summary>
/// Iter-32 AI-010 — explains the routing decision for a hypothetical
/// (modality, phi, tokens) tuple. Returns the selected provider plus a
/// per-candidate score breakdown (cost / quality / latency normalised
/// scores and the composite). Surfaced via
/// <c>GET /api/ai/routing/preview</c> for ItAdmin support tooling.
/// </summary>
public interface IRoutingPreviewService
{
    Task<RoutingPreview> PreviewAsync(
        Tenant tenant,
        bool containsPhi,
        string? modality,
        int? estimatedInputTokens,
        int? estimatedOutputTokens,
        CancellationToken ct);
}

public sealed record RoutingPreview(
    Guid? SelectedProviderId,
    string? SelectedProviderName,
    string? Reason,
    IReadOnlyList<RoutingCandidate> Candidates,
    RoutingWeights Weights);

public sealed record RoutingWeights(double Cost, double Quality, double Latency);

public sealed record RoutingCandidate(
    Guid ProviderId,
    string Name,
    string Adapter,
    string Compliance,
    bool Eligible,
    string? IneligibleReason,
    decimal CostUsdEstimate,
    double CostScore,
    double QualityScore,
    double LatencyScore,
    int? P95LatencyMs24h,
    double CompositeScore);

/// <summary>
/// Per-tenant AI usage ledger. Every <see cref="AiGateway"/> route call writes
/// one <see cref="AiRequest"/> row regardless of outcome (ok / blocked / error)
/// so administrators have an exhaustive trace for AI-012, BILL-002, and the
/// §13.2 audit-completeness KPI.
/// </summary>
public interface IAiUsageStore
{
    Task RecordAsync(AiRequest request, CancellationToken ct);

    Task<UsageSummary> SummariseAsync(
        Guid tenantId, DateTimeOffset? from, DateTimeOffset? to, CancellationToken ct);
}

public sealed record UsageSummary(
    int TotalRequests,
    int OkCount,
    int BlockedCount,
    int ErrorCount,
    long InputTokens,
    long OutputTokens,
    int AvgLatencyMs,
    IReadOnlyList<UsageByProvider> ByProvider,
    decimal CostTotalUsd = 0m);

/// <summary>
/// Iter-34 BILL-005 — per-provider usage rollup priced via the matching
/// <c>ProviderConfig.CostPerInputKToken</c> / <c>CostPerOutputKToken</c>
/// (USD per 1K tokens). When no current <see cref="ProviderConfig"/> matches
/// the historical <see cref="AiRequest.Provider"/> name (e.g. retired
/// provider) the cost columns stay <c>0</c> and <see cref="Unpriced"/> is
/// <c>true</c> so the UI can flag it.
/// </summary>
public sealed record UsageByProvider(
    string Provider,
    string Adapter,
    int Requests,
    long InputTokens,
    long OutputTokens,
    decimal CostInputUsd = 0m,
    decimal CostOutputUsd = 0m,
    decimal CostTotalUsd = 0m,
    bool Unpriced = false);

/// <summary>
/// PRD BILL-001..006 — minimal data-access surface for plan-quota enforcement.
/// Lives in the Application layer (abstraction); the EF implementation lives
/// in <c>RadioPad.Infrastructure</c>. Used by <c>PlanQuotaService</c>.
/// </summary>
public interface IPlanQuotaStore
{
    /// <summary>Successful AI usage totals since a billing-period boundary.</summary>
    Task<PlanQuotaUsage> GetOkAiUsageAsync(Guid tenantId, DateTimeOffset since, CancellationToken ct);

    /// <summary>
    /// Count successful (Status == "ok") AI calls for a tenant since the given
    /// instant. Used to compare against the plan's monthly AI-call quota.
    /// </summary>
    Task<int> CountOkAiCallsAsync(Guid tenantId, DateTimeOffset since, CancellationToken ct);

    /// <summary>
    /// Persist a mutated <see cref="TenantSettings"/> row (e.g. flipping
    /// <c>SuspendedAt</c> when the grace period elapses). Implementation is
    /// idempotent — passing a row with no changes is a no-op.
    /// </summary>
    Task SaveSettingsAsync(TenantSettings settings, CancellationToken ct);

    /// <summary>
    /// Load the singleton <see cref="TenantSettings"/> row for the given
    /// tenant, or <c>null</c> when none has been provisioned yet. Used by
    /// the AI gateway and suspension guard to evaluate quotas without
    /// reaching across the layering boundary into the EF context.
    /// </summary>
    Task<TenantSettings?> LoadSettingsAsync(Guid tenantId, CancellationToken ct);
}

public sealed record PlanQuotaUsage(int AiCalls, long InputTokens, long OutputTokens);

/// <summary>
/// Transactional email sender abstraction. Implementations may use HTTPS
/// REST APIs (Resend, SendGrid, Mailgun) or SMTP as a fallback.
/// DigitalOcean blocks SMTP ports so the primary implementation should be
/// an HTTPS-based provider for guaranteed deliverability.
/// </summary>
public interface IEmailSender
{
    Task<bool> SendAsync(EmailMessage message, CancellationToken ct);
}

public sealed record EmailMessage(
    string To,
    string Subject,
    string HtmlBody,
    string? PlainBody = null,
    string? From = null,
    string? ReplyTo = null);
