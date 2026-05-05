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
/// Iter-30 — multi-radiologist sign-off + addendum workflow.
/// </summary>
public class MultiSignTests : IClassFixture<RadioPadAppFactory>
{
    private readonly RadioPadAppFactory _factory;
    public MultiSignTests(RadioPadAppFactory factory) => _factory = factory;

    private async Task<Guid> CreateAndPopulateAsync(HttpClient client)
    {
        var create = await client.PostAsJsonAsync("/api/reports", new
        {
            modality = "CT",
            bodyPart = "Chest",
            indication = "Cough",
            accessionNumber = $"ACC-MS-{Guid.NewGuid():N}",
        });
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var doc = await JsonDocument.ParseAsync(await create.Content.ReadAsStreamAsync());
        var id = doc.RootElement.GetProperty("id").GetGuid();
        var patch = await client.PatchAsJsonAsync($"/api/reports/{id}", new
        {
            findings = "Lungs clear.",
            impression = "1. No acute findings.",
        });
        Assert.True(patch.IsSuccessStatusCode);
        return id;
    }

    [Fact]
    public async Task First_Signature_Must_Be_Primary()
    {
        using var client = _factory.CreateTenantClient();
        var id = await CreateAndPopulateAsync(client);

        var early = await client.PostAsJsonAsync($"/api/reports/{id}/sign", new { role = "CoSigner" });
        Assert.Equal(HttpStatusCode.Conflict, early.StatusCode);

        var primary = await client.PostAsJsonAsync($"/api/reports/{id}/sign", new { role = "Primary" });
        Assert.Equal(HttpStatusCode.OK, primary.StatusCode);

        var addPrimary = await client.PostAsJsonAsync($"/api/reports/{id}/sign", new { role = "Primary" });
        Assert.Equal(HttpStatusCode.Conflict, addPrimary.StatusCode);

        var co = await client.PostAsJsonAsync($"/api/reports/{id}/sign", new { role = "CoSigner" });
        Assert.Equal(HttpStatusCode.OK, co.StatusCode);

        var list = await client.GetAsync($"/api/reports/{id}/signatures");
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        var doc = await JsonDocument.ParseAsync(await list.Content.ReadAsStreamAsync());
        Assert.Equal(2, doc.RootElement.GetArrayLength());
    }

    [Fact]
    public async Task Addendum_Requires_Primary_Then_Creates_New_Version()
    {
        using var client = _factory.CreateTenantClient();
        var id = await CreateAndPopulateAsync(client);

        var early = await client.PostAsJsonAsync($"/api/reports/{id}/addendum", new { body = "Late finding noted." });
        Assert.Equal(HttpStatusCode.Conflict, early.StatusCode);

        var primary = await client.PostAsJsonAsync($"/api/reports/{id}/sign", new { role = "Primary" });
        Assert.Equal(HttpStatusCode.OK, primary.StatusCode);

        var add = await client.PostAsJsonAsync($"/api/reports/{id}/addendum", new
        {
            body = "Late finding: small left pleural effusion.",
        });
        Assert.Equal(HttpStatusCode.OK, add.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RadioPadDbContext>();
        var versions = await db.ReportVersions
            .Where(v => v.ReportId == id)
            .ToListAsync();
        Assert.Contains(versions, v => v.IsAddendum);
        var audited = await db.AuditEvents.AnyAsync(a =>
            a.ReportId == id && a.Action == AuditAction.ReportAddendumAppended);
        Assert.True(audited);
    }
}
