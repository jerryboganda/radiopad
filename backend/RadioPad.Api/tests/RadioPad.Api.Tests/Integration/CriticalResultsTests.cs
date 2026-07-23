using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using RadioPad.Api.Services;
using RadioPad.Domain.Entities;
using RadioPad.Domain.Enums;
using RadioPad.Infrastructure.Identity;
using RadioPad.Infrastructure.Persistence;
using Xunit;

namespace RadioPad.Api.Tests.Integration;

/// <summary>
/// PRD §14.15 (CR-001..010) — critical-results communication tracking.
/// Covers the closed loop (create → communicate → acknowledge → close), the
/// deadline derived from each criticality class, tenant isolation, and the
/// overdue → escalated sweep.
/// </summary>
public class CriticalResultsTests : IClassFixture<RadioPadAppFactory>
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    private readonly RadioPadAppFactory _factory;

    public CriticalResultsTests(RadioPadAppFactory factory) => _factory = factory;

    private sealed record CriticalResultDto(
        Guid Id,
        Guid ReportId,
        string Criticality,
        string Status,
        string FindingSummary,
        string? CommunicatedTo,
        string? CommunicationMethod,
        DateTimeOffset? CommunicatedAt,
        string? AcknowledgedBy,
        DateTimeOffset? AcknowledgedAt,
        DateTimeOffset DueAt,
        DateTimeOffset? EscalatedAt,
        DateTimeOffset? ClosedAt,
        string Notes,
        bool IsOverdue,
        DateTimeOffset CreatedAt,
        DateTimeOffset UpdatedAt);

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, $"{(int)response.StatusCode} {response.StatusCode}: {body}");
        return JsonSerializer.Deserialize<T>(body, Json)!;
    }

    /// <summary>Creates a draft report through the API and returns its id.</summary>
    private static async Task<Guid> CreateReportAsync(HttpClient client, string accession)
    {
        var response = await client.PostAsJsonAsync("/api/reports", new
        {
            modality = "CT",
            bodyPart = "Chest",
            accessionNumber = accession,
        });
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, $"create report failed: {(int)response.StatusCode} {body}");
        using var doc = JsonDocument.Parse(body);
        return doc.RootElement.GetProperty("id").GetGuid();
    }

    private static async Task<CriticalResultDto> CreateCriticalResultAsync(
        HttpClient client, Guid reportId, string criticality, string summary)
    {
        var response = await client.PostAsJsonAsync("/api/critical-results", new
        {
            reportId,
            criticality,
            findingSummary = summary,
        });
        return await ReadAsync<CriticalResultDto>(response);
    }

    [Fact]
    public async Task Create_Communicate_Acknowledge_Close_WalksTheWholeLoop_AndAudits()
    {
        var client = _factory.CreateTenantClient();
        var reportId = await CreateReportAsync(client, $"CR-LOOP-{Guid.NewGuid():N}");

        // ── create ───────────────────────────────────────────────────────────
        var created = await CreateCriticalResultAsync(client, reportId, "Red", "Large right pneumothorax");
        Assert.Equal("Open", created.Status);
        Assert.Equal("Red", created.Criticality);
        Assert.Equal(reportId, created.ReportId);
        Assert.Null(created.CommunicatedAt);
        Assert.False(created.IsOverdue); // freshly created, deadline still ahead

        // ── communicate ──────────────────────────────────────────────────────
        var communicated = await ReadAsync<CriticalResultDto>(
            await client.PostAsJsonAsync($"/api/critical-results/{created.Id}/communicate", new
            {
                communicatedTo = "Dr Osei (ED)",
                method = "Phone",
                notes = "Paged and spoke directly.",
            }));
        Assert.Equal("Communicated", communicated.Status);
        Assert.Equal("Dr Osei (ED)", communicated.CommunicatedTo);
        Assert.Equal("Phone", communicated.CommunicationMethod);
        Assert.NotNull(communicated.CommunicatedAt);
        Assert.Contains("Paged and spoke directly.", communicated.Notes);

        // ── acknowledge ──────────────────────────────────────────────────────
        var acknowledged = await ReadAsync<CriticalResultDto>(
            await client.PostAsJsonAsync($"/api/critical-results/{created.Id}/acknowledge", new
            {
                acknowledgedBy = "Dr Osei",
            }));
        Assert.Equal("Acknowledged", acknowledged.Status);
        Assert.Equal("Dr Osei", acknowledged.AcknowledgedBy);
        Assert.NotNull(acknowledged.AcknowledgedAt);

        // ── close ────────────────────────────────────────────────────────────
        var closed = await ReadAsync<CriticalResultDto>(
            await client.PostAsJsonAsync($"/api/critical-results/{created.Id}/close", new { notes = "Loop closed." }));
        Assert.Equal("Closed", closed.Status);
        Assert.NotNull(closed.ClosedAt);

        // Closing twice is a conflict, not a silent no-op.
        var again = await client.PostAsJsonAsync($"/api/critical-results/{created.Id}/close", new { });
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);

        // Every transition left an append-only audit row for this report.
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RadioPadDbContext>();
        var actions = await db.AuditEvents.AsNoTracking()
            .Where(a => a.ReportId == reportId)
            .Select(a => a.Action)
            .ToListAsync();

        Assert.Contains(AuditAction.CriticalResultCreated, actions);
        Assert.Contains(AuditAction.CriticalResultCommunicated, actions);
        Assert.Contains(AuditAction.CriticalResultAcknowledged, actions);
        Assert.Contains(AuditAction.CriticalResultClosed, actions);

        // The finding narrative must never reach the audit log.
        var details = await db.AuditEvents.AsNoTracking()
            .Where(a => a.ReportId == reportId)
            .Select(a => a.DetailsJson)
            .ToListAsync();
        Assert.DoesNotContain(details, d => d.Contains("pneumothorax", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Acknowledge_BeforeCommunication_IsRejected()
    {
        var client = _factory.CreateTenantClient();
        var reportId = await CreateReportAsync(client, $"CR-ACK-{Guid.NewGuid():N}");
        var created = await CreateCriticalResultAsync(client, reportId, "Orange", "New intracranial haemorrhage");

        var response = await client.PostAsJsonAsync(
            $"/api/critical-results/{created.Id}/acknowledge", new { acknowledgedBy = "Dr Ito" });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Theory]
    [InlineData("Red", 15)]
    [InlineData("Orange", 60)]
    [InlineData("Yellow", 1440)]
    public async Task DueAt_IsDerivedFromCriticality(string criticality, int expectedMinutes)
    {
        var client = _factory.CreateTenantClient();
        var reportId = await CreateReportAsync(client, $"CR-DUE-{criticality}-{Guid.NewGuid():N}");

        var created = await CreateCriticalResultAsync(client, reportId, criticality, $"{criticality} finding");

        var window = created.DueAt - created.CreatedAt;
        Assert.Equal(expectedMinutes, (int)Math.Round(window.TotalMinutes));
    }

    [Fact]
    public async Task CriticalResult_FromAnotherTenant_Is404()
    {
        var client = _factory.CreateTenantClient();
        var reportId = await CreateReportAsync(client, $"CR-ISO-{Guid.NewGuid():N}");
        var created = await CreateCriticalResultAsync(client, reportId, "Red", "Aortic dissection");

        // A second tenant with its own radiologist must not see it at all.
        var otherSlug = $"other-{Guid.NewGuid():N}"[..12];
        var otherEmail = $"{otherSlug}@radiopad.local";
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RadioPadDbContext>();
            var otherTenant = new Tenant { Slug = otherSlug, DisplayName = "Other Tenant" };
            db.Tenants.Add(otherTenant);
            db.Users.Add(new User
            {
                TenantId = otherTenant.Id,
                Email = otherEmail,
                DisplayName = "Other Radiologist",
                Role = UserRole.Radiologist,
            });
            await db.SaveChangesAsync();
            await EnterpriseIdentityBridge.EnsureForAllUsersAsync(db, CancellationToken.None);
        }

        var otherClient = _factory.CreateClient();
        otherClient.DefaultRequestHeaders.Add("X-RadioPad-Tenant", otherSlug);
        otherClient.DefaultRequestHeaders.Add("X-RadioPad-User", otherEmail);

        // Direct fetch is a 404 — indistinguishable from "does not exist".
        var fetched = await otherClient.GetAsync($"/api/critical-results/{created.Id}");
        Assert.Equal(HttpStatusCode.NotFound, fetched.StatusCode);

        // Mutations are 404 too, not 403 (which would confirm the row exists).
        var closeAttempt = await otherClient.PostAsJsonAsync(
            $"/api/critical-results/{created.Id}/close", new { });
        Assert.Equal(HttpStatusCode.NotFound, closeAttempt.StatusCode);

        // And it never shows up in the other tenant's list.
        var list = await ReadAsync<List<CriticalResultDto>>(await otherClient.GetAsync("/api/critical-results"));
        Assert.DoesNotContain(list, c => c.Id == created.Id);

        // The owning tenant still sees it.
        var ownList = await ReadAsync<List<CriticalResultDto>>(await client.GetAsync("/api/critical-results"));
        Assert.Contains(ownList, c => c.Id == created.Id);
    }

    [Fact]
    public async Task OverdueSweep_EscalatesOpenResultsPastDeadline_AndAudits()
    {
        var client = _factory.CreateTenantClient();
        var reportId = await CreateReportAsync(client, $"CR-OVD-{Guid.NewGuid():N}");
        var created = await CreateCriticalResultAsync(client, reportId, "Red", "Ruptured AAA");

        // Backdate the deadline so the row is genuinely overdue.
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RadioPadDbContext>();
            var row = await db.CriticalResults.FirstAsync(c => c.Id == created.Id);
            row.DueAt = DateTimeOffset.UtcNow.AddMinutes(-5);
            await db.SaveChangesAsync();
        }

        // It surfaces on the overdue endpoint before the sweep runs.
        var overdueBefore = await ReadAsync<List<CriticalResultDto>>(
            await client.GetAsync("/api/critical-results/overdue"));
        Assert.Contains(overdueBefore, c => c.Id == created.Id);

        var sweeper = new RadioPad.Api.Jobs.CriticalResultEscalationJob(
            _factory.Services.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<RadioPad.Api.Jobs.CriticalResultEscalationJob>.Instance);
        var escalatedCount = await sweeper.ScanOnceAsync(CancellationToken.None);
        Assert.True(escalatedCount >= 1);

        var after = await ReadAsync<CriticalResultDto>(
            await client.GetAsync($"/api/critical-results/{created.Id}"));
        Assert.Equal("Escalated", after.Status);
        Assert.NotNull(after.EscalatedAt);
        Assert.True(after.IsOverdue);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RadioPadDbContext>();
            var escalations = await db.AuditEvents.AsNoTracking()
                .Where(a => a.ReportId == reportId && a.Action == AuditAction.CriticalResultEscalated)
                .Select(a => a.DetailsJson)
                .ToListAsync();
            Assert.Contains(escalations, d => d.Contains("overdue_sweep", StringComparison.Ordinal));
        }
    }

    [Fact]
    public async Task OverdueSweep_LeavesCommunicatedResultsAlone()
    {
        var client = _factory.CreateTenantClient();
        var reportId = await CreateReportAsync(client, $"CR-COMM-{Guid.NewGuid():N}");
        var created = await CreateCriticalResultAsync(client, reportId, "Red", "Tension pneumothorax");

        await ReadAsync<CriticalResultDto>(
            await client.PostAsJsonAsync($"/api/critical-results/{created.Id}/communicate", new
            {
                communicatedTo = "Dr Vance",
                method = "SecureMessage",
            }));

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RadioPadDbContext>();
            var row = await db.CriticalResults.FirstAsync(c => c.Id == created.Id);
            row.DueAt = DateTimeOffset.UtcNow.AddMinutes(-30);
            await db.SaveChangesAsync();
        }

        var sweeper = new RadioPad.Api.Jobs.CriticalResultEscalationJob(
            _factory.Services.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<RadioPad.Api.Jobs.CriticalResultEscalationJob>.Instance);
        await sweeper.ScanOnceAsync(CancellationToken.None);

        // Already communicated: the loop moved, so the sweep must not touch it.
        var after = await ReadAsync<CriticalResultDto>(
            await client.GetAsync($"/api/critical-results/{created.Id}"));
        Assert.Equal("Communicated", after.Status);
        Assert.Null(after.EscalatedAt);
        Assert.False(after.IsOverdue);
    }
}
