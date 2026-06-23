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
/// Iter-32 Agent E (AI completeness) — covers AI-008 approved follow-ups,
/// AI-009 prompt-override approval gate, and AI-010 routing-preview composite
/// scoring. AI-001 dictation cleanup and AI-011 local-provider transports
/// have their own dedicated test files.
/// </summary>
public class Iter32AiCompletenessTests : IClassFixture<RadioPadAppFactory>
{
    private readonly RadioPadAppFactory _factory;
    public Iter32AiCompletenessTests(RadioPadAppFactory f) => _factory = f;

    // ===== AI-008 approved follow-ups =====

    [Fact]
    public async Task Validate_Flags_Unauthorized_Followup_When_Allowlist_Present()
    {
        var rbId = await EnsureFollowupRulebookAsync();
        Guid reportId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RadioPadDbContext>();
            var report = new Report
            {
                TenantId = _factory.SeedTenant.Id,
                CreatedByUserId = _factory.SeedUser.Id,
                RulebookId = rbId,
                Status = ReportStatus.Draft,
                Indication = "ind",
                Findings = "lungs clear",
                Impression = "no acute findings",
                Recommendations = "Recommend brain biopsy.", // not on allow-list
                Study = new StudyContext
                {
                    Modality = "CT", BodyPart = "Chest",
                    AccessionNumber = $"ACC-IT32-FU-{Guid.NewGuid():N}".Substring(0, 18),
                },
            };
            db.Reports.Add(report);
            await db.SaveChangesAsync();
            reportId = report.Id;
        }

        using var client = _factory.CreateTenantClient();
        var resp = await client.PostAsync($"/api/reports/{reportId}/validate", null);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        using var doc = await JsonDocument.ParseAsync(await resp.Content.ReadAsStreamAsync());
        var rules = doc.RootElement.GetProperty("findings").EnumerateArray()
            .Select(f => f.GetProperty("ruleId").GetString()).ToList();
        Assert.Contains("unauthorized_followup", rules);
    }

    [Fact]
    public async Task Validate_Allows_Followup_On_Allowlist()
    {
        var rbId = await EnsureFollowupRulebookAsync();
        Guid reportId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RadioPadDbContext>();
            var report = new Report
            {
                TenantId = _factory.SeedTenant.Id,
                CreatedByUserId = _factory.SeedUser.Id,
                RulebookId = rbId,
                Status = ReportStatus.Draft,
                Indication = "ind",
                Findings = "lungs clear",
                Impression = "no acute findings",
                Recommendations = "Recommend clinical correlation.",
                Study = new StudyContext
                {
                    Modality = "CT", BodyPart = "Chest",
                    AccessionNumber = $"ACC-IT32-FUOK-{Guid.NewGuid():N}".Substring(0, 18),
                },
            };
            db.Reports.Add(report);
            await db.SaveChangesAsync();
            reportId = report.Id;
        }

        using var client = _factory.CreateTenantClient();
        var resp = await client.PostAsync($"/api/reports/{reportId}/validate", null);
        using var doc = await JsonDocument.ParseAsync(await resp.Content.ReadAsStreamAsync());
        var rules = doc.RootElement.GetProperty("findings").EnumerateArray()
            .Select(f => f.GetProperty("ruleId").GetString()).ToList();
        Assert.DoesNotContain("unauthorized_followup", rules);
    }

    [Fact]
    public async Task FollowupSuggestions_Audits_Filtered_Suggestions()
    {
        var rbId = await EnsureFollowupRulebookAsync();
        Guid reportId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RadioPadDbContext>();
            var report = new Report
            {
                TenantId = _factory.SeedTenant.Id,
                CreatedByUserId = _factory.SeedUser.Id,
                RulebookId = rbId,
                Status = ReportStatus.Draft,
                Indication = "ind",
                Findings = "lungs clear",
                Impression = "no acute findings",
                Study = new StudyContext
                {
                    Modality = "CT", BodyPart = "Chest",
                    AccessionNumber = $"ACC-IT32-FUA-{Guid.NewGuid():N}".Substring(0, 18),
                },
            };
            db.Reports.Add(report);
            await db.SaveChangesAsync();
            reportId = report.Id;
        }

        using var client = _factory.CreateTenantClient();
        var resp = await client.GetAsync($"/api/reports/{reportId}/followup-suggestions");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        using var doc = await JsonDocument.ParseAsync(await resp.Content.ReadAsStreamAsync());
        Assert.Empty(doc.RootElement.GetProperty("suggestions").EnumerateArray());

        using var scope2 = _factory.Services.CreateScope();
        var db2 = scope2.ServiceProvider.GetRequiredService<RadioPadDbContext>();
        var audits = await db2.AuditEvents
            .Where(a => a.ReportId == reportId && a.Action == AuditAction.PolicyViolation)
            .ToListAsync();
        Assert.NotEmpty(audits);
        Assert.All(audits, a =>
        {
            Assert.Contains("approved_followups", a.DetailsJson);
            Assert.DoesNotContain("No acute intracranial", a.DetailsJson);
        });
    }

    private async Task<Guid> EnsureFollowupRulebookAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RadioPadDbContext>();
        var existing = await db.Rulebooks.FirstOrDefaultAsync(
            r => r.TenantId == _factory.SeedTenant.Id && r.RulebookId == "iter32_followup_rb");
        if (existing is not null) return existing.Id;
        var yaml = """
            rulebook_id: iter32_followup_rb
            name: Iter32 Follow-up Test
            version: 0.1.0
            owner: it
            status: approved
            applies_to:
              modalities: [CT]
              body_parts: [Chest]
            style:
              approved_followups:
                - Recommend clinical correlation.
                - Recommend follow-up imaging in 3 months.
            required_sections: []
            rules:
              - id: unauthorized_followup
                severity: warning
                description: Recommendations must be on the allow-list.
            prompt_blocks:
              system: "test"
            """;
        var rb = new Rulebook
        {
            TenantId = _factory.SeedTenant.Id,
            RulebookId = "iter32_followup_rb",
            Name = "Iter32 Follow-up Test",
            Version = "0.1.0",
            Owner = "it",
            Status = RulebookStatus.Approved,
            SourceYaml = yaml,
        };
        db.Rulebooks.Add(rb);
        await db.SaveChangesAsync();
        return rb.Id;
    }

    // ===== AI-009 prompt-override approval gate =====

    [Fact]
    public async Task PromptOverride_Save_Lands_In_Draft()
    {
        // PromptOverridesManage is ItAdmin/MedicalDirector (least-privilege 2026-06-23,
        // moved off ReportingAdmin to keep manage/approve separation-of-duties).
        await EnsureItAdminAsync();
        using var client = AdminClient(UserRole.ItAdmin);
        var save = await client.PostAsJsonAsync("/api/prompts/overrides", new
        {
            rulebookId = "iter32_followup_rb",
            blockKey = "system_drafttest",
            body = "DRAFT system prompt for tests",
        });
        Assert.Equal(HttpStatusCode.OK, save.StatusCode);
        using var doc = await JsonDocument.ParseAsync(await save.Content.ReadAsStreamAsync());
        Assert.Equal("Draft", doc.RootElement.GetProperty("status").GetString());
    }

    [Fact]
    public async Task PromptOverride_Approve_Requires_MedicalDirector()
    {
        await EnsureMedicalDirectorAsync();
        await EnsureItAdminAsync();

        // Separation-of-duties (2026-06-23): ItAdmin MANAGES prompt overrides (save),
        // but only MedicalDirector may APPROVE them.
        using var itClient = AdminClient(UserRole.ItAdmin);
        var save = await itClient.PostAsJsonAsync("/api/prompts/overrides", new
        {
            rulebookId = "iter32_followup_rb",
            blockKey = "approve_test_block",
            body = "needs approval",
        });
        Assert.Equal(HttpStatusCode.OK, save.StatusCode);
        using var sdoc = await JsonDocument.ParseAsync(await save.Content.ReadAsStreamAsync());
        var id = sdoc.RootElement.GetProperty("id").GetGuid();

        // ItAdmin can manage but NOT approve → 403.
        var deny = await itClient.PostAsync($"/api/prompts/overrides/{id}/approve", null);
        Assert.Equal(HttpStatusCode.Forbidden, deny.StatusCode);

        // ReportingAdmin (no manage, no approve) is also denied approval.
        using var raClient = AdminClient(UserRole.ReportingAdmin);
        var denyRa = await raClient.PostAsync($"/api/prompts/overrides/{id}/approve", null);
        Assert.Equal(HttpStatusCode.Forbidden, denyRa.StatusCode);

        // MedicalDirector → 200, audit row written.
        using var mdClient = AdminClient(UserRole.MedicalDirector);
        var ok = await mdClient.PostAsync($"/api/prompts/overrides/{id}/approve", null);
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RadioPadDbContext>();
        var row = await db.PromptOverrides.FirstAsync(p => p.Id == id);
        Assert.Equal(PromptOverrideStatus.Approved, row.Status);
        Assert.NotNull(row.ApprovedAt);
        var audited = await db.AuditEvents.AnyAsync(a =>
            a.TenantId == _factory.SeedTenant.Id && a.Action == AuditAction.PromptOverrideApproved);
        Assert.True(audited);
    }

    [Fact]
    public async Task PromptOverrideStore_Returns_Only_Approved_Rows()
    {
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RadioPadDbContext>();
            db.PromptOverrides.RemoveRange(db.PromptOverrides.Where(p =>
                p.TenantId == _factory.SeedTenant.Id && p.RulebookId == "iter32_store_rb"));
            db.PromptOverrides.Add(new PromptOverride
            {
                TenantId = _factory.SeedTenant.Id,
                RulebookId = "iter32_store_rb",
                BlockKey = "draft_only",
                Body = "draft body",
                Status = PromptOverrideStatus.Draft,
            });
            db.PromptOverrides.Add(new PromptOverride
            {
                TenantId = _factory.SeedTenant.Id,
                RulebookId = "iter32_store_rb",
                BlockKey = "approved_only",
                Body = "approved body",
                Status = PromptOverrideStatus.Approved,
            });
            await db.SaveChangesAsync();
        }

        using var scope2 = _factory.Services.CreateScope();
        var store = scope2.ServiceProvider
            .GetRequiredService<RadioPad.Application.Abstractions.IPromptOverrideStore>();
        var map = await store.LoadAsync(_factory.SeedTenant.Id, "iter32_store_rb", default);
        Assert.False(map.ContainsKey("draft_only"));
        Assert.True(map.ContainsKey("approved_only"));
        Assert.Equal("approved body", map["approved_only"]);
    }

    // ===== AI-010 routing-preview composite scoring =====

    [Fact]
    public async Task RoutingPreview_Selects_Composite_Winner_And_Requires_Admin()
    {
        await EnsureItAdminAsync();
        await SeedRoutingProvidersAsync();

        using var radClient = _factory.CreateTenantClient();
        var deny = await radClient.GetAsync("/api/ai/routing/preview?phi=false&input=1000&output=500");
        Assert.Equal(HttpStatusCode.Forbidden, deny.StatusCode);

        using var adminClient = AdminClient(UserRole.ItAdmin);
        var ok = await adminClient.GetAsync("/api/ai/routing/preview?phi=false&input=1000&output=500");
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        using var doc = await JsonDocument.ParseAsync(await ok.Content.ReadAsStreamAsync());

        Assert.True(doc.RootElement.TryGetProperty("candidates", out var cands));
        Assert.True(cands.GetArrayLength() >= 2);
        var weights = doc.RootElement.GetProperty("weights");
        var sum = weights.GetProperty("cost").GetDouble()
                + weights.GetProperty("quality").GetDouble()
                + weights.GetProperty("latency").GetDouble();
        Assert.InRange(sum, 0.999, 1.001);
        foreach (var c in cands.EnumerateArray())
        {
            Assert.True(c.TryGetProperty("compositeScore", out _));
            Assert.True(c.TryGetProperty("costScore", out _));
            Assert.True(c.TryGetProperty("qualityScore", out _));
            Assert.True(c.TryGetProperty("latencyScore", out _));
        }
    }

    private async Task SeedRoutingProvidersAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RadioPadDbContext>();
        var existing = await db.Providers
            .Where(p => p.TenantId == _factory.SeedTenant.Id
                     && (p.Name == "iter32-cheap" || p.Name == "iter32-quality"))
            .ToListAsync();
        db.Providers.RemoveRange(existing);
        db.Providers.Add(new ProviderConfig
        {
            TenantId = _factory.SeedTenant.Id,
            Name = "iter32-cheap",
            Adapter = "mock",
            Compliance = ProviderComplianceClass.PhiApproved,
            Enabled = true,
            CostPerInputKToken = 0.5m,
            CostPerOutputKToken = 1m,
            Quality = 0.4m,
            Priority = 100,
        });
        db.Providers.Add(new ProviderConfig
        {
            TenantId = _factory.SeedTenant.Id,
            Name = "iter32-quality",
            Adapter = "mock",
            Compliance = ProviderComplianceClass.PhiApproved,
            Enabled = true,
            CostPerInputKToken = 5m,
            CostPerOutputKToken = 10m,
            Quality = 0.95m,
            Priority = 100,
        });
        await db.SaveChangesAsync();
    }

    private async Task EnsureItAdminAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RadioPadDbContext>();
        if (!await db.Users.AnyAsync(u =>
            u.TenantId == _factory.SeedTenant.Id && u.Email == "iter32-itadmin@radiopad.local"))
        {
            db.Users.Add(new User
            {
                TenantId = _factory.SeedTenant.Id,
                Email = "iter32-itadmin@radiopad.local",
                DisplayName = "Iter32 IT Admin",
                Role = UserRole.ItAdmin,
            });
            await db.SaveChangesAsync();
        }
    }

    private async Task EnsureMedicalDirectorAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RadioPadDbContext>();
        if (!await db.Users.AnyAsync(u =>
            u.TenantId == _factory.SeedTenant.Id && u.Email == "iter32-md@radiopad.local"))
        {
            db.Users.Add(new User
            {
                TenantId = _factory.SeedTenant.Id,
                Email = "iter32-md@radiopad.local",
                DisplayName = "Iter32 MD",
                Role = UserRole.MedicalDirector,
            });
        }
        if (!await db.Users.AnyAsync(u =>
            u.TenantId == _factory.SeedTenant.Id && u.Email == "iter32-ra@radiopad.local"))
        {
            db.Users.Add(new User
            {
                TenantId = _factory.SeedTenant.Id,
                Email = "iter32-ra@radiopad.local",
                DisplayName = "Iter32 RA",
                Role = UserRole.ReportingAdmin,
            });
        }
        await db.SaveChangesAsync();
    }

    private HttpClient AdminClient(UserRole role)
    {
        var email = role switch
        {
            UserRole.MedicalDirector => "iter32-md@radiopad.local",
            UserRole.ReportingAdmin  => "iter32-ra@radiopad.local",
            UserRole.ItAdmin         => "iter32-itadmin@radiopad.local",
            _ => _factory.SeedUser.Email,
        };
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-RadioPad-Tenant", _factory.SeedTenant.Slug);
        client.DefaultRequestHeaders.Add("X-RadioPad-User", email);
        return client;
    }
}
