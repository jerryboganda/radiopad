using RadioPad.Api.Services;
using Xunit;

namespace RadioPad.Api.Tests.Services;

/// <summary>
/// State-machine tests for the async report-AI job registry (submit + poll,
/// 2026-07-12). The registry is the contract between the submit endpoint, the
/// detached background task, and the poll endpoint — a wrong transition here
/// means a poll that never terminates or a lost result.
/// </summary>
public class AiJobRegistryTests
{
    private static AiJobRegistry.AiJobState NewJob(AiJobRegistry reg) =>
        reg.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "ai", "impression");

    [Fact]
    public void Create_StartsRunning_AndIsRetrievable()
    {
        var reg = new AiJobRegistry();
        var job = NewJob(reg);

        Assert.Equal("running", job.Status);
        Assert.True(reg.TryGet(job.Id, out var got));
        Assert.Equal(job.Id, got.Id);
        Assert.Equal("ai", got.Kind);
        Assert.Equal("impression", got.Mode);
        Assert.Null(got.CompletedAt);
    }

    [Fact]
    public void Complete_TransitionsToOk_WithPayload()
    {
        var reg = new AiJobRegistry();
        var job = NewJob(reg);

        reg.Complete(job.Id, new { text = "impression text" });

        Assert.True(reg.TryGet(job.Id, out var got));
        Assert.Equal("ok", got.Status);
        Assert.NotNull(got.Payload);
        Assert.NotNull(got.CompletedAt);
        Assert.Null(got.Error);
    }

    [Fact]
    public void Fail_TransitionsToError_WithKind()
    {
        var reg = new AiJobRegistry();
        var job = NewJob(reg);

        reg.Fail(job.Id, "ubag: manual_action_required:gemini_web", "provider_transport");

        Assert.True(reg.TryGet(job.Id, out var got));
        Assert.Equal("error", got.Status);
        Assert.Equal("provider_transport", got.ErrorKind);
        Assert.Null(got.Payload);
    }

    [Fact]
    public void SecondTerminalTransition_KeepsFirstOutcome()
    {
        // The safety timeout races the real result; whichever lands first wins
        // and the loser must not overwrite it.
        var reg = new AiJobRegistry();
        var job = NewJob(reg);

        reg.Complete(job.Id, new { text = "won" });
        reg.Fail(job.Id, "late timeout", "timeout");

        Assert.True(reg.TryGet(job.Id, out var got));
        Assert.Equal("ok", got.Status);
        Assert.Null(got.Error);
    }

    [Fact]
    public void TryGet_UnknownId_IsFalse()
    {
        var reg = new AiJobRegistry();
        Assert.False(reg.TryGet(Guid.NewGuid(), out _));
    }

    [Fact]
    public void TryGetRunning_MatchesOnlyRunningJobs_WithSameScopeKindMode()
    {
        var reg = new AiJobRegistry();
        var tenantId = Guid.NewGuid();
        var reportId = Guid.NewGuid();
        var job = reg.Create(tenantId, reportId, Guid.NewGuid(), "ai", "impression");

        // Same (tenant, report, kind, mode) → found (single-flight attach point).
        Assert.True(reg.TryGetRunning(tenantId, reportId, "ai", "impression", out var found));
        Assert.Equal(job.Id, found.Id);

        // Different mode, kind, report, or tenant → not found.
        Assert.False(reg.TryGetRunning(tenantId, reportId, "ai", "cleanup", out _));
        Assert.False(reg.TryGetRunning(tenantId, reportId, "generate", "generate", out _));
        Assert.False(reg.TryGetRunning(tenantId, Guid.NewGuid(), "ai", "impression", out _));
        Assert.False(reg.TryGetRunning(Guid.NewGuid(), reportId, "ai", "impression", out _));

        // Terminal jobs are never single-flight targets.
        reg.Complete(job.Id, new { text = "done" });
        Assert.False(reg.TryGetRunning(tenantId, reportId, "ai", "impression", out _));
    }

    [Fact]
    public void TryRequestCancel_FiresRegisteredCts_EvenWithNoHotSnapshotYet()
    {
        // Regression test: AiJobCoordinator.RunAsync registers the job's CTS the
        // instant it dequeues — BEFORE the durable row is claimed queued→running,
        // let alone before a hot AiJobState snapshot exists via Create(). A cancel
        // arriving in that window must still fire the CTS; gating on a snapshot
        // (the original implementation) silently dropped it, letting a "cancelled"
        // job run to completion — including writing report sections for a
        // "generate" job after the radiologist was told it was cancelled.
        var reg = new AiJobRegistry();
        var jobId = Guid.NewGuid();
        using var cts = new CancellationTokenSource();
        reg.RegisterCancellation(jobId, cts);

        Assert.False(reg.WasCancelRequested(jobId));
        Assert.True(reg.TryRequestCancel(jobId));
        Assert.True(reg.WasCancelRequested(jobId));
        Assert.True(cts.IsCancellationRequested);
    }

    [Fact]
    public void TryRequestCancel_UnknownId_IsFalse_AndRecordsNoRequest()
    {
        var reg = new AiJobRegistry();
        Assert.False(reg.TryRequestCancel(Guid.NewGuid()));
    }

    [Fact]
    public void TryRequestCancel_AfterTerminal_IsFalse_CtsAlreadyReleased()
    {
        // Terminal() removes the CTS/cancel-requested side maps on every completion
        // path, so a cancel arriving after the job already finished correctly no-ops
        // instead of firing a disposed/reused CancellationTokenSource.
        var reg = new AiJobRegistry();
        var job = NewJob(reg);
        using var cts = new CancellationTokenSource();
        reg.RegisterCancellation(job.Id, cts);

        reg.Complete(job.Id, new { text = "done" });

        Assert.False(reg.TryRequestCancel(job.Id));
        Assert.False(cts.IsCancellationRequested);
    }

    [Fact]
    public void Cancel_TransitionsToCancelled_DistinctFromError()
    {
        var reg = new AiJobRegistry();
        var job = NewJob(reg);

        reg.Cancel(job.Id);

        Assert.True(reg.TryGet(job.Id, out var got));
        Assert.Equal("cancelled", got.Status);
        Assert.Null(got.Error);
        Assert.NotNull(got.CompletedAt);
    }

    [Fact]
    public void CapEviction_SparesJustCompletedJobs()
    {
        // Over-cap pressure must not evict a job that completed moments ago —
        // its poller (2s cadence) hasn't read the result yet. The cap is soft:
        // when every terminal job is inside the floor, the table may exceed
        // MaxJobs rather than lose a result.
        var reg = new AiJobRegistry();
        var first = NewJob(reg);
        reg.Complete(first.Id, new { text = "fresh result" });

        for (var i = 0; i < 520; i++) NewJob(reg); // 520 running jobs > MaxJobs(500)

        Assert.True(reg.TryGet(first.Id, out var survived));
        Assert.Equal("ok", survived.Status);
    }
}
