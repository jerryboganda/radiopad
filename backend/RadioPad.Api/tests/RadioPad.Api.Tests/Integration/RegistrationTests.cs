using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RadioPad.Api.Controllers;
using RadioPad.Domain.Enums;
using RadioPad.Infrastructure.Persistence;
using Xunit;

namespace RadioPad.Api.Tests.Integration;

/// <summary>
/// Self-serve SaaS onboarding — <c>POST /api/registration/create-organization</c>
/// creates a tenant + first admin + settings and mints a magic link. In the
/// Testing host (dev headers on, no SMTP) the raw link is returned as
/// <c>devLink</c> so the test can complete the passwordless loop through the
/// existing <c>/api/auth/magic-link/consume</c> endpoint.
/// </summary>
[Collection("OrgCreationSerial")]
public sealed class RegistrationTests : IClassFixture<RadioPadAppFactory>
{
    private readonly RadioPadAppFactory _factory;

    public RegistrationTests(RadioPadAppFactory factory)
    {
        _factory = factory;
        MagicLinkRateLimiter.ResetForTesting();
    }

    [Fact]
    public async Task CreateOrganization_Provisions_Tenant_Admin_Settings_And_Working_MagicLink()
    {
        using var c = _factory.CreateClient();
        var slug = "acme-radiology";

        var res = await c.PostAsJsonAsync("/api/registration/create-organization", new
        {
            organizationName = "Acme Radiology",
            slug,
            adminEmail = "founder@acme-radiology.example",
            adminName = "Dr. Founder",
        });
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);

        var body = await res.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(body.GetProperty("ok").GetBoolean());
        Assert.Equal(slug, body.GetProperty("slug").GetString());
        Assert.True(body.TryGetProperty("devLink", out var devLinkEl), "devLink should be returned in the Testing host");
        var devLink = devLinkEl.GetString()!;

        // Tenant + admin + settings landed.
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RadioPadDbContext>();
            var tenant = await db.Tenants.SingleAsync(t => t.Slug == slug);
            Assert.Equal("Acme Radiology", tenant.DisplayName);
            var admin = await db.Users.SingleAsync(u => u.TenantId == tenant.Id);
            Assert.Equal("founder@acme-radiology.example", admin.Email);
            Assert.True(admin.IsActive);
            Assert.Equal(UserRole.MedicalDirector, admin.Role);
            Assert.True(await db.TenantSettings.AnyAsync(s => s.TenantId == tenant.Id));
            Assert.True(await db.AuditEvents.AnyAsync(e => e.TenantId == tenant.Id && e.Action == AuditAction.OrganizationCreated));
        }

        // The emailed link completes the passwordless loop.
        var magic = ExtractQueryParam(devLink, "magic");
        Assert.False(string.IsNullOrWhiteSpace(magic));
        var consume = await c.PostAsJsonAsync("/api/auth/magic-link/consume", new { token = magic });
        Assert.Equal(HttpStatusCode.OK, consume.StatusCode);
        var session = await consume.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(slug, session.GetProperty("tenant").GetString());
        Assert.False(string.IsNullOrWhiteSpace(session.GetProperty("token").GetString()));
    }

    [Fact]
    public async Task CreateOrganization_DuplicateSlug_Returns_409()
    {
        using var c = _factory.CreateClient();
        var slug = "dup-clinic";

        var first = await c.PostAsJsonAsync("/api/registration/create-organization", new
        {
            organizationName = "Dup Clinic",
            slug,
            adminEmail = "a@dup-clinic.example",
        });
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        var second = await c.PostAsJsonAsync("/api/registration/create-organization", new
        {
            organizationName = "Dup Clinic Two",
            slug,
            adminEmail = "b@dup-clinic.example",
        });
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        var body = await second.Content.ReadAsStringAsync();
        Assert.Contains("\"kind\":\"slug_taken\"", body);
    }

    [Fact]
    public async Task CreateOrganization_InvalidSlug_Returns_400()
    {
        using var c = _factory.CreateClient();
        var res = await c.PostAsJsonAsync("/api/registration/create-organization", new
        {
            organizationName = "Bad Slug Co",
            slug = "Has Spaces!",
            adminEmail = "x@bad.example",
        });
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.Contains("\"kind\":\"validation\"", await res.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task CreateOrganization_Respects_Disable_Flag()
    {
        var prev = Environment.GetEnvironmentVariable("RADIOPAD_ALLOW_SELF_SIGNUP");
        Environment.SetEnvironmentVariable("RADIOPAD_ALLOW_SELF_SIGNUP", "false");
        try
        {
            using var c = _factory.CreateClient();
            var res = await c.PostAsJsonAsync("/api/registration/create-organization", new
            {
                organizationName = "Disabled Co",
                slug = "disabled-co",
                adminEmail = "x@disabled.example",
            });
            Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
            Assert.Contains("\"kind\":\"signup_disabled\"", await res.Content.ReadAsStringAsync());
        }
        finally
        {
            Environment.SetEnvironmentVariable("RADIOPAD_ALLOW_SELF_SIGNUP", prev);
        }
    }

    private static string? ExtractQueryParam(string url, string key)
    {
        var query = new Uri(url).Query.TrimStart('?');
        foreach (var pair in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var eq = pair.IndexOf('=');
            if (eq <= 0) continue;
            if (Uri.UnescapeDataString(pair[..eq]) == key)
                return Uri.UnescapeDataString(pair[(eq + 1)..]);
        }
        return null;
    }
}
