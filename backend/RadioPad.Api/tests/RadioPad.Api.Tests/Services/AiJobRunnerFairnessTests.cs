using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading.Channels;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using RadioPad.Api.Services;
using Xunit;

namespace RadioPad.Api.Tests.Services;

/// <summary>
/// Fairness tests for <see cref="AiJobRunner"/> (PR-B2). These exercise the runner's
/// two-gate scheduler — a per-tenant cap acquired before the global gate — in isolation:
/// the coordinator's per-job body is swapped for a test-controlled delegate (via the
/// <c>RunJobAsync</c> seam) that blocks on signals we own, so we can observe how many jobs
/// of a given tenant are executing at once and prove a flooding tenant cannot starve a
/// second tenant. No database, provider, or real coordinator is involved.
/// </summary>
public sealed class AiJobRunnerFairnessTests
{
    /// <summary>Test double: overrides only the per-job execution body so we control blocking.</summary>
    private sealed class TestAiJobRunner : AiJobRunner
    {
        private readonly Func<AiJobWork, CancellationTokenSource, Task> _body;

        public TestAiJobRunner(
            Channel<AiJobWork> channel,
            IHostApplicationLifetime lifetime,
            IConfiguration config,
            Func<AiJobWork, CancellationTokenSource, Task> body)
            : base(channel, EmptyScopeFactory(), lifetime, config, NullLogger<AiJobRunner>.Instance)
        {
            _body = body;
        }

        protected override Task RunJobAsync(AiJobWork work, CancellationTokenSource jobCts) => _body(work, jobCts);

        // The scope factory is never touched because RunJobAsync is fully overridden.
        private static IServiceScopeFactory EmptyScopeFactory() =>
            new ServiceCollection().BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();
    }

    private sealed class TestLifetime : IHostApplicationLifetime
    {
        private readonly CancellationTokenSource _stopping = new();
        public CancellationToken ApplicationStarted => CancellationToken.None;
        public CancellationToken ApplicationStopping => _stopping.Token;
        public CancellationToken ApplicationStopped => CancellationToken.None;
        public void StopApplication() => _stopping.Cancel();
    }

    private static IConfiguration Config(int max, int perTenant, int safetySeconds = 600) =>
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["AiJobs:MaxConcurrency"] = max.ToString(),
            ["AiJobs:PerTenantMaxConcurrency"] = perTenant.ToString(),
            ["AiJobs:SafetyTimeoutSeconds"] = safetySeconds.ToString(),
        }).Build();

    private static AiJobWork Work(Guid tenantId) =>
        new(Guid.NewGuid(), tenantId, Guid.NewGuid(), Guid.NewGuid(), "ai", "impression", null);

    private static async Task WaitUntil(Func<bool> condition, TimeSpan timeout)
    {
        var sw = Stopwatch.StartNew();
        while (!condition())
        {
            if (sw.Elapsed > timeout) throw new TimeoutException("condition was not met within the timeout");
            await Task.Delay(15);
        }
    }

    [Fact]
    public async Task TenantCap_LimitsConcurrentJobsPerTenant()
    {
        var tenant = Guid.NewGuid();
        var channel = Channel.CreateUnbounded<AiJobWork>();
        var lifetime = new TestLifetime();
        var release = new SemaphoreSlim(0);      // test admits completions one at a time
        var gate = new object();
        var current = 0;
        var maxConcurrent = 0;
        var entered = 0;

        Func<AiJobWork, CancellationTokenSource, Task> body = async (_, _) =>
        {
            lock (gate) { current++; entered++; if (current > maxConcurrent) maxConcurrent = current; }
            await release.WaitAsync();
            lock (gate) { current--; }
        };

        var runner = new TestAiJobRunner(channel, lifetime, Config(max: 4, perTenant: 1), body);
        for (var i = 0; i < 4; i++) channel.Writer.TryWrite(Work(tenant));
        await runner.StartAsync(CancellationToken.None);

        // With perTenant = 1, exactly one job may execute at a time even though the global
        // gate has room for 4. Let any (wrongly) concurrent entry surface before asserting.
        await WaitUntil(() => Volatile.Read(ref entered) >= 1, TimeSpan.FromSeconds(2));
        await Task.Delay(200);
        lock (gate) Assert.Equal(1, maxConcurrent);

        // Drain: each release admits exactly one more job through the tenant gate.
        for (var i = 0; i < 4; i++) { release.Release(); await Task.Delay(50); }
        await WaitUntil(() => Volatile.Read(ref entered) >= 4, TimeSpan.FromSeconds(3));
        lock (gate) Assert.Equal(1, maxConcurrent);

        await runner.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task OtherTenant_NotStarvedByFloodingTenant()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var channel = Channel.CreateUnbounded<AiJobWork>();
        var lifetime = new TestLifetime();
        var release = new SemaphoreSlim(0);
        var gate = new object();
        var counts = new Dictionary<Guid, int> { [a] = 0, [b] = 0 };

        Func<AiJobWork, CancellationTokenSource, Task> body = async (w, _) =>
        {
            lock (gate) counts[w.TenantId]++;
            await release.WaitAsync();
            lock (gate) counts[w.TenantId]--;
        };

        var runner = new TestAiJobRunner(channel, lifetime, Config(max: 4, perTenant: 2), body);
        for (var i = 0; i < 10; i++) channel.Writer.TryWrite(Work(a));  // A floods the queue first
        channel.Writer.TryWrite(Work(b));                                // B enqueued LAST, behind the flood
        await runner.StartAsync(CancellationToken.None);

        // Deterministic steady state under fairness: A saturates its own cap (2 of the 4
        // global slots) with 8 more jobs queued behind its tenant gate, and B — enqueued
        // dead last — still runs, because A can never hold more than 2 global slots. Were
        // fairness broken (no tenant cap, or global-gate-first) the 4 global slots would go
        // to A's flood and B would sit behind all 10 of A's jobs: counts[b] stays 0 and this
        // wait times out.
        await WaitUntil(() =>
        {
            lock (gate) { return counts[a] == 2 && counts[b] == 1; }
        }, TimeSpan.FromSeconds(3));

        // And A is never allowed above its cap, even briefly.
        lock (gate) Assert.True(counts[a] <= 2, "tenant A must be capped at 2 concurrent jobs");

        for (var i = 0; i < 11; i++) release.Release();
        await runner.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task Shutdown_WhileWaitingOnTenantGate_LeavesRowQueued()
    {
        var tenant = Guid.NewGuid();
        var channel = Channel.CreateUnbounded<AiJobWork>();
        var lifetime = new TestLifetime();
        var release = new SemaphoreSlim(0);
        var invoked = new ConcurrentBag<Guid>();
        var firstEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        Func<AiJobWork, CancellationTokenSource, Task> body = async (w, _) =>
        {
            invoked.Add(w.JobId);
            firstEntered.TrySetResult();
            await release.WaitAsync();
        };

        var runner = new TestAiJobRunner(channel, lifetime, Config(max: 4, perTenant: 1), body);
        var first = Work(tenant);
        var second = Work(tenant);
        channel.Writer.TryWrite(first);
        channel.Writer.TryWrite(second);
        await runner.StartAsync(CancellationToken.None);

        // first holds the sole tenant slot; second is blocked on the tenant gate.
        await firstEntered.Task.WaitAsync(TimeSpan.FromSeconds(2));

        // Graceful shutdown: cancels ExecuteAsync's stoppingToken → second's gate wait
        // throws OperationCanceledException → it never reaches the coordinator, leaving its
        // durable row "queued" for the boot sweep.
        await runner.StopAsync(CancellationToken.None);
        await Task.Delay(150);

        Assert.Contains(first.JobId, invoked);
        Assert.DoesNotContain(second.JobId, invoked);

        release.Release();  // let first drain cleanly
    }

    [Fact]
    public async Task SafetyTimeout_StartsAfterGateAcquisition()
    {
        var tenant = Guid.NewGuid();
        var channel = Channel.CreateUnbounded<AiJobWork>();
        var lifetime = new TestLifetime();
        var firstRelease = new SemaphoreSlim(0);
        var firstEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondCompleted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        bool? secondTokenCancelledAtEntry = null;
        var order = 0;

        Func<AiJobWork, CancellationTokenSource, Task> body = async (_, cts) =>
        {
            var mine = Interlocked.Increment(ref order);
            if (mine == 1)
            {
                firstEntered.TrySetResult();
                await firstRelease.WaitAsync();   // hold the sole slot past the safety timeout
            }
            else
            {
                // This job waited behind the tenant gate for longer than the 1s safety
                // timeout. Its budget must start at gate acquisition, so the token is fresh
                // here — if CancelAfter were armed at CTS creation, it would already be
                // cancelled and the brief work below would throw.
                secondTokenCancelledAtEntry = cts.IsCancellationRequested;
                await Task.Delay(50, cts.Token);
                secondCompleted.TrySetResult();
            }
        };

        var runner = new TestAiJobRunner(channel, lifetime, Config(max: 4, perTenant: 1, safetySeconds: 1), body);
        channel.Writer.TryWrite(Work(tenant));
        channel.Writer.TryWrite(Work(tenant));
        await runner.StartAsync(CancellationToken.None);

        await firstEntered.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await Task.Delay(1300);        // exceed the 1s timeout while the second job waits behind the gate
        firstRelease.Release();        // first finishes → second acquires the gate ~1.3s after being queued

        await secondCompleted.Task.WaitAsync(TimeSpan.FromSeconds(3));  // must complete, not time out
        Assert.False(secondTokenCancelledAtEntry);                      // budget started at gate acquisition

        await runner.StopAsync(CancellationToken.None);
    }
}
