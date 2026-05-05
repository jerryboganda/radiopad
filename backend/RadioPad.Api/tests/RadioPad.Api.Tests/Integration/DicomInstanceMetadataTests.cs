using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RadioPad.Api.Services;
using RadioPad.Domain.Entities;
using RadioPad.Domain.Enums;
using RadioPad.Infrastructure.Persistence;
using Xunit;

namespace RadioPad.Api.Tests.Integration;

/// <summary>
/// Iter-31 DCM-007 — verifies <c>GET /api/reports/{id}/dicom-context/instance</c>
/// short-circuits with <c>configured:false</c> when DICOMweb is not set up,
/// and returns the parsed metadata block when a stub
/// <see cref="IDicomWebClient"/> is registered.
/// </summary>
public class DicomInstanceMetadataTests : IClassFixture<RadioPadAppFactory>
{
    private readonly RadioPadAppFactory _factory;
    public DicomInstanceMetadataTests(RadioPadAppFactory f) => _factory = f;

    private async Task<Guid> SeedReportAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RadioPadDbContext>();
        var report = new Report
        {
            TenantId = _factory.SeedTenant.Id,
            CreatedByUserId = _factory.SeedUser.Id,
            Status = ReportStatus.Draft,
            Study = new StudyContext { AccessionNumber = "ACC-DCM-INST-1", Modality = "CT", BodyPart = "Chest" },
        };
        db.Reports.Add(report);
        await db.SaveChangesAsync();
        return report.Id;
    }

    [Fact]
    public async Task Configured_False_When_Tenant_Not_Setup()
    {
        var id = await SeedReportAsync();
        using var client = _factory.CreateTenantClient();
        var resp = await client.GetAsync(
            $"/api/reports/{id}/dicom-context/instance?studyUid=1.2&seriesUid=1.2.3&instanceUid=1.2.3.4");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        using var doc = await JsonDocument.ParseAsync(await resp.Content.ReadAsStreamAsync());
        Assert.False(doc.RootElement.GetProperty("configured").GetBoolean());
    }

    [Fact]
    public async Task BadRequest_When_Uids_Missing()
    {
        var id = await SeedReportAsync();
        using var client = _factory.CreateTenantClient();
        var resp = await client.GetAsync($"/api/reports/{id}/dicom-context/instance?studyUid=&seriesUid=&instanceUid=");
        // Configure DICOMweb so we exit the configured-false short-circuit and
        // hit the validation branch instead.
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RadioPadDbContext>();
            var s = await db.TenantSettings.FirstOrDefaultAsync(x => x.TenantId == _factory.SeedTenant.Id)
                ?? new TenantSettings { TenantId = _factory.SeedTenant.Id };
            if (s.Id == Guid.Empty) db.TenantSettings.Add(s);
            s.DicomWebBaseUrl = "https://pacs.example/wado";
            await db.SaveChangesAsync();
        }
        try
        {
            var resp2 = await client.GetAsync(
                $"/api/reports/{id}/dicom-context/instance?studyUid=&seriesUid=&instanceUid=");
            Assert.Equal(HttpStatusCode.BadRequest, resp2.StatusCode);
        }
        finally
        {
            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<RadioPadDbContext>();
            var s = await db.TenantSettings.FirstOrDefaultAsync(x => x.TenantId == _factory.SeedTenant.Id);
            if (s is not null) { db.TenantSettings.Remove(s); await db.SaveChangesAsync(); }
        }
    }

    [Fact]
    public async Task Configured_Returns_Metadata_When_Tenant_Setup()
    {
        // Build the sub-factory FIRST, then seed both the report and the
        // TenantSettings row through *its* DI scope. WithWebHostBuilder
        // resolves a fresh service provider; seeding through the parent
        // factory before the sub-factory exists can race with EF model
        // initialization on the sub-host and the rows are sometimes not
        // visible to the request executed via the sub-factory's client.
        using var factory = _factory.WithWebHostBuilder(b =>
            b.ConfigureServices(services =>
            {
                var existing = services.Where(d => d.ServiceType == typeof(IDicomWebClient)).ToList();
                foreach (var d in existing) services.Remove(d);
                services.AddSingleton<IDicomWebClient, StubDicomWebClient>();
            }));

        Guid id;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RadioPadDbContext>();
            var report = new Report
            {
                TenantId = _factory.SeedTenant.Id,
                CreatedByUserId = _factory.SeedUser.Id,
                Status = ReportStatus.Draft,
                Study = new StudyContext { AccessionNumber = "ACC-DCM-INST-2", Modality = "CT", BodyPart = "Chest" },
            };
            db.Reports.Add(report);
            var s = await db.TenantSettings.FirstOrDefaultAsync(x => x.TenantId == _factory.SeedTenant.Id);
            if (s is null)
            {
                s = new TenantSettings { TenantId = _factory.SeedTenant.Id };
                db.TenantSettings.Add(s);
            }
            s.DicomWebBaseUrl = "https://pacs.example/wado";
            await db.SaveChangesAsync();
            id = report.Id;
        }
        try
        {
            var client = factory.CreateClient();
            client.DefaultRequestHeaders.Add("X-RadioPad-Tenant", _factory.SeedTenant.Slug);
            client.DefaultRequestHeaders.Add("X-RadioPad-User", _factory.SeedUser.Email);
            var resp = await client.GetAsync(
                $"/api/reports/{id}/dicom-context/instance?studyUid=1.2&seriesUid=1.2.3&instanceUid=1.2.3.4");
            Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
            using var doc = await JsonDocument.ParseAsync(await resp.Content.ReadAsStreamAsync());
            Assert.True(doc.RootElement.GetProperty("configured").GetBoolean());
            var meta = doc.RootElement.GetProperty("metadata");
            Assert.Equal(JsonValueKind.Array, meta.ValueKind);
            Assert.True(meta.GetArrayLength() >= 1);
            // Verify the SOPInstanceUID tag (00080018) survived the round-trip.
            Assert.Equal("1.2.3.4", meta[0].GetProperty("00080018").GetProperty("Value")[0].GetString());

            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<RadioPadDbContext>();
            var audited = await db.AuditEvents.AnyAsync(a =>
                a.TenantId == _factory.SeedTenant.Id
                && a.Action == AuditAction.DicomContextFetched
                && a.DetailsJson.Contains("\"scope\":\"instance\""));
            Assert.True(audited);
        }
        finally
        {
            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<RadioPadDbContext>();
            var s = await db.TenantSettings.FirstOrDefaultAsync(x => x.TenantId == _factory.SeedTenant.Id);
            if (s is not null) { db.TenantSettings.Remove(s); await db.SaveChangesAsync(); }
        }
    }

    private sealed class StubDicomWebClient : IDicomWebClient
    {
        public Task<DicomStudyContext?> FetchStudyAsync(TenantSettings settings, string accessionNumber, CancellationToken ct)
            => Task.FromResult<DicomStudyContext?>(null);

        public Task<JsonDocument?> RetrieveInstanceMetadataAsync(
            TenantSettings settings, string studyUid, string seriesUid, string instanceUid, CancellationToken ct)
        {
            var json = JsonSerializer.Serialize(new[]
            {
                new Dictionary<string, object>
                {
                    ["00080016"] = new { vr = "UI", Value = new[] { "1.2.840.10008.5.1.4.1.1.2" } },
                    ["00080018"] = new { vr = "UI", Value = new[] { instanceUid } },
                    ["00080060"] = new { vr = "CS", Value = new[] { "CT" } },
                },
            });
            return Task.FromResult<JsonDocument?>(JsonDocument.Parse(json));
        }

        public Task<(JsonDocument? body, int statusCode)> SearchStudiesAsync(
            TenantSettings settings, string query, CancellationToken ct)
            => Task.FromResult<(JsonDocument?, int)>((null, 0));

        public Task<(int statusCode, JsonDocument? body)> StoreInstancesAsync(
            TenantSettings settings, byte[] body, string contentType, CancellationToken ct)
            => Task.FromResult<(int, JsonDocument?)>((0, null));

        public Task<bool> HealthAsync(TenantSettings settings, CancellationToken ct)
            => Task.FromResult(false);
    }
}
