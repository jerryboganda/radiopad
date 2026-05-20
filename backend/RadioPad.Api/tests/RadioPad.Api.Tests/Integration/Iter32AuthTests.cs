using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Security.Cryptography.Xml;
using System.Text;
using System.Text.Json;
using System.Xml;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RadioPad.Api.Auth;
using RadioPad.Api.Controllers;
using RadioPad.Api.Tests.Infrastructure;
using RadioPad.Application.Abstractions;
using RadioPad.Domain.Entities;
using Microsoft.AspNetCore.Hosting;
using RadioPad.Domain.Enums;
using RadioPad.Infrastructure.Identity;
using RadioPad.Infrastructure.Persistence;
using Xunit;

namespace RadioPad.Api.Tests.Integration;

/// <summary>
/// Iter-32 AUTH-006 — sliding-window account lockout. Five failed TOTP
/// codes within 15 minutes lock the account; an admin /unlock clears the
/// lock and the failure window. /revoke-sessions bumps SessionEpoch and
/// audits as <see cref="AuditAction.SessionsRevoked"/>.
/// </summary>
public class Iter32AccountLockoutTests : IClassFixture<RadioPadAppFactory>
{
    private readonly RadioPadAppFactory _factory;
    public Iter32AccountLockoutTests(RadioPadAppFactory f) => _factory = f;

    [Fact]
    public async Task FiveBadTotpAttempts_LockTheAccount()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RadioPadDbContext>();
        var lockout = scope.ServiceProvider.GetRequiredService<LockoutPolicy>();

        var user = new User
        {
            TenantId = _factory.SeedTenant.Id,
            Email = $"lock-{Guid.NewGuid():N}@radiopad.local",
            DisplayName = "Lock Test",
            Role = UserRole.Radiologist,
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();

        for (var i = 0; i < LockoutPolicy.MaxAttempts; i++)
        {
            await lockout.OnFailureAsync(user, "totp", default);
        }

        Assert.True(LockoutPolicy.IsLocked(user));
        Assert.Equal(LockoutPolicy.MaxAttempts, user.FailedLoginCount);
        Assert.NotNull(user.LockedUntil);

        var alerts = await db.AuditEvents.AsNoTracking()
            .Where(e => e.TenantId == _factory.SeedTenant.Id
                     && e.UserId == user.Id
                     && e.Action == AuditAction.UserLockedOut)
            .CountAsync();
        Assert.True(alerts >= 1);
    }

    [Fact]
    public async Task SuccessAfterFailures_ClearsCounter()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RadioPadDbContext>();
        var lockout = scope.ServiceProvider.GetRequiredService<LockoutPolicy>();

        var user = new User
        {
            TenantId = _factory.SeedTenant.Id,
            Email = $"clear-{Guid.NewGuid():N}@radiopad.local",
            DisplayName = "Clear Test",
            Role = UserRole.Radiologist,
            FailedLoginCount = 3,
            FailedLoginWindowStart = DateTimeOffset.UtcNow.AddMinutes(-1),
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();

        await lockout.OnSuccessAsync(user, default);

        Assert.Equal(0, user.FailedLoginCount);
        Assert.Null(user.FailedLoginWindowStart);
        Assert.Null(user.LockedUntil);
    }

    [Fact]
    public async Task RevokeSessionsEndpoint_BumpsEpoch_AndAuditsSessionsRevoked()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RadioPadDbContext>();

        var admin = new User
        {
            TenantId = _factory.SeedTenant.Id,
            Email = $"itadmin-{Guid.NewGuid():N}@radiopad.local",
            DisplayName = "Admin",
            Role = UserRole.ItAdmin,
        };
        var target = new User
        {
            TenantId = _factory.SeedTenant.Id,
            Email = $"target-{Guid.NewGuid():N}@radiopad.local",
            DisplayName = "Target",
            Role = UserRole.Radiologist,
            SessionEpoch = 0,
        };
        db.Users.AddRange(admin, target);
        await db.SaveChangesAsync();

        var http = _factory.CreateClient();
        http.DefaultRequestHeaders.Add("X-RadioPad-Tenant", _factory.SeedTenant.Slug);
        http.DefaultRequestHeaders.Add("X-RadioPad-User", admin.Email);

        var resp = await http.PostAsync($"/api/users/{target.Id}/revoke-sessions", content: null);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        var refreshed = await db.Users.AsNoTracking().FirstAsync(u => u.Id == target.Id);
        Assert.Equal(1, refreshed.SessionEpoch);

        var revokedRows = await db.AuditEvents.AsNoTracking()
            .Where(e => e.TenantId == _factory.SeedTenant.Id
                     && e.Action == AuditAction.SessionsRevoked)
            .CountAsync();
        Assert.True(revokedRows >= 1);
    }

    [Fact]
    public async Task UnlockEndpoint_ClearsLockAndCounter()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RadioPadDbContext>();

        var admin = new User
        {
            TenantId = _factory.SeedTenant.Id,
            Email = $"itadmin-unlock-{Guid.NewGuid():N}@radiopad.local",
            DisplayName = "Admin",
            Role = UserRole.ItAdmin,
        };
        var target = new User
        {
            TenantId = _factory.SeedTenant.Id,
            Email = $"locked-{Guid.NewGuid():N}@radiopad.local",
            DisplayName = "Locked",
            Role = UserRole.Radiologist,
            FailedLoginCount = 5,
            LockedUntil = DateTimeOffset.UtcNow.AddMinutes(15),
            IsActive = false,
        };
        db.Users.AddRange(admin, target);
        await db.SaveChangesAsync();

        var http = _factory.CreateClient();
        http.DefaultRequestHeaders.Add("X-RadioPad-Tenant", _factory.SeedTenant.Slug);
        http.DefaultRequestHeaders.Add("X-RadioPad-User", admin.Email);

        var resp = await http.PostAsync($"/api/users/{target.Id}/unlock", content: null);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        var refreshed = await db.Users.AsNoTracking().FirstAsync(u => u.Id == target.Id);
        Assert.True(refreshed.IsActive);
        Assert.Null(refreshed.LockedUntil);
        Assert.Equal(0, refreshed.FailedLoginCount);
    }
}

/// <summary>
/// Iter-32 AUTH-001 — OIDC preset registration. Verifies that
/// <see cref="OidcProfiles"/> emits the documented set and that
/// <see cref="OidcProfiles.ApplyToEnvironment"/> populates env vars
/// without overwriting operator-supplied overrides.
/// </summary>
public class Iter32OidcPresetTests
{
    [Fact]
    public void Resolve_ReturnsKnownPresets()
    {
        Assert.NotNull(OidcProfiles.Resolve("keycloak"));
        Assert.NotNull(OidcProfiles.Resolve("auth0"));
        Assert.NotNull(OidcProfiles.Resolve("OKTA"));
        Assert.Null(OidcProfiles.Resolve("does-not-exist"));
    }

    [Fact]
    public void Apply_FillsDefaults_AndPreservesOperatorOverride()
    {
        var prevTenant = Environment.GetEnvironmentVariable("RADIOPAD_OIDC_TENANT_CLAIM");
        var prevEmail = Environment.GetEnvironmentVariable("RADIOPAD_OIDC_EMAIL_CLAIM");
        var prevMfa = Environment.GetEnvironmentVariable("RADIOPAD_OIDC_REQUIRE_MFA");
        try
        {
            Environment.SetEnvironmentVariable("RADIOPAD_OIDC_TENANT_CLAIM", "custom_tenant");
            Environment.SetEnvironmentVariable("RADIOPAD_OIDC_EMAIL_CLAIM", null);
            Environment.SetEnvironmentVariable("RADIOPAD_OIDC_REQUIRE_MFA", null);

            var profile = OidcProfiles.ApplyToEnvironment("keycloak");
            Assert.NotNull(profile);
            Assert.Equal("custom_tenant", Environment.GetEnvironmentVariable("RADIOPAD_OIDC_TENANT_CLAIM"));
            Assert.Equal("email", Environment.GetEnvironmentVariable("RADIOPAD_OIDC_EMAIL_CLAIM"));
            Assert.Equal("1", Environment.GetEnvironmentVariable("RADIOPAD_OIDC_REQUIRE_MFA"));
        }
        finally
        {
            Environment.SetEnvironmentVariable("RADIOPAD_OIDC_TENANT_CLAIM", prevTenant);
            Environment.SetEnvironmentVariable("RADIOPAD_OIDC_EMAIL_CLAIM", prevEmail);
            Environment.SetEnvironmentVariable("RADIOPAD_OIDC_REQUIRE_MFA", prevMfa);
        }
    }
}

public class VerifiedTenantAccessContextTests : IClassFixture<RadioPadAppFactory>
{
    private readonly RadioPadAppFactory _factory;
    public VerifiedTenantAccessContextTests(RadioPadAppFactory f) => _factory = f;

    [Fact]
    public async Task DevHeaders_StillWork_WhenExplicitlyEnabledForTests()
    {
        var http = _factory.CreateTenantClient();

        var resp = await http.GetAsync("/api/tenant/me");

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
    }

    [Fact]
    public async Task HeadersOnly_AreRejected_WhenDevHeadersDisabled()
    {
        var factory = new NoDevHeadersFactory();
        await factory.InitializeAsync();
        try
        {
            var http = factory.CreateClient();
            http.DefaultRequestHeaders.Add("X-RadioPad-Tenant", factory.SeedTenant.Slug);
            http.DefaultRequestHeaders.Add("X-RadioPad-User", factory.SeedUser.Email);

            var resp = await http.GetAsync("/api/tenant/me");

            Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
        }
        finally
        {
            await factory.DisposeAsync();
        }
    }

    [Fact]
    public async Task ValidRadioPadBearer_ResolvesTenantContext_WhenDevHeadersDisabled()
    {
        var factory = new NoDevHeadersFactory();
        await factory.InitializeAsync();
        try
        {
            var token = await MintSessionBearerAsync(factory, factory.SeedTenant.Slug, factory.SeedUser.Email);
            var http = factory.CreateClient();
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
            http.DefaultRequestHeaders.Add("X-RadioPad-Tenant", factory.SeedTenant.Slug);
            http.DefaultRequestHeaders.Add("X-RadioPad-User", factory.SeedUser.Email);

            var resp = await http.GetAsync("/api/tenant/me");

            Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
            var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal(factory.SeedUser.Email, body.GetProperty("user").GetProperty("email").GetString());
        }
        finally
        {
            await factory.DisposeAsync();
        }
    }

    [Fact]
    public async Task ForgedHeaders_DoNotOverrideValidatedBearer()
    {
        var factory = new NoDevHeadersFactory();
        await factory.InitializeAsync();
        try
        {
            var token = await MintSessionBearerAsync(factory, factory.SeedTenant.Slug, factory.SeedUser.Email);
            var http = factory.CreateClient();
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
            http.DefaultRequestHeaders.Add("X-RadioPad-Tenant", factory.SeedTenant.Slug);
            http.DefaultRequestHeaders.Add("X-RadioPad-User", factory.SeedAdmin.Email);

            var resp = await http.GetAsync("/api/tenant/me");

            Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
        }
        finally
        {
            await factory.DisposeAsync();
        }
    }

    [Fact]
    public async Task CrossTenantBearerReplay_IsRejected()
    {
        var factory = new NoDevHeadersFactory();
        await factory.InitializeAsync();
        try
        {
            var token = await MintSessionBearerAsync(factory, factory.SeedTenant.Slug, factory.SeedUser.Email);
            const string otherTenantSlug = "it-replay-target";
            using (var scope = factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<RadioPadDbContext>();
                var otherTenant = new Tenant { Slug = otherTenantSlug, DisplayName = "Replay Target" };
                db.Tenants.Add(otherTenant);
                db.Users.Add(new User
                {
                    TenantId = otherTenant.Id,
                    Email = factory.SeedUser.Email,
                    DisplayName = "Replay Target Radiologist",
                    Role = UserRole.Radiologist,
                });
                await db.SaveChangesAsync();
            }

            var http = factory.CreateClient();
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
            http.DefaultRequestHeaders.Add("X-RadioPad-Tenant", otherTenantSlug);
            http.DefaultRequestHeaders.Add("X-RadioPad-User", factory.SeedUser.Email);

            var resp = await http.GetAsync("/api/tenant/me");

            Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
        }
        finally
        {
            await factory.DisposeAsync();
        }
    }

    [Fact]
    public async Task RevokedSessionEpoch_InvalidatesExistingBearer()
    {
        var factory = new NoDevHeadersFactory();
        await factory.InitializeAsync();
        try
        {
            var token = await MintSessionBearerAsync(factory, factory.SeedTenant.Slug, factory.SeedUser.Email);

            using (var scope = factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<RadioPadDbContext>();
                var user = await db.Users.FirstAsync(u => u.Id == factory.SeedUser.Id);
                user.SessionEpoch += 1;
                await db.SaveChangesAsync();
            }

            var http = factory.CreateClient();
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
            http.DefaultRequestHeaders.Add("X-RadioPad-Tenant", factory.SeedTenant.Slug);
            http.DefaultRequestHeaders.Add("X-RadioPad-User", factory.SeedUser.Email);

            var resp = await http.GetAsync("/api/tenant/me");

            Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
        }
        finally
        {
            await factory.DisposeAsync();
        }
    }

    [Fact]
    public async Task RevokedAuthSessionRow_InvalidatesMatchingBearer()
    {
        var factory = new NoDevHeadersFactory();
        await factory.InitializeAsync();
        try
        {
            var token = await MintSessionBearerAsync(factory, factory.SeedTenant.Slug, factory.SeedUser.Email);
            using (var scope = factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<RadioPadDbContext>();
                var user = await db.Users.FirstAsync(u => u.Id == factory.SeedUser.Id);
                var session = await EnterpriseIdentityBridge.RecordAuthSessionAsync(
                    db,
                    user,
                    token,
                    "test",
                    DateTimeOffset.UtcNow.Add(RadioPadBearerToken.Lifetime),
                    CancellationToken.None);
                session.RevokedAt = DateTimeOffset.UtcNow;
                session.RevocationReason = "test-revoked";
                await db.SaveChangesAsync();
            }

            var http = factory.CreateClient();
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
            http.DefaultRequestHeaders.Add("X-RadioPad-Tenant", factory.SeedTenant.Slug);
            http.DefaultRequestHeaders.Add("X-RadioPad-User", factory.SeedUser.Email);

            var resp = await http.GetAsync("/api/tenant/me");

            Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
        }
        finally
        {
            await factory.DisposeAsync();
        }
    }

    [Fact]
    public async Task Logout_RevokesCurrentAuthSession_AndClearsSessionCookie()
    {
        using var http = _factory.CreateClient();
        var signIn = await http.PostAsJsonAsync("/api/auth/signin", new
        {
            tenant = _factory.SeedTenant.Slug,
            user = _factory.SeedUser.Email,
        });
        Assert.Equal(HttpStatusCode.OK, signIn.StatusCode);
        Assert.True(signIn.Headers.TryGetValues("Set-Cookie", out var issuedCookies));
        Assert.Contains(issuedCookies, c => c.Contains($"{RadioPadSessionCookies.CookieName}=", StringComparison.Ordinal));

        var body = await signIn.Content.ReadFromJsonAsync<JsonElement>();
        var token = body.GetProperty("token").GetString()!;

        using var logoutClient = _factory.CreateClient();
        logoutClient.DefaultRequestHeaders.Add("Cookie", $"{RadioPadSessionCookies.CookieName}={token}");

        var logout = await logoutClient.PostAsync("/api/auth/logout", content: null);

        Assert.Equal(HttpStatusCode.OK, logout.StatusCode);
        Assert.True(logout.Headers.TryGetValues("Set-Cookie", out var clearedCookies));
        Assert.Contains(clearedCookies, c => c.Contains($"{RadioPadSessionCookies.CookieName}=", StringComparison.Ordinal)
            && c.Contains("expires=", StringComparison.OrdinalIgnoreCase));

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RadioPadDbContext>();
        var tokenHash = EnterpriseIdentityBridge.Sha256Hex(token);
        var session = await db.AuthSessions.AsNoTracking().SingleAsync(s => s.TokenHash == tokenHash);
        Assert.NotNull(session.RevokedAt);
        Assert.Equal("logout", session.RevocationReason);
    }

    [Fact]
    public async Task ExpiredRadioPadBearer_IsRejected()
    {
        var factory = new NoDevHeadersFactory();
        await factory.InitializeAsync();
        try
        {
            var issuedAt = DateTimeOffset.UtcNow.AddHours(-13);
            var token = RadioPadBearerTokens.Mint(
                factory.SeedTenant.Slug,
                factory.SeedUser.Email,
                factory.SeedUser.SessionEpoch,
                now: issuedAt);
            var http = factory.CreateClient();
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
            http.DefaultRequestHeaders.Add("X-RadioPad-Tenant", factory.SeedTenant.Slug);
            http.DefaultRequestHeaders.Add("X-RadioPad-User", factory.SeedUser.Email);

            var resp = await http.GetAsync("/api/tenant/me");

            Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
        }
        finally
        {
            await factory.DisposeAsync();
        }
    }

    [Fact]
    public async Task InactiveOrLockedUsers_AreRejectedEvenWithPreviouslyValidBearer()
    {
        var factory = new NoDevHeadersFactory();
        await factory.InitializeAsync();
        try
        {
            var token = MintBearer(factory, factory.SeedTenant.Slug, factory.SeedUser.Email);

            using (var scope = factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<RadioPadDbContext>();
                var user = await db.Users.FirstAsync(u => u.Id == factory.SeedUser.Id);
                user.IsActive = false;
                user.LockedUntil = DateTimeOffset.UtcNow.AddMinutes(15);
                await db.SaveChangesAsync();
            }

            var http = factory.CreateClient();
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
            http.DefaultRequestHeaders.Add("X-RadioPad-Tenant", factory.SeedTenant.Slug);
            http.DefaultRequestHeaders.Add("X-RadioPad-User", factory.SeedUser.Email);

            var resp = await http.GetAsync("/api/tenant/me");

            Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
        }
        finally
        {
            await factory.DisposeAsync();
        }
    }

    private static string MintBearer(RadioPadAppFactory factory, string tenant, string user) =>
        RadioPadBearerTokens.Mint(tenant, user, factory.SeedUser.SessionEpoch);

    private static async Task<string> MintSessionBearerAsync(RadioPadAppFactory factory, string tenant, string user)
    {
        var token = MintBearer(factory, tenant, user);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RadioPadDbContext>();
        var dbTenant = await db.Tenants.FirstAsync(t => t.Slug == tenant);
        var dbUser = await db.Users.FirstAsync(u => u.TenantId == dbTenant.Id && u.Email == user);
        await EnterpriseIdentityBridge.RecordAuthSessionAsync(
            db,
            dbUser,
            token,
            "test",
            RadioPadBearerTokens.ExpiresAt(DateTimeOffset.UtcNow),
            CancellationToken.None);
        return token;
    }

    private sealed class NoDevHeadersFactory : RadioPadAppFactory
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.UseSetting("RadioPad:DevHeaders", "false");
        }
    }
}

[Collection("MagicLinkRateLimiter")]
public class AuthSurfaceHardeningTests
{
    [Fact]
    public async Task DevSignIn_IsRejected_WhenDevHeadersDisabled()
    {
        var factory = new NoDevHeadersFactory();
        await factory.InitializeAsync();
        try
        {
            var http = factory.CreateClient();

            var resp = await http.PostAsJsonAsync("/api/auth/signin", new
            {
                tenant = factory.SeedTenant.Slug,
                user = factory.SeedUser.Email,
            });

            Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
            using var scope = factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<RadioPadDbContext>();
            var audit = await db.AuditEvents.AsNoTracking()
                .AnyAsync(a => a.TenantId == factory.SeedTenant.Id
                    && a.UserId == factory.SeedUser.Id
                    && a.Action == AuditAction.PolicyViolation
                    && a.DetailsJson.Contains("dev_signin_disabled"));
            Assert.True(audit);
        }
        finally
        {
            await factory.DisposeAsync();
        }
    }

    [Fact]
    public async Task MfaEnroll_RequiresVerifiedIdentity_WhenDevHeadersDisabled()
    {
        var factory = new NoDevHeadersFactory();
        await factory.InitializeAsync();
        try
        {
            var http = factory.CreateClient();

            var resp = await http.PostAsJsonAsync("/api/auth/mfa/enroll", new
            {
                tenant = factory.SeedTenant.Slug,
                email = factory.SeedUser.Email,
            });

            Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
            using var scope = factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<RadioPadDbContext>();
            var user = await db.Users.AsNoTracking().FirstAsync(u => u.Id == factory.SeedUser.Id);
            Assert.True(string.IsNullOrEmpty(user.MfaSecret));
        }
        finally
        {
            await factory.DisposeAsync();
        }
    }

    [Fact]
    public async Task DeviceApprove_RequiresVerifiedIdentity_WhenDevHeadersDisabled()
    {
        var factory = new NoDevHeadersFactory();
        await factory.InitializeAsync();
        try
        {
            var http = factory.CreateClient();
            var auth = await http.PostAsJsonAsync("/api/auth/device/authorize", new { clientId = "test-cli" });
            Assert.Equal(HttpStatusCode.OK, auth.StatusCode);
            var authBody = await auth.Content.ReadFromJsonAsync<JsonElement>();
            var userCode = authBody.GetProperty("userCode").GetString()!;

            var approve = await http.PostAsJsonAsync("/api/auth/device/approve", new
            {
                tenant = factory.SeedTenant.Slug,
                email = factory.SeedUser.Email,
                userCode,
            });

            Assert.Equal(HttpStatusCode.Unauthorized, approve.StatusCode);
            using var scope = factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<RadioPadDbContext>();
            var row = await db.DeviceAuth.AsNoTracking().FirstAsync(d => d.UserCode == userCode);
            Assert.Equal("pending", row.Status);
        }
        finally
        {
            await factory.DisposeAsync();
        }
    }

    [Fact]
    public async Task MagicLinkRequest_DoesNotReturnDevLink_WhenDevHeadersDisabled()
    {
        MagicLinkRateLimiter.ResetForTesting();
        var factory = new NoDevHeadersFactory();
        await factory.InitializeAsync();
        try
        {
            var email = $"no-dev-link-{Guid.NewGuid():N}@radiopad.local";
            using (var scope = factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<RadioPadDbContext>();
                db.Users.Add(new User
                {
                    TenantId = factory.SeedTenant.Id,
                    Email = email,
                    DisplayName = "No Dev Link",
                    Role = UserRole.Radiologist,
                    IsActive = true,
                });
                await db.SaveChangesAsync();
            }

            var http = factory.CreateClient();
            var resp = await http.PostAsJsonAsync("/api/auth/magic-link/request", new
            {
                tenant = factory.SeedTenant.Slug,
                email,
            });

            Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
            var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
            Assert.False(body.TryGetProperty("devLink", out _));
        }
        finally
        {
            await factory.DisposeAsync();
        }
    }

    [Fact]
    public async Task MagicLinkConsume_ForInactiveUser_ConsumesLink()
    {
        MagicLinkRateLimiter.ResetForTesting();
        var factory = new RadioPadAppFactory();
        await factory.InitializeAsync();
        try
        {
            var email = $"inactive-magic-{Guid.NewGuid():N}@radiopad.local";
            using (var scope = factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<RadioPadDbContext>();
                db.Users.Add(new User
                {
                    TenantId = factory.SeedTenant.Id,
                    Email = email,
                    DisplayName = "Inactive Magic",
                    Role = UserRole.Radiologist,
                    IsActive = true,
                });
                await db.SaveChangesAsync();
            }

            var http = factory.CreateTenantClient();
            var request = await http.PostAsJsonAsync("/api/auth/magic-link/request", new
            {
                tenant = factory.SeedTenant.Slug,
                email,
            });
            Assert.Equal(HttpStatusCode.OK, request.StatusCode);
            var requestBody = await request.Content.ReadFromJsonAsync<JsonElement>();
            var url = requestBody.GetProperty("devLink").GetString()!;
            var token = Microsoft.AspNetCore.WebUtilities.QueryHelpers
                .ParseQuery(new Uri(url).Query)["magic"].ToString();

            using (var scope = factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<RadioPadDbContext>();
                var user = await db.Users.FirstAsync(u => u.Email == email);
                user.IsActive = false;
                await db.SaveChangesAsync();
            }

            var consume = await http.PostAsJsonAsync("/api/auth/magic-link/consume", new { token });

            Assert.Equal(HttpStatusCode.Unauthorized, consume.StatusCode);
            using (var scope = factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<RadioPadDbContext>();
                var row = await db.MagicLinks.AsNoTracking().FirstAsync(m => m.TokenHash == MagicLinkController.Sha256Hex(token));
                Assert.NotNull(row.ConsumedAt);
            }
        }
        finally
        {
            await factory.DisposeAsync();
        }
    }

    [Fact]
    public async Task MagicLinkConsume_ConcurrentRequests_OnlyOneSucceeds()
    {
        MagicLinkRateLimiter.ResetForTesting();
        var factory = new RadioPadAppFactory();
        await factory.InitializeAsync();
        try
        {
            var email = $"single-use-magic-{Guid.NewGuid():N}@radiopad.local";
            using (var scope = factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<RadioPadDbContext>();
                db.Users.Add(new User
                {
                    TenantId = factory.SeedTenant.Id,
                    Email = email,
                    DisplayName = "Single Use Magic",
                    Role = UserRole.Radiologist,
                    IsActive = true,
                });
                await db.SaveChangesAsync();
            }

            var http = factory.CreateTenantClient();
            var request = await http.PostAsJsonAsync("/api/auth/magic-link/request", new
            {
                tenant = factory.SeedTenant.Slug,
                email,
            });
            Assert.Equal(HttpStatusCode.OK, request.StatusCode);
            var requestBody = await request.Content.ReadFromJsonAsync<JsonElement>();
            var url = requestBody.GetProperty("devLink").GetString()!;
            var token = Microsoft.AspNetCore.WebUtilities.QueryHelpers
                .ParseQuery(new Uri(url).Query)["magic"].ToString();

            var attempts = await Task.WhenAll(
                http.PostAsJsonAsync("/api/auth/magic-link/consume", new { token }),
                http.PostAsJsonAsync("/api/auth/magic-link/consume", new { token }));

            Assert.Equal(1, attempts.Count(r => r.StatusCode == HttpStatusCode.OK));
            Assert.Equal(1, attempts.Count(r => r.StatusCode == HttpStatusCode.Unauthorized));
        }
        finally
        {
            await factory.DisposeAsync();
        }
    }

    [Fact]
    public async Task DeviceToken_ConcurrentRequests_OnlyOneSucceeds()
    {
        var factory = new RadioPadAppFactory();
        await factory.InitializeAsync();
        try
        {
            var http = factory.CreateTenantClient();
            var authorize = await http.PostAsJsonAsync("/api/auth/device/authorize", new { clientId = "test-cli" });
            Assert.Equal(HttpStatusCode.OK, authorize.StatusCode);
            var authorizeBody = await authorize.Content.ReadFromJsonAsync<JsonElement>();
            var deviceCode = authorizeBody.GetProperty("deviceCode").GetString()!;
            var userCode = authorizeBody.GetProperty("userCode").GetString()!;

            var approve = await http.PostAsJsonAsync("/api/auth/device/approve", new
            {
                tenant = factory.SeedTenant.Slug,
                email = factory.SeedUser.Email,
                userCode,
            });
            Assert.Equal(HttpStatusCode.OK, approve.StatusCode);

            var attempts = await Task.WhenAll(
                http.PostAsJsonAsync("/api/auth/device/token", new
                {
                    grantType = "urn:ietf:params:oauth:grant-type:device_code",
                    deviceCode,
                }),
                http.PostAsJsonAsync("/api/auth/device/token", new
                {
                    grantType = "urn:ietf:params:oauth:grant-type:device_code",
                    deviceCode,
                }));

            Assert.Equal(1, attempts.Count(r => r.StatusCode == HttpStatusCode.OK));
            Assert.Equal(1, attempts.Count(r => r.StatusCode == HttpStatusCode.BadRequest));
        }
        finally
        {
            await factory.DisposeAsync();
        }
    }

    private sealed class NoDevHeadersFactory : RadioPadAppFactory
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.UseSetting("RadioPad:DevHeaders", "false");
        }
    }
}

/// <summary>
/// Iter-32 INT-002 — SAML 2.0 ACS happy-path / failure path. Builds a
/// minimal unsigned SAML response and verifies the controller rejects it
/// when no IdP cert is configured and the assertion is invalid; the
/// metadata endpoint emits well-formed XML containing the SP entity id.
/// </summary>
[Collection(EnvironmentVariableCollection.Name)]
public class Iter32SamlAcsTests : IClassFixture<RadioPadAppFactory>
{
    private readonly RadioPadAppFactory _factory;
    public Iter32SamlAcsTests(RadioPadAppFactory f) => _factory = f;

    [Fact]
    public async Task Metadata_EmitsSpDescriptor()
    {
        var http = _factory.CreateClient();
        var resp = await http.GetAsync("/saml/metadata");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var body = await resp.Content.ReadAsStringAsync();
        Assert.Contains("EntityDescriptor", body);
        Assert.Contains("AssertionConsumerService", body);
    }

    [Fact]
    public async Task Acs_RejectsMissingResponse()
    {
        var http = _factory.CreateClient();
        var form = new FormUrlEncodedContent(new Dictionary<string, string>());
        var resp = await http.PostAsync("/saml/acs", form);
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task Acs_HappyPath_UnsignedAssertion_When_NoIdpCertConfigured()
    {
        // With no RADIOPAD_SAML_IDP_CERT_PEM env var, the controller is
        // fail-CLOSED by default (iter-32 closeout, Momus finding #1). The
        // explicit dev escape hatch RADIOPAD_SAML_DEV_INSECURE=true is the
        // ONLY way to accept an unsigned assertion in tests.
        var prevCert = Environment.GetEnvironmentVariable("RADIOPAD_SAML_IDP_CERT_PEM");
        var prevInsecure = Environment.GetEnvironmentVariable("RADIOPAD_SAML_DEV_INSECURE");
        var prevEnvironment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT");
        try
        {
            Environment.SetEnvironmentVariable("RADIOPAD_SAML_IDP_CERT_PEM", null);
            Environment.SetEnvironmentVariable("RADIOPAD_SAML_DEV_INSECURE", "true");
            Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Development");

            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<RadioPadDbContext>();
            var samlUser = new User
            {
                TenantId = _factory.SeedTenant.Id,
                Email = $"saml-{Guid.NewGuid():N}@radiopad.local",
                DisplayName = "SAML User",
                Role = UserRole.Radiologist,
            };
            db.Users.Add(samlUser);
            await db.SaveChangesAsync();

            var assertion = $@"<?xml version=""1.0""?>
<samlp:Response xmlns:samlp=""urn:oasis:names:tc:SAML:2.0:protocol"" xmlns:saml=""urn:oasis:names:tc:SAML:2.0:assertion"">
  <saml:Assertion>
    <saml:Subject>
      <saml:NameID Format=""urn:oasis:names:tc:SAML:1.1:nameid-format:emailAddress"">{samlUser.Email}</saml:NameID>
    </saml:Subject>
    <saml:AttributeStatement>
      <saml:Attribute Name=""tenant_slug"">
        <saml:AttributeValue>{_factory.SeedTenant.Slug}</saml:AttributeValue>
      </saml:Attribute>
    </saml:AttributeStatement>
  </saml:Assertion>
</samlp:Response>";
            var b64 = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(assertion));
            var http = _factory.CreateClient();
            var form = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("SAMLResponse", b64),
            });
            var resp = await http.PostAsync("/saml/acs", form);
            Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
            var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal(_factory.SeedTenant.Slug, body.GetProperty("tenant").GetString());
            Assert.Equal(samlUser.Email, body.GetProperty("user").GetString());
            Assert.StartsWith("rp_", body.GetProperty("token").GetString());

            var login = await db.AuditEvents.AsNoTracking()
                .Where(e => e.TenantId == _factory.SeedTenant.Id
                         && e.UserId == samlUser.Id
                         && e.Action == AuditAction.UserLogin)
                .ToListAsync();
            Assert.Contains(login, e => e.DetailsJson.Contains("\"saml\""));
        }
        finally
        {
            Environment.SetEnvironmentVariable("RADIOPAD_SAML_IDP_CERT_PEM", prevCert);
            Environment.SetEnvironmentVariable("RADIOPAD_SAML_DEV_INSECURE", prevInsecure);
            Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", prevEnvironment);
        }
    }

    [Fact]
    public async Task Acs_FailClosed_When_NoCert_And_No_DevInsecureFlag()
    {
        // Iter-32 closeout regression test: with neither
        // RADIOPAD_SAML_IDP_CERT_PEM nor RADIOPAD_SAML_DEV_INSECURE set,
        // an otherwise-valid unsigned assertion MUST be rejected. Prevents
        // the fail-open auth bypass flagged by Momus review #1.
        var prevCert = Environment.GetEnvironmentVariable("RADIOPAD_SAML_IDP_CERT_PEM");
        var prevInsecure = Environment.GetEnvironmentVariable("RADIOPAD_SAML_DEV_INSECURE");
        var prevEnvironment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT");
        try
        {
            Environment.SetEnvironmentVariable("RADIOPAD_SAML_IDP_CERT_PEM", null);
            Environment.SetEnvironmentVariable("RADIOPAD_SAML_DEV_INSECURE", null);
            Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Development");

            var assertion = $@"<?xml version=""1.0""?>
<samlp:Response xmlns:samlp=""urn:oasis:names:tc:SAML:2.0:protocol"" xmlns:saml=""urn:oasis:names:tc:SAML:2.0:assertion"">
  <saml:Assertion>
    <saml:Subject>
      <saml:NameID Format=""urn:oasis:names:tc:SAML:1.1:nameid-format:emailAddress"">attacker@example.com</saml:NameID>
    </saml:Subject>
    <saml:AttributeStatement>
      <saml:Attribute Name=""tenant_slug"">
        <saml:AttributeValue>{_factory.SeedTenant.Slug}</saml:AttributeValue>
      </saml:Attribute>
    </saml:AttributeStatement>
  </saml:Assertion>
</samlp:Response>";
            var b64 = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(assertion));
            var http = _factory.CreateClient();
            var form = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("SAMLResponse", b64),
            });
            var resp = await http.PostAsync("/saml/acs", form);
            // Controller returns 401 Unauthorized when ProcessAcs returns null.
            Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
        }
        finally
        {
            Environment.SetEnvironmentVariable("RADIOPAD_SAML_IDP_CERT_PEM", prevCert);
            Environment.SetEnvironmentVariable("RADIOPAD_SAML_DEV_INSECURE", prevInsecure);
                        Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", prevEnvironment);
                }
        }

        [Theory]
        [InlineData("ASPNETCORE_ENVIRONMENT")]
        [InlineData("DOTNET_ENVIRONMENT")]
        public async Task Acs_FailClosed_InProduction_EvenWithDevInsecureFlag(string environmentVariableName)
        {
                var prevCert = Environment.GetEnvironmentVariable("RADIOPAD_SAML_IDP_CERT_PEM");
                var prevInsecure = Environment.GetEnvironmentVariable("RADIOPAD_SAML_DEV_INSECURE");
            var prevAspNetEnvironment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT");
            var prevDotnetEnvironment = Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT");
                try
                {
                        Environment.SetEnvironmentVariable("RADIOPAD_SAML_IDP_CERT_PEM", null);
                        Environment.SetEnvironmentVariable("RADIOPAD_SAML_DEV_INSECURE", "true");
                Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", null);
                Environment.SetEnvironmentVariable("DOTNET_ENVIRONMENT", null);
                Environment.SetEnvironmentVariable(environmentVariableName, "Production");

                        var assertion = $@"<?xml version=""1.0""?>
<samlp:Response xmlns:samlp=""urn:oasis:names:tc:SAML:2.0:protocol"" xmlns:saml=""urn:oasis:names:tc:SAML:2.0:assertion"">
    <saml:Assertion>
        <saml:Subject>
            <saml:NameID Format=""urn:oasis:names:tc:SAML:1.1:nameid-format:emailAddress"">attacker@example.com</saml:NameID>
        </saml:Subject>
        <saml:AttributeStatement>
            <saml:Attribute Name=""tenant_slug"">
                <saml:AttributeValue>{_factory.SeedTenant.Slug}</saml:AttributeValue>
            </saml:Attribute>
        </saml:AttributeStatement>
    </saml:Assertion>
</samlp:Response>";
                        var b64 = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(assertion));
                        var http = _factory.CreateClient();
                        var form = new FormUrlEncodedContent(new[]
                        {
                                new KeyValuePair<string, string>("SAMLResponse", b64),
                        });
                        var resp = await http.PostAsync("/saml/acs", form);

                        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
                }
                finally
                {
                        Environment.SetEnvironmentVariable("RADIOPAD_SAML_IDP_CERT_PEM", prevCert);
                        Environment.SetEnvironmentVariable("RADIOPAD_SAML_DEV_INSECURE", prevInsecure);
                        Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", prevAspNetEnvironment);
                        Environment.SetEnvironmentVariable("DOTNET_ENVIRONMENT", prevDotnetEnvironment);
        }
    }

        [Fact]
        public async Task Acs_RejectsSignedAssertion_WhenSignatureReferenceDoesNotCoverAssertion()
        {
                var prevCert = Environment.GetEnvironmentVariable("RADIOPAD_SAML_IDP_CERT_PEM");
                var prevInsecure = Environment.GetEnvironmentVariable("RADIOPAD_SAML_DEV_INSECURE");
                var prevEnvironment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT");
                try
                {
                        using var rsa = RSA.Create(2048);
                        var request = new CertificateRequest("CN=RadioPad Test SAML IdP", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
                        using var cert = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddHours(1));

                        Environment.SetEnvironmentVariable("RADIOPAD_SAML_IDP_CERT_PEM", ExportCertificatePem(cert));
                        Environment.SetEnvironmentVariable("RADIOPAD_SAML_DEV_INSECURE", null);
                        Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Development");

                        var assertion = BuildSignedSamlResponse(
                                _factory.SeedUser.Email,
                                _factory.SeedTenant.Slug,
                                cert,
                                "#_signed-conditions");
                        var b64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(assertion));
                        var http = _factory.CreateClient();
                        var form = new FormUrlEncodedContent(new[]
                        {
                                new KeyValuePair<string, string>("SAMLResponse", b64),
                        });

                        var resp = await http.PostAsync("/saml/acs", form);

                        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
                }
                finally
                {
                        Environment.SetEnvironmentVariable("RADIOPAD_SAML_IDP_CERT_PEM", prevCert);
                        Environment.SetEnvironmentVariable("RADIOPAD_SAML_DEV_INSECURE", prevInsecure);
                        Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", prevEnvironment);
                }
        }

        private static string BuildSignedSamlResponse(string email, string tenantSlug, X509Certificate2 cert, string referenceUri)
        {
                var notBefore = DateTimeOffset.UtcNow.AddMinutes(-1).ToString("O");
                var notOnOrAfter = DateTimeOffset.UtcNow.AddMinutes(5).ToString("O");
                var xml = $@"<?xml version=""1.0""?>
<samlp:Response xmlns:samlp=""urn:oasis:names:tc:SAML:2.0:protocol"" xmlns:saml=""urn:oasis:names:tc:SAML:2.0:assertion"">
    <saml:Assertion ID=""_assertion"">
        <saml:Issuer>test-idp</saml:Issuer>
        <saml:Subject>
            <saml:NameID Format=""urn:oasis:names:tc:SAML:1.1:nameid-format:emailAddress"">{email}</saml:NameID>
        </saml:Subject>
        <saml:Conditions ID=""_signed-conditions"" NotBefore=""{notBefore}"" NotOnOrAfter=""{notOnOrAfter}"">
            <saml:AudienceRestriction>
                <saml:Audience>https://radiopad.local/saml</saml:Audience>
            </saml:AudienceRestriction>
        </saml:Conditions>
        <saml:AttributeStatement>
            <saml:Attribute Name=""tenant_slug"">
                <saml:AttributeValue>{tenantSlug}</saml:AttributeValue>
            </saml:Attribute>
        </saml:AttributeStatement>
    </saml:Assertion>
</samlp:Response>";

                var doc = new XmlDocument { PreserveWhitespace = true };
                doc.LoadXml(xml);
                var assertion = (XmlElement)doc.GetElementsByTagName("Assertion", "urn:oasis:names:tc:SAML:2.0:assertion")[0]!;
                var signed = new SignedXml(assertion)
                {
                        SigningKey = cert.GetRSAPrivateKey(),
                };
                signed.SignedInfo.CanonicalizationMethod = SignedXml.XmlDsigExcC14NTransformUrl;
                var reference = new Reference(referenceUri);
                reference.AddTransform(new XmlDsigEnvelopedSignatureTransform());
                reference.AddTransform(new XmlDsigExcC14NTransform());
                signed.AddReference(reference);
                var keyInfo = new KeyInfo();
                keyInfo.AddClause(new KeyInfoX509Data(cert));
                signed.KeyInfo = keyInfo;
                signed.ComputeSignature();

                var importedSignature = doc.ImportNode(signed.GetXml(), true);
                var issuer = assertion.GetElementsByTagName("Issuer", "urn:oasis:names:tc:SAML:2.0:assertion")[0];
                assertion.InsertAfter(importedSignature, issuer);
                return doc.OuterXml;
        }

        private static string ExportCertificatePem(X509Certificate2 cert) =>
                PemEncoding.WriteString("CERTIFICATE", cert.Export(X509ContentType.Cert));
}

/// <summary>
/// Iter-32 AUTH-001 — WebAuthn registration + signin happy paths.
/// The integration deliberately does not exercise full attestation
/// verification (deferred to a follow-up); it asserts that the option
/// envelopes are well-formed and that registering then listing surfaces
/// the credential to the operator.
/// </summary>
public class Iter32WebAuthnFlowTests : IClassFixture<RadioPadAppFactory>
{
    private readonly RadioPadAppFactory _factory;
    public Iter32WebAuthnFlowTests(RadioPadAppFactory f) => _factory = f;

    [Fact]
    public async Task RegisterOptions_ReturnsChallengeAndRpId()
    {
        var http = _factory.CreateTenantClient();
        var resp = await http.PostAsJsonAsync("/api/auth/webauthn/register-options", new { label = "yubikey" });
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(string.IsNullOrEmpty(body.GetProperty("challenge").GetString()));
        Assert.False(string.IsNullOrEmpty(body.GetProperty("rp").GetProperty("id").GetString()));
    }

    [Fact]
    public async Task Register_ThenList_ShowsCredential()
    {
        var http = _factory.CreateTenantClient();
        var (attObj, clientData) = RadioPad.Api.Tests.Iter33.WebAuthnTestVectors.NoneAttestation();
        var register = await http.PostAsJsonAsync("/api/auth/webauthn/register", new
        {
            attestationObject = attObj,
            clientDataJson = clientData,
            label = "Integration",
        });
        Assert.Equal(HttpStatusCode.OK, register.StatusCode);

        var list = await http.GetFromJsonAsync<List<JsonElement>>("/api/auth/webauthn/credentials");
        Assert.NotNull(list);
        Assert.Contains(list!, c => c.GetProperty("label").GetString() == "Integration");
    }

    [Fact]
    public async Task SignIn_WithUnknownCredential_FailsAndAccrues()
    {
        var http = _factory.CreateTenantClient();
        var resp = await http.PostAsJsonAsync("/api/auth/webauthn/signin", new
        {
            credentialId = "definitely-not-registered",
            clientDataJson = "{}",
            authenticatorData = "AA==",
            signature = "AA==",
            signCount = 1u,
        });
        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }
}

internal static class JsonElementHelpers
{
}
