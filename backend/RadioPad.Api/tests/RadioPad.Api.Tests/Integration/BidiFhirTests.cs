using System.Net;
using System.Net.Http.Headers;
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
/// Iter-30 — Bidirectional FHIR. Verifies that
/// <c>POST /api/ingest/fhir/servicerequest</c> stores a
/// <see cref="Report.ServiceRequestRef"/> on the draft report, and that
/// <c>POST /api/ingest/fhir/diagnosticreport</c> creates a draft and audits
/// <see cref="AuditAction.ReportImported"/>.
/// </summary>
public class BidiFhirTests : IClassFixture<RadioPadAppFactory>
{
    private const string Bearer = "bidi_fhir_test_secret";
    private readonly RadioPadAppFactory _factory;
    public BidiFhirTests(RadioPadAppFactory f) => _factory = f;

    private async Task ConfigureBearerAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RadioPadDbContext>();
        var s = await db.TenantSettings.FirstOrDefaultAsync(x => x.TenantId == _factory.SeedTenant.Id);
        if (s is null) { s = new TenantSettings { TenantId = _factory.SeedTenant.Id }; db.TenantSettings.Add(s); }
        s.IngestBearerSecret = Bearer;
        await db.SaveChangesAsync();
    }

    private async Task ResetAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RadioPadDbContext>();
        var s = await db.TenantSettings.FirstOrDefaultAsync(x => x.TenantId == _factory.SeedTenant.Id);
        if (s is not null) { db.TenantSettings.Remove(s); await db.SaveChangesAsync(); }
    }

    [Fact]
    public async Task ServiceRequest_Sets_ServiceRequestRef_On_Draft()
    {
        await ConfigureBearerAsync();
        try
        {
            using var client = _factory.CreateClient();
            var sr = new
            {
                resourceType = "ServiceRequest",
                id = "sr-bidi-1",
                identifier = new[] { new { value = "ACC-BIDI-SR-1" } },
                code = new { coding = new[] { new { display = "CT" } } },
                bodySite = new[] { new { text = "Chest" } },
            };
            var req = new HttpRequestMessage(HttpMethod.Post, "/api/ingest/fhir/servicerequest")
            {
                Content = JsonContent.Create(sr),
            };
            req.Headers.Add("X-RadioPad-Tenant", _factory.SeedTenant.Slug);
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Bearer);
            var resp = await client.SendAsync(req);
            Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
            var doc = await JsonDocument.ParseAsync(await resp.Content.ReadAsStreamAsync());
            var id = doc.RootElement.GetProperty("id").GetGuid();

            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<RadioPadDbContext>();
            var report = await db.Reports.FindAsync(id);
            Assert.NotNull(report);
            Assert.Equal("ServiceRequest/sr-bidi-1", report!.ServiceRequestRef);
        }
        finally { await ResetAsync(); }
    }

    [Fact]
    public async Task DiagnosticReport_Creates_Draft_And_Audits_ReportImported()
    {
        await ConfigureBearerAsync();
        try
        {
            using var client = _factory.CreateClient();
            var dr = new
            {
                resourceType = "DiagnosticReport",
                id = "dr-bidi-1",
                identifier = new[] { new { value = "ACC-BIDI-DR-1" } },
                code = new { coding = new[] { new { display = "CT" } } },
                category = new[] { new { text = "Chest" } },
                conclusion = "No acute findings.",
                basedOn = new[] { new { reference = "ServiceRequest/sr-bidi-1" } },
            };
            var req = new HttpRequestMessage(HttpMethod.Post, "/api/ingest/fhir/diagnosticreport")
            {
                Content = JsonContent.Create(dr),
            };
            req.Headers.Add("X-RadioPad-Tenant", _factory.SeedTenant.Slug);
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Bearer);
            var resp = await client.SendAsync(req);
            Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
            var doc = await JsonDocument.ParseAsync(await resp.Content.ReadAsStreamAsync());
            var id = doc.RootElement.GetProperty("id").GetGuid();

            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<RadioPadDbContext>();
            var report = await db.Reports.FindAsync(id);
            Assert.NotNull(report);
            Assert.Equal("ACC-BIDI-DR-1", report!.Study.AccessionNumber);
            Assert.Equal("ServiceRequest/sr-bidi-1", report.ServiceRequestRef);
            Assert.Equal("No acute findings.", report.Impression);
            var audited = await db.AuditEvents.AnyAsync(a =>
                a.TenantId == _factory.SeedTenant.Id
                && a.ReportId == id
                && a.Action == AuditAction.ReportImported);
            Assert.True(audited);
        }
        finally { await ResetAsync(); }
    }
}
