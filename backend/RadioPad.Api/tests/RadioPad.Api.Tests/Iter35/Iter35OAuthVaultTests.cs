using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RadioPad.Api.Services;
using RadioPad.Application.Abstractions;
using RadioPad.Application.Services;
using RadioPad.Application.Services.Kms;
using RadioPad.Domain.Entities;
using RadioPad.Domain.Enums;
using RadioPad.Infrastructure.Persistence;
using Xunit;

namespace RadioPad.Api.Tests.Iter35;

/// <summary>
/// Iter-35 PROV-007 — exercises the OAuth refresh-token vault end to end:
/// crypto round-trip with an in-process KMS, the admin HTTP surface
/// (save / delete / status — never returns ciphertext), RBAC gating,
/// tenant isolation, append-only audit, and the
/// <see cref="OAuthRefreshRotationService"/> scan loop driven by a
/// fake <see cref="IOAuthTokenIssuer"/>.
/// </summary>
[Collection(RadioPad.Api.Tests.Infrastructure.EnvironmentVariableCollection.Name)]
public class Iter35OAuthVaultTests
{
    private const string KekEnvName = "RADIOPAD_TENANT_KEK_DEFAULT";

    private static void EnsureKekEnv()
    {
        var existing = Environment.GetEnvironmentVariable(KekEnvName);
        if (!string.IsNullOrEmpty(existing)) return;
        var key = RandomNumberGenerator.GetBytes(32);
        Environment.SetEnvironmentVariable(KekEnvName, Convert.ToBase64String(key));
    }

    // ------------------------------------------------------------------
    // 1. Crypto round-trip — direct vault unit test (no HTTP).
    // ------------------------------------------------------------------
    [Fact]
    public async Task SaveLoadRoundTrip_ReturnsOriginalToken_AndZeroesOnDelete()
    {
        EnsureKekEnv();
        var resolver = new DefaultKmsResolver(new IKmsProvider[] { new EnvKmsProvider() });
        var vault = new OAuthRefreshVault(resolver);

        var tenant = new Tenant { Id = Guid.NewGuid(), Slug = "round-trip" };
        var provider = new ProviderConfig { Id = Guid.NewGuid(), TenantId = tenant.Id, Name = "p" };
        var keyRef = OAuthRefreshVault.ResolveKekRef(null);

        const string token = "rt_abc123_super_secret_value";
        await vault.SaveAsync(tenant, provider, keyRef, token, DateTimeOffset.UtcNow.AddDays(30), "before_expiry", default);

        Assert.NotNull(provider.OAuthRefreshTokenEnc);
        Assert.NotNull(provider.OAuthRefreshTokenIv);
        Assert.NotNull(provider.OAuthRefreshTokenTag);
        Assert.NotNull(provider.OAuthRefreshTokenWrappedDek);
        // Ciphertext never equals plaintext (sanity check).
        Assert.NotEqual(System.Text.Encoding.UTF8.GetBytes(token), provider.OAuthRefreshTokenEnc);

        var loaded = await vault.LoadAsync(tenant, provider, keyRef, default);
        Assert.Equal(token, loaded);

        vault.Delete(provider);
        Assert.Null(provider.OAuthRefreshTokenEnc);
        Assert.Null(provider.OAuthRefreshTokenIv);
        Assert.Null(provider.OAuthRefreshTokenTag);
        Assert.Null(provider.OAuthRefreshTokenWrappedDek);
        Assert.Null(provider.OAuthRefreshTokenUpdatedAt);
        Assert.Null(provider.OAuthRefreshTokenExpiresAt);

        Assert.Null(await vault.LoadAsync(tenant, provider, keyRef, default));
    }

    [Fact]
    public void ShouldRotate_RespectsPolicy()
    {
        var now = DateTimeOffset.UtcNow;
        var p = new ProviderConfig
        {
            OAuthRefreshTokenEnc = new byte[] { 1 },
            OAuthRefreshTokenIv = new byte[] { 2 },
            OAuthRefreshTokenTag = new byte[] { 3 },
            OAuthRefreshTokenWrappedDek = new byte[] { 4 },
            OAuthRefreshTokenUpdatedAt = now.AddHours(-1),
        };

        // never → false even when expired.
        p.OAuthRefreshTokenRotationPolicy = "never";
        p.OAuthRefreshTokenExpiresAt = now.AddMinutes(-5);
        Assert.False(OAuthRefreshVault.ShouldRotate(p, now));

        // before_expiry — rotates within 1h of expiry.
        p.OAuthRefreshTokenRotationPolicy = "before_expiry";
        p.OAuthRefreshTokenExpiresAt = now.AddMinutes(30);
        Assert.True(OAuthRefreshVault.ShouldRotate(p, now));
        p.OAuthRefreshTokenExpiresAt = now.AddHours(5);
        Assert.False(OAuthRefreshVault.ShouldRotate(p, now));

        // every_24h — based on updated-at.
        p.OAuthRefreshTokenRotationPolicy = "every_24h";
        p.OAuthRefreshTokenExpiresAt = null;
        p.OAuthRefreshTokenUpdatedAt = now.AddHours(-25);
        Assert.True(OAuthRefreshVault.ShouldRotate(p, now));
        p.OAuthRefreshTokenUpdatedAt = now.AddHours(-1);
        Assert.False(OAuthRefreshVault.ShouldRotate(p, now));
    }

    // ------------------------------------------------------------------
    // Custom factory used by all HTTP-driven tests below. Owns its own
    // DB so audit-row assertions are deterministic.
    // ------------------------------------------------------------------
    private sealed class StubIssuer : IOAuthTokenIssuer
    {
        public bool CanRefresh { get; set; }
        public Func<string>? Mint { get; set; }
        public Task<(string token, DateTimeOffset? expiresAt)> RefreshAsync(
            ProviderConfig provider, string currentRefreshToken, CancellationToken ct)
        {
            var next = (Mint ?? (() => "rt_rotated_" + Guid.NewGuid().ToString("N")))();
            return Task.FromResult<(string, DateTimeOffset?)>((next, DateTimeOffset.UtcNow.AddDays(30)));
        }
    }

    private sealed class VaultFactory : WebApplicationFactory<Program>, IAsyncLifetime
    {
        public string DbPath { get; } = Path.Combine(Path.GetTempPath(), $"radiopad-it35vault-{Guid.NewGuid():N}.db");
        public Tenant SeedTenant { get; private set; } = null!;
        public Tenant OtherTenant { get; private set; } = null!;
        public User Radiologist { get; private set; } = null!;
        public User ItAdmin { get; private set; } = null!;
        public User BillingAdmin { get; private set; } = null!;
        public ProviderConfig Provider { get; private set; } = null!;
        public ProviderConfig OtherTenantProvider { get; private set; } = null!;
        public StubIssuer Issuer { get; } = new();

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("ConnectionStrings:RadioPad", $"Data Source={DbPath}");
            builder.ConfigureServices(services =>
            {
                // Override the no-op issuer with our controllable stub.
                var existing = services.Where(d => d.ServiceType == typeof(IOAuthTokenIssuer)).ToList();
                foreach (var d in existing) services.Remove(d);
                services.AddSingleton<IOAuthTokenIssuer>(Issuer);
            });
        }

        public async Task InitializeAsync()
        {
            EnsureKekEnv();
            using var scope = Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<RadioPadDbContext>();
            await db.Database.EnsureCreatedAsync();

            SeedTenant = new Tenant { Slug = "it", DisplayName = "Iter35 Vault" };
            OtherTenant = new Tenant { Slug = "other", DisplayName = "Iter35 Other" };
            db.Tenants.AddRange(SeedTenant, OtherTenant);

            Radiologist = new User
            {
                TenantId = SeedTenant.Id,
                Email = "it-radiologist@radiopad.local",
                DisplayName = "IT Radiologist",
                Role = UserRole.Radiologist,
            };
            ItAdmin = new User
            {
                TenantId = SeedTenant.Id,
                Email = "it-admin@radiopad.local",
                DisplayName = "IT Admin",
                Role = UserRole.ItAdmin,
            };
            BillingAdmin = new User
            {
                TenantId = SeedTenant.Id,
                Email = "it-billing@radiopad.local",
                DisplayName = "IT Billing",
                Role = UserRole.BillingAdmin,
            };
            db.Users.AddRange(Radiologist, ItAdmin, BillingAdmin);

            Provider = new ProviderConfig
            {
                TenantId = SeedTenant.Id,
                Name = "MockOauth",
                Adapter = "mock",
                Compliance = ProviderComplianceClass.LocalOnly,
            };
            OtherTenantProvider = new ProviderConfig
            {
                TenantId = OtherTenant.Id,
                Name = "OtherTenantMock",
                Adapter = "mock",
                Compliance = ProviderComplianceClass.LocalOnly,
            };
            db.Providers.AddRange(Provider, OtherTenantProvider);
            await db.SaveChangesAsync();
        }

        public new Task DisposeAsync()
        {
            try { if (File.Exists(DbPath)) File.Delete(DbPath); } catch { /* best effort */ }
            return base.DisposeAsync().AsTask();
        }

        public HttpClient CreateClient(User u)
        {
            var c = CreateClient();
            c.DefaultRequestHeaders.Add("X-RadioPad-Tenant", SeedTenant.Slug);
            c.DefaultRequestHeaders.Add("X-RadioPad-User", u.Email);
            return c;
        }
    }

    // ------------------------------------------------------------------
    // 2. HTTP surface — save / status / delete + audit + role gating.
    // ------------------------------------------------------------------
    [Fact]
    public async Task Http_AdminSave_ThenStatus_ThenDelete_WritesAuditRows_AndStatusNeverLeaksCiphertext()
    {
        await using var f = new VaultFactory();
        await f.InitializeAsync();
        try
        {
            using var admin = f.CreateClient(f.ItAdmin);
            var save = await admin.PostAsJsonAsync(
                $"/api/providers/{f.Provider.Id}/oauth/refresh-token",
                new { refreshToken = "rt_initial_abc", expiresAt = DateTimeOffset.UtcNow.AddDays(30), rotationPolicy = "before_expiry" });
            Assert.Equal(HttpStatusCode.NoContent, save.StatusCode);

            var statusResp = await admin.GetAsync($"/api/providers/{f.Provider.Id}/oauth/refresh-token/status");
            statusResp.EnsureSuccessStatusCode();
            var statusBody = await statusResp.Content.ReadAsStringAsync();
            Assert.Contains("\"hasToken\":true", statusBody);
            Assert.Contains("\"rotationPolicy\":\"before_expiry\"", statusBody);
            // Status surface MUST NOT echo any ciphertext column names.
            Assert.DoesNotContain("OAuthRefreshTokenEnc", statusBody);
            Assert.DoesNotContain("OAuthRefreshTokenIv", statusBody);
            Assert.DoesNotContain("OAuthRefreshTokenTag", statusBody);
            Assert.DoesNotContain("WrappedDek", statusBody);
            Assert.DoesNotContain("rt_initial_abc", statusBody);

            // Crypto columns are populated in the database.
            using (var scope = f.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<RadioPadDbContext>();
                var p = await db.Providers.AsNoTracking().FirstAsync(x => x.Id == f.Provider.Id);
                Assert.NotNull(p.OAuthRefreshTokenEnc);
                Assert.NotNull(p.OAuthRefreshTokenWrappedDek);
                // Ciphertext never contains the plaintext UTF8 bytes.
                var plain = System.Text.Encoding.UTF8.GetBytes("rt_initial_abc");
                Assert.False(p.OAuthRefreshTokenEnc!.AsSpan().IndexOf(plain) >= 0,
                    "ciphertext must not contain plaintext token bytes");
            }

            var del = await admin.DeleteAsync($"/api/providers/{f.Provider.Id}/oauth/refresh-token");
            Assert.Equal(HttpStatusCode.NoContent, del.StatusCode);

            using (var scope = f.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<RadioPadDbContext>();
                var p = await db.Providers.AsNoTracking().FirstAsync(x => x.Id == f.Provider.Id);
                Assert.Null(p.OAuthRefreshTokenEnc);

                var saveAudits = await db.AuditEvents.AsNoTracking()
                    .Where(a => a.TenantId == f.SeedTenant.Id
                        && a.Action == AuditAction.OAuthRefreshRotated
                        && a.DetailsJson.Contains("\"saved\""))
                    .CountAsync();
                Assert.True(saveAudits >= 1);

                var deleteAudits = await db.AuditEvents.AsNoTracking()
                    .Where(a => a.TenantId == f.SeedTenant.Id
                        && a.Action == AuditAction.OAuthRefreshRotated
                        && a.DetailsJson.Contains("\"deleted\""))
                    .CountAsync();
                Assert.Equal(1, deleteAudits);

                // No audit row contains the plaintext token.
                var allDetails = await db.AuditEvents.AsNoTracking()
                    .Where(a => a.TenantId == f.SeedTenant.Id
                        && a.Action == AuditAction.OAuthRefreshRotated)
                    .Select(a => a.DetailsJson).ToListAsync();
                foreach (var d in allDetails)
                {
                    Assert.DoesNotContain("rt_initial_abc", d);
                }
            }
        }
        finally
        {
            await f.DisposeAsync();
        }
    }

    [Fact]
    public async Task Http_BillingAdmin_CanSave()
    {
        await using var f = new VaultFactory();
        await f.InitializeAsync();
        try
        {
            using var billing = f.CreateClient(f.BillingAdmin);
            var resp = await billing.PostAsJsonAsync(
                $"/api/providers/{f.Provider.Id}/oauth/refresh-token",
                new { refreshToken = "rt_billing_token" });
            Assert.Equal(HttpStatusCode.NoContent, resp.StatusCode);
        }
        finally { await f.DisposeAsync(); }
    }

    [Fact]
    public async Task Http_Radiologist_IsForbidden()
    {
        await using var f = new VaultFactory();
        await f.InitializeAsync();
        try
        {
            using var rad = f.CreateClient(f.Radiologist);
            var save = await rad.PostAsJsonAsync(
                $"/api/providers/{f.Provider.Id}/oauth/refresh-token",
                new { refreshToken = "rt_should_be_rejected" });
            Assert.Equal(HttpStatusCode.Forbidden, save.StatusCode);

            var status = await rad.GetAsync($"/api/providers/{f.Provider.Id}/oauth/refresh-token/status");
            Assert.Equal(HttpStatusCode.Forbidden, status.StatusCode);

            var del = await rad.DeleteAsync($"/api/providers/{f.Provider.Id}/oauth/refresh-token");
            Assert.Equal(HttpStatusCode.Forbidden, del.StatusCode);
        }
        finally { await f.DisposeAsync(); }
    }

    [Fact]
    public async Task Http_TenantIsolation_OtherTenantsProvider_Returns404()
    {
        await using var f = new VaultFactory();
        await f.InitializeAsync();
        try
        {
            using var admin = f.CreateClient(f.ItAdmin);
            var resp = await admin.PostAsJsonAsync(
                $"/api/providers/{f.OtherTenantProvider.Id}/oauth/refresh-token",
                new { refreshToken = "rt_cross_tenant" });
            Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);

            var status = await admin.GetAsync($"/api/providers/{f.OtherTenantProvider.Id}/oauth/refresh-token/status");
            Assert.Equal(HttpStatusCode.NotFound, status.StatusCode);

            var del = await admin.DeleteAsync($"/api/providers/{f.OtherTenantProvider.Id}/oauth/refresh-token");
            Assert.Equal(HttpStatusCode.NotFound, del.StatusCode);
        }
        finally { await f.DisposeAsync(); }
    }

    // ------------------------------------------------------------------
    // 3. Rotation worker — drives ScanOnceAsync against a stub issuer.
    // ------------------------------------------------------------------
    [Fact]
    public async Task RotationService_RotatesEligibleTokens_AndAuditsRotated()
    {
        await using var f = new VaultFactory();
        await f.InitializeAsync();
        try
        {
            // Seed an existing token via the admin endpoint with a near-expiry stamp.
            using (var admin = f.CreateClient(f.ItAdmin))
            {
                var resp = await admin.PostAsJsonAsync(
                    $"/api/providers/{f.Provider.Id}/oauth/refresh-token",
                    new
                    {
                        refreshToken = "rt_pre_rotation",
                        expiresAt = DateTimeOffset.UtcNow.AddMinutes(30),
                        rotationPolicy = "before_expiry",
                    });
                Assert.Equal(HttpStatusCode.NoContent, resp.StatusCode);
            }

            f.Issuer.CanRefresh = true;

            using var scope = f.Services.CreateScope();
            var worker = scope.ServiceProvider.GetRequiredService<OAuthRefreshRotationService>();
            var rotated = await worker.ScanOnceAsync(default);
            Assert.Equal(1, rotated);

            var db = scope.ServiceProvider.GetRequiredService<RadioPadDbContext>();
            var rotations = await db.AuditEvents.AsNoTracking()
                .Where(a => a.TenantId == f.SeedTenant.Id
                    && a.Action == AuditAction.OAuthRefreshRotated
                    && a.DetailsJson.Contains("\"rotated\""))
                .CountAsync();
            Assert.Equal(1, rotations);

            // The new token must decrypt to the rotated value, not the original.
            var resolver = scope.ServiceProvider.GetRequiredService<IKmsResolver>();
            var vault = new OAuthRefreshVault(resolver);
            var p = await db.Providers.AsNoTracking().FirstAsync(x => x.Id == f.Provider.Id);
            var t = scope.ServiceProvider.GetRequiredService<RadioPadDbContext>().Tenants.First(x => x.Id == p.TenantId);
            var current = await vault.LoadAsync(t, p, OAuthRefreshVault.ResolveKekRef(null), default);
            Assert.NotNull(current);
            Assert.NotEqual("rt_pre_rotation", current);
            Assert.StartsWith("rt_rotated_", current);
        }
        finally { await f.DisposeAsync(); }
    }

    [Fact]
    public async Task RotationService_NoopIssuer_DoesNothing()
    {
        await using var f = new VaultFactory();
        await f.InitializeAsync();
        try
        {
            using (var admin = f.CreateClient(f.ItAdmin))
            {
                await admin.PostAsJsonAsync(
                    $"/api/providers/{f.Provider.Id}/oauth/refresh-token",
                    new
                    {
                        refreshToken = "rt_noop_seed",
                        expiresAt = DateTimeOffset.UtcNow.AddMinutes(10),
                        rotationPolicy = "before_expiry",
                    });
            }

            f.Issuer.CanRefresh = false;
            using var scope = f.Services.CreateScope();
            var worker = scope.ServiceProvider.GetRequiredService<OAuthRefreshRotationService>();
            var rotated = await worker.ScanOnceAsync(default);
            Assert.Equal(0, rotated);
        }
        finally { await f.DisposeAsync(); }
    }
}
