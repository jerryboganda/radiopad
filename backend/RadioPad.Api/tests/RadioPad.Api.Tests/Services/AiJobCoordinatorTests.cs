using System.Threading.Channels;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using RadioPad.Api.Services;
using RadioPad.Domain.Entities;
using RadioPad.Infrastructure.Persistence;
using Xunit;

namespace RadioPad.Api.Tests.Services;

/// <summary>
/// State-machine tests for <see cref="AiJobCoordinator"/> — the durable write-through
/// layer for async AI jobs. Built on a real SQLite <see cref="RadioPadDbContext"/> and
/// a real <see cref="AiJobRegistry"/> but WITHOUT the hosted <c>AiJobRunner</c>, so
/// submit/retry/cancel transitions and the durable rows they write are exercised
/// deterministically (no background task races the assertions and no provider is ever
/// invoked). Provider execution (RunAsync) is covered separately through the endpoint
/// integration path.
/// </summary>
public sealed class AiJobCoordinatorTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"radiopad-coord-{Guid.NewGuid():N}.db");
    private readonly ServiceProvider _sp;
    private readonly Channel<AiJobWork> _channel = Channel.CreateUnbounded<AiJobWork>();
    private readonly Tenant _tenant = new() { Slug = "coord", DisplayName = "Coord" };
    private readonly User _user = new() { Email = "coord@radiopad.local", DisplayName = "Coord" };

    public AiJobCoordinatorTests()
    {
        _user.TenantId = _tenant.Id;

        var services = new ServiceCollection();
        services.AddDbContext<RadioPadDbContext>(o => o.UseSqlite($"Data Source={_dbPath}"));
        services.AddSingleton<AiJobRegistry>();
        services.AddSingleton(_channel);
        services.AddScoped<AiJobCoordinator>();
        services.AddLogging();
        _sp = services.BuildServiceProvider();

        using var scope = _sp.CreateScope();
        scope.ServiceProvider.GetRequiredService<RadioPadDbContext>().Database.EnsureCreated();
    }

    public void Dispose()
    {
        _sp.Dispose();
        try { if (File.Exists(_dbPath)) File.Delete(_dbPath); } catch { /* best effort */ }
    }

    private static AiJobCoordinator Coordinator(IServiceScope scope) =>
        scope.ServiceProvider.GetRequiredService<AiJobCoordinator>();
    private static RadioPadDbContext Db(IServiceScope scope) =>
        scope.ServiceProvider.GetRequiredService<RadioPadDbContext>();

    private bool TryDrain(out AiJobWork work) => _channel.Reader.TryRead(out work!);
    private int DrainCount() { var n = 0; while (_channel.Reader.TryRead(out _)) n++; return n; }

    [Fact]
    public async Task SubmitAsync_CreatesQueuedRow_AndEnqueues()
    {
        var reportId = Guid.NewGuid();
        using var scope = _sp.CreateScope();

        var (jobId, status, existing) = await Coordinator(scope)
            .SubmitAsync(_tenant, _user, reportId, "generate", "generate", null, default);

        Assert.Equal("queued", status);
        Assert.False(existing);

        var row = await Db(scope).AiJobs.FirstOrDefaultAsync(j => j.Id == jobId);
        Assert.NotNull(row);
        Assert.Equal("queued", row!.Status);
        Assert.Equal(1, row.Attempt);
        Assert.Null(row.RetryOfJobId);
        Assert.Null(row.StartedAt);

        Assert.True(TryDrain(out var work));
        Assert.Equal(jobId, work.JobId);
        Assert.Equal(reportId, work.ReportId);
        Assert.Equal("generate", work.Kind);
    }

    [Fact]
    public async Task SubmitAsync_SingleFlight_AcrossQueuedRow_DoesNotStack()
    {
        var reportId = Guid.NewGuid();
        using var scope = _sp.CreateScope();
        var coord = Coordinator(scope);

        var first = await coord.SubmitAsync(_tenant, _user, reportId, "ai", "rewrite", null, default);
        var second = await coord.SubmitAsync(_tenant, _user, reportId, "ai", "rewrite", null, default);

        Assert.Equal(first.jobId, second.jobId);
        Assert.True(second.alreadyExisting);
        Assert.Equal(1, await Db(scope).AiJobs.CountAsync());
        Assert.Equal(1, DrainCount()); // only the first submit enqueued work
    }

    [Fact]
    public async Task SubmitAsync_SingleFlight_AcrossRunningRow_ReturnsRunning()
    {
        var reportId = Guid.NewGuid();
        using var scope = _sp.CreateScope();
        var coord = Coordinator(scope);
        var db = Db(scope);

        var first = await coord.SubmitAsync(_tenant, _user, reportId, "generate", "generate", null, default);
        var row = await db.AiJobs.FirstAsync(j => j.Id == first.jobId);
        row.Status = "running";
        await db.SaveChangesAsync();

        var second = await coord.SubmitAsync(_tenant, _user, reportId, "generate", "generate", null, default);

        Assert.Equal(first.jobId, second.jobId);
        Assert.Equal("running", second.status);
        Assert.True(second.alreadyExisting);
        Assert.Equal(1, await db.AiJobs.CountAsync());
    }

    [Fact]
    public async Task RequestCancel_QueuedJob_MarksCancelled_WithoutEverRunning()
    {
        var reportId = Guid.NewGuid();
        using var scope = _sp.CreateScope();
        var coord = Coordinator(scope);
        var db = Db(scope);

        var submitted = await coord.SubmitAsync(_tenant, _user, reportId, "generate", "generate", null, default);
        var (changed, status) = await coord.RequestCancelAsync(_tenant.Id, submitted.jobId, default);
        Assert.True(changed);
        Assert.Equal("cancelled", status);

        var row = await db.AiJobs.FirstAsync(j => j.Id == submitted.jobId);
        Assert.Equal("cancelled", row.Status);
        Assert.True(row.CancelRequested);
        Assert.NotNull(row.CompletedAt);
        Assert.Null(row.StartedAt); // never flipped to running → zero provider cost
    }

    [Fact]
    public async Task RequestCancel_RunningJob_SetsCancelRequested_ButStaysRunning()
    {
        var reportId = Guid.NewGuid();
        using var scope = _sp.CreateScope();
        var coord = Coordinator(scope);
        var db = Db(scope);

        var submitted = await coord.SubmitAsync(_tenant, _user, reportId, "generate", "generate", null, default);
        var row = await db.AiJobs.FirstAsync(j => j.Id == submitted.jobId);
        row.Status = "running";
        await db.SaveChangesAsync();

        var (changed, status) = await coord.RequestCancelAsync(_tenant.Id, submitted.jobId, default);
        Assert.True(changed);
        Assert.Equal("running", status);
        await db.Entry(row).ReloadAsync();
        Assert.Equal("running", row.Status);
        Assert.True(row.CancelRequested);
    }

    [Fact]
    public async Task RequestCancel_TerminalJob_ReturnsFalse_AndKeepsFirstOutcome()
    {
        var reportId = Guid.NewGuid();
        using var scope = _sp.CreateScope();
        var coord = Coordinator(scope);
        var db = Db(scope);

        var submitted = await coord.SubmitAsync(_tenant, _user, reportId, "generate", "generate", null, default);
        var row = await db.AiJobs.FirstAsync(j => j.Id == submitted.jobId);
        row.Status = "ok";
        await db.SaveChangesAsync();

        var (changed, status) = await coord.RequestCancelAsync(_tenant.Id, submitted.jobId, default);
        Assert.False(changed);
        Assert.Equal("ok", status);
        await db.Entry(row).ReloadAsync();
        Assert.Equal("ok", row.Status); // a cancel can never resurrect a decided job
    }

    [Fact]
    public async Task RequestCancel_UnknownJob_ReturnsFalse()
    {
        using var scope = _sp.CreateScope();
        var (changed, status) = await Coordinator(scope).RequestCancelAsync(_tenant.Id, Guid.NewGuid(), default);
        Assert.False(changed);
        Assert.Equal("not_found", status);
    }

    [Fact]
    public async Task RequestCancel_OtherTenant_ReturnsFalse()
    {
        var reportId = Guid.NewGuid();
        using var scope = _sp.CreateScope();
        var coord = Coordinator(scope);

        var submitted = await coord.SubmitAsync(_tenant, _user, reportId, "generate", "generate", null, default);
        var (changed, status) = await coord.RequestCancelAsync(Guid.NewGuid(), submitted.jobId, default);
        Assert.False(changed);
        Assert.Equal("not_found", status);
    }

    [Fact]
    public async Task Retry_FromErrorRow_CreatesLinkedRow_AndKeepsOriginal()
    {
        var reportId = Guid.NewGuid();
        using var scope = _sp.CreateScope();
        var coord = Coordinator(scope);
        var db = Db(scope);

        var prior = new AiJob
        {
            TenantId = _tenant.Id, ReportId = reportId, UserId = _user.Id,
            Kind = "generate", Mode = "generate", Status = "error", ErrorKind = "provider_transport", Attempt = 1,
        };
        db.AiJobs.Add(prior);
        await db.SaveChangesAsync();
        DrainCount(); // ignore anything from earlier

        var newId = await coord.RetryAsync(_tenant, _user, prior.Id, default);
        Assert.NotEqual(prior.Id, newId);

        var created = await db.AiJobs.FirstAsync(j => j.Id == newId);
        Assert.Equal("queued", created.Status);
        Assert.Equal(2, created.Attempt);
        Assert.Equal(prior.Id, created.RetryOfJobId);
        Assert.Equal("generate", created.Kind);

        await db.Entry(prior).ReloadAsync();
        Assert.Equal("error", prior.Status); // original never resurrected

        Assert.True(TryDrain(out var work));
        Assert.Equal(newId, work.JobId);
    }

    [Fact]
    public async Task Retry_NonTerminalRow_Throws_NotRetryable()
    {
        var reportId = Guid.NewGuid();
        using var scope = _sp.CreateScope();
        var coord = Coordinator(scope);
        var db = Db(scope);

        var running = new AiJob
        {
            TenantId = _tenant.Id, ReportId = reportId, UserId = _user.Id,
            Kind = "generate", Mode = "generate", Status = "running",
        };
        db.AiJobs.Add(running);
        await db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => coord.RetryAsync(_tenant, _user, running.Id, default));
        Assert.Equal("job_not_retryable", ex.Message);
    }

    [Fact]
    public async Task Retry_UnknownJob_Throws_NotFound()
    {
        using var scope = _sp.CreateScope();
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => Coordinator(scope).RetryAsync(_tenant, _user, Guid.NewGuid(), default));
        Assert.Equal("job_not_found", ex.Message);
    }

    [Fact]
    public async Task Retry_ImpressionWithRegulatedFeatureOff_Throws_GateNotBypassable()
    {
        var reportId = Guid.NewGuid();
        using var scope = _sp.CreateScope();
        var coord = Coordinator(scope);
        var db = Db(scope);

        db.TenantSettings.Add(new TenantSettings
        {
            TenantId = _tenant.Id,
            FeatureFlagsJson = "{\"regulated.autoImpression\":false}",
        });
        var prior = new AiJob
        {
            TenantId = _tenant.Id, ReportId = reportId, UserId = _user.Id,
            Kind = "ai", Mode = "impression", Status = "error", Attempt = 1,
        };
        db.AiJobs.Add(prior);
        await db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => coord.RetryAsync(_tenant, _user, prior.Id, default));
        Assert.Equal("regulated_feature_disabled", ex.Message);
    }

    [Fact]
    public async Task BootRecovery_MarksQueuedAndRunning_ServerRestart_LeavingTerminalRowsAlone()
    {
        var reportId = Guid.NewGuid();
        using (var scope = _sp.CreateScope())
        {
            var db = Db(scope);
            db.AiJobs.Add(new AiJob { TenantId = _tenant.Id, ReportId = reportId, UserId = _user.Id, Kind = "generate", Mode = "generate", Status = "queued" });
            db.AiJobs.Add(new AiJob { TenantId = _tenant.Id, ReportId = reportId, UserId = _user.Id, Kind = "generate", Mode = "generate", Status = "running" });
            db.AiJobs.Add(new AiJob { TenantId = _tenant.Id, ReportId = reportId, UserId = _user.Id, Kind = "ai", Mode = "impression", Status = "ok" });
            db.AiJobs.Add(new AiJob { TenantId = _tenant.Id, ReportId = reportId, UserId = _user.Id, Kind = "ai", Mode = "rewrite", Status = "error", ErrorKind = "timeout" });
            await db.SaveChangesAsync();
        }

        var recovery = new AiJobRecoveryHostedService(
            _sp.GetRequiredService<IServiceScopeFactory>(),
            _sp.GetRequiredService<ILogger<AiJobRecoveryHostedService>>());
        await recovery.StartAsync(default);

        using (var scope = _sp.CreateScope())
        {
            var rows = await Db(scope).AiJobs.ToListAsync();
            Assert.Equal(2, rows.Count(r => r.Status == "error" && r.ErrorKind == "server_restart"));
            Assert.Contains(rows, r => r.Status == "ok"); // terminal untouched
            Assert.Contains(rows, r => r.Status == "error" && r.ErrorKind == "timeout"); // prior error untouched
            Assert.All(rows.Where(r => r.ErrorKind == "server_restart"), r => Assert.NotNull(r.CompletedAt));
        }
    }
}
