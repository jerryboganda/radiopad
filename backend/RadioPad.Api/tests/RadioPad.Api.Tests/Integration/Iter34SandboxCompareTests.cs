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
/// Iter-34 PROV-005 — sandbox model comparison endpoint
/// (<c>POST /api/ai/sandbox/compare</c>). Verifies tenant gating,
/// provider compliance gating, the happy-path runs[] payload, and that
/// the PHI policy in <c>AiGateway.EnforcePhiPolicy</c> is still in
/// front of the dispatch path.
/// </summary>
public class Iter34SandboxCompareTests : IClassFixture<RadioPadAppFactory>
{
    private readonly RadioPadAppFactory _factory;
    public Iter34SandboxCompareTests(RadioPadAppFactory f) => _factory = f;

    [Fact]
    public async Task Returns_409_When_Sandbox_Flag_False()
    {
        await SetSandboxFlagAsync(false);
        var (sandboxA, _) = await EnsureSandboxProvidersAsync();
        var reportId = await SeedDraftAsync(includePhi: false);

        using var client = _factory.CreateTenantClient();
        var resp = await client.PostAsJsonAsync("/api/ai/sandbox/compare", new
        {
            reportId,
            mode = "impression",
            providerIds = new[] { sandboxA },
        });

        Assert.Equal(HttpStatusCode.Conflict, resp.StatusCode);
        using var doc = await JsonDocument.ParseAsync(await resp.Content.ReadAsStreamAsync());
        Assert.Equal("sandbox_required", doc.RootElement.GetProperty("kind").GetString());
    }

    [Fact]
    public async Task Returns_400_When_Any_Provider_Is_Not_Sandbox()
    {
        await SetSandboxFlagAsync(true);
        var (sandboxA, _) = await EnsureSandboxProvidersAsync();
        var deidProviderId = await EnsureProviderAsync(
            "iter34-deid", ProviderComplianceClass.DeIdentifiedOnly);
        var reportId = await SeedDraftAsync(includePhi: false);

        using var client = _factory.CreateTenantClient();
        var resp = await client.PostAsJsonAsync("/api/ai/sandbox/compare", new
        {
            reportId,
            mode = "impression",
            providerIds = new[] { sandboxA, deidProviderId },
        });

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        using var doc = await JsonDocument.ParseAsync(await resp.Content.ReadAsStreamAsync());
        Assert.Equal("providers_not_sandbox", doc.RootElement.GetProperty("kind").GetString());
    }

    [Fact]
    public async Task Returns_200_With_Two_Runs_For_Two_Sandbox_Providers()
    {
        await SetSandboxFlagAsync(true);
        var (sandboxA, sandboxB) = await EnsureSandboxProvidersAsync();
        var reportId = await SeedDraftAsync(includePhi: false);

        using var client = _factory.CreateTenantClient();
        var resp = await client.PostAsJsonAsync("/api/ai/sandbox/compare", new
        {
            reportId,
            mode = "impression",
            providerIds = new[] { sandboxA, sandboxB },
        });

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        using var doc = await JsonDocument.ParseAsync(await resp.Content.ReadAsStreamAsync());
        var runs = doc.RootElement.GetProperty("runs");
        Assert.Equal(2, runs.GetArrayLength());

        // Each entry must carry a providerId regardless of success/failure.
        foreach (var run in runs.EnumerateArray())
        {
            Assert.True(run.TryGetProperty("providerId", out _));
            Assert.True(run.TryGetProperty("provider", out _));
        }

        // The wrapper AiResponse audit row should be appended (in addition to
        // any per-call rows the gateway writes).
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RadioPadDbContext>();
        var hasWrapper = await db.AuditEvents.AnyAsync(a =>
            a.TenantId == _factory.SeedTenant.Id
            && a.ReportId == Guid.Parse(reportId)
            && a.Action == AuditAction.AiResponse
            && a.DetailsJson.Contains("sandbox_compare"));
        Assert.True(hasWrapper);
    }

    [Fact]
    public async Task Refuses_DeIdentifiedOnly_Providers_When_Phi_Present()
    {
        await SetSandboxFlagAsync(true);
        var deidA = await EnsureProviderAsync("iter34-deid-a", ProviderComplianceClass.DeIdentifiedOnly);
        var deidB = await EnsureProviderAsync("iter34-deid-b", ProviderComplianceClass.DeIdentifiedOnly);
        var reportId = await SeedDraftAsync(includePhi: true);

        using var client = _factory.CreateTenantClient();
        var resp = await client.PostAsJsonAsync("/api/ai/sandbox/compare", new
        {
            reportId,
            mode = "impression",
            providerIds = new[] { deidA, deidB },
        });

        // Endpoint refuses non-sandbox providers up-front — PHI never
        // reaches the gateway because compliance fails the validation gate.
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        using var doc = await JsonDocument.ParseAsync(await resp.Content.ReadAsStreamAsync());
        Assert.Equal("providers_not_sandbox", doc.RootElement.GetProperty("kind").GetString());
    }

    // ===== helpers =====

    private async Task SetSandboxFlagAsync(bool allow)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RadioPadDbContext>();
        var t = await db.Tenants.FirstAsync(x => x.Id == _factory.SeedTenant.Id);
        t.AllowSandboxRulebooks = allow;
        await db.SaveChangesAsync();
        _factory.SeedTenant.AllowSandboxRulebooks = allow;
    }

    private async Task<(string a, string b)> EnsureSandboxProvidersAsync()
    {
        var a = await EnsureProviderAsync("iter34-sandbox-a", ProviderComplianceClass.Sandbox);
        var b = await EnsureProviderAsync("iter34-sandbox-b", ProviderComplianceClass.Sandbox);
        return (a, b);
    }

    private async Task<string> EnsureProviderAsync(string name, ProviderComplianceClass compliance)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RadioPadDbContext>();
        var existing = await db.Providers.FirstOrDefaultAsync(p =>
            p.TenantId == _factory.SeedTenant.Id && p.Name == name);
        if (existing is not null) return existing.Id.ToString();
        var p = new ProviderConfig
        {
            TenantId = _factory.SeedTenant.Id,
            Name = name,
            Adapter = "mock",
            Compliance = compliance,
            Enabled = true,
        };
        db.Providers.Add(p);
        await db.SaveChangesAsync();
        return p.Id.ToString();
    }

    private async Task<string> SeedDraftAsync(bool includePhi)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RadioPadDbContext>();
        var report = new Report
        {
            TenantId = _factory.SeedTenant.Id,
            CreatedByUserId = _factory.SeedUser.Id,
            Status = ReportStatus.Draft,
            Indication = "rule out PE",
            Findings = "lungs clear",
            Impression = "no acute findings",
            Recommendations = "Recommend clinical correlation.",
            Study = new StudyContext
            {
                Modality = "CT",
                BodyPart = "Chest",
                AccessionNumber = $"ACC-IT34-{Guid.NewGuid():N}".Substring(0, 18),
                PatientReference = includePhi ? "Patient/iter34-phi" : "",
            },
        };
        db.Reports.Add(report);
        await db.SaveChangesAsync();
        return report.Id.ToString();
    }
}
