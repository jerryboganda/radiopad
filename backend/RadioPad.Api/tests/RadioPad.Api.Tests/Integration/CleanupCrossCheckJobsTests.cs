using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using RadioPad.Api.Services;
using RadioPad.Domain.Entities;
using RadioPad.Infrastructure.Persistence;
using Xunit;

namespace RadioPad.Api.Tests.Integration;

/// <summary>
/// PR-B5 — dictation-cleanup and cross-check as durable job kinds. The dedicated
/// <c>.../dictation/cleanup/jobs</c> and <c>.../crosscheck/review/jobs</c> routes persist their
/// request payload in <c>AiJob.InputJson</c> (so a Retry can re-run them) and complete into a
/// suggestion set in <c>ResultJson</c> that mirrors the sync endpoints byte-for-byte — never
/// writing the report (the preview/accept gate stays client-side). These tests assert the
/// envelopes, RBAC parity, per-section single-flight, input-reuse on retry, the 24h input null-out,
/// and that the legacy generic <c>mode=cleanup</c> path is unchanged.
/// </summary>
public class CleanupCrossCheckJobsTests : IClassFixture<RadioPadAppFactory>
{
    private readonly RadioPadAppFactory _factory;
    public CleanupCrossCheckJobsTests(RadioPadAppFactory factory) => _factory = factory;

    // ── helpers ───────────────────────────────────────────────────────────────

    private async Task<Guid> CreateReportAsync(HttpClient client)
    {
        var create = await client.PostAsJsonAsync("/api/reports", new
        {
            modality = "CT",
            bodyPart = "Chest",
            indication = "Cough",
            comparison = "None",
            accessionNumber = $"ACC-B5-{Guid.NewGuid():N}",
        });
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        using var doc = await JsonDocument.ParseAsync(await create.Content.ReadAsStreamAsync());
        return doc.RootElement.GetProperty("id").GetGuid();
    }

    private async Task<Guid> SeedJobAsync(Guid reportId, Action<AiJob> configure)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RadioPadDbContext>();
        var job = new AiJob
        {
            TenantId = _factory.SeedTenant.Id,
            ReportId = reportId,
            UserId = _factory.SeedUser.Id,
            Kind = "ai",
            Mode = "cleanup",
            Status = "queued",
        };
        configure(job);
        db.AiJobs.Add(job);
        await db.SaveChangesAsync();
        return job.Id;
    }

    private async Task<(string findings, string impression)> ReportTextAsync(Guid reportId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RadioPadDbContext>();
        var r = await db.Reports.FindAsync(reportId);
        return (r!.Findings, r.Impression);
    }

    /// <summary>Polls the shared AI-job status endpoint until the job is terminal (or the deadline
    /// elapses) and returns a cloned copy of the terminal envelope.</summary>
    private static async Task<JsonElement> PollUntilTerminalAsync(
        HttpClient client, Guid reportId, Guid jobId, int seconds = 20)
    {
        var deadline = DateTime.UtcNow.AddSeconds(seconds);
        while (DateTime.UtcNow < deadline)
        {
            using var poll = await client.GetAsync($"/api/reports/{reportId}/ai/jobs/{jobId}");
            Assert.Equal(HttpStatusCode.OK, poll.StatusCode);
            using var doc = await JsonDocument.ParseAsync(await poll.Content.ReadAsStreamAsync());
            var status = doc.RootElement.GetProperty("status").GetString();
            if (status is "ok" or "error" or "cancelled")
                return doc.RootElement.Clone();
            await Task.Delay(15);
        }
        throw new TimeoutException($"job {jobId} did not reach a terminal state within {seconds}s");
    }

    // ── cleanup ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task CleanupJob_SubmitRunPoll_ReturnsCleanedSectionsEnvelope()
    {
        using var client = _factory.CreateTenantClient();
        var reportId = await CreateReportAsync(client);
        var before = await ReportTextAsync(reportId);

        var submit = await client.PostAsJsonAsync(
            $"/api/reports/{reportId}/dictation/cleanup/jobs",
            new { rawDictation = "lungs clear; no nodules; no effusion" });
        Assert.Equal(HttpStatusCode.Accepted, submit.StatusCode);
        using var submitDoc = await JsonDocument.ParseAsync(await submit.Content.ReadAsStreamAsync());
        var jobId = submitDoc.RootElement.GetProperty("jobId").GetGuid();

        var terminal = await PollUntilTerminalAsync(client, reportId, jobId);
        Assert.Equal("ok", terminal.GetProperty("status").GetString());
        Assert.Equal("ai", terminal.GetProperty("kind").GetString());
        Assert.Equal("cleanup", terminal.GetProperty("mode").GetString());

        var sections = terminal.GetProperty("result").GetProperty("cleanedSections");
        // camelCase, and every canonical section key present (empty string where not dictated).
        Assert.True(sections.TryGetProperty("indication", out _));
        Assert.True(sections.TryGetProperty("technique", out _));
        Assert.True(sections.TryGetProperty("findings", out _));
        Assert.True(sections.TryGetProperty("impression", out _));
        Assert.True(sections.TryGetProperty("recommendations", out _));
        Assert.True(terminal.GetProperty("result").TryGetProperty("provider", out _));

        // Gate preservation: the job is a suggestion set — the report row is untouched.
        var after = await ReportTextAsync(reportId);
        Assert.Equal(before.findings, after.findings);
        Assert.Equal(before.impression, after.impression);
    }

    [Fact]
    public async Task CleanupJob_RbacParity_ReportsEditWithoutRadiologistRole_Denied()
    {
        // A report exists in the tenant; a non-radiologist role (ItAdmin) is refused by the same
        // RequireRole gate the sync route uses — the durable route does NOT relax RBAC to ReportsEdit.
        using var radiologist = _factory.CreateTenantClient();
        var reportId = await CreateReportAsync(radiologist);

        using var admin = _factory.CreateAdminClient();
        var resp = await admin.PostAsJsonAsync(
            $"/api/reports/{reportId}/dictation/cleanup/jobs",
            new { rawDictation = "lungs clear" });

        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
        using var doc = await JsonDocument.ParseAsync(await resp.Content.ReadAsStreamAsync());
        Assert.Equal("forbidden", doc.RootElement.GetProperty("kind").GetString());
    }

    [Fact]
    public async Task CleanupJob_SingleFlight_SecondSubmitAttaches()
    {
        using var client = _factory.CreateTenantClient();
        var reportId = await CreateReportAsync(client);

        // A live cleanup already running for this report (seeded so it never terminates during
        // the test) — a fresh submit must attach to it rather than stack a second row.
        var runningId = await SeedJobAsync(reportId, j =>
        {
            j.Kind = "ai";
            j.Mode = "cleanup";
            j.Status = "running";
            j.StartedAt = DateTimeOffset.UtcNow;
            j.InputJson = "{\"rawDictation\":\"seeded\"}";
        });

        var submit = await client.PostAsJsonAsync(
            $"/api/reports/{reportId}/dictation/cleanup/jobs",
            new { rawDictation = "second attempt" });
        Assert.Equal(HttpStatusCode.Accepted, submit.StatusCode);
        using var doc = await JsonDocument.ParseAsync(await submit.Content.ReadAsStreamAsync());
        Assert.Equal(runningId, doc.RootElement.GetProperty("jobId").GetGuid());
    }

    // ── cross-check ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task CrossCheckJob_SubmitRunPoll_ReturnsCorrectionsArray()
    {
        using var client = _factory.CreateTenantClient();
        var reportId = await CreateReportAsync(client);
        var before = await ReportTextAsync(reportId);

        var submit = await client.PostAsJsonAsync(
            $"/api/reports/{reportId}/crosscheck/review/jobs",
            new { text = "no acute intracranial abnormality", sectionKey = "findings", useUbag = false });
        Assert.Equal(HttpStatusCode.Accepted, submit.StatusCode);
        using var submitDoc = await JsonDocument.ParseAsync(await submit.Content.ReadAsStreamAsync());
        var jobId = submitDoc.RootElement.GetProperty("jobId").GetGuid();

        var terminal = await PollUntilTerminalAsync(client, reportId, jobId);
        Assert.Equal("ok", terminal.GetProperty("status").GetString());
        Assert.Equal("crosscheck", terminal.GetProperty("kind").GetString());
        Assert.Equal("findings", terminal.GetProperty("mode").GetString());

        var result = terminal.GetProperty("result");
        Assert.True(result.TryGetProperty("provider", out _));
        Assert.True(result.TryGetProperty("model", out _));
        Assert.True(result.TryGetProperty("latencyMs", out _));
        var corrections = result.GetProperty("corrections");
        Assert.Equal(JsonValueKind.Array, corrections.ValueKind);
        // Each correction (if any) carries the anchoring offsets the editor re-highlights with.
        foreach (var c in corrections.EnumerateArray())
        {
            Assert.True(c.TryGetProperty("originalText", out _));
            Assert.True(c.TryGetProperty("startOffset", out _));
            Assert.True(c.TryGetProperty("endOffset", out _));
        }

        // Suggestions only — never persisted to the report.
        var after = await ReportTextAsync(reportId);
        Assert.Equal(before.findings, after.findings);
        Assert.Equal(before.impression, after.impression);
    }

    [Fact]
    public async Task CrossCheckJob_SectionKeyScopesSingleFlight()
    {
        using var client = _factory.CreateTenantClient();
        var reportId = await CreateReportAsync(client);

        // A live cross-check on the "findings" section (seeded running).
        var runningFindingsId = await SeedJobAsync(reportId, j =>
        {
            j.Kind = "crosscheck";
            j.Mode = "findings";
            j.Status = "running";
            j.StartedAt = DateTimeOffset.UtcNow;
            j.InputJson = "{\"text\":\"seeded\",\"sectionKey\":\"findings\"}";
        });

        // Same section attaches.
        var sameSection = await client.PostAsJsonAsync(
            $"/api/reports/{reportId}/crosscheck/review/jobs",
            new { text = "again", sectionKey = "findings", useUbag = false });
        Assert.Equal(HttpStatusCode.Accepted, sameSection.StatusCode);
        using var sameDoc = await JsonDocument.ParseAsync(await sameSection.Content.ReadAsStreamAsync());
        Assert.Equal(runningFindingsId, sameDoc.RootElement.GetProperty("jobId").GetGuid());

        // A DIFFERENT section is a distinct job — the two run concurrently.
        var otherSection = await client.PostAsJsonAsync(
            $"/api/reports/{reportId}/crosscheck/review/jobs",
            new { text = "impression text", sectionKey = "impression", useUbag = false });
        Assert.Equal(HttpStatusCode.Accepted, otherSection.StatusCode);
        using var otherDoc = await JsonDocument.ParseAsync(await otherSection.Content.ReadAsStreamAsync());
        Assert.NotEqual(runningFindingsId, otherDoc.RootElement.GetProperty("jobId").GetGuid());
    }

    [Fact]
    public async Task CrossCheckJob_UseUbag_NoEnabledUbagProvider_FallsBackToRouter()
    {
        // useUbag is set but the tenant has no enabled UBAG provider → the forced provider resolves
        // to null and the router decides (mock), matching the sync route. The job still completes ok.
        using var client = _factory.CreateTenantClient();
        var reportId = await CreateReportAsync(client);

        var submit = await client.PostAsJsonAsync(
            $"/api/reports/{reportId}/crosscheck/review/jobs",
            new { text = "left kidney is normal", sectionKey = "findings", useUbag = true });
        Assert.Equal(HttpStatusCode.Accepted, submit.StatusCode);
        using var submitDoc = await JsonDocument.ParseAsync(await submit.Content.ReadAsStreamAsync());
        var jobId = submitDoc.RootElement.GetProperty("jobId").GetGuid();

        var terminal = await PollUntilTerminalAsync(client, reportId, jobId);
        Assert.Equal("ok", terminal.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Array, terminal.GetProperty("result").GetProperty("corrections").ValueKind);
    }

    // ── retry / input lifecycle ────────────────────────────────────────────────

    [Fact]
    public async Task Retry_CleanupJob_ReusesPersistedInput()
    {
        using var client = _factory.CreateTenantClient();
        var reportId = await CreateReportAsync(client);

        // A previously-failed cleanup job whose raw dictation survives in InputJson.
        var failedId = await SeedJobAsync(reportId, j =>
        {
            j.Kind = "ai";
            j.Mode = "cleanup";
            j.Status = "error";
            j.ErrorKind = "provider_transport";
            j.Error = "boom";
            j.InputJson = "{\"rawDictation\":\"lungs are clear bilaterally\"}";
            j.CompletedAt = DateTimeOffset.UtcNow;
        });

        // The retry carries NO body — its only source of raw dictation is the persisted InputJson.
        var retry = await client.PostAsync($"/api/jobs/{failedId}/retry", content: null);
        Assert.Equal(HttpStatusCode.Accepted, retry.StatusCode);
        using var retryDoc = await JsonDocument.ParseAsync(await retry.Content.ReadAsStreamAsync());
        var newId = retryDoc.RootElement.GetProperty("jobId").GetGuid();
        Assert.NotEqual(failedId, newId);

        var terminal = await PollUntilTerminalAsync(client, reportId, newId);
        Assert.Equal("ok", terminal.GetProperty("status").GetString());
        Assert.True(terminal.GetProperty("result").GetProperty("cleanedSections").TryGetProperty("findings", out _));
    }

    [Fact]
    public async Task Retry_InputExpired_409JobInputExpired()
    {
        using var client = _factory.CreateTenantClient();
        var reportId = await CreateReportAsync(client);

        // A failed cross-check whose input has been retention-swept (InputJson == null). Cross-check
        // always requires input, so it can no longer be reconstructed → 409.
        var failedId = await SeedJobAsync(reportId, j =>
        {
            j.Kind = "crosscheck";
            j.Mode = "findings";
            j.Status = "error";
            j.ErrorKind = "provider_transport";
            j.InputJson = null;
            j.CompletedAt = DateTimeOffset.UtcNow.AddHours(-25);
        });

        var retry = await client.PostAsync($"/api/jobs/{failedId}/retry", content: null);
        Assert.Equal(HttpStatusCode.Conflict, retry.StatusCode);
        using var doc = await JsonDocument.ParseAsync(await retry.Content.ReadAsStreamAsync());
        Assert.Equal("job_input_expired", doc.RootElement.GetProperty("kind").GetString());
    }

    [Fact]
    public async Task RetentionSweep_NullsInputJsonAt24h()
    {
        using var client = _factory.CreateTenantClient();
        var reportId = await CreateReportAsync(client);

        // A terminal cleanup job completed >24h ago, still carrying its raw dictation.
        var jobId = await SeedJobAsync(reportId, j =>
        {
            j.Kind = "ai";
            j.Mode = "cleanup";
            j.Status = "ok";
            j.ResultJson = "{\"cleanedSections\":{\"findings\":\"clear\"}}";
            j.InputJson = "{\"rawDictation\":\"raw phi text at rest\"}";
            j.CompletedAt = DateTimeOffset.UtcNow.AddHours(-25);
        });

        using (var scope = _factory.Services.CreateScope())
        {
            var worker = ActivatorUtilities.CreateInstance<RadioPad.Api.Jobs.RetentionSweepJob>(scope.ServiceProvider);
            await (Task)worker.GetType()
                .GetMethod("SweepAsync", BindingFlags.NonPublic | BindingFlags.Instance)!
                .Invoke(worker, new object[] { CancellationToken.None })!;
        }

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RadioPadDbContext>();
            var row = await db.AiJobs.FindAsync(jobId);
            Assert.NotNull(row);
            Assert.Null(row!.InputJson);
            Assert.Null(row.ResultJson);
        }
    }

    [Fact]
    public async Task LegacyModeCleanup_ViaAiJobs_StillRunsGenericPath()
    {
        // The pre-existing generic /ai/jobs route with mode=cleanup carries no InputJson, so the
        // coordinator's presence guard keeps it on the legacy single-text path (payload.text), NOT
        // the new section-cleanup envelope. Wire-additive regression guard.
        using var client = _factory.CreateTenantClient();
        var reportId = await CreateReportAsync(client);

        var submit = await client.PostAsJsonAsync(
            $"/api/reports/{reportId}/ai/jobs",
            new { mode = "cleanup", providerId = _factory.MockProvider.Id });
        Assert.Equal(HttpStatusCode.Accepted, submit.StatusCode);
        using var submitDoc = await JsonDocument.ParseAsync(await submit.Content.ReadAsStreamAsync());
        var jobId = submitDoc.RootElement.GetProperty("jobId").GetGuid();

        var terminal = await PollUntilTerminalAsync(client, reportId, jobId);
        Assert.Equal("ok", terminal.GetProperty("status").GetString());
        var result = terminal.GetProperty("result");
        Assert.True(result.TryGetProperty("text", out _));
        Assert.False(result.TryGetProperty("cleanedSections", out _));
    }

    [Fact]
    public async Task CleanupJob_RunningAtRestart_SweptServerRestart_NoReattach()
    {
        using var client = _factory.CreateTenantClient();
        var reportId = await CreateReportAsync(client);

        // A cleanup job caught mid-run by a restart: running, no ProviderJobId (only UBAG populates
        // that). Boot recovery is kind-agnostic — it sweeps it to server_restart, never re-attaches.
        var jobId = await SeedJobAsync(reportId, j =>
        {
            j.Kind = "ai";
            j.Mode = "cleanup";
            j.Status = "running";
            j.ProviderJobId = null;
            j.StartedAt = DateTimeOffset.UtcNow.AddMinutes(-1);
            j.InputJson = "{\"rawDictation\":\"interrupted\"}";
        });

        var recovery = new AiJobRecoveryHostedService(
            _factory.Services.GetRequiredService<IServiceScopeFactory>(),
            _factory.Services.GetRequiredService<ILogger<AiJobRecoveryHostedService>>());
        await recovery.StartAsync(CancellationToken.None);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RadioPadDbContext>();
        var row = await db.AiJobs.FindAsync(jobId);
        Assert.NotNull(row);
        Assert.Equal("error", row!.Status);
        Assert.Equal("server_restart", row.ErrorKind);
    }
}
