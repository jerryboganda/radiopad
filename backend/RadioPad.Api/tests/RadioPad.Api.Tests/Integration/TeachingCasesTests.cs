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
/// PRD §14.14 (TF-001..008) — teaching file &amp; education module, end to end.
///
/// The load-bearing test here is <see cref="CreateFromReport_Strips_Phi"/>: it
/// asserts on the PERSISTED ROW, not just the response body, because the risk
/// being guarded is an identifier reaching the database — a clean response with
/// a dirty row would be the worst possible outcome.
/// </summary>
public class TeachingCasesTests : IClassFixture<RadioPadAppFactory>
{
    private readonly RadioPadAppFactory _factory;
    public TeachingCasesTests(RadioPadAppFactory factory) => _factory = factory;

    private const string Accession = "ACC-TEACH-77301";
    private const string PatientRef = "PAT-TEACH-99814";

    private static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage res) =>
        await JsonDocument.ParseAsync(await res.Content.ReadAsStreamAsync());

    /// <summary>Seeds a signed-off-looking report whose narrative is deliberately full of PHI.</summary>
    private async Task<Guid> SeedPhiReportAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RadioPadDbContext>();
        var report = new Report
        {
            TenantId = _factory.SeedTenant.Id,
            CreatedByUserId = _factory.SeedUser.Id,
            Study = new StudyContext
            {
                AccessionNumber = Accession,
                Modality = "CT",
                BodyPart = "Abdomen",
                PatientReference = PatientRef,
            },
            Indication = $"Patient: Jane Doe, MRN: 004812345, DOB: 1974-03-02. 45-year-old with RLQ pain.",
            Findings = $"Study {Accession} for {PatientRef}. Dilated appendix measuring 12 mm with periappendiceal fat stranding.",
            Impression = "Acute appendicitis. Discussed with Dr. Alan Grant on 05/01/2024.",
        };
        db.Reports.Add(report);
        await db.SaveChangesAsync();
        return report.Id;
    }

    private async Task<Guid> CreateBlankCaseAsync(HttpClient client, object body)
    {
        var res = await client.PostAsJsonAsync("/api/teaching-cases", body);
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        using var doc = await ReadJsonAsync(res);
        return doc.RootElement.GetProperty("id").GetGuid();
    }

    // ---------------------------------------------------------------- TF-001/002

    [Fact]
    public async Task CreateFromReport_Strips_Phi()
    {
        var reportId = await SeedPhiReportAsync();
        using var client = _factory.CreateTenantClient();

        var res = await client.PostAsJsonAsync(
            $"/api/teaching-cases/from-report/{reportId}",
            new { title = "Acute appendicitis on CT", diagnosis = "Acute appendicitis", tags = "GI,Emergency" });
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);

        using var doc = await ReadJsonAsync(res);
        var id = doc.RootElement.GetProperty("id").GetGuid();

        // The PERSISTED row is the thing that matters.
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RadioPadDbContext>();
        var row = await db.TeachingCases.AsNoTracking().FirstAsync(c => c.Id == id);

        var everything = string.Join(
            "\n",
            row.Title, row.Diagnosis, row.TeachingPoints,
            row.ClinicalHistory, row.FindingsText, row.ImpressionText, row.Tags);

        Assert.DoesNotContain(Accession, everything);
        Assert.DoesNotContain(PatientRef, everything);
        Assert.DoesNotContain("Jane", everything);
        Assert.DoesNotContain("Doe", everything);
        Assert.DoesNotContain("004812345", everything);
        Assert.DoesNotContain("1974-03-02", everything);
        Assert.DoesNotContain("Alan Grant", everything);
        Assert.DoesNotContain("05/01/2024", everything);

        // ... while the teaching content survives.
        Assert.Contains("12 mm", row.FindingsText);
        Assert.Contains("appendix", row.FindingsText);
        Assert.Equal("CT", row.Modality);
        Assert.Equal("Abdomen", row.BodyPart);
        Assert.Equal(reportId, row.SourceReportId);
        Assert.Equal(TeachingVisibility.Private, row.Visibility);
    }

    [Fact]
    public async Task CreateFromReport_Audits_The_Creation()
    {
        var reportId = await SeedPhiReportAsync();
        using var client = _factory.CreateTenantClient();
        var res = await client.PostAsJsonAsync($"/api/teaching-cases/from-report/{reportId}", new { title = "Audited case" });
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        using var doc = await ReadJsonAsync(res);
        var id = doc.RootElement.GetProperty("id").GetGuid();

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RadioPadDbContext>();
        var events = await db.AuditEvents.AsNoTracking()
            .Where(e => e.TenantId == _factory.SeedTenant.Id && e.Action == AuditAction.TeachingCaseCreated)
            .ToListAsync();

        var match = Assert.Single(events, e => e.DetailsJson.Contains(id.ToString()));
        Assert.Contains("\"deidentified\":true", match.DetailsJson);
        // The audit row itself must not carry the identifiers it is recording around.
        Assert.DoesNotContain(Accession, match.DetailsJson);
        Assert.DoesNotContain(PatientRef, match.DetailsJson);
    }

    // ------------------------------------------------------------------- TF-004

    [Fact]
    public async Task Search_Filters_By_Modality_BodyPart_Difficulty_And_Tag()
    {
        using var client = _factory.CreateTenantClient();
        var marker = Guid.NewGuid().ToString("N")[..8];

        var ct = await CreateBlankCaseAsync(client, new
        {
            title = $"CT chest case {marker}",
            modality = "CT",
            bodyPart = "Chest",
            difficulty = (int)TeachingDifficulty.Advanced,
            tags = "pneumothorax,emergency",
            teachingPoints = $"marker {marker}",
        });
        var mri = await CreateBlankCaseAsync(client, new
        {
            title = $"MRI brain case {marker}",
            modality = "MRI",
            bodyPart = "Brain",
            difficulty = (int)TeachingDifficulty.Introductory,
            tags = "stroke",
            teachingPoints = $"marker {marker}",
        });

        async Task<Guid[]> SearchAsync(string qs)
        {
            using var doc = await ReadJsonAsync(await client.GetAsync($"/api/teaching-cases?{qs}"));
            return doc.RootElement.GetProperty("items").EnumerateArray()
                .Select(e => e.GetProperty("id").GetGuid())
                .ToArray();
        }

        var byModality = await SearchAsync($"modality=CT&q={marker}");
        Assert.Contains(ct, byModality);
        Assert.DoesNotContain(mri, byModality);

        var byBodyPart = await SearchAsync($"bodyPart=Brain&q={marker}");
        Assert.Contains(mri, byBodyPart);
        Assert.DoesNotContain(ct, byBodyPart);

        var byDifficulty = await SearchAsync($"difficulty={(int)TeachingDifficulty.Advanced}&q={marker}");
        Assert.Contains(ct, byDifficulty);
        Assert.DoesNotContain(mri, byDifficulty);

        var byTag = await SearchAsync($"tag=stroke&q={marker}");
        Assert.Contains(mri, byTag);
        Assert.DoesNotContain(ct, byTag);

        // Free text hits the title.
        var byText = await SearchAsync($"q=MRI brain case {marker}");
        Assert.Contains(mri, byText);
        Assert.DoesNotContain(ct, byText);
    }

    // ------------------------------------------------------------------- TF-007

    [Fact]
    public async Task Private_Case_Is_Invisible_To_Other_Users_Until_Published()
    {
        using var author = _factory.CreateTenantClient();
        var marker = Guid.NewGuid().ToString("N")[..8];
        var id = await CreateBlankCaseAsync(author, new { title = $"Private draft {marker}", modality = "CT" });

        var (_, otherEmail) = await SeedColleagueAsync();
        using var colleague = ClientFor(_factory.SeedTenant.Slug, otherEmail);

        Assert.Equal(HttpStatusCode.NotFound, (await colleague.GetAsync($"/api/teaching-cases/{id}")).StatusCode);

        var publish = await author.PostAsync($"/api/teaching-cases/{id}/publish", null);
        Assert.True(publish.IsSuccessStatusCode);

        var afterPublish = await colleague.GetAsync($"/api/teaching-cases/{id}");
        Assert.Equal(HttpStatusCode.OK, afterPublish.StatusCode);

        using var doc = await ReadJsonAsync(afterPublish);
        // Provenance is author-only: a reader of the library never learns which
        // report the case came from. The API serialises with
        // JsonIgnoreCondition.WhenWritingNull, so "withheld" shows up as the key
        // being absent entirely rather than an explicit null — assert on both
        // shapes so this stays correct if that global option ever changes.
        var hasSource = doc.RootElement.TryGetProperty("sourceReportId", out var source);
        Assert.True(!hasSource || source.ValueKind == JsonValueKind.Null);
        // TF-008 — a read by someone other than the author counts.
        Assert.Equal(1, doc.RootElement.GetProperty("viewCount").GetInt32());
    }

    // -------------------------------------------------------- tenant isolation

    [Fact]
    public async Task Published_Case_Is_Invisible_To_Another_Tenant()
    {
        using var author = _factory.CreateTenantClient();
        var marker = Guid.NewGuid().ToString("N")[..8];
        var id = await CreateBlankCaseAsync(author, new { title = $"Cross tenant {marker}", modality = "CT" });
        Assert.True((await author.PostAsync($"/api/teaching-cases/{id}/publish", null)).IsSuccessStatusCode);

        var (otherSlug, otherEmail) = await SeedForeignTenantAsync();
        using var outsider = ClientFor(otherSlug, otherEmail);

        // Direct fetch by id must not cross the tenant boundary...
        Assert.Equal(HttpStatusCode.NotFound, (await outsider.GetAsync($"/api/teaching-cases/{id}")).StatusCode);

        // ... and neither must a search, even one that matches the title exactly.
        using var doc = await ReadJsonAsync(await outsider.GetAsync($"/api/teaching-cases?q=Cross tenant {marker}"));
        Assert.Equal(0, doc.RootElement.GetProperty("total").GetInt32());
    }

    // ------------------------------------------------------ delete permissions

    [Fact]
    public async Task Delete_Is_Restricted_To_The_Author_Or_An_Admin()
    {
        using var author = _factory.CreateTenantClient();
        var idForColleague = await CreateBlankCaseAsync(author, new { title = "Delete me (colleague)", modality = "CT" });
        var idForAdmin = await CreateBlankCaseAsync(author, new { title = "Delete me (admin)", modality = "CT" });
        var idForAuthor = await CreateBlankCaseAsync(author, new { title = "Delete me (author)", modality = "CT" });

        var (_, colleagueEmail) = await SeedColleagueAsync();
        using var colleague = ClientFor(_factory.SeedTenant.Slug, colleagueEmail);

        // A non-owning clinician is refused even though they hold teaching_cases.manage.
        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await colleague.DeleteAsync($"/api/teaching-cases/{idForColleague}")).StatusCode);

        // The author may delete their own.
        Assert.Equal(
            HttpStatusCode.NoContent,
            (await author.DeleteAsync($"/api/teaching-cases/{idForAuthor}")).StatusCode);

        // A library admin may delete a case they do not own (moderation).
        using var admin = _factory.CreateAdminClient();
        Assert.Equal(
            HttpStatusCode.NoContent,
            (await admin.DeleteAsync($"/api/teaching-cases/{idForAdmin}")).StatusCode);
    }

    // ------------------------------------------------------------------ helpers

    private HttpClient ClientFor(string tenantSlug, string email)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-RadioPad-Tenant", tenantSlug);
        client.DefaultRequestHeaders.Add("X-RadioPad-User", email);
        return client;
    }

    /// <summary>A second clinician inside the SAME tenant (holds manage, owns nothing).</summary>
    private async Task<(Guid userId, string email)> SeedColleagueAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RadioPadDbContext>();
        var email = $"teach-colleague-{Guid.NewGuid():N}@radiopad.local";
        var user = new User
        {
            TenantId = _factory.SeedTenant.Id,
            Email = email,
            DisplayName = "Teaching Colleague",
            Role = UserRole.Resident,
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return (user.Id, email);
    }

    /// <summary>A radiologist in a DIFFERENT tenant.</summary>
    private async Task<(string slug, string email)> SeedForeignTenantAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RadioPadDbContext>();
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var tenant = new Tenant { Slug = $"teach-other-{suffix}", DisplayName = "Other Teaching Tenant" };
        db.Tenants.Add(tenant);
        var email = $"teach-outsider-{suffix}@radiopad.local";
        db.Users.Add(new User
        {
            TenantId = tenant.Id,
            Email = email,
            DisplayName = "Outside Radiologist",
            Role = UserRole.Radiologist,
        });
        await db.SaveChangesAsync();
        return (tenant.Slug, email);
    }
}
