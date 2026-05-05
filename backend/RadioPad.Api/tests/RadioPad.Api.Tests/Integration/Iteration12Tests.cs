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
/// Iteration 12 closures: audit-chain verification (PRD §13.2 / AUTH-006),
/// RB-010 production rulebook gate, RB-008 rollback.
/// </summary>
public class AuditVerifyTests : IClassFixture<RadioPadAppFactory>
{
    private readonly RadioPadAppFactory _factory;
    public AuditVerifyTests(RadioPadAppFactory f) => _factory = f;

    [Fact]
    public async Task Verify_Returns_Intact_For_Pristine_Chain()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RadioPadDbContext>();
        var user = await db.Users.FirstAsync(u => u.Id == _factory.SeedUser.Id);
        var original = user.Role;
        user.Role = UserRole.ComplianceReviewer;
        await db.SaveChangesAsync();
        try
        {
            using var client = _factory.CreateTenantClient();
            var resp = await client.GetAsync("/api/audit/verify");
            Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
            var doc = await JsonDocument.ParseAsync(await resp.Content.ReadAsStreamAsync());
            Assert.True(doc.RootElement.GetProperty("intact").GetBoolean());
        }
        finally
        {
            user.Role = original;
            await db.SaveChangesAsync();
        }
    }

    [Fact]
    public async Task Verify_Detects_Tampered_Row()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RadioPadDbContext>();
        var user = await db.Users.FirstAsync(u => u.Id == _factory.SeedUser.Id);
        var original = user.Role;
        user.Role = UserRole.ComplianceReviewer;

        // Inject a row with a bad chain.
        var bad = new AuditEvent
        {
            TenantId = _factory.SeedTenant.Id,
            UserId = _factory.SeedUser.Id,
            Action = AuditAction.PolicyViolation,
            DetailsJson = "{\"why\":\"tamper-test\"}",
            IntegrityChain = "deadbeef",
        };
        db.AuditEvents.Add(bad);
        await db.SaveChangesAsync();

        try
        {
            using var client = _factory.CreateTenantClient();
            var resp = await client.GetAsync("/api/audit/verify");
            Assert.Equal(HttpStatusCode.UnprocessableEntity, resp.StatusCode);
            var doc = await JsonDocument.ParseAsync(await resp.Content.ReadAsStreamAsync());
            Assert.Equal("audit_chain_broken", doc.RootElement.GetProperty("kind").GetString());
        }
        finally
        {
            db.AuditEvents.Remove(bad);
            user.Role = original;
            await db.SaveChangesAsync();
        }
    }

    [Fact]
    public async Task Verify_Forbidden_For_Radiologist()
    {
        using var client = _factory.CreateTenantClient(); // seeded as Radiologist
        var resp = await client.GetAsync("/api/audit/verify");
        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
    }
}

public class Rb010Tests : IClassFixture<RadioPadAppFactory>
{
    private readonly RadioPadAppFactory _factory;
    public Rb010Tests(RadioPadAppFactory f) => _factory = f;

    [Fact]
    public async Task Draft_Rulebook_Blocked_From_Ai_Run_Without_Sandbox()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RadioPadDbContext>();
        var tenant = await db.Tenants.FirstAsync(t => t.Id == _factory.SeedTenant.Id);
        tenant.AllowSandboxRulebooks = false;

        var rb = new Rulebook
        {
            TenantId = tenant.Id,
            RulebookId = "rb010_test",
            Version = "0.1.0",
            Status = RulebookStatus.Draft,
            SourceYaml = "rulebook_id: rb010_test\nversion: 0.1.0\nstatus: draft\nrules: []\n",
            CompiledJson = "{}",
        };
        db.Rulebooks.Add(rb);
        await db.SaveChangesAsync();

        using var client = _factory.CreateTenantClient();
        var create = await client.PostAsJsonAsync("/api/reports", new
        {
            modality = "CT",
            bodyPart = "Chest",
            indication = "x",
            accessionNumber = "ACC-RB010-1",
            rulebookId = rb.Id,
        });
        var id = (await JsonDocument.ParseAsync(await create.Content.ReadAsStreamAsync()))
            .RootElement.GetProperty("id").GetGuid();

        var ai = await client.PostAsJsonAsync($"/api/reports/{id}/ai", new
        {
            mode = "impression",
            providerId = _factory.MockProvider.Id,
        });
        Assert.Equal(HttpStatusCode.Conflict, ai.StatusCode);
        var body = await JsonDocument.ParseAsync(await ai.Content.ReadAsStreamAsync());
        Assert.Equal("rulebook_governance", body.RootElement.GetProperty("kind").GetString());

        // Now flip sandbox on; AI should pass.
        tenant.AllowSandboxRulebooks = true;
        await db.SaveChangesAsync();
        var ai2 = await client.PostAsJsonAsync($"/api/reports/{id}/ai", new
        {
            mode = "impression",
            providerId = _factory.MockProvider.Id,
        });
        Assert.Equal(HttpStatusCode.OK, ai2.StatusCode);

        // Restore tenant default.
        tenant.AllowSandboxRulebooks = false;
        await db.SaveChangesAsync();
    }
}

public class RollbackTests : IClassFixture<RadioPadAppFactory>
{
    private readonly RadioPadAppFactory _factory;
    public RollbackTests(RadioPadAppFactory f) => _factory = f;

    [Fact]
    public async Task Rollback_Creates_New_Approved_Copy()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RadioPadDbContext>();
        var user = await db.Users.FirstAsync(u => u.Id == _factory.SeedUser.Id);
        var original = user.Role;
        user.Role = UserRole.MedicalDirector;

        var prior = new Rulebook
        {
            TenantId = _factory.SeedTenant.Id,
            RulebookId = "rb008_test",
            Version = "1.0.0",
            Status = RulebookStatus.Approved,
            SourceYaml = "rulebook_id: rb008_test\nversion: 1.0.0\nstatus: approved\nrules: []\n",
            CompiledJson = "{\"rulebookId\":\"rb008_test\",\"version\":\"1.0.0\"}",
        };
        var current = new Rulebook
        {
            TenantId = _factory.SeedTenant.Id,
            RulebookId = "rb008_test",
            Version = "1.1.0",
            Status = RulebookStatus.Approved,
            SourceYaml = "rulebook_id: rb008_test\nversion: 1.1.0\nstatus: approved\nrules: []\n",
            CompiledJson = "{\"rulebookId\":\"rb008_test\",\"version\":\"1.1.0\"}",
        };
        db.Rulebooks.AddRange(prior, current);
        await db.SaveChangesAsync();

        try
        {
            using var client = _factory.CreateTenantClient();
            var resp = await client.PostAsJsonAsync(
                $"/api/rulebooks/{current.Id}/rollback", new { version = "1.0.0" });
            Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
            var doc = await JsonDocument.ParseAsync(await resp.Content.ReadAsStreamAsync());
            Assert.StartsWith("1.0.0+rollback-", doc.RootElement.GetProperty("version").GetString());
            Assert.Equal((int)RulebookStatus.Approved, doc.RootElement.GetProperty("status").GetInt32());
        }
        finally
        {
            user.Role = original;
            await db.SaveChangesAsync();
        }
    }

    [Fact]
    public async Task Rollback_To_Unknown_Version_Returns_400()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RadioPadDbContext>();
        var user = await db.Users.FirstAsync(u => u.Id == _factory.SeedUser.Id);
        var original = user.Role;
        user.Role = UserRole.MedicalDirector;

        var current = new Rulebook
        {
            TenantId = _factory.SeedTenant.Id,
            RulebookId = "rb008_unknown",
            Version = "1.0.0",
            Status = RulebookStatus.Approved,
            SourceYaml = "rulebook_id: rb008_unknown\nversion: 1.0.0\nstatus: approved\nrules: []\n",
            CompiledJson = "{}",
        };
        db.Rulebooks.Add(current);
        await db.SaveChangesAsync();

        try
        {
            using var client = _factory.CreateTenantClient();
            var resp = await client.PostAsJsonAsync(
                $"/api/rulebooks/{current.Id}/rollback", new { version = "9.9.9" });
            Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        }
        finally
        {
            user.Role = original;
            await db.SaveChangesAsync();
        }
    }
}
