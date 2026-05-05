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
/// Iteration 11 closures: multi-mode AI (PRD AI-001/AI-002, RPT-006/RPT-007),
/// RBAC enforcement on rulebook + provider admin endpoints (AUTH-002),
/// tenant lexicon (STD-006), and prior-report comparison endpoint (RPT-009).
/// </summary>
public class AiModesTests : IClassFixture<RadioPadAppFactory>
{
    private readonly RadioPadAppFactory _factory;
    public AiModesTests(RadioPadAppFactory f) => _factory = f;

    [Theory]
    [InlineData("impression")]
    [InlineData("cleanup")]
    [InlineData("draft")]
    [InlineData("concise")]
    [InlineData("formal")]
    [InlineData("patient_friendly")]
    [InlineData("referring_summary")]
    public async Task Each_Supported_Mode_Returns_200(string mode)
    {
        using var client = _factory.CreateTenantClient();
        var create = await client.PostAsJsonAsync("/api/reports", new
        {
            modality = "CT",
            bodyPart = "Chest",
            indication = "Cough",
            accessionNumber = $"ACC-MODE-{mode}",
        });
        var id = (await JsonDocument.ParseAsync(await create.Content.ReadAsStreamAsync()))
            .RootElement.GetProperty("id").GetGuid();
        await client.PatchAsJsonAsync($"/api/reports/{id}", new
        {
            findings = "Lungs clear. No nodules.",
            impression = "No acute findings.",
        });

        var resp = await client.PostAsJsonAsync($"/api/reports/{id}/ai", new
        {
            mode,
            providerId = _factory.MockProvider.Id,
        });
        Assert.True(resp.IsSuccessStatusCode, await resp.Content.ReadAsStringAsync());
        var body = await JsonDocument.ParseAsync(await resp.Content.ReadAsStreamAsync());
        Assert.Equal(mode, body.RootElement.GetProperty("mode").GetString());
    }

    [Fact]
    public async Task Unknown_Mode_Returns_400()
    {
        using var client = _factory.CreateTenantClient();
        var create = await client.PostAsJsonAsync("/api/reports", new
        {
            modality = "CT",
            bodyPart = "Chest",
            indication = "x",
            accessionNumber = "ACC-MODE-BAD",
        });
        var id = (await JsonDocument.ParseAsync(await create.Content.ReadAsStreamAsync()))
            .RootElement.GetProperty("id").GetGuid();

        var resp = await client.PostAsJsonAsync($"/api/reports/{id}/ai", new
        {
            mode = "auto_sign",
            providerId = _factory.MockProvider.Id,
        });
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }
}

public class RbacTests : IClassFixture<RadioPadAppFactory>
{
    private readonly RadioPadAppFactory _factory;
    public RbacTests(RadioPadAppFactory f) => _factory = f;

    [Fact]
    public async Task Radiologist_Cannot_Approve_Rulebook()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RadioPadDbContext>();
        var rb = new Rulebook
        {
            TenantId = _factory.SeedTenant.Id,
            RulebookId = "rbac_test",
            Version = "0.1.0",
            Status = RulebookStatus.Draft,
            SourceYaml = "rulebook_id: rbac_test\nversion: 0.1.0\nstatus: draft\nrules: []\n",
            CompiledJson = "{}",
        };
        db.Rulebooks.Add(rb);
        await db.SaveChangesAsync();

        using var client = _factory.CreateTenantClient(); // seeded user is Radiologist
        var resp = await client.PostAsync($"/api/rulebooks/{rb.Id}/approve", null);
        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);

        // Promote user, retry
        var user = await db.Users.FirstAsync(u => u.Id == _factory.SeedUser.Id);
        user.Role = UserRole.MedicalDirector;
        await db.SaveChangesAsync();
        var ok = await client.PostAsync($"/api/rulebooks/{rb.Id}/approve", null);
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);

        // Restore for downstream tests
        user.Role = UserRole.Radiologist;
        await db.SaveChangesAsync();
    }
}

public class LexiconTests : IClassFixture<RadioPadAppFactory>
{
    private readonly RadioPadAppFactory _factory;
    public LexiconTests(RadioPadAppFactory f) => _factory = f;

    [Fact]
    public async Task Forbidden_Term_Surfaces_As_Warning()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RadioPadDbContext>();

        // Promote so we can save lexicon rows; revert at the end.
        var user = await db.Users.FirstAsync(u => u.Id == _factory.SeedUser.Id);
        var originalRole = user.Role;
        user.Role = UserRole.MedicalDirector;
        await db.SaveChangesAsync();

        try
        {
            using var client = _factory.CreateTenantClient();

            var save = await client.PostAsJsonAsync("/api/lexicon", new
            {
                term = "rule out",
                forbidden = true,
                replacement = "concerning for",
                note = "House style.",
            });
            Assert.True(save.IsSuccessStatusCode, await save.Content.ReadAsStringAsync());

            // Create a report containing the forbidden term in Findings.
            var create = await client.PostAsJsonAsync("/api/reports", new
            {
                modality = "CT",
                bodyPart = "Chest",
                indication = "x",
                accessionNumber = "ACC-LEX-1",
            });
            var id = (await JsonDocument.ParseAsync(await create.Content.ReadAsStreamAsync()))
                .RootElement.GetProperty("id").GetGuid();
            await client.PatchAsJsonAsync($"/api/reports/{id}", new
            {
                indication = "x",
                technique = "x",
                comparison = "n",
                findings = "Subtle opacity, rule out infection.",
                impression = "Indeterminate.",
            });

            var validate = await client.PostAsync($"/api/reports/{id}/validate", null);
            var body = await validate.Content.ReadAsStringAsync();
            Assert.Contains("lexicon:rule out", body);
        }
        finally
        {
            user.Role = originalRole;
            // Drop lexicon rows to avoid bleed into other tests.
            db.Lexicons.RemoveRange(db.Lexicons.Where(l => l.TenantId == _factory.SeedTenant.Id));
            await db.SaveChangesAsync();
        }
    }
}

public class PriorReportTests : IClassFixture<RadioPadAppFactory>
{
    private readonly RadioPadAppFactory _factory;
    public PriorReportTests(RadioPadAppFactory f) => _factory = f;

    [Fact]
    public async Task Prior_Endpoint_Finds_Most_Recent_Acknowledged_Report()
    {
        using var client = _factory.CreateTenantClient();

        // First report — acknowledged
        var first = await client.PostAsJsonAsync("/api/reports", new
        {
            modality = "MRI",
            bodyPart = "Brain",
            indication = "headache",
            accessionNumber = "ACC-PRIOR-1",
        });
        var firstId = (await JsonDocument.ParseAsync(await first.Content.ReadAsStreamAsync()))
            .RootElement.GetProperty("id").GetGuid();
        await client.PatchAsJsonAsync($"/api/reports/{firstId}", new
        {
            indication = "headache",
            technique = "MRI brain.",
            comparison = "None.",
            findings = "Normal.",
            impression = "Normal study.",
        });
        await client.PostAsync($"/api/reports/{firstId}/validate", null);
        await client.PostAsync($"/api/reports/{firstId}/acknowledge", null);

        // Second — current
        var second = await client.PostAsJsonAsync("/api/reports", new
        {
            modality = "MRI",
            bodyPart = "Brain",
            indication = "follow-up",
            accessionNumber = "ACC-PRIOR-2",
        });
        var secondId = (await JsonDocument.ParseAsync(await second.Content.ReadAsStreamAsync()))
            .RootElement.GetProperty("id").GetGuid();

        var resp = await client.GetAsync($"/api/reports/{secondId}/prior");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var doc = await JsonDocument.ParseAsync(await resp.Content.ReadAsStreamAsync());
        Assert.True(doc.RootElement.TryGetProperty("prior", out var prior));
        Assert.Equal(JsonValueKind.Object, prior.ValueKind);
        Assert.Equal(firstId, prior.GetProperty("id").GetGuid());
    }
}
