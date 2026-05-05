using RadioPad.Application.Abstractions;
using RadioPad.Domain.Entities;
using RadioPad.Domain.ValueObjects;

namespace RadioPad.Api.Services;

/// <summary>
/// Iter-33 PERF-004 — instrumentation decorator that records the
/// wall-clock duration of every <see cref="IAiGateway.RouteAsync"/> call
/// on the <c>radiopad.ai.draft.duration_ms</c> histogram. Tags
/// <c>provider</c> + <c>adapter</c> + <c>tenant</c> so dashboards can
/// drill into per-provider P95.
/// </summary>
public sealed class PerfInstrumentedAiGateway : IAiGateway
{
    private readonly IAiGateway _inner;

    public PerfInstrumentedAiGateway(IAiGateway inner)
    {
        _inner = inner;
    }

    public Task<AiResult> RouteAsync(Tenant tenant, AiCompletionRequest request, CancellationToken cancellationToken)
    {
        return PerfBudgets.RecordAsync(
            PerfBudgets.AiDraftDurationMs,
            () => _inner.RouteAsync(tenant, request, cancellationToken),
            new KeyValuePair<string, object?>("tenant", tenant.Slug),
            new KeyValuePair<string, object?>("provider", request.Provider.Name),
            new KeyValuePair<string, object?>("adapter", request.Provider.Adapter));
    }
}
