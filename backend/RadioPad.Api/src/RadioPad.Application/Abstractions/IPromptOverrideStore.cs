namespace RadioPad.Application.Abstractions;

/// <summary>
/// Iter-31 AI-009 — per-tenant overrides for named prompt blocks
/// (e.g. <c>system</c>, <c>impression</c>, <c>dictation_cleanup</c>,
/// <c>follow_up</c>) on a specific rulebook id. Returned as a case-sensitive
/// dictionary keyed by <c>BlockKey</c>. Empty dictionary when no overrides
/// exist for the (tenant, rulebookId) pair.
/// </summary>
public interface IPromptOverrideStore
{
    Task<IReadOnlyDictionary<string, string>> LoadAsync(
        Guid tenantId, string rulebookId, CancellationToken ct);
}
