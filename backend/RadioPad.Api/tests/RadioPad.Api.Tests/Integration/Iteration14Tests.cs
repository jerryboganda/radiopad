using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RadioPad.Domain.Entities;
using RadioPad.Domain.Enums;
using RadioPad.Infrastructure.Persistence;
using Xunit;

namespace RadioPad.Api.Tests.Integration;

/// <summary>
/// Iteration 14 closures: HL7/FHIR ingest webhook (PRD INT-001..004) and the
/// DICOMweb context endpoint (PRD DCM-001..006) — auth, idempotency, audit.
/// </summary>
public class IngestWebhookTests : IClassFixture<RadioPadAppFactory>
{
    private const string Bearer = "ingest_test_secret_12345";
    private readonly RadioPadAppFactory _factory;
    public IngestWebhookTests(RadioPadAppFactory f) => _factory = f;

    private async Task ConfigureBearerAsync(string secret)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RadioPadDbContext>();
        var s = await db.TenantSettings.FirstOrDefaultAsync(x => x.TenantId == _factory.SeedTenant.Id);
        if (s is null) { s = new TenantSettings { TenantId = _factory.SeedTenant.Id }; db.TenantSettings.Add(s); }
        s.IngestBearerSecret = secret;
        await db.SaveChangesAsync();
    }

    private async Task ResetSettingsAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RadioPadDbContext>();
        var s = await db.TenantSettings.FirstOrDefaultAsync(x => x.TenantId == _factory.SeedTenant.Id);
        if (s is not null) { db.TenantSettings.Remove(s); await db.SaveChangesAsync(); }
    }

    [Fact]
    public async Task Ingest_503_When_Not_Configured()
    {
        await ResetSettingsAsync();
        using var client = _factory.CreateClient();
        var req = new HttpRequestMessage(HttpMethod.Post, "/api/ingest/order")
        {
            Content = JsonContent.Create(new { accessionNumber = "A", modality = "CT", bodyPart = "Chest" }),
        };
        req.Headers.Add("X-RadioPad-Tenant", _factory.SeedTenant.Slug);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "anything");
        var resp = await client.SendAsync(req);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, resp.StatusCode);
    }

    [Fact]
    public async Task Ingest_401_On_Bad_Bearer()
    {
        await ConfigureBearerAsync(Bearer);
        try
        {
            using var client = _factory.CreateClient();
            var req = new HttpRequestMessage(HttpMethod.Post, "/api/ingest/order")
            {
                Content = JsonContent.Create(new { accessionNumber = "A", modality = "CT", bodyPart = "Chest" }),
            };
            req.Headers.Add("X-RadioPad-Tenant", _factory.SeedTenant.Slug);
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "wrong-secret");
            var resp = await client.SendAsync(req);
            Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
        }
        finally { await ResetSettingsAsync(); }
    }

    [Fact]
    public async Task Ingest_Creates_Draft_Report_And_Audits()
    {
        await ConfigureBearerAsync(Bearer);
        try
        {
            using var client = _factory.CreateClient();
            var req = new HttpRequestMessage(HttpMethod.Post, "/api/ingest/order")
            {
                Content = JsonContent.Create(new
                {
                    accessionNumber = "ACC-INGEST-1",
                    modality = "CT",
                    bodyPart = "Chest",
                    indication = "rule out PE",
                }),
            };
            req.Headers.Add("X-RadioPad-Tenant", _factory.SeedTenant.Slug);
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Bearer);
            var resp = await client.SendAsync(req);
            Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
            var doc = await JsonDocument.ParseAsync(await resp.Content.ReadAsStreamAsync());
            var id = doc.RootElement.GetProperty("id").GetGuid();
            Assert.False(doc.RootElement.GetProperty("deduplicated").GetBoolean());

            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<RadioPadDbContext>();
            var report = await db.Reports.FindAsync(id);
            Assert.NotNull(report);
            Assert.Equal("ACC-INGEST-1", report!.Study.AccessionNumber);
            var audited = await db.AuditEvents.AnyAsync(
                a => a.TenantId == _factory.SeedTenant.Id && a.Action == AuditAction.OrderIngested);
            Assert.True(audited);
        }
        finally
        {
            await ResetSettingsAsync();
            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<RadioPadDbContext>();
            var dup = await db.Reports.Where(r => r.Study.AccessionNumber == "ACC-INGEST-1").ToListAsync();
            db.Reports.RemoveRange(dup);
            await db.SaveChangesAsync();
        }
    }

    [Fact]
    public async Task Ingest_Is_Idempotent_On_Same_Accession()
    {
        await ConfigureBearerAsync(Bearer);
        try
        {
            using var client = _factory.CreateClient();
            HttpRequestMessage MakeReq()
            {
                var r = new HttpRequestMessage(HttpMethod.Post, "/api/ingest/order")
                {
                    Content = JsonContent.Create(new
                    {
                        accessionNumber = "ACC-INGEST-DUP",
                        modality = "CT",
                        bodyPart = "Chest",
                    }),
                };
                r.Headers.Add("X-RadioPad-Tenant", _factory.SeedTenant.Slug);
                r.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Bearer);
                return r;
            }

            var first = await client.SendAsync(MakeReq());
            var second = await client.SendAsync(MakeReq());
            Assert.Equal(HttpStatusCode.OK, first.StatusCode);
            Assert.Equal(HttpStatusCode.OK, second.StatusCode);
            var d1 = await JsonDocument.ParseAsync(await first.Content.ReadAsStreamAsync());
            var d2 = await JsonDocument.ParseAsync(await second.Content.ReadAsStreamAsync());
            Assert.Equal(d1.RootElement.GetProperty("id").GetGuid(), d2.RootElement.GetProperty("id").GetGuid());
            Assert.True(d2.RootElement.GetProperty("deduplicated").GetBoolean());
        }
        finally
        {
            await ResetSettingsAsync();
            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<RadioPadDbContext>();
            var dup = await db.Reports.Where(r => r.Study.AccessionNumber == "ACC-INGEST-DUP").ToListAsync();
            db.Reports.RemoveRange(dup);
            await db.SaveChangesAsync();
        }
    }
}

public class DicomContextEndpointTests : IClassFixture<RadioPadAppFactory>
{
    private readonly RadioPadAppFactory _factory;
    public DicomContextEndpointTests(RadioPadAppFactory f) => _factory = f;

    [Fact]
    public async Task DicomContext_Returns_Configured_False_When_Not_Setup()
    {
        using var client = _factory.CreateTenantClient();
        var make = await client.PostAsJsonAsync("/api/reports", new
        {
            modality = "CT",
            bodyPart = "Chest",
            indication = "x",
            accessionNumber = "ACC-DCM-1",
        });
        var id = (await JsonDocument.ParseAsync(await make.Content.ReadAsStreamAsync()))
            .RootElement.GetProperty("id").GetGuid();
        var resp = await client.GetAsync($"/api/reports/{id}/dicom-context");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var doc = await JsonDocument.ParseAsync(await resp.Content.ReadAsStreamAsync());
        Assert.False(doc.RootElement.GetProperty("configured").GetBoolean());
    }
}


public class FhirServiceRequestIngestTests : IClassFixture<RadioPadAppFactory>
{
    private const string Bearer = "fhir_test_secret_xyz";
    private readonly RadioPadAppFactory _factory;
    public FhirServiceRequestIngestTests(RadioPadAppFactory f) => _factory = f;

    private async Task ConfigureBearerAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RadioPadDbContext>();
        var s = await db.TenantSettings.FirstOrDefaultAsync(x => x.TenantId == _factory.SeedTenant.Id);
        if (s is null) { s = new TenantSettings { TenantId = _factory.SeedTenant.Id }; db.TenantSettings.Add(s); }
        s.IngestBearerSecret = Bearer;
        await db.SaveChangesAsync();
    }

    private async Task ClearAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RadioPadDbContext>();
        var s = await db.TenantSettings.FirstOrDefaultAsync(x => x.TenantId == _factory.SeedTenant.Id);
        if (s is not null) { db.TenantSettings.Remove(s); await db.SaveChangesAsync(); }
    }

    private HttpRequestMessage MakeReq(string body)
    {
        var r = new HttpRequestMessage(HttpMethod.Post, "/api/ingest/fhir/servicerequest");
        r.Content = new StringContent(body, System.Text.Encoding.UTF8, "application/fhir+json");
        r.Headers.Add("X-RadioPad-Tenant", _factory.SeedTenant.Slug);
        r.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Bearer);
        return r;
    }

    [Fact]
    public async Task FhirServiceRequest_Creates_Draft_From_Bare_Resource()
    {
        await ConfigureBearerAsync();
        try
        {
            var sr = """
            {
              "resourceType": "ServiceRequest",
              "identifier": [{"system":"urn:ris","value":"ACC-FHIR-1"}],
              "code": {"coding":[{"system":"http://loinc","code":"24558-9","display":"CT"}],"text":"CT Chest"},
              "bodySite": [{"text":"Chest"}],
              "reasonCode": [{"text":"rule out PE"}]
            }
            """;
            using var client = _factory.CreateClient();
            var resp = await client.SendAsync(MakeReq(sr));
            Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
            var doc = await JsonDocument.ParseAsync(await resp.Content.ReadAsStreamAsync());
            Assert.False(doc.RootElement.GetProperty("deduplicated").GetBoolean());

            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<RadioPadDbContext>();
            var rep = await db.Reports.FirstOrDefaultAsync(r => r.Study.AccessionNumber == "ACC-FHIR-1");
            Assert.NotNull(rep);
            Assert.Equal("CT", rep!.Study.Modality);
            Assert.Equal("Chest", rep.Study.BodyPart);
        }
        finally { await ClearAsync(); }
    }

    [Fact]
    public async Task FhirServiceRequest_Extracts_From_Bundle()
    {
        await ConfigureBearerAsync();
        try
        {
            var bundle = """
            {
              "resourceType": "Bundle",
              "type": "transaction",
              "entry": [
                {"resource": {
                  "resourceType": "ServiceRequest",
                  "identifier":[{"value":"ACC-FHIR-2"}],
                  "code":{"coding":[{"display":"MR"}]},
                  "bodySite":[{"coding":[{"display":"Brain"}]}]
                }}
              ]
            }
            """;
            using var client = _factory.CreateClient();
            var resp = await client.SendAsync(MakeReq(bundle));
            Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        }
        finally { await ClearAsync(); }
    }

    [Fact]
    public async Task FhirServiceRequest_400_When_Missing_Identifier()
    {
        await ConfigureBearerAsync();
        try
        {
            var sr = """
            {
              "resourceType": "ServiceRequest",
              "code": {"coding":[{"display":"CT"}]},
              "bodySite":[{"text":"Chest"}]
            }
            """;
            using var client = _factory.CreateClient();
            var resp = await client.SendAsync(MakeReq(sr));
            Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        }
        finally { await ClearAsync(); }
    }
}


public class AuthSignInTests : IClassFixture<RadioPadAppFactory>
{
    private readonly RadioPadAppFactory _factory;
    public AuthSignInTests(RadioPadAppFactory f) => _factory = f;

    [Fact]
    public async Task SignIn_Issues_Token_For_Known_User()
    {
        using var client = _factory.CreateClient();
        var resp = await client.PostAsJsonAsync("/api/auth/signin", new
        {
            tenant = _factory.SeedTenant.Slug,
            user = "it-radiologist@radiopad.local",
        });
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var doc = await JsonDocument.ParseAsync(await resp.Content.ReadAsStreamAsync());
        var token = doc.RootElement.GetProperty("token").GetString();
        Assert.NotNull(token);
        Assert.StartsWith("rp_", token);
    }

    [Fact]
    public async Task SignIn_Rejects_Unknown_Tenant()
    {
        using var client = _factory.CreateClient();
        var resp = await client.PostAsJsonAsync("/api/auth/signin", new
        {
            tenant = "no-such-tenant",
            user = "it-radiologist@radiopad.local",
        });
        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    [Fact]
    public async Task SignIn_Rejects_Unknown_User()
    {
        using var client = _factory.CreateClient();
        var resp = await client.PostAsJsonAsync("/api/auth/signin", new
        {
            tenant = _factory.SeedTenant.Slug,
            user = "stranger@radiopad.local",
        });
        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }
}

public class AnalyticsEndpointTests : IClassFixture<RadioPadAppFactory>
{
    private readonly RadioPadAppFactory _factory;
    public AnalyticsEndpointTests(RadioPadAppFactory f) => _factory = f;

    [Fact]
    public async Task Analytics_Returns_Tenant_Scoped_Counts()
    {
        using var client = _factory.CreateTenantClient();
        var resp = await client.GetAsync("/api/usage/analytics");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var doc = await JsonDocument.ParseAsync(await resp.Content.ReadAsStreamAsync());
        Assert.True(doc.RootElement.TryGetProperty("reports", out _));
        Assert.True(doc.RootElement.TryGetProperty("ai", out _));
        Assert.True(doc.RootElement.TryGetProperty("governance", out _));
    }
}


public class Hl7ExportTests : IClassFixture<RadioPadAppFactory>
{
    private readonly RadioPadAppFactory _factory;
    public Hl7ExportTests(RadioPadAppFactory f) => _factory = f;

    [Fact]
    public async Task Hl7_Export_Requires_Acknowledged_Status()
    {
        Guid reportId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RadioPadDbContext>();
            var r = new RadioPad.Domain.Entities.Report
            {
                TenantId = _factory.SeedTenant.Id,
                CreatedByUserId = _factory.SeedUser.Id,
                Status = RadioPad.Domain.Enums.ReportStatus.Draft,
                Indication = "Cough",
                Findings = "Pending review.",
                Impression = "Pending.",
            };
            db.Reports.Add(r);
            await db.SaveChangesAsync();
            reportId = r.Id;
        }

        using var client = _factory.CreateTenantClient();
        var resp = await client.GetAsync($"/api/reports/{reportId}/export/hl7");
        // Seed report is Draft, so we expect 409 Conflict per RPT-012 gating.
        Assert.Equal(HttpStatusCode.Conflict, resp.StatusCode);
    }

    [Fact]
    public async Task Hl7_Export_Returns_OruR01_For_Acknowledged_Report()
    {
        Guid reportId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RadioPadDbContext>();
            var r = new RadioPad.Domain.Entities.Report
            {
                TenantId = _factory.SeedTenant.Id,
                CreatedByUserId = _factory.SeedUser.Id,
                Status = RadioPad.Domain.Enums.ReportStatus.Acknowledged,
                Indication = "Cough",
                Findings = "Right lung clear. No pleural effusion.",
                Impression = "No acute thoracic abnormality.",
            };
            db.Reports.Add(r);
            await db.SaveChangesAsync();
            reportId = r.Id;
        }

        using var client = _factory.CreateTenantClient();
        var resp = await client.GetAsync($"/api/reports/{reportId}/export/hl7");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var body = await resp.Content.ReadAsStringAsync();
        Assert.StartsWith("MSH|^~\\&|RADIOPAD^", body);
        Assert.Contains("ORU^R01^ORU_R01", body);
        Assert.Contains("OBR|", body);
        Assert.Contains("OBX|", body);
        Assert.Contains("IMPRESSION", body);
    }
}

public class SiemExportTests : IClassFixture<RadioPadAppFactory>
{
    private readonly RadioPadAppFactory _factory;
    public SiemExportTests(RadioPadAppFactory f) => _factory = f;

    [Fact]
    public async Task Siem_Export_Returns_Ndjson()
    {
        using var client = _factory.CreateComplianceClient();
        var resp = await client.GetAsync("/api/audit/siem?format=json");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        Assert.Equal("application/x-ndjson", resp.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Siem_Export_Returns_Cef()
    {
        using var client = _factory.CreateComplianceClient();
        var resp = await client.GetAsync("/api/audit/siem?format=cef");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var body = await resp.Content.ReadAsStringAsync();
        if (body.Length > 0)
        {
            Assert.StartsWith("CEF:0|RadioPad|", body);
        }
    }

    [Fact]
    public async Task Siem_Export_Rejects_Invalid_Format()
    {
        using var client = _factory.CreateComplianceClient();
        var resp = await client.GetAsync("/api/audit/siem?format=xml");
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }
}

[Collection(RadioPad.Api.Tests.Infrastructure.EnvironmentVariableCollection.Name)]
public class ScimUsersTests : IClassFixture<RadioPadAppFactory>
{
    private const string Bearer = "test-scim-bearer-token";
    private readonly RadioPadAppFactory _factory;

    public ScimUsersTests(RadioPadAppFactory f)
    {
        _factory = f;
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RadioPadDbContext>();
        var s = db.TenantSettings.FirstOrDefault(x => x.TenantId == _factory.SeedTenant.Id);
        if (s is null)
        {
            s = new TenantSettings { TenantId = _factory.SeedTenant.Id };
            db.TenantSettings.Add(s);
        }
        s.ScimBearerSecret = Bearer;
        db.SaveChanges();
    }

    private HttpClient ScimClient()
    {
        var c = _factory.CreateClient();
        c.DefaultRequestHeaders.Add("X-RadioPad-Tenant", _factory.SeedTenant.Slug);
        c.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", Bearer);
        return c;
    }

    private void ConfigureGroupRoleMap(string groupName, string role)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RadioPadDbContext>();
        var s = db.TenantSettings.First(x => x.TenantId == _factory.SeedTenant.Id);
        s.ScimGroupRoleMapJson = JsonSerializer.Serialize(new Dictionary<string, string>
        {
            [groupName] = role,
        });
        db.SaveChanges();
    }

    [Fact]
    public async Task Scim_Requires_Bearer()
    {
        using var c = _factory.CreateClient();
        c.DefaultRequestHeaders.Add("X-RadioPad-Tenant", _factory.SeedTenant.Slug);
        var resp = await c.GetAsync("/scim/v2/Users");
        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    [Fact]
    public async Task Scim_Accepts_Env_Backend_Bearer_Secret()
    {
        const string envName = "RADIOPAD_TEST_SCIM_BEARER";
        const string envBearer = "env-backed-scim-bearer";
        var old = Environment.GetEnvironmentVariable(envName);
        try
        {
            Environment.SetEnvironmentVariable(envName, envBearer);
            using (var scope = _factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<RadioPadDbContext>();
                var s = db.TenantSettings.First(x => x.TenantId == _factory.SeedTenant.Id);
                s.ScimBearerSecret = $"env:{envName}";
                await db.SaveChangesAsync();
            }

            using var c = _factory.CreateClient();
            c.DefaultRequestHeaders.Add("X-RadioPad-Tenant", _factory.SeedTenant.Slug);
            c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", envBearer);
            var resp = await c.GetAsync("/scim/v2/Users?count=1");
            Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        }
        finally
        {
            Environment.SetEnvironmentVariable(envName, old);
            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<RadioPadDbContext>();
            var s = db.TenantSettings.First(x => x.TenantId == _factory.SeedTenant.Id);
            s.ScimBearerSecret = Bearer;
            await db.SaveChangesAsync();
        }
    }

    [Fact]
    public async Task Scim_Lists_Tenant_Users()
    {
        using var c = ScimClient();
        var resp = await c.GetAsync("/scim/v2/Users?count=10");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var doc = await JsonDocument.ParseAsync(await resp.Content.ReadAsStreamAsync());
        Assert.True(doc.RootElement.GetProperty("totalResults").GetInt32() >= 1);
    }

    [Fact]
    public async Task Scim_Creates_And_Deprovisions_User()
    {
        using var c = ScimClient();
        var email = $"scim-{Guid.NewGuid():N}@radiopad.local";
        var create = await c.PostAsJsonAsync("/scim/v2/Users", new
        {
            schemas = new[] { "urn:ietf:params:scim:schemas:core:2.0:User" },
            userName = email,
            name = new { formatted = "Scim Test User" },
            active = true,
            roles = new[] { new { value = "ItAdmin" } },
        });
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var created = await JsonDocument.ParseAsync(await create.Content.ReadAsStreamAsync());
        var id = created.RootElement.GetProperty("id").GetString()!;

        // PATCH active:false
        var patch = await c.PatchAsync($"/scim/v2/Users/{id}", JsonContent.Create(new
        {
            schemas = new[] { "urn:ietf:params:scim:api:messages:2.0:PatchOp" },
            Operations = new object[]
            {
                new { op = "replace", path = "active", value = false },
            },
        }));
        Assert.Equal(HttpStatusCode.OK, patch.StatusCode);

        // Sign-in must fail for the deprovisioned user
        var signin = await c.PostAsJsonAsync("/api/auth/signin", new { tenant = _factory.SeedTenant.Slug, user = email });
        Assert.Equal(HttpStatusCode.Unauthorized, signin.StatusCode);

        var afterPatchList = await c.GetAsync($"/scim/v2/Users?filter=userName+eq+%22{Uri.EscapeDataString(email)}%22");
        Assert.Equal(HttpStatusCode.OK, afterPatchList.StatusCode);
        var afterPatchDoc = await JsonDocument.ParseAsync(await afterPatchList.Content.ReadAsStreamAsync());
        Assert.Equal(0, afterPatchDoc.RootElement.GetProperty("totalResults").GetInt32());

        // DELETE soft-deletes
        var del = await c.DeleteAsync($"/scim/v2/Users/{id}");
        Assert.Equal(HttpStatusCode.NoContent, del.StatusCode);
    }

    [Fact]
    public async Task Scim_Filter_By_UserName_Eq()
    {
        using var c = ScimClient();
        var resp = await c.GetAsync($"/scim/v2/Users?filter=userName+eq+%22it-radiologist%40radiopad.local%22");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var doc = await JsonDocument.ParseAsync(await resp.Content.ReadAsStreamAsync());
        Assert.Equal(1, doc.RootElement.GetProperty("totalResults").GetInt32());
    }

    [Fact]
    public async Task Scim_Groups_Create_List_And_Project_Role()
    {
        using var c = ScimClient();
        var email = $"scim-group-{Guid.NewGuid():N}@radiopad.local";
        var userCreate = await c.PostAsJsonAsync("/scim/v2/Users", new
        {
            schemas = new[] { "urn:ietf:params:scim:schemas:core:2.0:User" },
            userName = email,
            name = new { formatted = "Scim Group User" },
            active = true,
        });
        Assert.Equal(HttpStatusCode.Created, userCreate.StatusCode);
        var userDoc = await JsonDocument.ParseAsync(await userCreate.Content.ReadAsStreamAsync());
        var userId = userDoc.RootElement.GetProperty("id").GetString()!;
        var groupName = $"radiology-admins-{Guid.NewGuid():N}";
        ConfigureGroupRoleMap(groupName, nameof(UserRole.ReportingAdmin));

        var groupCreate = await c.PostAsJsonAsync("/scim/v2/Groups", new
        {
            schemas = new[] { "urn:ietf:params:scim:schemas:core:2.0:Group" },
            displayName = groupName,
            members = new[] { new { value = userId, display = email } },
        });
        Assert.Equal(HttpStatusCode.Created, groupCreate.StatusCode);
        var groupDoc = await JsonDocument.ParseAsync(await groupCreate.Content.ReadAsStreamAsync());
        var groupId = groupDoc.RootElement.GetProperty("id").GetString()!;
        Assert.Single(groupDoc.RootElement.GetProperty("members").EnumerateArray());

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RadioPadDbContext>();
            var projected = await db.Users.SingleAsync(u => u.Email == email);
            Assert.Equal(UserRole.ReportingAdmin, projected.Role);
        }

        var list = await c.GetAsync($"/scim/v2/Groups?filter=displayName+eq+%22{Uri.EscapeDataString(groupName)}%22");
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        var listDoc = await JsonDocument.ParseAsync(await list.Content.ReadAsStreamAsync());
        Assert.Equal(1, listDoc.RootElement.GetProperty("totalResults").GetInt32());

        var patch = await c.PatchAsync($"/scim/v2/Groups/{groupId}", JsonContent.Create(new
        {
            schemas = new[] { "urn:ietf:params:scim:api:messages:2.0:PatchOp" },
            Operations = new object[]
            {
                new { op = "remove", path = $"members[value eq \"{userId}\"]" },
            },
        }));
        Assert.Equal(HttpStatusCode.OK, patch.StatusCode);
        var patchedDoc = await JsonDocument.ParseAsync(await patch.Content.ReadAsStreamAsync());
        Assert.Empty(patchedDoc.RootElement.GetProperty("members").EnumerateArray());

        var delete = await c.DeleteAsync($"/scim/v2/Groups/{groupId}");
        Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);
    }

    [Fact]
    public async Task Scim_Group_Delete_Revokes_Project_Role()
    {
        using var c = ScimClient();
        var email = $"scim-group-delete-{Guid.NewGuid():N}@radiopad.local";
        var userId = await CreateScimUserAsync(c, email);
        var groupName = $"delete-admins-{Guid.NewGuid():N}";
        ConfigureGroupRoleMap(groupName, nameof(UserRole.ReportingAdmin));

        var groupCreate = await c.PostAsJsonAsync("/scim/v2/Groups", new
        {
            schemas = new[] { "urn:ietf:params:scim:schemas:core:2.0:Group" },
            displayName = groupName,
            members = new[] { new { value = userId, display = email } },
        });
        Assert.Equal(HttpStatusCode.Created, groupCreate.StatusCode);
        var groupDoc = await JsonDocument.ParseAsync(await groupCreate.Content.ReadAsStreamAsync());
        var groupId = groupDoc.RootElement.GetProperty("id").GetString()!;

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RadioPadDbContext>();
            var projected = await db.Users.SingleAsync(u => u.Email == email);
            Assert.Equal(UserRole.ReportingAdmin, projected.Role);
        }

        var delete = await c.DeleteAsync($"/scim/v2/Groups/{groupId}");
        Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RadioPadDbContext>();
            var demoted = await db.Users.SingleAsync(u => u.Email == email);
            Assert.Equal(UserRole.Radiologist, demoted.Role);
        }
    }

    [Fact]
    public async Task Scim_Groups_Patch_Pathless_Value_Replaces_Members_And_Name()
    {
        using var c = ScimClient();
        var firstEmail = $"scim-group-first-{Guid.NewGuid():N}@radiopad.local";
        var secondEmail = $"scim-group-second-{Guid.NewGuid():N}@radiopad.local";
        var firstId = await CreateScimUserAsync(c, firstEmail);
        var secondId = await CreateScimUserAsync(c, secondEmail);
        var groupName = $"patch-source-{Guid.NewGuid():N}";
        var renamedGroup = $"patch-target-{Guid.NewGuid():N}";

        var groupCreate = await c.PostAsJsonAsync("/scim/v2/Groups", new
        {
            schemas = new[] { "urn:ietf:params:scim:schemas:core:2.0:Group" },
            displayName = groupName,
            members = new[] { new { value = firstId, display = firstEmail } },
        });
        Assert.Equal(HttpStatusCode.Created, groupCreate.StatusCode);
        var groupDoc = await JsonDocument.ParseAsync(await groupCreate.Content.ReadAsStreamAsync());
        var groupId = groupDoc.RootElement.GetProperty("id").GetString()!;

        var patch = await c.PatchAsync($"/scim/v2/Groups/{groupId}", JsonContent.Create(new
        {
            schemas = new[] { "urn:ietf:params:scim:api:messages:2.0:PatchOp" },
            Operations = new object[]
            {
                new
                {
                    op = "replace",
                    value = new
                    {
                        displayName = renamedGroup,
                        members = new[] { new { value = secondId, display = secondEmail } },
                    },
                },
            },
        }));
        Assert.Equal(HttpStatusCode.OK, patch.StatusCode);
        var patchedDoc = await JsonDocument.ParseAsync(await patch.Content.ReadAsStreamAsync());
        Assert.Equal(renamedGroup, patchedDoc.RootElement.GetProperty("displayName").GetString());
        var members = patchedDoc.RootElement.GetProperty("members").EnumerateArray().ToArray();
        Assert.Single(members);
        Assert.Equal(secondId, members[0].GetProperty("value").GetString());
    }

    [Fact]
    public async Task Scim_Groups_Duplicate_Rename_Returns_Conflict()
    {
        using var c = ScimClient();
        var firstName = $"rename-source-{Guid.NewGuid():N}";
        var secondName = $"rename-target-{Guid.NewGuid():N}";

        var first = await c.PostAsJsonAsync("/scim/v2/Groups", new
        {
            schemas = new[] { "urn:ietf:params:scim:schemas:core:2.0:Group" },
            displayName = firstName,
        });
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        var firstDoc = await JsonDocument.ParseAsync(await first.Content.ReadAsStreamAsync());
        var firstId = firstDoc.RootElement.GetProperty("id").GetString()!;

        var second = await c.PostAsJsonAsync("/scim/v2/Groups", new
        {
            schemas = new[] { "urn:ietf:params:scim:schemas:core:2.0:Group" },
            displayName = secondName,
        });
        Assert.Equal(HttpStatusCode.Created, second.StatusCode);

        var rename = await c.PutAsJsonAsync($"/scim/v2/Groups/{firstId}", new
        {
            schemas = new[] { "urn:ietf:params:scim:schemas:core:2.0:Group" },
            displayName = secondName,
        });
        Assert.Equal(HttpStatusCode.Conflict, rename.StatusCode);
    }

    [Fact]
    public async Task Scim_ResourceTypes_Advertises_Groups()
    {
        using var c = ScimClient();
        var resp = await c.GetAsync("/scim/v2/ResourceTypes");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var doc = await JsonDocument.ParseAsync(await resp.Content.ReadAsStreamAsync());
        Assert.Equal(2, doc.RootElement.GetProperty("totalResults").GetInt32());
        Assert.Contains(doc.RootElement.GetProperty("Resources").EnumerateArray(),
            r => r.GetProperty("endpoint").GetString() == "/Groups");
    }

    private static async Task<string> CreateScimUserAsync(HttpClient c, string email)
    {
        var resp = await c.PostAsJsonAsync("/scim/v2/Users", new
        {
            schemas = new[] { "urn:ietf:params:scim:schemas:core:2.0:User" },
            userName = email,
            name = new { formatted = email },
            active = true,
        });
        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
        var doc = await JsonDocument.ParseAsync(await resp.Content.ReadAsStreamAsync());
        return doc.RootElement.GetProperty("id").GetString()!;
    }
}

[Collection(RadioPad.Api.Tests.Infrastructure.EnvironmentVariableCollection.Name)]
public class SecurityWebhookAdminTests : IClassFixture<RadioPadAppFactory>
{
    private readonly RadioPadAppFactory _factory;
    public SecurityWebhookAdminTests(RadioPadAppFactory f) => _factory = f;

    [Fact]
    public async Task Security_Webhook_Test_Returns_Configured_False_When_No_Endpoint()
    {
        var oldSecurity = Environment.GetEnvironmentVariable("RADIOPAD_SECURITY_WEBHOOK_URL");
        var oldAnomaly = Environment.GetEnvironmentVariable("RADIOPAD_ANOMALY_WEBHOOK_URL");
        try
        {
            Environment.SetEnvironmentVariable("RADIOPAD_SECURITY_WEBHOOK_URL", null);
            Environment.SetEnvironmentVariable("RADIOPAD_ANOMALY_WEBHOOK_URL", null);
            // SecurityManage is ItAdmin/MedicalDirector only (least-privilege 2026-06-23);
            // ComplianceReviewer reviews + audits but no longer owns security infra.
            using var c = _factory.CreateAdminClient();
            var resp = await c.PostAsync("/api/admin/security/test-webhook", null);
            Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
            var doc = await JsonDocument.ParseAsync(await resp.Content.ReadAsStreamAsync());
            Assert.False(doc.RootElement.GetProperty("configured").GetBoolean());
            Assert.False(doc.RootElement.GetProperty("sent").GetBoolean());
        }
        finally
        {
            Environment.SetEnvironmentVariable("RADIOPAD_SECURITY_WEBHOOK_URL", oldSecurity);
            Environment.SetEnvironmentVariable("RADIOPAD_ANOMALY_WEBHOOK_URL", oldAnomaly);
        }
    }

    [Fact]
    public async Task Security_Webhook_Test_Returns_Configured_True_When_Endpoint_Unreachable()
    {
        var oldSecurity = Environment.GetEnvironmentVariable("RADIOPAD_SECURITY_WEBHOOK_URL");
        var oldAnomaly = Environment.GetEnvironmentVariable("RADIOPAD_ANOMALY_WEBHOOK_URL");
        try
        {
            Environment.SetEnvironmentVariable("RADIOPAD_SECURITY_WEBHOOK_URL", "http://127.0.0.1:1/radiopad-security-test");
            Environment.SetEnvironmentVariable("RADIOPAD_ANOMALY_WEBHOOK_URL", null);
            // SecurityManage is ItAdmin/MedicalDirector only (least-privilege 2026-06-23).
            using var c = _factory.CreateAdminClient();
            var resp = await c.PostAsync("/api/admin/security/test-webhook", null);
            Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
            var doc = await JsonDocument.ParseAsync(await resp.Content.ReadAsStreamAsync());
            Assert.True(doc.RootElement.GetProperty("configured").GetBoolean());
            Assert.False(doc.RootElement.GetProperty("sent").GetBoolean());
            Assert.False(doc.RootElement.TryGetProperty("statusCode", out var statusCode) && statusCode.ValueKind != JsonValueKind.Null);
        }
        finally
        {
            Environment.SetEnvironmentVariable("RADIOPAD_SECURITY_WEBHOOK_URL", oldSecurity);
            Environment.SetEnvironmentVariable("RADIOPAD_ANOMALY_WEBHOOK_URL", oldAnomaly);
        }
    }
}

public class TenantRetentionTests : IClassFixture<RadioPadAppFactory>
{
    private readonly RadioPadAppFactory _factory;
    public TenantRetentionTests(RadioPadAppFactory f) => _factory = f;

    [Fact]
    public async Task Tenant_Settings_Persist_Retention_Policy()
    {
        using var c = _factory.CreateAdminClient();
        var save = await c.PostAsJsonAsync("/api/tenant/settings", new
        {
            hallucinationDetectionEnabled = true,
            hallucinationSeverity = "Warning",
            hallucinationAllowList = "",
            hallucinationMinSupport = 0.3,
            plan = 1,
            featureFlagsJson = "{}",
            retentionDays = 365,
            hashOnlyAuditMode = true,
            legalHold = false,
        });
        Assert.Equal(HttpStatusCode.OK, save.StatusCode);

        var get = await c.GetAsync("/api/tenant/settings");
        Assert.Equal(HttpStatusCode.OK, get.StatusCode);
        var doc = await JsonDocument.ParseAsync(await get.Content.ReadAsStreamAsync());
        var ret = doc.RootElement.GetProperty("retention");
        Assert.Equal(365, ret.GetProperty("days").GetInt32());
        Assert.True(ret.GetProperty("hashOnlyAuditMode").GetBoolean());
        Assert.False(ret.GetProperty("legalHold").GetBoolean());
    }
}

public class RetentionWorkerTests : IClassFixture<RadioPadAppFactory>
{
    private readonly RadioPadAppFactory _factory;
    public RetentionWorkerTests(RadioPadAppFactory f) => _factory = f;

    [Fact]
    public async Task Retention_Worker_Purges_Stale_AiRequests_And_Audits()
    {
        // Arrange: configure a 7-day retention; insert an AiRequest dated 30 days ago.
        Guid tenantId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RadioPadDbContext>();
            tenantId = _factory.SeedTenant.Id;
            var s = await db.TenantSettings.FirstOrDefaultAsync(x => x.TenantId == tenantId);
            if (s is null) { s = new TenantSettings { TenantId = tenantId }; db.TenantSettings.Add(s); }
            s.RetentionDays = 7;
            s.LegalHold = false;
            db.AiRequests.Add(new RadioPad.Domain.Entities.AiRequest
            {
                TenantId = tenantId,
                UserId = Guid.NewGuid(),
                Provider = "mock",
                Model = "stale",
                CreatedAt = DateTimeOffset.UtcNow.AddDays(-30),
            });
            await db.SaveChangesAsync();
        }

        // Act: run one sweep manually.
        using (var scope = _factory.Services.CreateScope())
        {
            var worker = ActivatorUtilities.CreateInstance<RadioPad.Api.Services.RetentionWorker>(
                scope.ServiceProvider);
            await worker.GetType()
                .GetMethod("SweepAsync", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
                .Invoke(worker, new object[] { CancellationToken.None })
                .Let(t => (Task)t!);
        }

        // Assert: stale row removed; RetentionPurge audit appended.
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RadioPadDbContext>();
            var stale = await db.AiRequests.AnyAsync(a => a.TenantId == tenantId && a.Model == "stale");
            Assert.False(stale);
            var purgeEvent = await db.AuditEvents
                .Where(a => a.TenantId == tenantId && a.Action == RadioPad.Domain.Enums.AuditAction.RetentionPurge)
                .OrderByDescending(a => a.CreatedAt)
                .FirstOrDefaultAsync();
            Assert.NotNull(purgeEvent);
        }
    }

    [Fact]
    public async Task Retention_Worker_Skips_When_LegalHold()
    {
        Guid tenantId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RadioPadDbContext>();
            tenantId = _factory.SeedTenant.Id;
            var s = await db.TenantSettings.FirstOrDefaultAsync(x => x.TenantId == tenantId);
            if (s is null) { s = new TenantSettings { TenantId = tenantId }; db.TenantSettings.Add(s); }
            s.RetentionDays = 1;
            s.LegalHold = true;
            db.AiRequests.Add(new RadioPad.Domain.Entities.AiRequest
            {
                TenantId = tenantId,
                UserId = Guid.NewGuid(),
                Provider = "mock",
                Model = "hold",
                CreatedAt = DateTimeOffset.UtcNow.AddDays(-30),
            });
            await db.SaveChangesAsync();
        }

        using (var scope = _factory.Services.CreateScope())
        {
            var worker = ActivatorUtilities.CreateInstance<RadioPad.Api.Services.RetentionWorker>(scope.ServiceProvider);
            await (Task)worker.GetType()
                .GetMethod("SweepAsync", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
                .Invoke(worker, new object[] { CancellationToken.None })!;
        }

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RadioPadDbContext>();
            Assert.True(await db.AiRequests.AnyAsync(a => a.TenantId == tenantId && a.Model == "hold"));
        }
    }
}

internal static class TaskAwaitHelper
{
    public static T Let<T>(this object self, Func<object, T> f) => f(self);
}

[Collection(RadioPad.Api.Tests.Infrastructure.EnvironmentVariableCollection.Name)]
public class CmkVerifyTests : IClassFixture<RadioPadAppFactory>
{
    private readonly RadioPadAppFactory _factory;
    public CmkVerifyTests(RadioPadAppFactory f) => _factory = f;

    [Fact]
    public async Task Cmk_Verify_Rejects_Empty_KeyRef()
    {
        using var c = _factory.CreateAdminClient();
        // Ensure CMK is unset.
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RadioPadDbContext>();
            var s = await db.TenantSettings.FirstOrDefaultAsync(x => x.TenantId == _factory.SeedTenant.Id);
            if (s is not null) { s.CmkKeyRef = ""; await db.SaveChangesAsync(); }
        }
        var resp = await c.PostAsync("/api/tenant/settings/kms/verify", null);
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task Cmk_Verify_Env_Provider_Round_Trip()
    {
        // Arrange: 32-byte AES-256 key in env var.
        var b64 = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
        Environment.SetEnvironmentVariable("RADIOPAD_TEST_CMK", b64);
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RadioPadDbContext>();
            var s = await db.TenantSettings.FirstOrDefaultAsync(x => x.TenantId == _factory.SeedTenant.Id);
            if (s is null) { s = new TenantSettings { TenantId = _factory.SeedTenant.Id }; db.TenantSettings.Add(s); }
            s.CmkKeyRef = "env:RADIOPAD_TEST_CMK";
            await db.SaveChangesAsync();
        }

        using var c = _factory.CreateAdminClient();
        var resp = await c.PostAsync("/api/tenant/settings/kms/verify", null);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var doc = await JsonDocument.ParseAsync(await resp.Content.ReadAsStreamAsync());
        Assert.True(doc.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal("env", doc.RootElement.GetProperty("scheme").GetString());
    }
}

[Collection(RadioPad.Api.Tests.Infrastructure.EnvironmentVariableCollection.Name)]
public class AuthFlowsTests : IClassFixture<RadioPadAppFactory>
{
    private readonly RadioPadAppFactory _factory;
    public AuthFlowsTests(RadioPadAppFactory f) => _factory = f;

    [Fact]
    public async Task Totp_Enroll_And_Verify_Round_Trip()
    {
        using var c = _factory.CreateTenantClient();
        var resp = await c.PostAsJsonAsync("/api/auth/mfa/enroll", new
        {
            tenant = _factory.SeedTenant.Slug,
            email = "it-radiologist@radiopad.local",
        });
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var doc = await JsonDocument.ParseAsync(await resp.Content.ReadAsStreamAsync());
        var secret = doc.RootElement.GetProperty("secret").GetString()!;

        // Compute the current TOTP code and verify.
        var key = RadioPad.Api.Controllers.MfaController_TestAccess.B32Decode(secret);
        var counter = DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 30;
        var code = RadioPad.Api.Controllers.MfaController_TestAccess.Hotp(key, counter);
        var verify = await c.PostAsJsonAsync("/api/auth/mfa/verify", new
        {
            tenant = _factory.SeedTenant.Slug,
            email = "it-radiologist@radiopad.local",
            code,
        });
        Assert.Equal(HttpStatusCode.OK, verify.StatusCode);
    }

    [Fact]
    public async Task MagicLink_Request_Returns_Dev_Link_And_Consume_Issues_Bearer()
    {
        var email = $"magic-link-{Guid.NewGuid():N}@radiopad.local";
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RadioPadDbContext>();
            db.Users.Add(new User
            {
                TenantId = _factory.SeedTenant.Id,
                Email = email,
                DisplayName = "Magic Link Test",
                Role = UserRole.Radiologist,
                PasswordHash = "dev",
                IsActive = true,
            });
            await db.SaveChangesAsync();
        }

        var oldTrustForwarded = Environment.GetEnvironmentVariable("RADIOPAD_TRUST_FORWARDED_FOR");
        Environment.SetEnvironmentVariable("RADIOPAD_TRUST_FORWARDED_FOR", "1");
        using var c = _factory.CreateTenantClient();
        c.DefaultRequestHeaders.Add("X-Forwarded-For", $"203.0.113.{Random.Shared.Next(1, 250)}");
        try
        {
            var req = await c.PostAsJsonAsync("/api/auth/magic-link/request", new
            {
                tenant = _factory.SeedTenant.Slug,
                email,
            });
        Assert.Equal(HttpStatusCode.OK, req.StatusCode);
        var doc = await JsonDocument.ParseAsync(await req.Content.ReadAsStreamAsync());
        Assert.True(doc.RootElement.TryGetProperty("devLink", out var devLink),
            "Dev link must be returned when SMTP is not configured.");
        var url = devLink.GetString()!;
        var token = Microsoft.AspNetCore.WebUtilities.QueryHelpers
            .ParseQuery(new Uri(url).Query)["magic"].ToString();

        var consume = await c.PostAsJsonAsync("/api/auth/magic-link/consume", new { token });
        Assert.Equal(HttpStatusCode.OK, consume.StatusCode);
        var c2 = await JsonDocument.ParseAsync(await consume.Content.ReadAsStreamAsync());
        Assert.StartsWith("rp_", c2.RootElement.GetProperty("token").GetString());
        }
        finally
        {
            Environment.SetEnvironmentVariable("RADIOPAD_TRUST_FORWARDED_FOR", oldTrustForwarded);
        }
    }

    [Fact]
    public async Task MagicLink_Consume_Rejects_Replay()
    {
        var raw = $"ml-{Guid.NewGuid():N}";
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RadioPadDbContext>();
            db.MagicLinks.Add(new MagicLinkToken
            {
                TenantId = _factory.SeedTenant.Id,
                UserId = _factory.SeedUser.Id,
                TokenHash = Sha256Hex(raw),
                ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(5),
            });
            await db.SaveChangesAsync();
        }

        using var c = _factory.CreateTenantClient();
        var first = await c.PostAsJsonAsync("/api/auth/magic-link/consume", new { token = raw });
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        var replay = await c.PostAsJsonAsync("/api/auth/magic-link/consume", new { token = raw });
        Assert.Equal(HttpStatusCode.Unauthorized, replay.StatusCode);
    }

    [Fact]
    public async Task MagicLink_Consume_Rejects_Inactive_User()
    {
        var raw = $"ml-{Guid.NewGuid():N}";
        var email = $"inactive-magic-{Guid.NewGuid():N}@radiopad.local";
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RadioPadDbContext>();
            var user = new User
            {
                TenantId = _factory.SeedTenant.Id,
                Email = email,
                DisplayName = "Inactive Magic Link Test",
                Role = UserRole.Radiologist,
                PasswordHash = "dev",
                IsActive = false,
            };
            db.Users.Add(user);
            await db.SaveChangesAsync();
            db.MagicLinks.Add(new MagicLinkToken
            {
                TenantId = _factory.SeedTenant.Id,
                UserId = user.Id,
                TokenHash = Sha256Hex(raw),
                ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(5),
            });
            await db.SaveChangesAsync();
        }

        using var c = _factory.CreateTenantClient();
        var consume = await c.PostAsJsonAsync("/api/auth/magic-link/consume", new { token = raw });
        Assert.Equal(HttpStatusCode.Unauthorized, consume.StatusCode);
    }

    private static string Sha256Hex(string s)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(s));
        return Convert.ToHexString(bytes);
    }

    [Fact]
    public async Task Device_Flow_Pending_Then_Approved_Then_Token()
    {
        using var c = _factory.CreateTenantClient();
        var auth = await c.PostAsJsonAsync("/api/auth/device/authorize", new { clientId = "test-cli" });
        Assert.Equal(HttpStatusCode.OK, auth.StatusCode);
        var ad = await JsonDocument.ParseAsync(await auth.Content.ReadAsStreamAsync());
        var deviceCode = ad.RootElement.GetProperty("deviceCode").GetString()!;
        var userCode = ad.RootElement.GetProperty("userCode").GetString()!;

        // Pending � must return error: authorization_pending.
        var poll1 = await c.PostAsJsonAsync("/api/auth/device/token", new
        {
            deviceCode,
            grantType = "urn:ietf:params:oauth:grant-type:device_code",
        });
        Assert.Equal(HttpStatusCode.BadRequest, poll1.StatusCode);

        // Approve.
        var approve = await c.PostAsJsonAsync("/api/auth/device/approve", new
        {
            tenant = _factory.SeedTenant.Slug,
            email = "it-radiologist@radiopad.local",
            userCode,
        });
        Assert.Equal(HttpStatusCode.OK, approve.StatusCode);

        // Wait long enough to bypass slow_down (interval = 5s); use far-past mutation instead.
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RadioPadDbContext>();
            var row = await db.DeviceAuth.FirstAsync(d => d.UserCode == userCode);
            row.LastPolledAt = DateTimeOffset.UtcNow.AddSeconds(-30);
            await db.SaveChangesAsync();
        }

        var poll2 = await c.PostAsJsonAsync("/api/auth/device/token", new
        {
            deviceCode,
            grantType = "urn:ietf:params:oauth:grant-type:device_code",
        });
        Assert.Equal(HttpStatusCode.OK, poll2.StatusCode);
        var tok = await JsonDocument.ParseAsync(await poll2.Content.ReadAsStreamAsync());
        Assert.StartsWith("rp_", tok.RootElement.GetProperty("accessToken").GetString());
    }
}

// Test-only helper that re-exposes the internal TOTP helpers via reflection.
internal static class MfaController_TestAccess
{
    public static byte[] B32Decode(string s)
    {
        var m = typeof(RadioPad.Api.Controllers.MfaController)
            .GetMethod("Base32Decode", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
        return (byte[])m.Invoke(null, new object[] { s })!;
    }

    public static string Hotp(byte[] key, long counter)
    {
        var m = typeof(RadioPad.Api.Controllers.MfaController)
            .GetMethod("HotpAt", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
        return (string)m.Invoke(null, new object[] { key, counter })!;
    }
}