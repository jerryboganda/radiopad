using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using RadioPad.Domain.Enums;
using RadioPad.Infrastructure.Persistence;
using Xunit;

namespace RadioPad.Api.Tests.Integration;

public class ReportsFlowTests : IClassFixture<RadioPadAppFactory>
{
    private readonly RadioPadAppFactory _factory;
    public ReportsFlowTests(RadioPadAppFactory factory) => _factory = factory;

    [Fact]
    public async Task Create_Patch_Validate_Acknowledge_Roundtrip()
    {
        using var client = _factory.CreateTenantClient();

        // Create
        var create = await client.PostAsJsonAsync("/api/reports", new
        {
            modality = "CT",
            bodyPart = "Chest",
            indication = "Cough",
            comparison = "None",
            accessionNumber = "ACC-IT-1",
        });
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var doc = await JsonDocument.ParseAsync(await create.Content.ReadAsStreamAsync());
        var id = doc.RootElement.GetProperty("id").GetGuid();

        // Patch all required sections so validation passes
        var patch = await client.PatchAsJsonAsync($"/api/reports/{id}", new
        {
            indication = "Persistent cough.",
            technique = "Non-contrast CT.",
            comparison = "None.",
            findings = "Lungs clear. No nodules.",
            impression = "1. No acute pulmonary findings.",
        });
        Assert.True(patch.IsSuccessStatusCode, await patch.Content.ReadAsStringAsync());

        // Validate
        var validate = await client.PostAsync($"/api/reports/{id}/validate", null);
        Assert.Equal(HttpStatusCode.OK, validate.StatusCode);

        // Acknowledge — no blockers expected
        var ack = await client.PostAsync($"/api/reports/{id}/acknowledge", null);
        Assert.Equal(HttpStatusCode.OK, ack.StatusCode);

        // Verify status transitioned
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RadioPadDbContext>();
        var report = await db.Reports.FindAsync(id);
        Assert.NotNull(report);
        Assert.Equal(ReportStatus.Acknowledged, report!.Status);
    }

    [Fact]
    public async Task Tenant_Isolation_Other_Tenant_Cannot_See_Reports()
    {
        using var c1 = _factory.CreateTenantClient();
        var create = await c1.PostAsJsonAsync("/api/reports", new
        {
            modality = "CT",
            bodyPart = "Chest",
            indication = "x",
            accessionNumber = "ACC-ISO-1",
        });
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);

        // Foreign tenant — even if the user matches, the resolver should fail
        // because the tenant slug 'foreign' does not exist in the test DB.
        using var c2 = _factory.CreateClient();
        c2.DefaultRequestHeaders.Add("X-RadioPad-Tenant", "foreign");
        c2.DefaultRequestHeaders.Add("X-RadioPad-User", "x@x");
        var listed = await c2.GetAsync("/api/reports");
        // Either 500 (resolver throws) wrapped to 500, or 404 — accept any non-2xx.
        Assert.False(listed.IsSuccessStatusCode);
    }
}
