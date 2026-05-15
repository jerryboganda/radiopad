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
/// PRD Enterprise GA #13 — Marketplace submission &amp; approval workflow.
/// Validates the full lifecycle: submit → pending_review → approve/reject,
/// install into tenant, and RBAC enforcement.
/// </summary>
public class MarketplaceSubmissionTests : IClassFixture<RadioPadAppFactory>
{
    private readonly RadioPadAppFactory _factory;
    public MarketplaceSubmissionTests(RadioPadAppFactory f) => _factory = f;

    private async Task<Guid> SeedRulebook(string rulebookId = "test-rb")
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RadioPadDbContext>();
        var rb = new Rulebook
        {
            TenantId = _factory.SeedTenant.Id,
            RulebookId = rulebookId,
            Name = "Test Rulebook",
            Version = "1.0.0",
            Owner = _factory.SeedUser.Email,
            Status = RulebookStatus.Approved,
            SourceYaml = "modality: CT\nrules:\n  - check: findings_present",
        };
        db.Rulebooks.Add(rb);
        await db.SaveChangesAsync();
        return rb.Id;
    }

    private async Task<Guid> SeedTemplate(string templateId = "test-tmpl")
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RadioPadDbContext>();
        var tmpl = new ReportTemplate
        {
            TenantId = _factory.SeedTenant.Id,
            TemplateId = templateId,
            Name = "Test Template",
            SectionsJson = "[{\"name\":\"Findings\",\"content\":\"\"}]",
            Status = TemplateStatus.Approved,
        };
        db.Templates.Add(tmpl);
        await db.SaveChangesAsync();
        return tmpl.Id;
    }

    [Fact]
    public async Task Submit_Rulebook_Creates_PendingReview_Listing()
    {
        var rbId = "submit-rb-" + Guid.NewGuid().ToString("N")[..8];
        await SeedRulebook(rbId);
        var client = _factory.CreateTenantClient();

        var res = await client.PostAsJsonAsync("/api/marketplace/submissions", new
        {
            category = "rulebook",
            sourceId = rbId,
            version = "1.0.0",
            description = "A great rulebook for CT scans.",
        });
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);

        var body = await res.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("pending_review", body.GetProperty("status").GetString());
        var listingId = body.GetProperty("id").GetString();
        Assert.False(string.IsNullOrEmpty(listingId));
    }

    [Fact]
    public async Task Submit_Missing_Source_Returns_NotFound()
    {
        var client = _factory.CreateTenantClient();
        var res = await client.PostAsJsonAsync("/api/marketplace/submissions", new
        {
            category = "rulebook",
            sourceId = "nonexistent-rb",
            version = "1.0.0",
        });
        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
    }

    [Fact]
    public async Task Approve_Submission_Sets_Approved_Status()
    {
        var rbId = "approve-rb-" + Guid.NewGuid().ToString("N")[..8];
        await SeedRulebook(rbId);
        var client = _factory.CreateTenantClient();

        var submitRes = await client.PostAsJsonAsync("/api/marketplace/submissions", new
        {
            category = "rulebook",
            sourceId = rbId,
            version = "1.0.0",
        });
        var submitBody = await submitRes.Content.ReadFromJsonAsync<JsonElement>();
        var listingId = submitBody.GetProperty("id").GetString()!;

        // Approve with admin client
        var adminClient = _factory.CreateAdminClient();
        var approveRes = await adminClient.PostAsync($"/api/marketplace/submissions/{listingId}/approve", null);
        Assert.Equal(HttpStatusCode.OK, approveRes.StatusCode);

        var approveBody = await approveRes.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("approved", approveBody.GetProperty("status").GetString());

        // Verify the listing now appears in approved catalogue
        var listRes = await client.GetAsync("/api/marketplace/listings");
        var listings = await listRes.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(listings.GetArrayLength() > 0);
    }

    [Fact]
    public async Task Reject_Submission_Sets_Rejected_With_Notes()
    {
        var rbId = "reject-rb-" + Guid.NewGuid().ToString("N")[..8];
        await SeedRulebook(rbId);
        var client = _factory.CreateTenantClient();

        var submitRes = await client.PostAsJsonAsync("/api/marketplace/submissions", new
        {
            category = "rulebook",
            sourceId = rbId,
            version = "1.0.0",
        });
        var submitBody = await submitRes.Content.ReadFromJsonAsync<JsonElement>();
        var listingId = submitBody.GetProperty("id").GetString()!;

        var adminClient = _factory.CreateAdminClient();
        var rejectRes = await adminClient.PostAsJsonAsync(
            $"/api/marketplace/submissions/{listingId}/reject",
            new { reviewNotes = "Needs more detail in the rules section." });
        Assert.Equal(HttpStatusCode.OK, rejectRes.StatusCode);

        var rejectBody = await rejectRes.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("rejected", rejectBody.GetProperty("status").GetString());
        Assert.Equal("Needs more detail in the rules section.", rejectBody.GetProperty("reviewNotes").GetString());
    }

    [Fact]
    public async Task Install_Copies_Content_To_Tenant_As_Draft()
    {
        var rbId = "install-rb-" + Guid.NewGuid().ToString("N")[..8];
        await SeedRulebook(rbId);
        var client = _factory.CreateTenantClient();

        // Submit + approve
        var submitRes = await client.PostAsJsonAsync("/api/marketplace/submissions", new
        {
            category = "rulebook",
            sourceId = rbId,
            version = "2.0.0",
        });
        var submitBody = await submitRes.Content.ReadFromJsonAsync<JsonElement>();
        var listingId = submitBody.GetProperty("id").GetString()!;

        var adminClient = _factory.CreateAdminClient();
        await adminClient.PostAsync($"/api/marketplace/submissions/{listingId}/approve", null);

        // Install
        var installRes = await client.PostAsync($"/api/marketplace/listings/{listingId}/install", null);
        Assert.Equal(HttpStatusCode.OK, installRes.StatusCode);

        var installBody = await installRes.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(installBody.GetProperty("installed").GetBoolean());
        Assert.True(installBody.GetProperty("installCount").GetInt32() >= 1);

        // Verify rulebook was created in tenant
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RadioPadDbContext>();
        var installed = await db.Rulebooks.FirstOrDefaultAsync(r =>
            r.TenantId == _factory.SeedTenant.Id &&
            r.RulebookId == rbId &&
            r.Version == "2.0.0");
        Assert.NotNull(installed);
        Assert.Equal(RulebookStatus.Draft, installed!.Status);
        Assert.StartsWith("[Marketplace]", installed.Name);
    }

    [Fact]
    public async Task Install_Template_Copies_As_Draft()
    {
        var tmplId = "install-tmpl-" + Guid.NewGuid().ToString("N")[..8];
        await SeedTemplate(tmplId);
        var client = _factory.CreateTenantClient();

        var submitRes = await client.PostAsJsonAsync("/api/marketplace/submissions", new
        {
            category = "template",
            sourceId = tmplId,
            version = "1.0.0",
        });
        var submitBody = await submitRes.Content.ReadFromJsonAsync<JsonElement>();
        var listingId = submitBody.GetProperty("id").GetString()!;

        var adminClient = _factory.CreateAdminClient();
        await adminClient.PostAsync($"/api/marketplace/submissions/{listingId}/approve", null);

        var installRes = await client.PostAsync($"/api/marketplace/listings/{listingId}/install", null);
        Assert.Equal(HttpStatusCode.OK, installRes.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RadioPadDbContext>();
        var installed = await db.Templates.FirstOrDefaultAsync(t =>
            t.TenantId == _factory.SeedTenant.Id &&
            t.TemplateId == tmplId);
        Assert.NotNull(installed);
        Assert.Equal(TemplateStatus.Draft, installed!.Status);
    }

    [Fact]
    public async Task Radiologist_Cannot_Approve_Submission()
    {
        var rbId = "rbac-rb-" + Guid.NewGuid().ToString("N")[..8];
        await SeedRulebook(rbId);
        var client = _factory.CreateTenantClient();

        var submitRes = await client.PostAsJsonAsync("/api/marketplace/submissions", new
        {
            category = "rulebook",
            sourceId = rbId,
            version = "1.0.0",
        });
        var submitBody = await submitRes.Content.ReadFromJsonAsync<JsonElement>();
        var listingId = submitBody.GetProperty("id").GetString()!;

        // Try to approve with radiologist (non-admin) client
        var approveRes = await client.PostAsync($"/api/marketplace/submissions/{listingId}/approve", null);
        Assert.Equal(HttpStatusCode.Forbidden, approveRes.StatusCode);
    }

    [Fact]
    public async Task Radiologist_Cannot_Reject_Submission()
    {
        var rbId = "rbac-rej-rb-" + Guid.NewGuid().ToString("N")[..8];
        await SeedRulebook(rbId);
        var client = _factory.CreateTenantClient();

        var submitRes = await client.PostAsJsonAsync("/api/marketplace/submissions", new
        {
            category = "rulebook",
            sourceId = rbId,
            version = "1.0.0",
        });
        var submitBody = await submitRes.Content.ReadFromJsonAsync<JsonElement>();
        var listingId = submitBody.GetProperty("id").GetString()!;

        var rejectRes = await client.PostAsJsonAsync(
            $"/api/marketplace/submissions/{listingId}/reject",
            new { reviewNotes = "bad" });
        Assert.Equal(HttpStatusCode.Forbidden, rejectRes.StatusCode);
    }

    [Fact]
    public async Task ListSubmissions_Returns_Own_Submissions()
    {
        var rbId = "list-rb-" + Guid.NewGuid().ToString("N")[..8];
        await SeedRulebook(rbId);
        var client = _factory.CreateTenantClient();

        await client.PostAsJsonAsync("/api/marketplace/submissions", new
        {
            category = "rulebook",
            sourceId = rbId,
            version = "1.0.0",
            description = "list test",
        });

        var listRes = await client.GetAsync("/api/marketplace/submissions");
        Assert.Equal(HttpStatusCode.OK, listRes.StatusCode);

        var list = await listRes.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(list.GetArrayLength() >= 1);
    }

    [Fact]
    public async Task Cannot_Approve_Already_Approved_Submission()
    {
        var rbId = "double-approve-" + Guid.NewGuid().ToString("N")[..8];
        await SeedRulebook(rbId);
        var client = _factory.CreateTenantClient();

        var submitRes = await client.PostAsJsonAsync("/api/marketplace/submissions", new
        {
            category = "rulebook",
            sourceId = rbId,
            version = "1.0.0",
        });
        var submitBody = await submitRes.Content.ReadFromJsonAsync<JsonElement>();
        var listingId = submitBody.GetProperty("id").GetString()!;

        var adminClient = _factory.CreateAdminClient();
        await adminClient.PostAsync($"/api/marketplace/submissions/{listingId}/approve", null);

        // Second approval should fail
        var secondRes = await adminClient.PostAsync($"/api/marketplace/submissions/{listingId}/approve", null);
        Assert.Equal(HttpStatusCode.BadRequest, secondRes.StatusCode);
    }
}
