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
/// Iter-35 — versioned clinical validation packs (rulebook golden suites).
/// Covers: import → list → run (2/2 pass); approve transitions Draft →
/// Approved; deprecate then re-approve emits a 409 with
/// <c>kind:"validation_packs"</c>; cross-tenant read → 404.
/// </summary>
public class Iter35ValidationPackTests : IClassFixture<RadioPadAppFactory>
{
    private const string ChestCtV1Yaml = """
rulebook_id: chest_ct_v1
name: Chest CT Reporting Rulebook
version: 1.0.0
owner: Iter35 Tests
status: approved
applies_to:
  modalities: [CT]
  body_parts: [Chest]
  report_types: [diagnostic]
style:
  tone: concise_clinical
  impression_max_bullets: 5
  avoid_terms: [unremarkable]
required_sections: [Indication, Technique, Comparison, Findings, Impression]
rules:
  - id: laterality_consistency
    severity: blocker
    description: laterality
prompt_blocks:
  system: x
""";

    private readonly RadioPadAppFactory _factory;
    public Iter35ValidationPackTests(RadioPadAppFactory f) => _factory = f;

    [Fact]
    public async Task Import_List_And_Run_TwoPasses()
    {
        await EnsureChestCtRulebookAsync();
        var admin = AdminClient(UserRole.MedicalDirector, "iter35-md@radiopad.local");

        var version = $"0.1.{DateTime.UtcNow.Ticks % 1000}";
        var create = await admin.PostAsJsonAsync("/api/validation-packs", new
        {
            rulebookId = "chest_ct_v1",
            version,
            name = "Iter35 Smoke",
            goldenCases = new[]
            {
                CleanCase("ACC-IT35-1"),
                CleanCase("ACC-IT35-2"),
            },
        });
        Assert.True(create.IsSuccessStatusCode, await create.Content.ReadAsStringAsync());
        using var createdDoc = await JsonDocument.ParseAsync(await create.Content.ReadAsStreamAsync());
        var packId = createdDoc.RootElement.GetProperty("id").GetGuid();

        // List shows the version.
        var listResp = await admin.GetAsync("/api/validation-packs?rulebookId=chest_ct_v1");
        Assert.Equal(HttpStatusCode.OK, listResp.StatusCode);
        using var listDoc = await JsonDocument.ParseAsync(await listResp.Content.ReadAsStreamAsync());
        var versions = listDoc.RootElement.EnumerateArray()
            .Select(e => e.GetProperty("version").GetString()).ToList();
        Assert.Contains(version, versions);

        // Run as Radiologist (allowed).
        using var rad = _factory.CreateTenantClient();
        var run = await rad.PostAsync($"/api/validation-packs/{packId}/run", null);
        Assert.Equal(HttpStatusCode.OK, run.StatusCode);
        using var runDoc = await JsonDocument.ParseAsync(await run.Content.ReadAsStreamAsync());
        Assert.Equal(2, runDoc.RootElement.GetProperty("totalCases").GetInt32());
        Assert.Equal(2, runDoc.RootElement.GetProperty("passed").GetInt32());
        Assert.Equal(0, runDoc.RootElement.GetProperty("failed").GetInt32());

        // Audit row written.
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RadioPadDbContext>();
        var actions = await db.AuditEvents.Where(a => a.TenantId == _factory.SeedTenant.Id)
            .Select(a => a.Action).ToListAsync();
        Assert.Contains(AuditAction.ValidationPackRun, actions);
    }

    [Fact]
    public async Task Approve_Transitions_Draft_To_Approved()
    {
        await EnsureChestCtRulebookAsync();
        var admin = AdminClient(UserRole.MedicalDirector, "iter35-md@radiopad.local");
        var packId = await CreatePackAsync(admin, $"0.2.{DateTime.UtcNow.Ticks % 1000}");

        var approve = await admin.PostAsync($"/api/validation-packs/{packId}/approve", null);
        Assert.Equal(HttpStatusCode.OK, approve.StatusCode);
        using var doc = await JsonDocument.ParseAsync(await approve.Content.ReadAsStreamAsync());
        Assert.Equal("Approved", doc.RootElement.GetProperty("status").GetString());

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RadioPadDbContext>();
        var actions = await db.AuditEvents.Where(a => a.TenantId == _factory.SeedTenant.Id)
            .Select(a => a.Action).ToListAsync();
        Assert.Contains(AuditAction.ValidationPackApproved, actions);
    }

    [Fact]
    public async Task Deprecate_Then_ReApprove_Returns_Conflict()
    {
        await EnsureChestCtRulebookAsync();
        var admin = AdminClient(UserRole.MedicalDirector, "iter35-md@radiopad.local");
        var packId = await CreatePackAsync(admin, $"0.3.{DateTime.UtcNow.Ticks % 1000}");

        var approve1 = await admin.PostAsync($"/api/validation-packs/{packId}/approve", null);
        Assert.Equal(HttpStatusCode.OK, approve1.StatusCode);

        var deprecate = await admin.PostAsync($"/api/validation-packs/{packId}/deprecate", null);
        Assert.Equal(HttpStatusCode.OK, deprecate.StatusCode);

        var approve2 = await admin.PostAsync($"/api/validation-packs/{packId}/approve", null);
        Assert.Equal(HttpStatusCode.Conflict, approve2.StatusCode);
        using var body = await JsonDocument.ParseAsync(await approve2.Content.ReadAsStreamAsync());
        Assert.Equal("validation_packs", body.RootElement.GetProperty("kind").GetString());
    }

    [Fact]
    public async Task CrossTenant_Read_Returns_NotFound()
    {
        await EnsureChestCtRulebookAsync();

        // Create a foreign tenant + admin and a pack under it.
        Guid foreignPackId;
        Tenant foreignTenant;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RadioPadDbContext>();
            foreignTenant = await db.Tenants.FirstOrDefaultAsync(t => t.Slug == "iter35-pack-foreign")
                ?? new Tenant { Slug = "iter35-pack-foreign", DisplayName = "Iter35 Foreign" };
            if (foreignTenant.Id == Guid.Empty || !await db.Tenants.AnyAsync(t => t.Id == foreignTenant.Id))
            {
                db.Tenants.Add(foreignTenant);
                await db.SaveChangesAsync();
            }

            var foreignAdmin = await db.Users.FirstOrDefaultAsync(u =>
                u.TenantId == foreignTenant.Id && u.Email == "iter35-foreign-md@radiopad.local");
            if (foreignAdmin is null)
            {
                foreignAdmin = new User
                {
                    TenantId = foreignTenant.Id,
                    Email = "iter35-foreign-md@radiopad.local",
                    DisplayName = "Foreign MD",
                    Role = UserRole.MedicalDirector,
                };
                db.Users.Add(foreignAdmin);
                await db.SaveChangesAsync();
            }

            var pack = new ValidationPack
            {
                TenantId = foreignTenant.Id,
                RulebookId = "chest_ct_v1",
                Version = $"9.9.{DateTime.UtcNow.Ticks % 1000}",
                Name = "Foreign pack",
                CreatedBy = foreignAdmin.Id,
                GoldenCasesJson = "[]",
                Status = ValidationPackStatus.Draft,
            };
            db.ValidationPacks.Add(pack);
            await db.SaveChangesAsync();
            foreignPackId = pack.Id;
        }

        // Caller in seed tenant ("it") cannot read it.
        var admin = AdminClient(UserRole.MedicalDirector, "iter35-md@radiopad.local");
        var resp = await admin.GetAsync($"/api/validation-packs/{foreignPackId}/export");
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    // ---------------------------------------------------------------- helpers

    private static object CleanCase(string accession) => new
    {
        name = $"clean-{accession}",
        report = new
        {
            study = new { modality = "CT", bodyPart = "Chest", indication = "Cough", accessionNumber = accession },
            indication = "Persistent cough.",
            technique = "Non-contrast CT of the chest.",
            comparison = "None.",
            findings = "Lungs clear. No nodules. No effusion.",
            impression = "1. No acute pulmonary findings.",
        },
        expectFlagged = Array.Empty<string>(),
    };

    private async Task<Guid> CreatePackAsync(HttpClient admin, string version)
    {
        var resp = await admin.PostAsJsonAsync("/api/validation-packs", new
        {
            rulebookId = "chest_ct_v1",
            version,
            name = "Iter35 Pack",
            goldenCases = new[] { CleanCase($"ACC-{version}") },
        });
        Assert.True(resp.IsSuccessStatusCode, await resp.Content.ReadAsStringAsync());
        using var doc = await JsonDocument.ParseAsync(await resp.Content.ReadAsStreamAsync());
        return doc.RootElement.GetProperty("id").GetGuid();
    }

    private async Task EnsureChestCtRulebookAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RadioPadDbContext>();
        var existing = await db.Rulebooks.FirstOrDefaultAsync(r =>
            r.TenantId == _factory.SeedTenant.Id && r.RulebookId == "chest_ct_v1");
        if (existing is not null) return;
        db.Rulebooks.Add(new Rulebook
        {
            TenantId = _factory.SeedTenant.Id,
            RulebookId = "chest_ct_v1",
            Name = "Chest CT Reporting Rulebook",
            Version = "1.0.0",
            Owner = "Iter35 Tests",
            Status = RulebookStatus.Approved,
            SourceYaml = ChestCtV1Yaml,
        });
        await db.SaveChangesAsync();
    }

    private HttpClient AdminClient(UserRole role, string email)
    {
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RadioPadDbContext>();
            if (!db.Users.Any(u => u.TenantId == _factory.SeedTenant.Id && u.Email == email))
            {
                db.Users.Add(new User
                {
                    TenantId = _factory.SeedTenant.Id,
                    Email = email,
                    DisplayName = $"Iter35 {role}",
                    Role = role,
                });
                db.SaveChanges();
            }
        }
        var c = _factory.CreateClient();
        c.DefaultRequestHeaders.Add("X-RadioPad-Tenant", _factory.SeedTenant.Slug);
        c.DefaultRequestHeaders.Add("X-RadioPad-User", email);
        return c;
    }
}
