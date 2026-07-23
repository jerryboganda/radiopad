using System.Collections.Concurrent;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using RadioPad.Api.Services;
using RadioPad.Api.Tests.Integration;
using RadioPad.Application.Abstractions;
using RadioPad.Domain.Entities;
using RadioPad.Infrastructure.Persistence;
using Xunit;

namespace RadioPad.Api.Tests.Services;

/// <summary>
/// PR-B4 — end-to-end tests for UBAG re-attach: boot recovery partitions still-running rows
/// that captured a gateway job id (<see cref="AiJob.ProviderJobId"/>) OUT of the server_restart
/// sweep and re-polls the gateway job to completion instead. Built on the full app pipeline with
/// a programmable fake <see cref="IUbagClient"/> swapped in and a fast re-poll cadence so the
/// re-attach loop runs deterministically without wall-clock waits.
/// </summary>
public sealed class AiJobReattachTests : IClassFixture<AiJobReattachTests.ReattachAppFactory>
{
    private readonly ReattachAppFactory _factory;
    public AiJobReattachTests(ReattachAppFactory factory) => _factory = factory;

    private static RadioPadDbContext Db(IServiceScope scope) =>
        scope.ServiceProvider.GetRequiredService<RadioPadDbContext>();
    private static AiJobCoordinator Coordinator(IServiceScope scope) =>
        scope.ServiceProvider.GetRequiredService<AiJobCoordinator>();

    private async Task<Guid> CreateReportAsync(HttpClient client)
    {
        var create = await client.PostAsJsonAsync("/api/reports", new
        {
            modality = "CT",
            bodyPart = "Chest",
            indication = "Cough",
            comparison = "None",
            accessionNumber = $"ACC-REATTACH-{Guid.NewGuid():N}",
        });
        create.EnsureSuccessStatusCode();
        using var doc = await JsonDocument.ParseAsync(await create.Content.ReadAsStreamAsync());
        return doc.RootElement.GetProperty("id").GetGuid();
    }

    private async Task<Guid> SeedRunningJobAsync(
        Guid reportId, string kind, string mode, string providerJobId, DateTimeOffset startedAt)
    {
        using var scope = _factory.Services.CreateScope();
        var db = Db(scope);
        var job = new AiJob
        {
            TenantId = _factory.SeedTenant.Id,
            ReportId = reportId,
            UserId = _factory.SeedUser.Id,
            Kind = kind,
            Mode = mode,
            Status = "running",
            ProviderJobId = providerJobId,
            StartedAt = startedAt,
        };
        db.AiJobs.Add(job);
        await db.SaveChangesAsync();
        return job.Id;
    }

    private async Task ClearJobsAsync()
    {
        using var scope = _factory.Services.CreateScope();
        await Db(scope).AiJobs.ExecuteDeleteAsync();
    }

    private AiJobRecoveryHostedService NewRecovery() => new(
        _factory.Services.GetRequiredService<IServiceScopeFactory>(),
        _factory.Services.GetRequiredService<ILogger<AiJobRecoveryHostedService>>());

    private static UbagJob Running(string id) =>
        new(id, "gemini_web", "running", false, null, null, null, null, "{}");
    private static UbagJob Completed(string id, string output, int latencyMs = 25) =>
        new(id, "gemini_web", "completed", true, output, null, null, latencyMs, "{}");

    // ── recovery partitioning ─────────────────────────────────────────────────

    [Fact]
    public async Task Recovery_RunningRowWithProviderJobId_IsNotSwept()
    {
        await ClearJobsAsync();
        _factory.Ubag.Reset();
        // Gateway job concludes → recovery LEFT the row running and its detached re-attach
        // completes it, rather than sweeping it to server_restart.
        _factory.Ubag.Responder = (id, _) =>
            id == "gw-recover" ? Completed(id, "ubag response") : Running(id);

        using var client = _factory.CreateTenantClient();
        var reportId = await CreateReportAsync(client);
        var jobId = await SeedRunningJobAsync(reportId, "ai", "rewrite", "gw-recover", DateTimeOffset.UtcNow);

        await NewRecovery().StartAsync(default);

        // The detached re-attach lands its terminal write shortly after StartAsync returns.
        var row = await WaitForRowAsync(jobId, r => r.Status != "running");
        Assert.Equal("ok", row.Status);
        Assert.NotEqual("server_restart", row.ErrorKind); // re-attached, never swept
    }

    [Fact]
    public async Task Reattach_QueuedRowsStillSwept()
    {
        await ClearJobsAsync();
        _factory.Ubag.Reset();

        Guid jobId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = Db(scope);
            var job = new AiJob
            {
                TenantId = _factory.SeedTenant.Id,
                ReportId = Guid.NewGuid(),
                UserId = _factory.SeedUser.Id,
                Kind = "generate",
                Mode = "generate",
                Status = "queued",
                ProviderJobId = "bogus-gw", // never started → gateway id is meaningless
            };
            db.AiJobs.Add(job);
            await db.SaveChangesAsync();
            jobId = job.Id;
        }

        await NewRecovery().StartAsync(default);

        using var readScope = _factory.Services.CreateScope();
        var row = await Db(readScope).AiJobs.AsNoTracking().FirstAsync(j => j.Id == jobId);
        Assert.Equal("error", row.Status);
        Assert.Equal("server_restart", row.ErrorKind); // queued is swept regardless of ProviderJobId
    }

    // ── re-attach terminal shaping ────────────────────────────────────────────

    [Fact]
    public async Task Reattach_AiKind_CompletesRowWithShapedPayload()
    {
        _factory.Ubag.Reset();
        _factory.Ubag.Responder = (id, n) =>
            id == "gw-ai" && n >= 2 ? Completed(id, "ubag response") : Running(id);

        using var client = _factory.CreateTenantClient();
        var reportId = await CreateReportAsync(client);
        var jobId = await SeedRunningJobAsync(reportId, "ai", "rewrite", "gw-ai", DateTimeOffset.UtcNow);

        using (var scope = _factory.Services.CreateScope())
            await Coordinator(scope).ReattachUbagAsync(jobId, CancellationToken.None);

        using var readScope = _factory.Services.CreateScope();
        var row = await Db(readScope).AiJobs.AsNoTracking().FirstAsync(j => j.Id == jobId);
        Assert.Equal("ok", row.Status);
        Assert.NotNull(row.ResultJson);
        using var doc = JsonDocument.Parse(row.ResultJson!);
        Assert.Equal("ubag response", doc.RootElement.GetProperty("text").GetString());
        Assert.Equal("reattach", doc.RootElement.GetProperty("routedBy").GetString());
    }

    [Fact]
    public async Task Reattach_GenerateKind_UntouchedReport_AppliesSectionsAndSnapshots()
    {
        _factory.Ubag.Reset();
        const string output = "{\"findings\":\"Reattached findings.\",\"impression\":\"Reattached impression.\","
            + "\"recommendations\":\"Follow up in 3 months.\"}";
        _factory.Ubag.Responder = (id, n) =>
            id == "gw-gen-a" && n >= 2 ? Completed(id, output, 40) : Running(id);

        using var client = _factory.CreateTenantClient();
        var reportId = await CreateReportAsync(client);
        // StartedAt clearly after the report's creation UpdatedAt → freshness guard passes.
        var jobId = await SeedRunningJobAsync(reportId, "generate", "generate", "gw-gen-a", DateTimeOffset.UtcNow.AddMinutes(5));

        using (var scope = _factory.Services.CreateScope())
            await Coordinator(scope).ReattachUbagAsync(jobId, CancellationToken.None);

        using var readScope = _factory.Services.CreateScope();
        var db = Db(readScope);
        var row = await db.AiJobs.AsNoTracking().FirstAsync(j => j.Id == jobId);
        Assert.Equal("ok", row.Status);
        Assert.Null(row.ResultJson); // generate: the report row + its ReportVersion ARE the result

        var report = await db.Reports.AsNoTracking().FirstAsync(r => r.Id == reportId);
        Assert.Contains("Reattached findings", report.Findings);
        Assert.Contains("Reattached impression", report.Impression);

        var versions = await db.ReportVersions.AsNoTracking().Where(v => v.ReportId == reportId).ToListAsync();
        Assert.Contains(versions, v => v.Action == "generate");
    }

    [Fact]
    public async Task Reattach_GenerateKind_ModifiedReport_DiscardsWithReportModified()
    {
        _factory.Ubag.Reset();
        const string output = "{\"findings\":\"Should be discarded.\",\"impression\":\"Should be discarded.\"}";
        _factory.Ubag.Responder = (id, _) =>
            id == "gw-gen-b" ? Completed(id, output, 40) : Running(id);

        using var client = _factory.CreateTenantClient();
        var reportId = await CreateReportAsync(client);
        var startedAt = DateTimeOffset.UtcNow;
        var jobId = await SeedRunningJobAsync(reportId, "generate", "generate", "gw-gen-b", startedAt);

        string originalFindings;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = Db(scope);
            var report = await db.Reports.FirstAsync(r => r.Id == reportId);
            originalFindings = report.Findings ?? string.Empty;
            report.UpdatedAt = startedAt.AddMinutes(1); // a human edit lands AFTER the job started
            await db.SaveChangesAsync();
        }

        using (var scope = _factory.Services.CreateScope())
            await Coordinator(scope).ReattachUbagAsync(jobId, CancellationToken.None);

        using var readScope = _factory.Services.CreateScope();
        var db2 = Db(readScope);
        var row = await db2.AiJobs.AsNoTracking().FirstAsync(j => j.Id == jobId);
        Assert.Equal("error", row.Status);
        Assert.Equal("report_modified", row.ErrorKind);

        var reportAfter = await db2.Reports.AsNoTracking().FirstAsync(r => r.Id == reportId);
        Assert.Equal(originalFindings, reportAfter.Findings ?? string.Empty); // AI result discarded
        Assert.DoesNotContain("Should be discarded", reportAfter.Findings ?? string.Empty);
    }

    [Fact]
    public async Task Reattach_GatewayFailure_FallsBackToServerRestart()
    {
        _factory.Ubag.Reset();
        // The re-poll cannot reach the gateway → the re-attach cannot conclude → server_restart,
        // the same safe state the boot sweep produces (NOT provider_transport, which is reserved
        // for a gateway job that concluded as a definitive failure).
        _factory.Ubag.Responder = (id, _) => id == "gw-fail"
            ? throw new HttpRequestException("gateway unreachable")
            : Running(id);

        using var client = _factory.CreateTenantClient();
        var reportId = await CreateReportAsync(client);
        var jobId = await SeedRunningJobAsync(reportId, "ai", "rewrite", "gw-fail", DateTimeOffset.UtcNow);

        using (var scope = _factory.Services.CreateScope())
            await Coordinator(scope).ReattachUbagAsync(jobId, CancellationToken.None);

        using var readScope = _factory.Services.CreateScope();
        var row = await Db(readScope).AiJobs.AsNoTracking().FirstAsync(j => j.Id == jobId);
        Assert.Equal("error", row.Status);
        Assert.Equal("server_restart", row.ErrorKind);
    }

    [Fact]
    public async Task Reattach_CancelDuringPoll_MarksCancelled()
    {
        _factory.Ubag.Reset();
        _factory.Ubag.Responder = (id, _) => Running(id); // gateway job never terminates

        using var client = _factory.CreateTenantClient();
        var reportId = await CreateReportAsync(client);
        var jobId = await SeedRunningJobAsync(reportId, "ai", "rewrite", "gw-cancel", DateTimeOffset.UtcNow);

        // Re-attach runs on its own scope, registers a cancellable CTS, and polls the never-
        // terminal gateway job.
        var reattach = Task.Run(async () =>
        {
            using var scope = _factory.Services.CreateScope();
            await Coordinator(scope).ReattachUbagAsync(jobId, CancellationToken.None);
        });

        // Wait until the re-attached job is poll-visible + its cancel CTS is registered.
        var registry = _factory.Services.GetRequiredService<AiJobRegistry>();
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < deadline && !registry.TryGet(jobId, out _))
            await Task.Delay(20);
        await Task.Delay(100); // let RegisterCancellation (synchronous, right after Create) settle

        using (var scope = _factory.Services.CreateScope())
        {
            var (changed, status) = await Coordinator(scope)
                .RequestCancelAsync(_factory.SeedTenant.Id, jobId, default);
            Assert.True(changed);
            Assert.Equal("running", status);
        }

        await reattach.WaitAsync(TimeSpan.FromSeconds(15));

        using var readScope = _factory.Services.CreateScope();
        var row = await Db(readScope).AiJobs.AsNoTracking().FirstAsync(j => j.Id == jobId);
        Assert.Equal("cancelled", row.Status);
        Assert.Contains("gw-cancel", _factory.Ubag.CancelledJobIds);
    }

    private async Task<AiJob> WaitForRowAsync(Guid jobId, Func<AiJob, bool> until, int timeoutSeconds = 10)
    {
        var deadline = DateTime.UtcNow.AddSeconds(timeoutSeconds);
        while (true)
        {
            using var scope = _factory.Services.CreateScope();
            var row = await Db(scope).AiJobs.AsNoTracking().FirstAsync(j => j.Id == jobId);
            if (until(row) || DateTime.UtcNow >= deadline) return row;
            await Task.Delay(50);
        }
    }

    // ── infrastructure ────────────────────────────────────────────────────────

    /// <summary>Integration factory that swaps <see cref="IUbagClient"/> for a programmable fake
    /// and drives the re-attach re-poll loop at 50 ms so tests never wait on the 2 s default.</summary>
    public sealed class ReattachAppFactory : RadioPadAppFactory
    {
        public ProgrammableUbagClient Ubag { get; } = new();

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.UseSetting("AiJobs:ReattachPollSeconds", "0.05");
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IUbagClient>();
                services.AddSingleton<IUbagClient>(Ubag);
            });
        }
    }

    /// <summary>Fake UBAG gateway whose <see cref="GetJobAsync"/> is driven per (providerJobId,
    /// per-id call count) so overlapping tests never interfere, and which records every hard
    /// cancel so the cancel path can be asserted.</summary>
    public sealed class ProgrammableUbagClient : IUbagClient
    {
        private readonly ConcurrentDictionary<string, int> _calls = new();

        /// <summary>(providerJobId, 1-based call count for that id) → the job to return. Null →
        /// a never-terminal running job. May throw to simulate an unreachable gateway.</summary>
        public Func<string, int, UbagJob>? Responder { get; set; }

        public ConcurrentBag<string> CancelledJobIds { get; private set; } = new();

        public void Reset()
        {
            Responder = null;
            _calls.Clear();
            CancelledJobIds = new ConcurrentBag<string>();
        }

        public Task<UbagJob> GetJobAsync(string jobId, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            var n = _calls.AddOrUpdate(jobId, 1, (_, c) => c + 1);
            var job = Responder?.Invoke(jobId, n)
                ?? new UbagJob(jobId, "gemini_web", "running", false, null, null, null, null, "{}");
            return Task.FromResult(job);
        }

        public Task CancelJobAsync(string jobId, CancellationToken ct)
        {
            CancelledJobIds.Add(jobId);
            return Task.CompletedTask;
        }

        // ── the rest of IUbagClient: safe, inert defaults (unused by these tests) ──
        public Task<UbagHealth> GetHealthAsync(CancellationToken ct) =>
            Task.FromResult(new UbagHealth(true, "ok", "2026-05-22", null));
        public Task<UbagBrowserSummary> GetBrowserSummaryAsync(CancellationToken ct) =>
            Task.FromResult(new UbagBrowserSummary(1, 3, 3, "ready", "{}"));
        public Task<IReadOnlyList<UbagTarget>> ListTargetsAsync(CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<UbagTarget>>(new[] { new UbagTarget("gemini_web", "Gemini", "ready", true, null) });
        public Task<IReadOnlyList<UbagBrowserContext>> ListBrowserContextsAsync(CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<UbagBrowserContext>>(Array.Empty<UbagBrowserContext>());
        public Task<UbagJob> CreateJobAsync(UbagJobRequest request, string idempotencyKey, CancellationToken ct) =>
            Task.FromResult(Completed("job_new", "ubag response"));
        public Task<UbagWorkflow> CreateWorkflowAsync(UbagWorkflowRequest request, string idempotencyKey, CancellationToken ct) =>
            Task.FromResult(new UbagWorkflow("wf_1", "created", "{}"));
        public Task<UbagWorkflowRun> RunWorkflowAsync(string workflowId, string idempotencyKey, CancellationToken ct) =>
            Task.FromResult(new UbagWorkflowRun("run_1", workflowId, "queued", false, null, null, null, "{}"));
        public Task<UbagWorkflowRun> GetWorkflowRunAsync(string runId, CancellationToken ct) =>
            Task.FromResult(new UbagWorkflowRun(runId, "wf_1", "completed", true, "done", null, null, "{}"));
        public Task<UbagJob> CreateTranscriptionJobAsync(UbagTranscriptionRequest request, string idempotencyKey, CancellationToken ct) =>
            Task.FromResult(Completed("job_tx_1", "transcript text"));
        public Task<UbagArtifact> UploadJobArtifactAsync(string jobId, string key, Stream content, string contentType, long contentLength, string idempotencyKey, CancellationToken ct) =>
            Task.FromResult(new UbagArtifact(jobId, key, contentType, contentLength, "sha256:stub"));
    }
}
