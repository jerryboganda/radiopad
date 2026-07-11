using Microsoft.Extensions.Logging.Abstractions;
using RadioPad.Application.Services;
using RadioPad.Domain.Entities;
using RadioPad.Domain.Enums;
using Xunit;

namespace RadioPad.Api.Tests;

/// <summary>
/// 2026-07-11 UBAG hardening — the shared failover loop for auto-routed AI
/// calls. Transport failures walk the ranked chain (capped); policy blocks and
/// cancellation never fail over; an empty chain is a policy error.
/// </summary>
public class ProviderFailoverTests
{
    private static ProviderConfig Provider(string name) => new()
    {
        Id = Guid.NewGuid(),
        Name = name,
        Adapter = "mock",
        Model = name,
        Compliance = ProviderComplianceClass.Sandbox,
        Enabled = true,
    };

    [Fact]
    public async Task Transport_failure_falls_through_to_next_ranked_provider()
    {
        var ranked = new[] { Provider("first"), Provider("second") };
        var attempted = new List<string>();

        var (result, provider) = await ProviderFailover.RunAsync(
            ranked,
            p =>
            {
                attempted.Add(p.Name);
                if (p.Name == "first") throw new ProviderTransportException("first: down");
                return Task.FromResult("ok-from-second");
            },
            NullLogger.Instance,
            default);

        Assert.Equal("ok-from-second", result);
        Assert.Equal("second", provider.Name);
        Assert.Equal(new[] { "first", "second" }, attempted);
    }

    [Fact]
    public async Task Policy_exception_does_not_fail_over()
    {
        var ranked = new[] { Provider("first"), Provider("second") };
        var attempted = new List<string>();

        await Assert.ThrowsAsync<ProviderPolicyException>(() =>
            ProviderFailover.RunAsync<string>(
                ranked,
                p =>
                {
                    attempted.Add(p.Name);
                    throw new ProviderPolicyException("blocked by policy");
                },
                NullLogger.Instance,
                default));

        Assert.Equal(new[] { "first" }, attempted);
    }

    [Fact]
    public async Task Attempt_cap_limits_chain_walk_and_rethrows_last_transport_error()
    {
        var ranked = new[] { Provider("p1"), Provider("p2"), Provider("p3"), Provider("p4") };
        var attempted = new List<string>();

        var ex = await Assert.ThrowsAsync<ProviderTransportException>(() =>
            ProviderFailover.RunAsync<string>(
                ranked,
                p =>
                {
                    attempted.Add(p.Name);
                    throw new ProviderTransportException($"{p.Name}: down");
                },
                NullLogger.Instance,
                default));

        Assert.Equal(ProviderFailover.MaxAttempts, attempted.Count);
        Assert.Equal("p3: down", ex.Message);
        Assert.DoesNotContain("p4", attempted);
    }

    [Fact]
    public async Task Empty_chain_is_a_policy_error()
    {
        var ex = await Assert.ThrowsAsync<ProviderPolicyException>(() =>
            ProviderFailover.RunAsync<string>(
                Array.Empty<ProviderConfig>(),
                _ => Task.FromResult("never"),
                NullLogger.Instance,
                default));
        Assert.Equal(ProviderFailover.NoProviderMessage, ex.Message);
    }

    [Fact]
    public async Task Caller_cancellation_propagates_without_further_attempts()
    {
        var ranked = new[] { Provider("first"), Provider("second") };
        using var cts = new CancellationTokenSource();
        var attempted = new List<string>();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            ProviderFailover.RunAsync<string>(
                ranked,
                p =>
                {
                    attempted.Add(p.Name);
                    cts.Cancel();
                    throw new ProviderTransportException("first: down mid-flight");
                },
                NullLogger.Instance,
                cts.Token));

        Assert.Equal(new[] { "first" }, attempted);
    }

    [Fact]
    public async Task First_provider_success_never_touches_the_rest()
    {
        var ranked = new[] { Provider("first"), Provider("second") };
        var attempted = new List<string>();

        var (result, provider) = await ProviderFailover.RunAsync(
            ranked,
            p => { attempted.Add(p.Name); return Task.FromResult(42); },
            NullLogger.Instance,
            default);

        Assert.Equal(42, result);
        Assert.Equal("first", provider.Name);
        Assert.Equal(new[] { "first" }, attempted);
    }
}
