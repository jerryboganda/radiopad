using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RadioPad.Application.Services;
using RadioPad.Domain.Entities;
using RadioPad.Domain.Enums;
using RadioPad.Infrastructure.Persistence;
using Xunit;

namespace RadioPad.Api.Tests.Integration;

/// <summary>
/// Iteration 13 closures: cost-aware provider routing, hallucination detector
/// (admin-managed), tenant settings GET/POST + RBAC, PDF + DOCX exports with
/// RPT-012 gating.
/// </summary>
public class CostAwareRoutingTests : IClassFixture<RadioPadAppFactory>
{
    private readonly RadioPadAppFactory _factory;
    public CostAwareRoutingTests(RadioPadAppFactory f) => _factory = f;

    [Fact]
    public async Task RunAi_Without_ProviderId_Picks_Cheapest_Enabled_Provider()
    {
        // Add a more expensive 'mock' provider; the seeded mock provider with
        // default cost 0 sorts last, so the explicit cheap one wins.
        Guid cheapId, expensiveId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RadioPadDbContext>();
            var cheap = new ProviderConfig
            {
                TenantId = _factory.SeedTenant.Id,
                Name = "cheap-mock",
                Adapter = "mock",
                Compliance = ProviderComplianceClass.LocalOnly,
                Enabled = true,
                Priority = 50,
                CostPerInputKToken = 0.01m,
                CostPerOutputKToken = 0.02m,
            };
            var expensive = new ProviderConfig
            {
                TenantId = _factory.SeedTenant.Id,
                Name = "expensive-mock",
                Adapter = "mock",
                Compliance = ProviderComplianceClass.LocalOnly,
                Enabled = true,
                Priority = 50,
                CostPerInputKToken = 0.5m,
                CostPerOutputKToken = 1.0m,
            };
            db.Providers.AddRange(cheap, expensive);
            await db.SaveChangesAsync();
            cheapId = cheap.Id; expensiveId = expensive.Id;
        }

        try
        {
            using var client = _factory.CreateTenantClient();
            var make = await client.PostAsJsonAsync("/api/reports", new
            {
                modality = "CT",
                bodyPart = "Chest",
                indication = "rule out PE",
                accessionNumber = "ACC-COST-1",
            });
            var id = (await JsonDocument.ParseAsync(await make.Content.ReadAsStreamAsync()))
                .RootElement.GetProperty("id").GetGuid();
            await client.PatchAsJsonAsync($"/api/reports/{id}", new
            {
                indication = "rule out PE",
                technique = "CTA chest.",
                comparison = "None.",
                findings = "Patent pulmonary arteries. Lungs clear.",
                impression = "No PE.",
            });

            // No providerId => auto-route. Must echo routedBy:auto and pick the cheap one.
            var resp = await client.PostAsJsonAsync($"/api/reports/{id}/ai", new { mode = "impression" });
            Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
            var doc = await JsonDocument.ParseAsync(await resp.Content.ReadAsStreamAsync());
            Assert.Equal("auto", doc.RootElement.GetProperty("routedBy").GetString());
            Assert.Equal(cheapId, doc.RootElement.GetProperty("selectedProviderId").GetGuid());
        }
        finally
        {
            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<RadioPadDbContext>();
            var c = await db.Providers.FindAsync(cheapId);
            var e = await db.Providers.FindAsync(expensiveId);
            if (c is not null) db.Providers.Remove(c);
            if (e is not null) db.Providers.Remove(e);
            await db.SaveChangesAsync();
        }
    }

    [Fact]
    public async Task RunAi_With_Explicit_ProviderId_Reports_Manual_Routing()
    {
        Guid pid;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RadioPadDbContext>();
            var p = await db.Providers.FirstAsync(x => x.TenantId == _factory.SeedTenant.Id);
            pid = p.Id;
        }

        using var client = _factory.CreateTenantClient();
        var make = await client.PostAsJsonAsync("/api/reports", new
        {
            modality = "CT",
            bodyPart = "Chest",
            indication = "manual route test",
            accessionNumber = "ACC-COST-2",
        });
        var id = (await JsonDocument.ParseAsync(await make.Content.ReadAsStreamAsync()))
            .RootElement.GetProperty("id").GetGuid();
        await client.PatchAsJsonAsync($"/api/reports/{id}", new
        {
            indication = "x",
            technique = "x",
            comparison = "None.",
            findings = "Lungs clear.",
            impression = "Normal.",
        });

        var resp = await client.PostAsJsonAsync($"/api/reports/{id}/ai", new { mode = "impression", providerId = pid });
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var doc = await JsonDocument.ParseAsync(await resp.Content.ReadAsStreamAsync());
        Assert.Equal("manual", doc.RootElement.GetProperty("routedBy").GetString());
    }
}

public class HallucinationDetectorTests : IClassFixture<RadioPadAppFactory>
{
    private readonly RadioPadAppFactory _factory;
    public HallucinationDetectorTests(RadioPadAppFactory f) => _factory = f;

    [Fact]
    public void Disabled_Detector_Returns_Empty()
    {
        var det = new HallucinationDetector();
        var report = MakeReport(
            findings: "Lungs are clear.",
            impression: "Massive new aortic dissection extends from the arch to the iliacs.");
        var settings = new TenantSettings
        {
            HallucinationDetectionEnabled = false,
            HallucinationMinSupport = 0.9,
            HallucinationSeverity = "Warning",
        };
        Assert.Empty(det.Detect(report, settings));
    }

    [Fact]
    public void Enabled_Detector_Flags_Unsupported_Sentence()
    {
        var det = new HallucinationDetector();
        var report = MakeReport(
            findings: "Lungs are clear. Heart size is normal.",
            impression: "Massive new aortic dissection extends from the arch to the iliacs.");
        var settings = new TenantSettings
        {
            HallucinationDetectionEnabled = true,
            HallucinationMinSupport = 0.5,
            HallucinationSeverity = "Warning",
            HallucinationAllowList = "",
        };
        var hits = det.Detect(report, settings);
        Assert.NotEmpty(hits);
        Assert.Equal("Warning", hits[0].Severity);
    }

    [Fact]
    public void AllowList_Suppresses_Sentence()
    {
        var det = new HallucinationDetector();
        var report = MakeReport(
            findings: "Lungs are clear.",
            impression: "Recommend clinical correlation.");
        var settings = new TenantSettings
        {
            HallucinationDetectionEnabled = true,
            HallucinationMinSupport = 0.9,
            HallucinationSeverity = "Warning",
            HallucinationAllowList = "recommend clinical correlation",
        };
        Assert.Empty(det.Detect(report, settings));
    }

    private static Report MakeReport(string findings, string impression) => new()
    {
        TenantId = Guid.NewGuid(),
        Findings = findings,
        Impression = impression,
        Indication = "",
        Comparison = "",
        Study = new StudyContext { Comparison = "", PriorReportSummary = "" },
    };
}

public class TenantSettingsApiTests : IClassFixture<RadioPadAppFactory>
{
    private readonly RadioPadAppFactory _factory;
    public TenantSettingsApiTests(RadioPadAppFactory f) => _factory = f;

    [Fact]
    public async Task Get_Returns_Defaults_When_Missing()
    {
        using var client = _factory.CreateTenantClient();
        var resp = await client.GetAsync("/api/tenant/settings");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var doc = await JsonDocument.ParseAsync(await resp.Content.ReadAsStreamAsync());
        Assert.True(doc.RootElement.GetProperty("hallucinationDetectionEnabled").GetBoolean());
        Assert.Equal("Warning", doc.RootElement.GetProperty("hallucinationSeverity").GetString());
    }

    [Fact]
    public async Task Save_Forbidden_For_Radiologist()
    {
        using var client = _factory.CreateTenantClient(); // seeded radiologist
        var resp = await client.PostAsJsonAsync("/api/tenant/settings", new
        {
            hallucinationDetectionEnabled = false,
            hallucinationSeverity = "Info",
            hallucinationAllowList = "",
            hallucinationMinSupport = 0.3,
            plan = 0,
            featureFlagsJson = "{}",
        });
        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
    }

    [Fact]
    public async Task Save_Roundtrip_For_Admin()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RadioPadDbContext>();
        var user = await db.Users.FirstAsync(u => u.Id == _factory.SeedUser.Id);
        var original = user.Role;
        user.Role = UserRole.ReportingAdmin;
        await db.SaveChangesAsync();
        try
        {
            using var client = _factory.CreateTenantClient();
            var resp = await client.PostAsJsonAsync("/api/tenant/settings", new
            {
                hallucinationDetectionEnabled = false,
                hallucinationSeverity = "Info",
                hallucinationAllowList = "incidental finding",
                hallucinationMinSupport = 0.2,
                plan = 1,
                featureFlagsJson = "{\"beta\":true}",
            });
            Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

            var get = await client.GetAsync("/api/tenant/settings");
            var doc = await JsonDocument.ParseAsync(await get.Content.ReadAsStreamAsync());
            Assert.False(doc.RootElement.GetProperty("hallucinationDetectionEnabled").GetBoolean());
            Assert.Equal("Info", doc.RootElement.GetProperty("hallucinationSeverity").GetString());
            Assert.Equal(1, doc.RootElement.GetProperty("plan").GetInt32());
        }
        finally
        {
            user.Role = original;
            // Reset settings so other tests see the default-on detector.
            var s = await db.TenantSettings.FirstOrDefaultAsync(x => x.TenantId == _factory.SeedTenant.Id);
            if (s is not null) { db.TenantSettings.Remove(s); }
            await db.SaveChangesAsync();
        }
    }

    [Fact]
    public async Task Save_Partial_IpAllowlist_Does_Not_Reset_Other_Settings()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RadioPadDbContext>();
        var user = await db.Users.FirstAsync(u => u.Id == _factory.SeedUser.Id);
        var original = user.Role;
        user.Role = UserRole.ItAdmin;
        var settings = await db.TenantSettings.FirstOrDefaultAsync(x => x.TenantId == _factory.SeedTenant.Id)
            ?? new TenantSettings { TenantId = _factory.SeedTenant.Id };
        if (db.Entry(settings).State == EntityState.Detached) db.TenantSettings.Add(settings);
        settings.HallucinationDetectionEnabled = false;
        settings.HallucinationSeverity = "Info";
        settings.Plan = TenantPlan.Team;
        await db.SaveChangesAsync();

        try
        {
            using var client = _factory.CreateTenantClient();
            var resp = await client.PostAsJsonAsync("/api/tenant/settings", new
            {
                ipAllowlistJson = "[\"10.0.0.0/8\"]",
            });
            Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

            db.ChangeTracker.Clear();
            var saved = await db.TenantSettings.FirstAsync(x => x.TenantId == _factory.SeedTenant.Id);
            Assert.False(saved.HallucinationDetectionEnabled);
            Assert.Equal("Info", saved.HallucinationSeverity);
            Assert.Equal(TenantPlan.Team, saved.Plan);
            Assert.Equal("[\"10.0.0.0/8\"]", saved.IpAllowlistJson);

            var get = await client.GetAsync("/api/tenant/settings");
            var doc = await JsonDocument.ParseAsync(await get.Content.ReadAsStreamAsync());
            Assert.Equal("[\"10.0.0.0/8\"]", doc.RootElement.GetProperty("ipAllowlistJson").GetString());
        }
        finally
        {
            var cleanupUser = await db.Users.FirstAsync(u => u.Id == _factory.SeedUser.Id);
            cleanupUser.Role = original;
            var s = await db.TenantSettings.FirstOrDefaultAsync(x => x.TenantId == _factory.SeedTenant.Id);
            if (s is not null) db.TenantSettings.Remove(s);
            await db.SaveChangesAsync();
        }
    }

    [Fact]
    public async Task Save_Rejects_Invalid_IpAllowlistJson()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RadioPadDbContext>();
        var user = await db.Users.FirstAsync(u => u.Id == _factory.SeedUser.Id);
        var original = user.Role;
        user.Role = UserRole.ItAdmin;
        await db.SaveChangesAsync();

        try
        {
            using var client = _factory.CreateTenantClient();
            var resp = await client.PostAsJsonAsync("/api/tenant/settings", new
            {
                ipAllowlistJson = "[\"999.0.0.0/8\"]",
            });
            Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        }
        finally
        {
            user.Role = original;
            await db.SaveChangesAsync();
        }
    }
}

public class PdfDocxExportTests : IClassFixture<RadioPadAppFactory>
{
    private readonly RadioPadAppFactory _factory;
    public PdfDocxExportTests(RadioPadAppFactory f) => _factory = f;

    [Fact]
    public async Task Pdf_Conflict_When_Not_Validated()
    {
        using var client = _factory.CreateTenantClient();
        var make = await client.PostAsJsonAsync("/api/reports", new
        {
            modality = "CT",
            bodyPart = "Chest",
            indication = "x",
            accessionNumber = "ACC-PDF-1",
        });
        var id = (await JsonDocument.ParseAsync(await make.Content.ReadAsStreamAsync()))
            .RootElement.GetProperty("id").GetGuid();
        var resp = await client.GetAsync($"/api/reports/{id}/export/pdf");
        Assert.Equal(HttpStatusCode.Conflict, resp.StatusCode);
    }

    [Fact]
    public async Task Pdf_And_Docx_Return_Bytes_When_Acknowledged()
    {
        using var client = _factory.CreateTenantClient();
        var make = await client.PostAsJsonAsync("/api/reports", new
        {
            modality = "CT",
            bodyPart = "Chest",
            indication = "x",
            accessionNumber = "ACC-PDF-2",
        });
        var id = (await JsonDocument.ParseAsync(await make.Content.ReadAsStreamAsync()))
            .RootElement.GetProperty("id").GetGuid();
        await client.PatchAsJsonAsync($"/api/reports/{id}", new
        {
            indication = "x",
            technique = "CT chest with contrast.",
            comparison = "None.",
            findings = "Lungs are clear.",
            impression = "Normal study.",
        });

        // Disable hallucination detector so validation passes cleanly.
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RadioPadDbContext>();
            var s = new TenantSettings { TenantId = _factory.SeedTenant.Id, HallucinationDetectionEnabled = false };
            db.TenantSettings.Add(s);
            await db.SaveChangesAsync();
        }
        try
        {
            await client.PostAsync($"/api/reports/{id}/validate", null);
            var ack = await client.PostAsync($"/api/reports/{id}/acknowledge", null);
            Assert.Equal(HttpStatusCode.OK, ack.StatusCode);

            var pdf = await client.GetAsync($"/api/reports/{id}/export/pdf");
            Assert.Equal(HttpStatusCode.OK, pdf.StatusCode);
            Assert.Equal("application/pdf", pdf.Content.Headers.ContentType?.MediaType);
            var pdfBytes = await pdf.Content.ReadAsByteArrayAsync();
            Assert.True(pdfBytes.Length > 100);
            // PDF magic bytes
            Assert.Equal((byte)'%', pdfBytes[0]);
            Assert.Equal((byte)'P', pdfBytes[1]);

            var docx = await client.GetAsync($"/api/reports/{id}/export/docx");
            Assert.Equal(HttpStatusCode.OK, docx.StatusCode);
            var docxBytes = await docx.Content.ReadAsByteArrayAsync();
            Assert.True(docxBytes.Length > 100);
            // DOCX is a ZIP — magic bytes PK\x03\x04
            Assert.Equal((byte)'P', docxBytes[0]);
            Assert.Equal((byte)'K', docxBytes[1]);
        }
        finally
        {
            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<RadioPadDbContext>();
            var s = await db.TenantSettings.FirstOrDefaultAsync(x => x.TenantId == _factory.SeedTenant.Id);
            if (s is not null) db.TenantSettings.Remove(s);
            await db.SaveChangesAsync();
        }
    }
}


[Collection(RadioPad.Api.Tests.Billing.StripeWebhookEnvCollection.Name)]
public class StripeWebhookTests : IClassFixture<RadioPadAppFactory>
{
    private const string TestSecret = "whsec_radiopad_test_secret";
    private readonly RadioPadAppFactory _factory;
    public StripeWebhookTests(RadioPadAppFactory f) => _factory = f;

    [Fact]
    public async Task Webhook_Rejects_Bad_Signature()
    {
        Environment.SetEnvironmentVariable("STRIPE_WEBHOOK_SECRET", TestSecret);
        try
        {
            using var client = _factory.CreateClient();
            var req = new HttpRequestMessage(HttpMethod.Post, "/api/billing/webhook");
            req.Content = new StringContent("{\"id\":\"evt_x\"}", Encoding.UTF8, "application/json");
            req.Headers.Add("Stripe-Signature", "t=1234,v1=deadbeef");
            var resp = await client.SendAsync(req);
            Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        }
        finally
        {
            Environment.SetEnvironmentVariable("STRIPE_WEBHOOK_SECRET", null);
        }
    }

    [Fact]
    public async Task Webhook_Returns_503_When_Not_Configured()
    {
        Environment.SetEnvironmentVariable("STRIPE_WEBHOOK_SECRET", null);
        using var client = _factory.CreateClient();
        var req = new HttpRequestMessage(HttpMethod.Post, "/api/billing/webhook")
        {
            Content = new StringContent("{}", Encoding.UTF8, "application/json"),
        };
        var resp = await client.SendAsync(req);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, resp.StatusCode);
    }

    [Fact]
    public async Task Webhook_Accepts_Valid_Signature()
    {
        Environment.SetEnvironmentVariable("STRIPE_WEBHOOK_SECRET", TestSecret);
        try
        {
            // Minimal Stripe Event payload � type we ignore so we don't need a
            // full Subscription object; the controller still returns 200.
            var payload = "{\"id\":\"evt_test\",\"object\":\"event\",\"type\":\"ping\",\"request\":{\"id\":\"req_test\",\"idempotency_key\":null},\"data\":{\"object\":{}}}";
            var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var signed = $"{timestamp}.{payload}";
            using var hmac = new System.Security.Cryptography.HMACSHA256(Encoding.UTF8.GetBytes(TestSecret));
            var sig = Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(signed))).ToLowerInvariant();

            using var client = _factory.CreateClient();
            var req = new HttpRequestMessage(HttpMethod.Post, "/api/billing/webhook");
            req.Content = new StringContent(payload, Encoding.UTF8, "application/json");
            req.Headers.Add("Stripe-Signature", $"t={timestamp},v1={sig}");
            var resp = await client.SendAsync(req);
            Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        }
        finally
        {
            Environment.SetEnvironmentVariable("STRIPE_WEBHOOK_SECRET", null);
        }
    }
}
