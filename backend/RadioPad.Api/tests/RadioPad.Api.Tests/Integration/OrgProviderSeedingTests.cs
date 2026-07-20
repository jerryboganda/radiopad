using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RadioPad.Api.Controllers;
using RadioPad.Infrastructure.Persistence;
using Xunit;

namespace RadioPad.Api.Tests.Integration;

/// <summary>
/// A brand-new organization must surface the curated UBAG models (Gemini +
/// DeepSeek) on its AI-models page the moment it is created — not only the dev
/// org. This drives the regression where production orgs created via the real
/// org-creation pipeline started with zero providers.
/// </summary>
// Serialized with RegistrationTests: that class flips the process-global
// RADIOPAD_ALLOW_SELF_SIGNUP env var to test the disabled path, which would
// otherwise race this class's create-organization call (intermittent 403).
[Collection("OrgCreationSerial")]
public sealed class OrgProviderSeedingTests : IClassFixture<RadioPadAppFactory>
{
    private readonly RadioPadAppFactory _factory;

    public OrgProviderSeedingTests(RadioPadAppFactory factory)
    {
        _factory = factory;
        MagicLinkRateLimiter.ResetForTesting();
    }

    [Fact]
    public async Task CreateOrganization_Seeds_Curated_Ubag_Primaries()
    {
        using var c = _factory.CreateClient();
        var slug = "ubag-seed-clinic";

        var res = await c.PostAsJsonAsync("/api/registration/create-organization", new
        {
            organizationName = "UBAG Seed Clinic",
            slug,
            adminEmail = "founder@ubag-seed-clinic.example",
        });
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RadioPadDbContext>();
        var tenant = await db.Tenants.SingleAsync(t => t.Slug == slug);

        var ubag = await db.Providers
            .Where(p => p.TenantId == tenant.Id && p.Adapter == "ubag")
            .OrderBy(p => p.Priority)
            .ToListAsync();

        Assert.Equal(2, ubag.Count);
        Assert.Equal("gemini_web", ubag[0].Model);
        Assert.Equal("deepseek_web", ubag[1].Model);
        Assert.All(ubag, p => Assert.True(p.Enabled));
    }
}
