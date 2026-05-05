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
/// Iter-35 i18n — covers tenant default locale + per-user override.
/// (a) Radiologist may read but not write the tenant default;
/// (b) IT-Admin can write the tenant default;
/// (c) only the supported set [en, es, de, fr, pt, hi] is accepted;
/// (d) per-user locale override is settable by any tenant member;
/// (e) tenant isolation: writing tenant A's locale must not leak into
/// tenant B.
/// </summary>
public class Iter35LocaleTests : IClassFixture<RadioPadAppFactory>
{
    private readonly RadioPadAppFactory _factory;
    public Iter35LocaleTests(RadioPadAppFactory f) => _factory = f;

    [Fact]
    public async Task Get_TenantLocale_DefaultsToEnglish()
    {
        var client = _factory.CreateTenantClient();
        var resp = await client.GetAsync("/api/tenant/settings/locale");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        Assert.Equal("en", doc.RootElement.GetProperty("locale").GetString());
        var supported = doc.RootElement.GetProperty("supported")
            .EnumerateArray().Select(e => e.GetString()).ToArray();
        Assert.Contains("es", supported);
        Assert.Contains("hi", supported);
    }

    [Fact]
    public async Task Put_TenantLocale_RequiresAdminRole()
    {
        var rad = _factory.CreateTenantClient();
        var resp = await rad.PutAsJsonAsync("/api/tenant/settings/locale", new { locale = "es" });
        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
    }

    [Fact]
    public async Task Put_TenantLocale_RejectsUnsupportedTag()
    {
        var admin = _factory.CreateAdminClient();
        var resp = await admin.PutAsJsonAsync("/api/tenant/settings/locale", new { locale = "klingon" });
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        Assert.Equal("validation", doc.RootElement.GetProperty("kind").GetString());
    }

    [Fact]
    public async Task Put_TenantLocale_PersistsAcceptedTag()
    {
        var admin = _factory.CreateAdminClient();
        var resp = await admin.PutAsJsonAsync("/api/tenant/settings/locale", new { locale = "DE" });
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        var rad = _factory.CreateTenantClient();
        var read = await rad.GetAsync("/api/tenant/settings/locale");
        using var doc = JsonDocument.Parse(await read.Content.ReadAsStringAsync());
        Assert.Equal("de", doc.RootElement.GetProperty("locale").GetString());

        // Reset so other tests on the shared seed see "en" again.
        await admin.PutAsJsonAsync("/api/tenant/settings/locale", new { locale = "en" });
    }

    [Fact]
    public async Task Put_UserLocale_AllowsAnyMember_AndAcceptsNullToClear()
    {
        var rad = _factory.CreateTenantClient();
        var set = await rad.PutAsJsonAsync("/api/users/me/locale", new { locale = "fr" });
        Assert.Equal(HttpStatusCode.OK, set.StatusCode);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RadioPadDbContext>();
            var u = await db.Users.FirstAsync(x => x.Id == _factory.SeedUser.Id);
            Assert.Equal("fr", u.PreferredLocale);
        }

        var clear = await rad.PutAsJsonAsync("/api/users/me/locale", new { locale = (string?)null });
        Assert.Equal(HttpStatusCode.OK, clear.StatusCode);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RadioPadDbContext>();
            var u = await db.Users.FirstAsync(x => x.Id == _factory.SeedUser.Id);
            Assert.Null(u.PreferredLocale);
        }
    }

    [Fact]
    public async Task Put_UserLocale_RejectsUnsupportedTag()
    {
        var rad = _factory.CreateTenantClient();
        var resp = await rad.PutAsJsonAsync("/api/users/me/locale", new { locale = "xx" });
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task Put_TenantLocale_DoesNotLeakAcrossTenants()
    {
        // Seed an isolated second tenant with its own admin user.
        var otherTenantId = Guid.NewGuid();
        var slug = $"loc-iso-{otherTenantId:N}";
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RadioPadDbContext>();
            db.Tenants.Add(new Tenant { Id = otherTenantId, Slug = slug, DisplayName = "Iso" });
            db.Users.Add(new User
            {
                TenantId = otherTenantId,
                Email = $"admin-{otherTenantId:N}@radiopad.local",
                DisplayName = "Iso Admin",
                Role = UserRole.ItAdmin,
            });
            await db.SaveChangesAsync();
        }

        // Tenant A admin sets locale=pt on the seed tenant.
        var adminA = _factory.CreateAdminClient();
        var setA = await adminA.PutAsJsonAsync("/api/tenant/settings/locale", new { locale = "pt" });
        Assert.Equal(HttpStatusCode.OK, setA.StatusCode);

        // Tenant B reads its own (default en, unaffected).
        var clientB = _factory.CreateClient();
        clientB.DefaultRequestHeaders.Add("X-RadioPad-Tenant", slug);
        clientB.DefaultRequestHeaders.Add("X-RadioPad-User", $"admin-{otherTenantId:N}@radiopad.local");
        var readB = await clientB.GetAsync("/api/tenant/settings/locale");
        using var docB = JsonDocument.Parse(await readB.Content.ReadAsStringAsync());
        Assert.Equal("en", docB.RootElement.GetProperty("locale").GetString());

        // Reset shared seed tenant.
        await adminA.PutAsJsonAsync("/api/tenant/settings/locale", new { locale = "en" });
    }
}
