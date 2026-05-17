using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RadioPad.Domain.Entities;
using RadioPad.Domain.Enums;
using RadioPad.Infrastructure.Identity;
using RadioPad.Infrastructure.Persistence;
using Xunit;

namespace RadioPad.Api.Tests.Integration;

public class EnterpriseIdentityFoundationTests : IClassFixture<RadioPadAppFactory>
{
    private readonly RadioPadAppFactory _factory;

    public EnterpriseIdentityFoundationTests(RadioPadAppFactory factory) => _factory = factory;

    [Fact]
    public async Task FactorySeed_CreatesGlobalIdentityBridge_ForTenantUsers()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RadioPadDbContext>();

        var userCount = await db.Users.CountAsync(u => u.TenantId == _factory.SeedTenant.Id);
        var membershipCount = await db.TenantMemberships.CountAsync(m => m.TenantId == _factory.SeedTenant.Id);
        var legacyIdentityCount = await db.ExternalIdentities.CountAsync(
            e => e.ProviderKey == EnterpriseIdentityBridge.LegacyProviderKey
                && e.Issuer == EnterpriseIdentityBridge.LegacyIssuer);

        Assert.Equal(userCount, membershipCount);
        Assert.Equal(userCount, legacyIdentityCount);
        Assert.Equal(userCount, await db.GlobalUsers.CountAsync());

        var membership = await db.TenantMemberships.AsNoTracking()
            .SingleAsync(m => m.TenantId == _factory.SeedTenant.Id && m.UserId == _factory.SeedUser.Id);
        Assert.Equal("active", membership.Status);
        Assert.True(membership.IsDefault);
    }

    [Fact]
    public async Task SameEmailAcrossTenants_CreatesSeparateGlobalUsers()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RadioPadDbContext>();

        var tenant = new Tenant
        {
            Slug = $"identity-{Guid.NewGuid():N}",
            DisplayName = "Identity Tenant",
        };
        var user = new User
        {
            TenantId = tenant.Id,
            Email = _factory.SeedUser.Email,
            DisplayName = "Same Email Tenant User",
            Role = UserRole.Radiologist,
        };
        db.Tenants.Add(tenant);
        db.Users.Add(user);
        await db.SaveChangesAsync();

        await EnterpriseIdentityBridge.EnsureMembershipForUserAsync(db, user, CancellationToken.None);

        var normalized = EnterpriseIdentityBridge.NormalizeEmail(_factory.SeedUser.Email);
        var globals = await db.GlobalUsers.AsNoTracking()
            .Where(g => g.NormalizedEmail == normalized)
            .ToListAsync();
        Assert.True(globals.Count >= 2);

        var memberships = await db.TenantMemberships.AsNoTracking()
            .Where(m => m.UserId == _factory.SeedUser.Id || m.UserId == user.Id)
            .ToListAsync();
        Assert.Equal(2, memberships.Select(m => m.GlobalUserId).Distinct().Count());
    }

    [Fact]
    public async Task ExternalIdentity_IsUniqueByProviderIssuerAndSubject()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RadioPadDbContext>();

        var globalA = new GlobalUser
        {
            PrimaryEmail = "external-a@radiopad.local",
            NormalizedEmail = "external-a@radiopad.local",
            DisplayName = "External A",
        };
        var globalB = new GlobalUser
        {
            PrimaryEmail = "external-b@radiopad.local",
            NormalizedEmail = "external-b@radiopad.local",
            DisplayName = "External B",
        };
        db.GlobalUsers.AddRange(globalA, globalB);
        db.ExternalIdentities.Add(new ExternalIdentity
        {
            GlobalUserId = globalA.Id,
            ProviderKey = "oidc",
            Issuer = "https://idp.example.test",
            Subject = "sub-123",
            Email = globalA.PrimaryEmail,
            NormalizedEmail = globalA.NormalizedEmail,
        });
        await db.SaveChangesAsync();

        db.ExternalIdentities.Add(new ExternalIdentity
        {
            GlobalUserId = globalB.Id,
            ProviderKey = "oidc",
            Issuer = "https://idp.example.test",
            Subject = "sub-123",
            Email = globalB.PrimaryEmail,
            NormalizedEmail = globalB.NormalizedEmail,
        });

        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task DevSignIn_RecordsAuthSessionWithoutPersistingRawBearer()
    {
        using var http = _factory.CreateClient();

        var response = await http.PostAsJsonAsync("/api/auth/signin", new
        {
            tenant = _factory.SeedTenant.Slug,
            user = _factory.SeedUser.Email,
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var token = body.GetProperty("token").GetString()!;
        var expectedHash = EnterpriseIdentityBridge.Sha256Hex(token);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RadioPadDbContext>();
        var session = await db.AuthSessions.AsNoTracking()
            .OrderByDescending(s => s.CreatedAt)
            .FirstAsync(s => s.TenantId == _factory.SeedTenant.Id && s.UserId == _factory.SeedUser.Id);

        Assert.Equal(expectedHash, session.TokenHash);
        Assert.DoesNotContain(token, session.TokenHash);
        Assert.Equal("dev-header", session.Method);
        Assert.Equal(_factory.SeedUser.SessionEpoch, session.SessionEpochAtIssue);
        Assert.True(session.ExpiresAt > DateTimeOffset.UtcNow);
        Assert.NotEqual(Guid.Empty, session.GlobalUserId);
        Assert.NotNull(session.TenantMembershipId);
    }
}
