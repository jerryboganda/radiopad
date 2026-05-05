using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RadioPad.Domain.Enums;
using RadioPad.Infrastructure.Persistence;
using Xunit;

namespace RadioPad.Api.Tests.Integration;

/// <summary>
/// Covers PRD RPT-012 (acknowledgement before export) and AI-012 / BILL-002
/// (AI usage ledger surfaced via <c>GET /api/usage/summary</c>).
/// </summary>
public class ExportAndUsageTests : IClassFixture<RadioPadAppFactory>
{
    private readonly RadioPadAppFactory _factory;
    public ExportAndUsageTests(RadioPadAppFactory factory) => _factory = factory;

    [Fact]
    public async Task Draft_Report_Cannot_Be_Exported_As_Fhir()
    {
        using var client = _factory.CreateTenantClient();

        var create = await client.PostAsJsonAsync("/api/reports", new
        {
            modality = "CT", bodyPart = "Chest", indication = "x",
            accessionNumber = "ACC-EXPORT-DRAFT",
        });
        var doc = await JsonDocument.ParseAsync(await create.Content.ReadAsStreamAsync());
        var id = doc.RootElement.GetProperty("id").GetGuid();

        var fhir = await client.GetAsync($"/api/reports/{id}/export/fhir");
        Assert.Equal(HttpStatusCode.Conflict, fhir.StatusCode);
        var body = await fhir.Content.ReadAsStringAsync();
        Assert.Contains("\"kind\":\"report_state\"", body);
    }

    [Fact]
    public async Task Acknowledged_Report_Exports_And_Audits_ReportExported()
    {
        using var client = _factory.CreateTenantClient();
        var create = await client.PostAsJsonAsync("/api/reports", new
        {
            modality = "CT", bodyPart = "Chest", indication = "Persistent cough.",
            accessionNumber = "ACC-EXPORT-OK",
        });
        var doc = await JsonDocument.ParseAsync(await create.Content.ReadAsStreamAsync());
        var id = doc.RootElement.GetProperty("id").GetGuid();

        await client.PatchAsJsonAsync($"/api/reports/{id}", new
        {
            indication = "Persistent cough.",
            technique = "Non-contrast CT.",
            comparison = "None.",
            findings = "Lungs clear. No nodules.",
            impression = "1. No acute pulmonary findings.",
        });
        var validate = await client.PostAsync($"/api/reports/{id}/validate", null);
        Assert.Equal(HttpStatusCode.OK, validate.StatusCode);
        var ack = await client.PostAsync($"/api/reports/{id}/acknowledge", null);
        Assert.Equal(HttpStatusCode.OK, ack.StatusCode);

        var fhir = await client.GetAsync($"/api/reports/{id}/export/fhir");
        Assert.Equal(HttpStatusCode.OK, fhir.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RadioPadDbContext>();
        var report = await db.Reports.FindAsync(id);
        Assert.NotNull(report);
        Assert.Equal(ReportStatus.Exported, report!.Status);

        var exportEvents = await db.AuditEvents
            .Where(e => e.ReportId == id && e.Action == AuditAction.ReportExported)
            .ToListAsync();
        Assert.NotEmpty(exportEvents);
    }

    [Fact]
    public async Task Validated_Report_Cannot_Be_Final_Exported_Until_Acknowledged()
    {
        using var client = _factory.CreateTenantClient();
        var create = await client.PostAsJsonAsync("/api/reports", new
        {
            modality = "CT", bodyPart = "Chest", indication = "Persistent cough.",
            accessionNumber = "ACC-EXPORT-VALIDATED",
        });
        var doc = await JsonDocument.ParseAsync(await create.Content.ReadAsStreamAsync());
        var id = doc.RootElement.GetProperty("id").GetGuid();

        await client.PatchAsJsonAsync($"/api/reports/{id}", new
        {
            indication = "Persistent cough.",
            technique = "Non-contrast CT.",
            comparison = "None.",
            findings = "Lungs clear. No nodules.",
            impression = "1. No acute pulmonary findings.",
        });
        var validate = await client.PostAsync($"/api/reports/{id}/validate", null);
        Assert.Equal(HttpStatusCode.OK, validate.StatusCode);

        var export = await client.GetAsync($"/api/reports/{id}/export/json");
        Assert.Equal(HttpStatusCode.Conflict, export.StatusCode);
        var body = await export.Content.ReadAsStringAsync();
        Assert.Contains("acknowledged", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Text_Export_Preview_Bypasses_Gate_Without_Auditing()
    {
        using var client = _factory.CreateTenantClient();
        var create = await client.PostAsJsonAsync("/api/reports", new
        {
            modality = "CT", bodyPart = "Chest", indication = "x",
            accessionNumber = "ACC-PREVIEW",
        });
        var doc = await JsonDocument.ParseAsync(await create.Content.ReadAsStreamAsync());
        var id = doc.RootElement.GetProperty("id").GetGuid();

        var preview = await client.GetAsync($"/api/reports/{id}/export/text?preview=true");
        Assert.Equal(HttpStatusCode.OK, preview.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RadioPadDbContext>();
        var report = await db.Reports.FindAsync(id);
        Assert.Equal(ReportStatus.Draft, report!.Status);
        var exportEvents = await db.AuditEvents
            .Where(e => e.ReportId == id && e.Action == AuditAction.ReportExported)
            .ToListAsync();
        Assert.Empty(exportEvents);
    }

    [Fact]
    public async Task Usage_Summary_Counts_Successful_Ai_Calls()
    {
        using var client = _factory.CreateTenantClient();
        var create = await client.PostAsJsonAsync("/api/reports", new
        {
            modality = "CT", bodyPart = "Chest", indication = "x",
            accessionNumber = "ACC-USAGE",
        });
        var doc = await JsonDocument.ParseAsync(await create.Content.ReadAsStreamAsync());
        var id = doc.RootElement.GetProperty("id").GetGuid();

        await client.PatchAsJsonAsync($"/api/reports/{id}", new
        {
            findings = "Lungs clear. No nodules.",
            impression = "No acute findings.",
        });

        var ai = await client.PostAsJsonAsync($"/api/reports/{id}/ai", new
        {
            mode = "impression",
            providerId = _factory.MockProvider.Id,
        });
        Assert.True(ai.IsSuccessStatusCode, await ai.Content.ReadAsStringAsync());

        var summary = await client.GetAsync("/api/usage/summary");
        Assert.Equal(HttpStatusCode.OK, summary.StatusCode);
        var body = await summary.Content.ReadAsStringAsync();
        var json = JsonDocument.Parse(body).RootElement;
        Assert.True(json.GetProperty("totalRequests").GetInt32() >= 1);
        Assert.True(json.GetProperty("okCount").GetInt32() >= 1);
    }
}
