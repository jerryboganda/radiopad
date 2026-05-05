using RadioPad.Application.Abstractions;
using RadioPad.Domain.Entities;
using RadioPad.Domain.Enums;

namespace RadioPad.Application.Services;

/// <summary>
/// PRD BILL-001..006 — checks whether a tenant may make another AI call given
/// their plan, suspension status, grace period, and month-to-date usage. The
/// caller (AI gateway) maps a non-allowed result to a 402 Payment Required.
/// </summary>
public sealed record PlanQuotaResult(
    bool AllowedToProceed,
    string? Reason,
    TenantPlan Plan,
    int AiCallsUsed,
    int AiCallsLimit,
    long InputTokensUsed,
    int InputTokensLimit,
    long OutputTokensUsed,
    int OutputTokensLimit,
    DateTimeOffset ResetAt);

/// <summary>
/// Throwing variant of <see cref="PlanQuotaResult"/> for callers that prefer
/// exception-based control flow. Carries the limit snapshot so the gateway
/// can render an RFC-7807 problem document.
/// </summary>
public sealed class QuotaExceededException : Exception
{
    public string Reason { get; }
    public PlanLimit? Limit { get; }
    public object? Detail { get; }

    public QuotaExceededException(string reason, PlanLimit limit)
        : base($"Plan quota exceeded: {reason}")
    {
        Reason = reason;
        Limit = limit;
    }

    /// <summary>
    /// Variant carrying a serialisable detail payload that the API layer
    /// renders into the RFC-7807 problem document for a 402 response.
    /// </summary>
    public QuotaExceededException(string reason, object detail)
        : base($"Plan quota exceeded: {reason}")
    {
        Reason = reason;
        Detail = detail;
    }
}

public sealed class PlanQuotaService
{
    private readonly IPlanQuotaStore _store;

    public PlanQuotaService(IPlanQuotaStore store) => _store = store;

    public async Task<PlanQuotaResult> CheckAsync(
        Tenant tenant,
        TenantSettings settings,
        CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var monthStart = new DateTimeOffset(now.Year, now.Month, 1, 0, 0, 0, TimeSpan.Zero);
        var nextMonth = monthStart.AddMonths(1);
        var limit = PlanLimits.For(settings.Plan);

        // Already suspended → block immediately.
        if (settings.SuspendedAt is not null)
        {
            return new PlanQuotaResult(false, "suspended", settings.Plan, 0, limit.AiCallsPerMonth, 0, limit.InputTokensPerMonth, 0, limit.OutputTokensPerMonth, nextMonth);
        }

        // Grace period elapsed → flip to suspended and block.
        if (settings.GracePeriodUntil is { } graceUntil && now > graceUntil)
        {
            settings.SuspendedAt = now;
            await _store.SaveSettingsAsync(settings, ct);
            return new PlanQuotaResult(false, "suspended", settings.Plan, 0, limit.AiCallsPerMonth, 0, limit.InputTokensPerMonth, 0, limit.OutputTokensPerMonth, nextMonth);
        }

        var used = await _store.GetOkAiUsageAsync(tenant.Id, monthStart, ct);
        if (used.AiCalls >= limit.AiCallsPerMonth)
        {
            return new PlanQuotaResult(false, "ai_calls", settings.Plan, used.AiCalls, limit.AiCallsPerMonth, used.InputTokens, limit.InputTokensPerMonth, used.OutputTokens, limit.OutputTokensPerMonth, nextMonth);
        }
        if (used.InputTokens >= limit.InputTokensPerMonth)
        {
            return new PlanQuotaResult(false, "input_tokens", settings.Plan, used.AiCalls, limit.AiCallsPerMonth, used.InputTokens, limit.InputTokensPerMonth, used.OutputTokens, limit.OutputTokensPerMonth, nextMonth);
        }
        if (used.OutputTokens >= limit.OutputTokensPerMonth)
        {
            return new PlanQuotaResult(false, "output_tokens", settings.Plan, used.AiCalls, limit.AiCallsPerMonth, used.InputTokens, limit.InputTokensPerMonth, used.OutputTokens, limit.OutputTokensPerMonth, nextMonth);
        }

        return new PlanQuotaResult(true, null, settings.Plan, used.AiCalls, limit.AiCallsPerMonth, used.InputTokens, limit.InputTokensPerMonth, used.OutputTokens, limit.OutputTokensPerMonth, nextMonth);
    }
}
