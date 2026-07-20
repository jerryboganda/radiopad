using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace RadioPad.Api.Tests.Integration;

/// <summary>
/// PRD RPT-021 — tenant / subspecialty shared macros
/// (<c>/api/macros</c>). Covers the read/author split (every member reads,
/// only governance roles author), scope validation, subspecialty filtering,
/// and idempotent upsert on the (scope, subspecialty, trigger) key.
/// </summary>
public class SharedMacrosTests : IClassFixture<RadioPadAppFactory>
{
    private readonly RadioPadAppFactory _factory;
    public SharedMacrosTests(RadioPadAppFactory f) => _factory = f;

    private static async Task<JsonDocument> ReadAsync(HttpResponseMessage r) =>
        await JsonDocument.ParseAsync(await r.Content.ReadAsStreamAsync());

    [Fact]
    public async Task Radiologist_Can_Read_But_Not_Author()
    {
        using var reader = _factory.CreateTenantClient();

        var list = await reader.GetAsync("/api/macros");
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);

        var write = await reader.PostAsJsonAsync("/api/macros", new
        {
            trigger = "nlchest-denied",
            body = "Should not be saved.",
        });
        Assert.Equal(HttpStatusCode.Forbidden, write.StatusCode);
    }

    [Fact]
    public async Task Admin_Publishes_A_Tenant_Macro_And_Everyone_Sees_It()
    {
        using var admin = _factory.CreateAdminClient();
        var saved = await admin.PostAsJsonAsync("/api/macros", new
        {
            trigger = "nlchest",
            body = "The lungs are clear. No pleural effusion.",
            description = "Departmental normal chest",
        });
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        using var savedDoc = await ReadAsync(saved);
        Assert.Equal("Tenant", savedDoc.RootElement.GetProperty("scope").GetString());
        var id = savedDoc.RootElement.GetProperty("id").GetGuid();

        using var reader = _factory.CreateTenantClient();
        using var listDoc = await ReadAsync(await reader.GetAsync("/api/macros"));
        Assert.Contains(
            listDoc.RootElement.EnumerateArray(),
            m => m.GetProperty("id").GetGuid() == id
                 && m.GetProperty("body").GetString()!.Contains("lungs are clear"));
    }

    [Fact]
    public async Task Subspecialty_Scope_Requires_A_Subspecialty()
    {
        using var admin = _factory.CreateAdminClient();
        var bad = await admin.PostAsJsonAsync("/api/macros", new
        {
            trigger = "neuro-normal",
            body = "No acute intracranial abnormality.",
            scope = "Subspecialty",
            subspecialty = "",
        });
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
        using var doc = await ReadAsync(bad);
        Assert.Equal("validation", doc.RootElement.GetProperty("kind").GetString());
    }

    [Fact]
    public async Task Subspecialty_Filter_Returns_Tenant_Wide_Plus_That_Subspecialty()
    {
        using var admin = _factory.CreateAdminClient();
        await admin.PostAsJsonAsync("/api/macros", new
        {
            trigger = "filter-tenantwide",
            body = "Tenant-wide body.",
        });
        await admin.PostAsJsonAsync("/api/macros", new
        {
            trigger = "filter-neuro",
            body = "Neuro body.",
            scope = "Subspecialty",
            subspecialty = "Neuro",
        });
        await admin.PostAsJsonAsync("/api/macros", new
        {
            trigger = "filter-msk",
            body = "MSK body.",
            scope = "Subspecialty",
            subspecialty = "MSK",
        });

        using var reader = _factory.CreateTenantClient();
        using var doc = await ReadAsync(await reader.GetAsync("/api/macros?subspecialty=Neuro"));
        var triggers = doc.RootElement.EnumerateArray()
            .Select(m => m.GetProperty("trigger").GetString())
            .ToList();

        Assert.Contains("filter-tenantwide", triggers);
        Assert.Contains("filter-neuro", triggers);
        Assert.DoesNotContain("filter-msk", triggers);
    }

    [Fact]
    public async Task Saving_The_Same_Trigger_Updates_Instead_Of_Duplicating()
    {
        using var admin = _factory.CreateAdminClient();
        var first = await admin.PostAsJsonAsync("/api/macros", new
        {
            trigger = "dupe-check",
            body = "First body.",
        });
        using var firstDoc = await ReadAsync(first);
        var firstId = firstDoc.RootElement.GetProperty("id").GetGuid();

        var second = await admin.PostAsJsonAsync("/api/macros", new
        {
            trigger = "dupe-check",
            body = "Second body.",
        });
        using var secondDoc = await ReadAsync(second);
        Assert.Equal(firstId, secondDoc.RootElement.GetProperty("id").GetGuid());
        Assert.Equal("Second body.", secondDoc.RootElement.GetProperty("body").GetString());

        using var listDoc = await ReadAsync(await admin.GetAsync("/api/macros"));
        var matches = listDoc.RootElement.EnumerateArray()
            .Count(m => m.GetProperty("trigger").GetString() == "dupe-check");
        Assert.Equal(1, matches);
    }

    [Fact]
    public async Task Admin_Can_Delete_A_Macro()
    {
        using var admin = _factory.CreateAdminClient();
        using var savedDoc = await ReadAsync(await admin.PostAsJsonAsync("/api/macros", new
        {
            trigger = "delete-me",
            body = "Temporary.",
        }));
        var id = savedDoc.RootElement.GetProperty("id").GetGuid();

        var del = await admin.DeleteAsync($"/api/macros/{id}");
        Assert.Equal(HttpStatusCode.NoContent, del.StatusCode);

        using var listDoc = await ReadAsync(await admin.GetAsync("/api/macros"));
        Assert.DoesNotContain(
            listDoc.RootElement.EnumerateArray(),
            m => m.GetProperty("id").GetGuid() == id);
    }
}
