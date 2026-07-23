using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using RadioPad.Domain.Entities;
using RadioPad.Domain.Enums;
using RadioPad.Infrastructure.Persistence;
using Xunit;

namespace RadioPad.Api.Tests.Integration;

/// <summary>
/// Contract tests for the async AI-job submit/poll endpoints after the durable
/// rewire. The wire shapes are unchanged (202 + {jobId, status}; poll envelope
/// {jobId, kind, mode, status, elapsedMs, result, error, errorKind}); the new
/// behaviour is the poll's DB fallback on a registry miss — which is what lets a
/// restart surface <c>errorKind: server_restart</c> instead of a bare 404, and a
/// still-queued job stay visible before the runner materialises its hot entry.
/// </summary>
public class AiJobEndpointsTests : IClassFixture<RadioPadAppFactory>
{
    private readonly RadioPadAppFactory _factory;
    public AiJobEndpointsTests(RadioPadAppFactory factory) => _factory = factory;

    private async Task<Guid> CreateReportAsync(HttpClient client)
    {
        var create = await client.PostAsJsonAsync("/api/reports", new
        {
            modality = "CT",
            bodyPart = "Chest",
            indication = "Cough",
            comparison = "None",
            accessionNumber = $"ACC-JOB-{Guid.NewGuid():N}",
        });
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var doc = await JsonDocument.ParseAsync(await create.Content.ReadAsStreamAsync());
        return doc.RootElement.GetProperty("id").GetGuid();
    }

    private async Task<Guid> SeedJobAsync(Guid tenantId, Guid reportId, Action<AiJob> configure)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RadioPadDbContext>();
        var job = new AiJob
        {
            TenantId = tenantId,
            ReportId = reportId,
            UserId = _factory.SeedUser.Id,
            Kind = "ai",
            Mode = "impression",
            Status = "queued",
        };
        configure(job);
        db.AiJobs.Add(job);
        await db.SaveChangesAsync();
        return job.Id;
    }

    [Fact]
    public async Task SubmitGenerateJob_Returns202_WithJobId()
    {
        using var client = _factory.CreateTenantClient();
        var reportId = await CreateReportAsync(client);

        var resp = await client.PostAsJsonAsync($"/api/reports/{reportId}/generate/jobs", new { });

        Assert.Equal(HttpStatusCode.Accepted, resp.StatusCode);
        var doc = await JsonDocument.ParseAsync(await resp.Content.ReadAsStreamAsync());
        Assert.True(doc.RootElement.TryGetProperty("jobId", out var jobId));
        Assert.NotEqual(Guid.Empty, jobId.GetGuid());
        Assert.True(doc.RootElement.TryGetProperty("status", out _));
    }

    [Fact]
    public async Task Poll_AfterRegistryMiss_FallsBackToDb_ForAiResult()
    {
        using var client = _factory.CreateTenantClient();
        var reportId = await CreateReportAsync(client);
        var jobId = await SeedJobAsync(_factory.SeedTenant.Id, reportId, j =>
        {
            j.Kind = "ai";
            j.Mode = "impression";
            j.Status = "ok";
            j.ResultJson = "{\"text\":\"impression text\",\"mode\":\"impression\"}";
            j.CompletedAt = DateTimeOffset.UtcNow;
        });

        var resp = await client.GetAsync($"/api/reports/{reportId}/ai/jobs/{jobId}");

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var doc = await JsonDocument.ParseAsync(await resp.Content.ReadAsStreamAsync());
        Assert.Equal("ok", doc.RootElement.GetProperty("status").GetString());
        Assert.Equal("impression text", doc.RootElement.GetProperty("result").GetProperty("text").GetString());
    }

    [Fact]
    public async Task Poll_RestartMarkedJob_SurfacesServerRestart_NotA404()
    {
        using var client = _factory.CreateTenantClient();
        var reportId = await CreateReportAsync(client);
        var jobId = await SeedJobAsync(_factory.SeedTenant.Id, reportId, j =>
        {
            j.Kind = "generate";
            j.Mode = "generate";
            j.Status = "error";
            j.ErrorKind = "server_restart";
            j.Error = "Interrupted by a server restart. Retry to run it again.";
            j.CompletedAt = DateTimeOffset.UtcNow;
        });

        var resp = await client.GetAsync($"/api/reports/{reportId}/ai/jobs/{jobId}");

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var doc = await JsonDocument.ParseAsync(await resp.Content.ReadAsStreamAsync());
        Assert.Equal("error", doc.RootElement.GetProperty("status").GetString());
        Assert.Equal("server_restart", doc.RootElement.GetProperty("errorKind").GetString());
    }

    [Fact]
    public async Task Poll_QueuedJob_IsVisibleFromDb_BeforeRunnerRuns()
    {
        using var client = _factory.CreateTenantClient();
        var reportId = await CreateReportAsync(client);
        var jobId = await SeedJobAsync(_factory.SeedTenant.Id, reportId, j =>
        {
            j.Kind = "generate";
            j.Mode = "generate";
            j.Status = "queued";
        });

        var resp = await client.GetAsync($"/api/reports/{reportId}/ai/jobs/{jobId}");

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var doc = await JsonDocument.ParseAsync(await resp.Content.ReadAsStreamAsync());
        Assert.Equal("queued", doc.RootElement.GetProperty("status").GetString());
        // App-wide JSON convention (Program.cs: DefaultIgnoreCondition = WhenWritingNull)
        // omits null-valued properties entirely rather than serializing "result": null.
        Assert.False(doc.RootElement.TryGetProperty("result", out _));
    }

    [Fact]
    public async Task Poll_UnknownJob_Returns404_JobNotFound()
    {
        using var client = _factory.CreateTenantClient();
        var reportId = await CreateReportAsync(client);

        var resp = await client.GetAsync($"/api/reports/{reportId}/ai/jobs/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
        var doc = await JsonDocument.ParseAsync(await resp.Content.ReadAsStreamAsync());
        Assert.Equal("job_not_found", doc.RootElement.GetProperty("kind").GetString());
    }

    [Fact]
    public async Task AiJob_WithMockStreaming_PollExposesProgressThenResult()
    {
        // A dedicated "mock-slow" provider spaces its 3 synthetic stream chunks out (60 ms each,
        // MockAiAdapter) so the async job stays "running" long enough for a poll to observe live
        // token progress before the terminal result lands. High cost keeps auto-routing from ever
        // preferring it for the other tests sharing this factory.
        Guid providerId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RadioPadDbContext>();
            var provider = new ProviderConfig
            {
                TenantId = _factory.SeedTenant.Id,
                Name = "Mock Slow Stream",
                Adapter = "mock",
                Model = "mock-slow",
                Compliance = ProviderComplianceClass.LocalOnly,
                Enabled = true,
                CostPerInputKToken = 999m,
                CostPerOutputKToken = 999m,
            };
            db.Providers.Add(provider);
            await db.SaveChangesAsync();
            providerId = provider.Id;
        }

        using var client = _factory.CreateTenantClient();
        var reportId = await CreateReportAsync(client);

        var submit = await client.PostAsJsonAsync($"/api/reports/{reportId}/ai/jobs",
            new { mode = "cleanup", providerId });
        Assert.Equal(HttpStatusCode.Accepted, submit.StatusCode);
        using var submitDoc = await JsonDocument.ParseAsync(await submit.Content.ReadAsStreamAsync());
        var jobId = submitDoc.RootElement.GetProperty("jobId").GetGuid();

        var sawProgress = false;
        JsonElement terminal = default;
        var haveTerminal = false;
        var deadline = DateTime.UtcNow.AddSeconds(20);
        while (DateTime.UtcNow < deadline)
        {
            using var poll = await client.GetAsync($"/api/reports/{reportId}/ai/jobs/{jobId}");
            using var doc = await JsonDocument.ParseAsync(await poll.Content.ReadAsStreamAsync());
            var root = doc.RootElement;
            var status = root.GetProperty("status").GetString();

            if (status == "running"
                && root.TryGetProperty("progress", out var progress)
                && progress.TryGetProperty("tokens", out var tokens)
                && tokens.GetInt32() > 0)
            {
                sawProgress = true;
                // Percent is always null on the streaming path → omitted (WhenWritingNull).
                Assert.False(progress.TryGetProperty("percent", out _));
            }

            if (status is "ok" or "error" or "cancelled")
            {
                terminal = root.Clone();
                haveTerminal = true;
                break;
            }
            await Task.Delay(15);
        }

        Assert.True(sawProgress, "expected to observe streaming progress (tokens > 0) while the job was running");
        Assert.True(haveTerminal, "job never reached a terminal state within the deadline");
        Assert.Equal("ok", terminal.GetProperty("status").GetString());
        // Terminal poll carries the AI result and no longer carries progress.
        Assert.True(terminal.TryGetProperty("result", out var result));
        Assert.True(result.TryGetProperty("text", out _));
        Assert.False(terminal.TryGetProperty("progress", out _));
    }

    [Fact]
    public async Task Poll_JobOfAnotherTenant_Returns404()
    {
        // Tenant isolation on the DB fallback: a row whose TenantId is not the
        // caller's must be invisible even when the report path matches.
        using var client = _factory.CreateTenantClient();
        var reportId = await CreateReportAsync(client);
        var jobId = await SeedJobAsync(Guid.NewGuid(), reportId, j =>
        {
            j.Status = "ok";
            j.ResultJson = "{\"text\":\"leaked\"}";
            j.CompletedAt = DateTimeOffset.UtcNow;
        });

        var resp = await client.GetAsync($"/api/reports/{reportId}/ai/jobs/{jobId}");

        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }
}
