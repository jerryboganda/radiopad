using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using RadioPad.Api.Jobs;
using RadioPad.Domain.Entities;
using RadioPad.Domain.Enums;
using RadioPad.Infrastructure.Persistence;
using Xunit;

namespace RadioPad.Api.Tests.Integration;

/// <summary>
/// PR-N2 — <see cref="AuditExportRollupJob"/>. Drives <c>RunForTenantAsync</c> directly (the
/// Hangfire server is skipped under the Testing environment). Confirms EnsureCreated builds the
/// new <c>AuditExportBundles</c> table, that the JSONL body mirrors the PHI-minimized Siem shape
/// (no DetailsJson), the manifest SHA + HMAC signature verify, and that a re-run replaces the
/// bundle in place.
/// </summary>
public class AuditExportRollupJobTests : IClassFixture<RadioPadAppFactory>
{
    private readonly RadioPadAppFactory _factory;
    public AuditExportRollupJobTests(RadioPadAppFactory f) => _factory = f;

    private static AuditEvent BackdatedEvent(Guid tenantId, DateTimeOffset createdAt, string detailsJson, string chain) => new()
    {
        TenantId = tenantId,
        Action = AuditAction.ReportEdited,
        DetailsJson = detailsJson,
        IntegrityChain = chain,
        CreatedAt = createdAt,
        UpdatedAt = createdAt,
    };

    [Fact]
    public async Task RunForTenant_BundlesPriorDayEvents_PhiMinimized_AndReRunReplaces()
    {
        var yesterday = DateOnly.FromDateTime(DateTime.UtcNow.Date.AddDays(-1));
        var atNoon = new DateTimeOffset(yesterday.ToDateTime(new TimeOnly(12, 0)), TimeSpan.Zero);

        Guid otherTenantId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RadioPadDbContext>();
            var other = new Tenant { Slug = $"export-other-{Guid.NewGuid():N}", DisplayName = "Other" };
            db.Tenants.Add(other);
            otherTenantId = other.Id;

            // 3 in-window events for the seed tenant, each carrying a PHI marker in DetailsJson.
            db.AuditEvents.Add(BackdatedEvent(_factory.SeedTenant.Id, atNoon, "{\"marker\":\"PHI_MUST_NOT_LEAK\"}", "chain-1"));
            db.AuditEvents.Add(BackdatedEvent(_factory.SeedTenant.Id, atNoon.AddHours(1), "{\"marker\":\"PHI_MUST_NOT_LEAK\"}", "chain-2"));
            db.AuditEvents.Add(BackdatedEvent(_factory.SeedTenant.Id, atNoon.AddHours(2), "{\"marker\":\"PHI_MUST_NOT_LEAK\"}", "chain-3"));
            // Out of window (today) — must be excluded.
            db.AuditEvents.Add(BackdatedEvent(_factory.SeedTenant.Id, DateTimeOffset.UtcNow, "{}", "chain-today"));
            // Different tenant, in window — must be excluded (tenant isolation).
            db.AuditEvents.Add(BackdatedEvent(other.Id, atNoon, "{}", "chain-other"));
            await db.SaveChangesAsync();
        }

        var job = _factory.Services.GetRequiredService<AuditExportRollupJob>();
        await job.RunForTenantAsync(_factory.SeedTenant.Id, yesterday, CancellationToken.None);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RadioPadDbContext>();
            var bundle = await db.AuditExportBundles
                .AsNoTracking()
                .SingleAsync(b => b.TenantId == _factory.SeedTenant.Id && b.Date == yesterday);

            Assert.Equal(3, bundle.EventCount);
            Assert.DoesNotContain("PHI_MUST_NOT_LEAK", bundle.ContentJsonl);

            var lines = bundle.ContentJsonl.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            Assert.Equal(4, lines.Length); // 3 event lines + 1 manifest line

            // Manifest bodySha256 must equal sha256 over the event-line body.
            var eventLines = lines[..3];
            var body = string.Concat(eventLines.Select(l => l + "\n"));
            var expectedBodySha = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(body))).ToLowerInvariant();
            var manifest = JsonSerializer.Deserialize<JsonElement>(lines[3]);
            Assert.Equal(3, manifest.GetProperty("eventCount").GetInt32());
            Assert.Equal(expectedBodySha, manifest.GetProperty("bodySha256").GetString());

            // No signing key configured on this factory → unsigned bundle.
            Assert.Null(bundle.Signature);

            // Audit provenance row was appended.
            var audited = await db.AuditEvents.AnyAsync(a =>
                a.TenantId == _factory.SeedTenant.Id && a.Action == AuditAction.AuditExportBundleCreated);
            Assert.True(audited);
        }

        // Re-run for the same day: replaces the single bundle, count unchanged (the
        // AuditExportBundleCreated audit rows are dated today, so they never enter yesterday's window).
        await job.RunForTenantAsync(_factory.SeedTenant.Id, yesterday, CancellationToken.None);
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RadioPadDbContext>();
            var count = await db.AuditExportBundles
                .CountAsync(b => b.TenantId == _factory.SeedTenant.Id && b.Date == yesterday);
            Assert.Equal(1, count);
            var bundle = await db.AuditExportBundles
                .SingleAsync(b => b.TenantId == _factory.SeedTenant.Id && b.Date == yesterday);
            Assert.Equal(3, bundle.EventCount);
        }
    }

    [Fact]
    public async Task RunForTenant_WithSigningKey_ProducesVerifiableHmac()
    {
        const string key = "test-signing-key-12345";
        var factory = new SigningKeyFactory(key);
        await factory.InitializeAsync();
        try
        {
            var yesterday = DateOnly.FromDateTime(DateTime.UtcNow.Date.AddDays(-1));
            var atNoon = new DateTimeOffset(yesterday.ToDateTime(new TimeOnly(9, 0)), TimeSpan.Zero);
            using (var scope = factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<RadioPadDbContext>();
                db.AuditEvents.Add(BackdatedEvent(factory.SeedTenant.Id, atNoon, "{}", "chain-a"));
                db.AuditEvents.Add(BackdatedEvent(factory.SeedTenant.Id, atNoon.AddHours(1), "{}", "chain-b"));
                await db.SaveChangesAsync();
            }

            var job = factory.Services.GetRequiredService<AuditExportRollupJob>();
            await job.RunForTenantAsync(factory.SeedTenant.Id, yesterday, CancellationToken.None);

            using var readScope = factory.Services.CreateScope();
            var readDb = readScope.ServiceProvider.GetRequiredService<RadioPadDbContext>();
            var bundle = await readDb.AuditExportBundles
                .SingleAsync(b => b.TenantId == factory.SeedTenant.Id && b.Date == yesterday);

            Assert.NotNull(bundle.Signature);
            using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(key));
            var expected = Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(bundle.ContentJsonl))).ToLowerInvariant();
            Assert.Equal(expected, bundle.Signature);
        }
        finally
        {
            await factory.DisposeAsync();
        }
    }

    private sealed class SigningKeyFactory : RadioPadAppFactory
    {
        private readonly string _key;
        public SigningKeyFactory(string key) => _key = key;

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.UseSetting("AuditExport:SigningKey", _key);
        }
    }
}
