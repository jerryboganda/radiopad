using Microsoft.Extensions.Logging;
using RadioPad.Domain.Entities;

namespace RadioPad.Application.Services;

/// <summary>
/// Shared failover loop for auto-routed AI calls (PRD AI-010 hardening,
/// 2026-07-11). Walks the router's ranked candidate chain and retries the
/// attempt on the next provider when the current one fails at the transport
/// layer. Policy blocks (<see cref="ProviderPolicyException"/>), quota gates,
/// and caller cancellation are NEVER failed over — a request a tenant's policy
/// rejects on the best provider must not silently reach a lower-ranked one.
/// Explicitly user-picked providers must not go through this helper at all
/// (fail-fast is the contract for an explicit pick; operator decision
/// 2026-07-11).
/// </summary>
public static class ProviderFailover
{
    /// <summary>Hard cap on providers tried per request, chain permitting.</summary>
    public const int MaxAttempts = 3;

    public const string NoProviderMessage =
        "No enabled provider matches the tenant's PHI / compliance requirements.";

    /// <summary>
    /// Runs <paramref name="attempt"/> against each provider in
    /// <paramref name="ranked"/> (best first, at most <see cref="MaxAttempts"/>)
    /// until one succeeds. Each failed attempt is already audited by
    /// <see cref="AiGateway.RouteAsync"/>; this helper adds the failover log
    /// line and rethrows the LAST transport failure when the whole chain is
    /// exhausted.
    /// </summary>
    public static async Task<(T Result, ProviderConfig Provider)> RunAsync<T>(
        IReadOnlyList<ProviderConfig> ranked,
        Func<ProviderConfig, Task<T>> attempt,
        ILogger log,
        CancellationToken ct)
    {
        if (ranked.Count == 0)
            throw new ProviderPolicyException(NoProviderMessage);

        ProviderTransportException? last = null;
        var attempts = Math.Min(ranked.Count, MaxAttempts);
        for (var i = 0; i < attempts; i++)
        {
            ct.ThrowIfCancellationRequested();
            var provider = ranked[i];
            try
            {
                return (await attempt(provider), provider);
            }
            catch (ProviderTransportException ex)
            {
                last = ex;
                if (i + 1 < attempts)
                {
                    log.LogWarning(ex,
                        "AI provider {Provider} ({Adapter}/{Model}) failed at transport level " +
                        "(attempt {Attempt}/{Max}); failing over to next-ranked provider {Next}",
                        provider.Name, provider.Adapter, provider.Model,
                        i + 1, attempts, ranked[i + 1].Name);
                }
                else
                {
                    log.LogError(ex,
                        "AI provider {Provider} failed and the failover chain is exhausted " +
                        "({Attempt} provider(s) tried)", provider.Name, attempts);
                }
            }
        }
        throw last!;
    }
}
