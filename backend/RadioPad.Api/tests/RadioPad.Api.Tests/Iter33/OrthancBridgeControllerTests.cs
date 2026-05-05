using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RadioPad.Api.Tests.Integration;
using RadioPad.Application.Services.Hl7Bridge;
using RadioPad.Domain.Enums;
using RadioPad.Infrastructure.Persistence;
using Xunit;

namespace RadioPad.Api.Tests.Iter33;

/// <summary>
/// Iter-33 INT-008 — exercises the Orthanc bridge endpoints over the real
/// HTTP pipeline. Verifies bearer auth (constant-time), audit emission, and
/// HL7 outbox handoff for a synthetic DICOM SR.
/// </summary>
public class OrthancBridgeControllerTests : IClassFixture<RadioPadAppFactory>, IDisposable
{
    private readonly RadioPadAppFactory _factory;
    private const string Token = "iter33-bridge-token";

    public OrthancBridgeControllerTests(RadioPadAppFactory factory)
    {
        _factory = factory;
        Environment.SetEnvironmentVariable("RADIOPAD_BRIDGE_TOKEN", Token);
        Environment.SetEnvironmentVariable("RADIOPAD_BRIDGE_TENANT", _factory.SeedTenant.Slug);
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("RADIOPAD_BRIDGE_TOKEN", null);
        Environment.SetEnvironmentVariable("RADIOPAD_BRIDGE_TENANT", null);
    }

    private HttpClient CreateBearerClient()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Token);
        return client;
    }

    [Fact]
    public async Task StudyStable_ValidBearer_AuditsStudyReceived()
    {
        var client = CreateBearerClient();
        var resp = await client.PostAsJsonAsync("/api/integrations/orthanc/study-stable", new
        {
            patientId = "PT-IT-1",
            accessionNumber = "ACC-IT-1",
            studyInstanceUid = "1.2.3.4",
            modality = "CT",
            studyDate = "20260503",
        });
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RadioPadDbContext>();
        var found = await db.AuditEvents.AnyAsync(a =>
            a.TenantId == _factory.SeedTenant.Id
            && a.Action == AuditAction.StudyReceived
            && EF.Functions.Like(a.DetailsJson, "%ACC-IT-1%"));
        Assert.True(found);
    }

    [Fact]
    public async Task StudyStable_InvalidBearer_Returns401()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "wrong-token");
        var resp = await client.PostAsJsonAsync("/api/integrations/orthanc/study-stable", new
        {
            patientId = "PT-IT-2",
            accessionNumber = "ACC-IT-2",
        });
        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    [Fact]
    public async Task SrStored_ValidBearer_EnqueuesOutboundHl7AndAudits()
    {
        // Build a synthetic DICOM SR JSON model with a single TEXT content item.
        var sr = new JsonObject
        {
            ["00080016"] = new JsonObject { ["vr"] = "UI", ["Value"] = new JsonArray { Hl7ToDicomSrConverter.SopClassUidBasicTextSr } },
            ["00080018"] = new JsonObject { ["vr"] = "UI", ["Value"] = new JsonArray { Hl7ToDicomSrConverter.NewSopInstanceUid() } },
            ["00080050"] = new JsonObject { ["vr"] = "SH", ["Value"] = new JsonArray { "ACC-SR-1" } },
            ["00100020"] = new JsonObject { ["vr"] = "LO", ["Value"] = new JsonArray { "PT-SR-1" } },
            ["0040A040"] = new JsonObject { ["vr"] = "CS", ["Value"] = new JsonArray { "CONTAINER" } },
            ["0040A730"] = new JsonObject
            {
                ["vr"] = "SQ",
                ["Value"] = new JsonArray
                {
                    new JsonObject
                    {
                        ["0040A040"] = new JsonObject { ["vr"] = "CS", ["Value"] = new JsonArray { "TEXT" } },
                        ["0040A160"] = new JsonObject { ["vr"] = "UT", ["Value"] = new JsonArray { "Synthetic SR text body" } },
                    },
                },
            },
        };

        var client = CreateBearerClient();
        using var content = new StringContent(sr.ToJsonString(), System.Text.Encoding.UTF8, "application/json");
        var resp = await client.PostAsync("/api/integrations/orthanc/sr-stored", content);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        var outbox = _factory.Services.GetRequiredService<IHl7Outbox>();
        var entry = Assert.Single(outbox.Snapshot(),
            e => e.AccessionNumber == "ACC-SR-1");
        Assert.Contains("OBR|1||ACC-SR-1|", entry.Hl7);
        Assert.Contains("Synthetic SR text body", entry.Hl7);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RadioPadDbContext>();
        var audited = await db.AuditEvents.AnyAsync(a =>
            a.TenantId == _factory.SeedTenant.Id
            && a.Action == AuditAction.OrderIngested
            && EF.Functions.Like(a.DetailsJson, "%orthanc-sr%"));
        Assert.True(audited);
    }

    [Fact]
    public async Task SrStored_InvalidBearer_Returns401()
    {
        var client = _factory.CreateClient();
        var resp = await client.PostAsJsonAsync("/api/integrations/orthanc/sr-stored", new { });
        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }
}
