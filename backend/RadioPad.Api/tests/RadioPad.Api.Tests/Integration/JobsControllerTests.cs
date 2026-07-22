using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RadioPad.Domain.Entities;
using RadioPad.Domain.Enums;
using RadioPad.Infrastructure.Persistence;
using Xunit;

namespace RadioPad.Api.Tests.Integration;

/// <summary>
/// Integration tests for the unified <c>api/jobs</c> surface (Phase 2.1 of the durable
/// async AI-job platform): the caller's own job list, single-job detail, and the
/// cancel/retry lifecycle. Jobs are seeded directly into the durable table (like
/// <see cref="AiJobEndpointsTests"/>) so list/get/cancel assertions are deterministic
/// and never depend on the background runner's timing; the retry test exercises the
/// real coordinator path (a new linked row) and asserts on its immutable linkage fields.
///
/// <para>The <see cref="RadioPadAppFactory"/> fixture is shared across the class, so
/// jobs seeded for <c>SeedUser</c> accumulate — every assertion locates its subject by
/// job id rather than relying on list counts.</para>
/// </summary>
public class JobsControllerTests : IClassFixture<RadioPadAppFactory>
{
    private readonly RadioPadAppFactory _factory;
    public JobsControllerTests(RadioPadAppFactory factory) => _factory = factory;

    // ── helpers ──────────────────────────────────────────────────────────────

    private async Task<Guid> CreateReportAsync(HttpClient client)
    {
        var create = await client.PostAsJsonAsync("/api/reports", new
        {
            modality = "CT",
            bodyPart = "Chest",
            indication = "Cough",
            comparison = "None",
            accessionNumber = $"ACC-JOBS-{Guid.NewGuid():N}",
        });
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var doc = await JsonDocument.ParseAsync(await create.Content.ReadAsStreamAsync());
        return doc.RootElement.GetProperty("id").GetGuid();
    }

    private async Task<Guid> SeedJobAsync(Guid tenantId, Guid userId, Guid reportId, Action<AiJob> configure)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RadioPadDbContext>();
        var job = new AiJob
        {
            TenantId = tenantId,
            ReportId = reportId,
            UserId = userId,
            Kind = "ai",
            Mode = "impression",
            Status = "queued",
        };
        configure(job);
        db.AiJobs.Add(job);
        await db.SaveChangesAsync();
        return job.Id;
    }

    private async Task<User> SeedRadiologistAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RadioPadDbContext>();
        var u = new User
        {
            TenantId = _factory.SeedTenant.Id,
            Email = $"it-radiologist2-{Guid.NewGuid():N}@radiopad.local",
            DisplayName = "Second Radiologist",
            Role = UserRole.Radiologist,
        };
        db.Users.Add(u);
        await db.SaveChangesAsync();
        return u;
    }

    private HttpClient ClientFor(string email)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-RadioPad-Tenant", _factory.SeedTenant.Slug);
        client.DefaultRequestHeaders.Add("X-RadioPad-User", email);
        return client;
    }

    private static async Task<JsonElement> RootAsync(HttpResponseMessage resp)
    {
        var doc = await JsonDocument.ParseAsync(await resp.Content.ReadAsStreamAsync());
        return doc.RootElement.Clone();
    }

    private static bool TryFindJob(JsonElement listRoot, Guid jobId, out JsonElement match)
    {
        foreach (var el in listRoot.GetProperty("jobs").EnumerateArray())
        {
            if (el.GetProperty("jobId").GetGuid() == jobId)
            {
                match = el;
                return true;
            }
        }
        match = default;
        return false;
    }

    private async Task<List<AuditEvent>> AuditFor(Guid reportId, AuditAction action)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RadioPadDbContext>();
        return await db.AuditEvents.AsNoTracking()
            .Where(a => a.ReportId == reportId && a.Action == action)
            .ToListAsync();
    }

    // ── list ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task List_ReturnsOwnJob_WithReportDescriptor_AndNoResultField()
    {
        using var client = _factory.CreateTenantClient();
        var reportId = await CreateReportAsync(client);
        var jobId = await SeedJobAsync(_factory.SeedTenant.Id, _factory.SeedUser.Id, reportId, j =>
        {
            j.Kind = "generate";
            j.Mode = "generate";
            j.Status = "queued";
        });

        var resp = await client.GetAsync("/api/jobs");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var root = await RootAsync(resp);

        Assert.True(TryFindJob(root, jobId, out var job));
        Assert.Equal("generate", job.GetProperty("kind").GetString());
        Assert.Equal("queued", job.GetProperty("status").GetString());
        Assert.Equal(reportId, job.GetProperty("reportId").GetGuid());
        Assert.Equal(1, job.GetProperty("attempt").GetInt32());
        Assert.True(job.TryGetProperty("elapsedMs", out var elapsed));
        Assert.True(elapsed.GetInt64() >= 0);

        // Report descriptor is projected from the tenant-scoped report.
        var report = job.GetProperty("report");
        Assert.Equal("CT", report.GetProperty("modality").GetString());
        Assert.Equal("Chest", report.GetProperty("bodyPart").GetString());
        Assert.False(string.IsNullOrEmpty(report.GetProperty("accession").GetString()));

        // The list is deliberately light — no result payload on list rows.
        Assert.False(job.TryGetProperty("result", out _));
    }

    [Fact]
    public async Task List_IsScopedToCaller_HidesOtherUserAndOtherTenantJobs()
    {
        using var client = _factory.CreateTenantClient();
        var reportId = await CreateReportAsync(client);

        // A job owned by a DIFFERENT user in the SAME tenant.
        var otherUser = await SeedRadiologistAsync();
        var otherUserJob = await SeedJobAsync(_factory.SeedTenant.Id, otherUser.Id, reportId, j => j.Status = "queued");

        // A job in a DIFFERENT tenant (same report path, foreign tenant id).
        var otherTenantJob = await SeedJobAsync(Guid.NewGuid(), _factory.SeedUser.Id, reportId, j => j.Status = "queued");

        // A job owned by the caller.
        var ownJob = await SeedJobAsync(_factory.SeedTenant.Id, _factory.SeedUser.Id, reportId, j => j.Status = "queued");

        var root = await RootAsync(await client.GetAsync("/api/jobs"));
        Assert.True(TryFindJob(root, ownJob, out _));
        Assert.False(TryFindJob(root, otherUserJob, out _));
        Assert.False(TryFindJob(root, otherTenantJob, out _));

        // The other user's own list must not contain the caller's job either — the list
        // is per-user, not per-tenant.
        using var otherClient = ClientFor(otherUser.Email);
        var otherRoot = await RootAsync(await otherClient.GetAsync("/api/jobs"));
        Assert.True(TryFindJob(otherRoot, otherUserJob, out _));
        Assert.False(TryFindJob(otherRoot, ownJob, out _));
    }

    [Fact]
    public async Task List_Active_KeepsQueuedRunningAndRecentTerminal_DropsOldTerminal()
    {
        using var client = _factory.CreateTenantClient();
        var reportId = await CreateReportAsync(client);

        var queued = await SeedJobAsync(_factory.SeedTenant.Id, _factory.SeedUser.Id, reportId, j => j.Status = "queued");
        var running = await SeedJobAsync(_factory.SeedTenant.Id, _factory.SeedUser.Id, reportId, j =>
        {
            j.Status = "running";
            j.StartedAt = DateTimeOffset.UtcNow;
        });
        var recentTerminal = await SeedJobAsync(_factory.SeedTenant.Id, _factory.SeedUser.Id, reportId, j =>
        {
            j.Status = "ok";
            j.CompletedAt = DateTimeOffset.UtcNow.AddHours(-2);
        });
        var oldTerminal = await SeedJobAsync(_factory.SeedTenant.Id, _factory.SeedUser.Id, reportId, j =>
        {
            j.Status = "ok";
            j.CompletedAt = DateTimeOffset.UtcNow.AddHours(-48);
        });

        var activeRoot = await RootAsync(await client.GetAsync("/api/jobs?active=true"));
        Assert.True(TryFindJob(activeRoot, queued, out _));
        Assert.True(TryFindJob(activeRoot, running, out _));
        Assert.True(TryFindJob(activeRoot, recentTerminal, out _));
        Assert.False(TryFindJob(activeRoot, oldTerminal, out _));

        // Without the active filter the old terminal row is visible again.
        var allRoot = await RootAsync(await client.GetAsync("/api/jobs?limit=200"));
        Assert.True(TryFindJob(allRoot, oldTerminal, out _));
    }

    // ── get ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Get_OkAiJob_IncludesResult_AndDescriptor()
    {
        using var client = _factory.CreateTenantClient();
        var reportId = await CreateReportAsync(client);
        var jobId = await SeedJobAsync(_factory.SeedTenant.Id, _factory.SeedUser.Id, reportId, j =>
        {
            j.Kind = "ai";
            j.Mode = "impression";
            j.Status = "ok";
            j.ResultJson = "{\"text\":\"impression text\",\"mode\":\"impression\"}";
            j.CompletedAt = DateTimeOffset.UtcNow;
        });

        var resp = await client.GetAsync($"/api/jobs/{jobId}");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var root = await RootAsync(resp);

        Assert.Equal("ok", root.GetProperty("status").GetString());
        Assert.Equal("impression text", root.GetProperty("result").GetProperty("text").GetString());
        Assert.Equal("CT", root.GetProperty("report").GetProperty("modality").GetString());
    }

    [Fact]
    public async Task Get_GenerateOkJob_HasNoResultPayload()
    {
        using var client = _factory.CreateTenantClient();
        var reportId = await CreateReportAsync(client);
        var jobId = await SeedJobAsync(_factory.SeedTenant.Id, _factory.SeedUser.Id, reportId, j =>
        {
            j.Kind = "generate";
            j.Mode = "generate";
            j.Status = "ok";
            j.CompletedAt = DateTimeOffset.UtcNow;
        });

        var root = await RootAsync(await client.GetAsync($"/api/jobs/{jobId}"));
        Assert.Equal("ok", root.GetProperty("status").GetString());
        // A generate job's result is the report itself — no ResultJson, so `result` is
        // null (omitted when null under the app's WhenWritingNull policy).
        var hasResult = root.TryGetProperty("result", out var result);
        Assert.True(!hasResult || result.ValueKind == JsonValueKind.Null);
    }

    [Fact]
    public async Task Get_CrossTenantJob_Returns404()
    {
        using var client = _factory.CreateTenantClient();
        var reportId = await CreateReportAsync(client);
        var jobId = await SeedJobAsync(Guid.NewGuid(), _factory.SeedUser.Id, reportId, j =>
        {
            j.Status = "ok";
            j.ResultJson = "{\"text\":\"leaked\"}";
            j.CompletedAt = DateTimeOffset.UtcNow;
        });

        var resp = await client.GetAsync($"/api/jobs/{jobId}");
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
        var root = await RootAsync(resp);
        Assert.Equal("job_not_found", root.GetProperty("kind").GetString());
    }

    // ── cancel ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Cancel_QueuedJob_Returns200Cancelled_Immediately_AndAudits()
    {
        using var client = _factory.CreateTenantClient();
        var reportId = await CreateReportAsync(client);
        var jobId = await SeedJobAsync(_factory.SeedTenant.Id, _factory.SeedUser.Id, reportId, j =>
        {
            j.Kind = "generate";
            j.Mode = "generate";
            j.Status = "queued";
        });

        var resp = await client.PostAsJsonAsync($"/api/jobs/{jobId}/cancel", new { });
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var root = await RootAsync(resp);
        Assert.Equal("cancelled", root.GetProperty("status").GetString());

        // The durable row was flipped terminal — the runner (which never even saw this
        // seeded row) will never invoke a provider for it.
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RadioPadDbContext>();
            var row = await db.AiJobs.AsNoTracking().FirstAsync(j => j.Id == jobId);
            Assert.Equal("cancelled", row.Status);
            Assert.NotNull(row.CompletedAt);
        }

        var audits = await AuditFor(reportId, AuditAction.AiJobCancelled);
        Assert.Contains(audits, a => a.DetailsJson.Contains(jobId.ToString()));
    }

    [Fact]
    public async Task Cancel_RunningJob_Returns202_SetsCancelRequested_AndAudits()
    {
        using var client = _factory.CreateTenantClient();
        var reportId = await CreateReportAsync(client);
        var jobId = await SeedJobAsync(_factory.SeedTenant.Id, _factory.SeedUser.Id, reportId, j =>
        {
            j.Status = "running";
            j.StartedAt = DateTimeOffset.UtcNow;
        });

        var resp = await client.PostAsJsonAsync($"/api/jobs/{jobId}/cancel", new { });
        Assert.Equal(HttpStatusCode.Accepted, resp.StatusCode);
        var root = await RootAsync(resp);
        Assert.Equal("running", root.GetProperty("status").GetString());
        Assert.True(root.GetProperty("cancelRequested").GetBoolean());

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RadioPadDbContext>();
            var row = await db.AiJobs.AsNoTracking().FirstAsync(j => j.Id == jobId);
            Assert.True(row.CancelRequested);
            Assert.Equal("running", row.Status); // requested, not yet stopped
        }

        var audits = await AuditFor(reportId, AuditAction.AiJobCancelled);
        Assert.Contains(audits, a => a.DetailsJson.Contains(jobId.ToString()));
    }

    [Fact]
    public async Task Cancel_TerminalJob_IsIdempotent200_NoAudit()
    {
        using var client = _factory.CreateTenantClient();
        var reportId = await CreateReportAsync(client);
        var jobId = await SeedJobAsync(_factory.SeedTenant.Id, _factory.SeedUser.Id, reportId, j =>
        {
            j.Status = "error";
            j.ErrorKind = "timeout";
            j.Error = "timed out";
            j.CompletedAt = DateTimeOffset.UtcNow;
        });

        var resp = await client.PostAsJsonAsync($"/api/jobs/{jobId}/cancel", new { });
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var root = await RootAsync(resp);
        Assert.Equal("error", root.GetProperty("status").GetString());

        // No cancel audit row for a job that was already terminal.
        var audits = await AuditFor(reportId, AuditAction.AiJobCancelled);
        Assert.Empty(audits);
    }

    [Fact]
    public async Task Cancel_UnknownJob_Returns404()
    {
        using var client = _factory.CreateTenantClient();
        var resp = await client.PostAsJsonAsync($"/api/jobs/{Guid.NewGuid()}/cancel", new { });
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
        var root = await RootAsync(resp);
        Assert.Equal("job_not_found", root.GetProperty("kind").GetString());
    }

    [Fact]
    public async Task Cancel_CrossTenantJob_Returns404()
    {
        using var client = _factory.CreateTenantClient();
        var reportId = await CreateReportAsync(client);
        var jobId = await SeedJobAsync(Guid.NewGuid(), _factory.SeedUser.Id, reportId, j => j.Status = "running");

        var resp = await client.PostAsJsonAsync($"/api/jobs/{jobId}/cancel", new { });
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    // ── retry ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Retry_FromError_CreatesNewLinkedRow_IncrementsAttempt_AndAudits()
    {
        using var client = _factory.CreateTenantClient();
        var reportId = await CreateReportAsync(client);
        var oldId = await SeedJobAsync(_factory.SeedTenant.Id, _factory.SeedUser.Id, reportId, j =>
        {
            j.Kind = "ai";
            j.Mode = "impression";
            j.ProviderId = _factory.MockProvider.Id;
            j.Status = "error";
            j.ErrorKind = "provider_transport";
            j.Error = "boom";
            j.Attempt = 1;
            j.CompletedAt = DateTimeOffset.UtcNow;
        });

        var resp = await client.PostAsJsonAsync($"/api/jobs/{oldId}/retry", new { });
        Assert.Equal(HttpStatusCode.Accepted, resp.StatusCode);
        var root = await RootAsync(resp);
        var newId = root.GetProperty("jobId").GetGuid();
        Assert.NotEqual(Guid.Empty, newId);
        Assert.NotEqual(oldId, newId);

        // The new row links back to the old and bumps the attempt counter; these are
        // immutable at creation so the background runner cannot race the assertion.
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RadioPadDbContext>();
            var newRow = await db.AiJobs.AsNoTracking().FirstAsync(j => j.Id == newId);
            Assert.Equal(oldId, newRow.RetryOfJobId);
            Assert.Equal(2, newRow.Attempt);
            Assert.Equal("ai", newRow.Kind);
            Assert.Equal("impression", newRow.Mode);
        }

        var audits = await AuditFor(reportId, AuditAction.AiJobRetried);
        Assert.Contains(audits, a => a.DetailsJson.Contains(oldId.ToString()));
    }

    [Theory]
    [InlineData("ok")]
    [InlineData("queued")]
    [InlineData("running")]
    public async Task Retry_NonTerminalOrOk_Returns409(string status)
    {
        using var client = _factory.CreateTenantClient();
        var reportId = await CreateReportAsync(client);
        var jobId = await SeedJobAsync(_factory.SeedTenant.Id, _factory.SeedUser.Id, reportId, j =>
        {
            j.Kind = "generate";
            j.Mode = "generate";
            j.Status = status;
            if (status == "ok") j.CompletedAt = DateTimeOffset.UtcNow;
        });

        var resp = await client.PostAsJsonAsync($"/api/jobs/{jobId}/retry", new { });
        Assert.Equal(HttpStatusCode.Conflict, resp.StatusCode);
        var root = await RootAsync(resp);
        Assert.Equal("job_not_retryable", root.GetProperty("kind").GetString());
    }

    [Fact]
    public async Task Retry_UnknownJob_Returns404()
    {
        using var client = _factory.CreateTenantClient();
        var resp = await client.PostAsJsonAsync($"/api/jobs/{Guid.NewGuid()}/retry", new { });
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
        var root = await RootAsync(resp);
        Assert.Equal("job_not_found", root.GetProperty("kind").GetString());
    }
}
