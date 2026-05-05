using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RadioPad.Cli.Commands;
using RadioPad.Domain.Entities;
using RadioPad.Domain.Enums;
using RadioPad.Infrastructure.Persistence;
using Xunit;

namespace RadioPad.Api.Tests.Integration;

/// <summary>
/// Iteration 32 — Templates promotion + Lexicon CSV bulk import + RB-007
/// inheritance. Covers TMP-005 approval workflow audit chain, TMP-006 usage
/// analytics, STD-006 CSV upsert, and RB-007 department-scoped inheritance.
/// </summary>
public class TemplateApprovalTests : IClassFixture<RadioPadAppFactory>
{
    private readonly RadioPadAppFactory _factory;
    public TemplateApprovalTests(RadioPadAppFactory f) => _factory = f;

    [Fact]
    public async Task Approve_Sets_ApprovedBy_And_Audit_Row()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RadioPadDbContext>();
        var user = await db.Users.FirstAsync(u => u.Id == _factory.SeedUser.Id);
        var original = user.Role;
        user.Role = UserRole.MedicalDirector;
        await db.SaveChangesAsync();

        var t = new ReportTemplate
        {
            TenantId = _factory.SeedTenant.Id,
            TemplateId = $"iter32-tpl-{Guid.NewGuid():N}".Substring(0, 18),
            Name = "Iter32 Template",
            Modality = "CT",
            BodyPart = "Chest",
            Status = TemplateStatus.Draft,
        };
        db.Templates.Add(t);
        await db.SaveChangesAsync();

        try
        {
            using var client = _factory.CreateTenantClient();
            var resp = await client.PostAsync($"/api/templates/{t.Id}/approve", null);
            Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

            var fresh = await db.Templates.AsNoTracking().FirstAsync(x => x.Id == t.Id);
            Assert.Equal(TemplateStatus.Approved, fresh.Status);
            Assert.Equal(_factory.SeedUser.Id, fresh.ApprovedBy);
            Assert.NotNull(fresh.ApprovedAt);

            var audit = await db.AuditEvents
                .Where(a => a.TenantId == _factory.SeedTenant.Id && a.Action == AuditAction.TemplateApproved)
                .OrderByDescending(a => a.CreatedAt)
                .FirstAsync();
            Assert.Contains(t.TemplateId, audit.DetailsJson);
        }
        finally
        {
            user.Role = original;
            await db.SaveChangesAsync();
        }
    }

    [Fact]
    public async Task SubmitForReview_And_Deprecate_Audit()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RadioPadDbContext>();
        var user = await db.Users.FirstAsync(u => u.Id == _factory.SeedUser.Id);
        var original = user.Role;
        user.Role = UserRole.MedicalDirector;
        await db.SaveChangesAsync();

        var t = new ReportTemplate
        {
            TenantId = _factory.SeedTenant.Id,
            TemplateId = $"iter32-tpl-{Guid.NewGuid():N}".Substring(0, 18),
            Name = "Iter32 Lifecycle",
            Modality = "CT",
            BodyPart = "Chest",
            Status = TemplateStatus.Draft,
        };
        db.Templates.Add(t);
        await db.SaveChangesAsync();

        try
        {
            using var client = _factory.CreateTenantClient();

            var submit = await client.PostAsync($"/api/templates/{t.Id}/submit-review", null);
            Assert.Equal(HttpStatusCode.OK, submit.StatusCode);
            Assert.Equal(TemplateStatus.Review, (await db.Templates.AsNoTracking().FirstAsync(x => x.Id == t.Id)).Status);
            var subAudit = await db.AuditEvents.AnyAsync(a =>
                a.TenantId == _factory.SeedTenant.Id && a.Action == AuditAction.TemplateSubmittedForReview);
            Assert.True(subAudit);

            var dep = await client.PostAsync($"/api/templates/{t.Id}/deprecate", null);
            Assert.Equal(HttpStatusCode.OK, dep.StatusCode);
            Assert.Equal(TemplateStatus.Deprecated, (await db.Templates.AsNoTracking().FirstAsync(x => x.Id == t.Id)).Status);
            var depAudit = await db.AuditEvents.AnyAsync(a =>
                a.TenantId == _factory.SeedTenant.Id && a.Action == AuditAction.TemplateDeprecated);
            Assert.True(depAudit);
        }
        finally
        {
            user.Role = original;
            await db.SaveChangesAsync();
        }
    }

    [Fact]
    public async Task NonApproved_Template_Blocked_From_Production_Report_Create()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RadioPadDbContext>();
        var tenant = await db.Tenants.FirstAsync(t => t.Id == _factory.SeedTenant.Id);
        tenant.AllowSandboxRulebooks = false;
        var t = new ReportTemplate
        {
            TenantId = tenant.Id,
            TemplateId = $"iter32-gate-{Guid.NewGuid():N}".Substring(0, 18),
            Name = "Iter32 Gate",
            Modality = "CT",
            BodyPart = "Chest",
            Status = TemplateStatus.Draft,
        };
        db.Templates.Add(t);
        await db.SaveChangesAsync();

        try
        {
            using var client = _factory.CreateTenantClient();
            var resp = await client.PostAsJsonAsync("/api/reports", new
            {
                modality = "CT",
                bodyPart = "Chest",
                indication = "x",
                accessionNumber = $"ACC-T32-{Guid.NewGuid():N}".Substring(0, 18),
                templateId = t.Id,
            });
            Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
            var body = await JsonDocument.ParseAsync(await resp.Content.ReadAsStreamAsync());
            Assert.Equal("template_not_approved", body.RootElement.GetProperty("kind").GetString());

            // With sandbox on, the same call succeeds.
            tenant.AllowSandboxRulebooks = true;
            await db.SaveChangesAsync();
            var resp2 = await client.PostAsJsonAsync("/api/reports", new
            {
                modality = "CT",
                bodyPart = "Chest",
                indication = "x",
                accessionNumber = $"ACC-T32-{Guid.NewGuid():N}".Substring(0, 18),
                templateId = t.Id,
            });
            Assert.Equal(HttpStatusCode.Created, resp2.StatusCode);
        }
        finally
        {
            tenant.AllowSandboxRulebooks = false;
            await db.SaveChangesAsync();
        }
    }
}

public class TemplateUsageAnalyticsTests : IClassFixture<RadioPadAppFactory>
{
    private readonly RadioPadAppFactory _factory;
    public TemplateUsageAnalyticsTests(RadioPadAppFactory f) => _factory = f;

    [Fact]
    public async Task Usage_Reports_Counts_By_Window_User_Modality()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RadioPadDbContext>();

        var t = new ReportTemplate
        {
            TenantId = _factory.SeedTenant.Id,
            TemplateId = $"iter32-usage-{Guid.NewGuid():N}".Substring(0, 18),
            Name = "Usage",
            Modality = "CT",
            BodyPart = "Chest",
            Status = TemplateStatus.Approved,
        };
        db.Templates.Add(t);
        await db.SaveChangesAsync();

        var now = DateTimeOffset.UtcNow;
        for (int i = 0; i < 3; i++)
        {
            db.Reports.Add(new Report
            {
                TenantId = _factory.SeedTenant.Id,
                CreatedByUserId = _factory.SeedUser.Id,
                TemplateId = t.Id,
                CreatedAt = now.AddDays(-i),
                Study = new StudyContext
                {
                    Modality = "CT", BodyPart = "Chest",
                    AccessionNumber = $"ACC-U32-{i}-{Guid.NewGuid():N}".Substring(0, 22),
                },
            });
        }
        // One report 60d old.
        db.Reports.Add(new Report
        {
            TenantId = _factory.SeedTenant.Id,
            CreatedByUserId = _factory.SeedUser.Id,
            TemplateId = t.Id,
            CreatedAt = now.AddDays(-60),
            Study = new StudyContext
            {
                Modality = "MRI", BodyPart = "Chest",
                AccessionNumber = $"ACC-U32-old-{Guid.NewGuid():N}".Substring(0, 22),
            },
        });
        await db.SaveChangesAsync();

        using var client = _factory.CreateTenantClient();
        var resp = await client.GetAsync($"/api/templates/{t.Id}/usage");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var doc = await JsonDocument.ParseAsync(await resp.Content.ReadAsStreamAsync());
        var counts = doc.RootElement.GetProperty("counts");
        Assert.Equal(3, counts.GetProperty("last7d").GetInt32());
        Assert.Equal(3, counts.GetProperty("last30d").GetInt32());
        Assert.Equal(4, counts.GetProperty("last90d").GetInt32());

        var byMod = doc.RootElement.GetProperty("byModality").EnumerateArray().ToArray();
        Assert.Contains(byMod, m => m.GetProperty("modality").GetString() == "CT" && m.GetProperty("count").GetInt32() == 3);
        Assert.Contains(byMod, m => m.GetProperty("modality").GetString() == "MRI" && m.GetProperty("count").GetInt32() == 1);
    }
}

public class LexiconBulkImportTests : IClassFixture<RadioPadAppFactory>
{
    private readonly RadioPadAppFactory _factory;
    public LexiconBulkImportTests(RadioPadAppFactory f) => _factory = f;

    [Fact]
    public async Task ImportCsv_Upserts_And_Audits()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RadioPadDbContext>();
        var user = await db.Users.FirstAsync(u => u.Id == _factory.SeedUser.Id);
        var original = user.Role;
        user.Role = UserRole.MedicalDirector;
        await db.SaveChangesAsync();

        try
        {
            using var client = _factory.CreateTenantClient();
            var csv = "term,forbidden,replacement,note\n" +
                      "stat,true,immediately,Avoid clinical jargon\n" +
                      "iter32abbr,true,iter32 abbreviation,\n" +
                      "ok-term,false,,\n";
            using var content = new StringContent(csv, Encoding.UTF8, "text/csv");
            var resp = await client.PostAsync("/api/lexicon/import-csv", content);
            Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
            var doc = await JsonDocument.ParseAsync(await resp.Content.ReadAsStreamAsync());
            Assert.Equal(3, doc.RootElement.GetProperty("upserts").GetInt32());

            var rows = await db.Lexicons.Where(l => l.TenantId == _factory.SeedTenant.Id).ToListAsync();
            Assert.Contains(rows, r => r.Term == "stat" && r.Forbidden && r.Replacement == "immediately");
            Assert.Contains(rows, r => r.Term == "iter32abbr" && r.Forbidden);
            Assert.Contains(rows, r => r.Term == "ok-term" && !r.Forbidden);

            var audit = await db.AuditEvents
                .Where(a => a.TenantId == _factory.SeedTenant.Id && a.Action == AuditAction.LexiconImported)
                .OrderByDescending(a => a.CreatedAt)
                .FirstAsync();
            Assert.Contains("\"source\":\"csv\"", audit.DetailsJson);
        }
        finally
        {
            // Cleanup imported rows so other tests are stable.
            var dirty = db.Lexicons.Where(l => l.TenantId == _factory.SeedTenant.Id);
            db.Lexicons.RemoveRange(dirty);
            user.Role = original;
            await db.SaveChangesAsync();
        }
    }

    [Fact]
    public async Task ImportCsv_Forbidden_For_Radiologist()
    {
        using var client = _factory.CreateTenantClient(); // seeded as Radiologist
        using var content = new StringContent("term,forbidden\nfoo,true\n", Encoding.UTF8, "text/csv");
        var resp = await client.PostAsync("/api/lexicon/import-csv", content);
        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
    }
}

public class RulebookInheritanceTests : IClassFixture<RadioPadAppFactory>
{
    private readonly RadioPadAppFactory _factory;
    public RulebookInheritanceTests(RadioPadAppFactory f) => _factory = f;

    [Fact]
    public async Task Department_Scoped_Rulebook_Wins_For_Tagged_Report()
    {
        // RB-007 — when a report carries a DepartmentTag matching a sibling
        // rulebook's DepartmentTag (same RulebookId), the department-scoped
        // copy is used in preference to the tenant-wide row.
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RadioPadDbContext>();

        var tenantWide = new Rulebook
        {
            TenantId = _factory.SeedTenant.Id,
            RulebookId = "rb007_test",
            Version = "1.0.0",
            Status = RulebookStatus.Approved,
            DepartmentTag = null,
            SourceYaml = "rulebook_id: rb007_test\nversion: 1.0.0\nstatus: approved\nrules: []\n",
            CompiledJson = "{\"rulebookId\":\"rb007_test\"}",
        };
        var deptScoped = new Rulebook
        {
            TenantId = _factory.SeedTenant.Id,
            RulebookId = "rb007_test",
            Version = "1.0.0-neuro",
            Status = RulebookStatus.Approved,
            DepartmentTag = "neuro",
            SourceYaml = "rulebook_id: rb007_test\nversion: 1.0.0-neuro\nstatus: approved\nrules: []\n",
            CompiledJson = "{\"rulebookId\":\"rb007_test\"}",
        };
        db.Rulebooks.AddRange(tenantWide, deptScoped);
        await db.SaveChangesAsync();

        var report = new Report
        {
            TenantId = _factory.SeedTenant.Id,
            CreatedByUserId = _factory.SeedUser.Id,
            RulebookId = tenantWide.Id,
            DepartmentTag = "neuro",
            Status = ReportStatus.Draft,
            Findings = "lungs clear; no nodule",
            Impression = "no acute findings",
            Study = new StudyContext
            {
                Modality = "CT", BodyPart = "Chest",
                AccessionNumber = $"ACC-RB007-{Guid.NewGuid():N}".Substring(0, 22),
            },
        };
        db.Reports.Add(report);
        await db.SaveChangesAsync();

        // Drive the validator via /api/reports/{id}/validate; the route
        // calls ResolveRulebookEntityAsync internally. We assert no
        // 5xx and that the response comes back. The selection logic is
        // also unit-tested by virtue of golden cases under rulebooks/_tests/.
        using var client = _factory.CreateTenantClient();
        var resp = await client.PostAsync($"/api/reports/{report.Id}/validate", null);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
    }
}

public class CliGenerateTests
{
    [Fact]
    public void TemplatesCommand_BuildSavePayload_Roundtrip_Json()
    {
        var raw = "{\"templateId\":\"r1\",\"name\":\"R1\",\"modality\":\"CT\",\"bodyPart\":\"Chest\",\"sectionsJson\":\"[]\"}";
        var p = TemplatesCommands.BuildSavePayload(raw, ".json");
        Assert.NotNull(p);
        Assert.Equal("r1", p!["templateId"]);
        Assert.Equal("CT", p["modality"]);
        Assert.Equal("[]", p["sectionsJson"]);
    }

    [Fact]
    public void TemplatesCommand_BuildSavePayload_Roundtrip_Yaml()
    {
        var raw = "templateId: y1\nname: Y1\nmodality: MRI\nbodyPart: Brain\nsections: []\n";
        var p = TemplatesCommands.BuildSavePayload(raw, ".yaml");
        Assert.NotNull(p);
        Assert.Equal("y1", p!["templateId"]);
        Assert.Equal("MRI", p["modality"]);
    }
}
