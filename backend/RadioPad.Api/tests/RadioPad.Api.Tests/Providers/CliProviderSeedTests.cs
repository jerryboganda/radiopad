using Microsoft.EntityFrameworkCore;
using RadioPad.Domain.Entities;
using RadioPad.Domain.Enums;
using RadioPad.Infrastructure.Persistence;
using RadioPad.Infrastructure.Providers.Cli;
using RadioPad.Infrastructure.Seeding;
using Xunit;

namespace RadioPad.Api.Tests.Providers;

/// <summary>
/// The curated Gemini API provider (gemini-cli + GEMINI_API_KEY) is seeded once per
/// tenant and surfaced in the report-intake dropdown. Covers the idempotent seed and
/// the 2026-07-13 rename backfill that migrates the legacy "Gemini CLI (OAuth)" name
/// (from before Google retired oauth-personal) to "Gemini API".
/// </summary>
public class CliProviderSeedTests
{
    private static readonly Guid Tenant = Guid.Parse("33333333-3333-3333-3333-333333333333");

    private static RadioPadDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<RadioPadDbContext>()
            .UseSqlite("Data Source=:memory:")
            .Options;
        var db = new RadioPadDbContext(options);
        db.Database.OpenConnection();
        db.Database.EnsureCreated();
        db.Tenants.Add(new Tenant { Id = Tenant, Slug = "cli-seed-test", DisplayName = "CLI Seed Test" });
        db.SaveChanges();
        return db;
    }

    [Fact]
    public async Task Seeds_curated_gemini_api_provider_with_current_name()
    {
        using var db = CreateDb();

        var added = await CliProviderSeed.EnsureGeminiCliAsync(db, Tenant, default);

        Assert.Equal(1, added);
        var row = await db.Providers.SingleAsync(p => p.TenantId == Tenant && p.Adapter == GeminiCliProvider.AdapterId);
        Assert.Equal("Gemini API", row.Name);
        Assert.Equal(CliProviderSeed.ProviderName, row.Name);
        Assert.Equal(ProviderComplianceClass.PhiApproved, row.Compliance);
        Assert.True(row.Enabled);
    }

    [Fact]
    public async Task Seed_is_idempotent()
    {
        using var db = CreateDb();
        await CliProviderSeed.EnsureGeminiCliAsync(db, Tenant, default);
        var second = await CliProviderSeed.EnsureGeminiCliAsync(db, Tenant, default);
        Assert.Equal(0, second);
        Assert.Equal(1, await db.Providers.CountAsync(p => p.Adapter == GeminiCliProvider.AdapterId));
    }

    [Fact]
    public async Task Rename_backfill_migrates_legacy_oauth_name()
    {
        using var db = CreateDb();
        db.Providers.Add(new ProviderConfig
        {
            TenantId = Tenant,
            Name = CliProviderSeed.LegacyProviderName, // "Gemini CLI (OAuth)"
            Adapter = GeminiCliProvider.AdapterId,
            Compliance = ProviderComplianceClass.PhiApproved,
            Enabled = true,
        });
        await db.SaveChangesAsync();

        var renamed = await CliProviderSeed.EnsureGeminiCliNameAsync(db, default);

        Assert.Equal(1, renamed);
        var row = await db.Providers.SingleAsync(p => p.Adapter == GeminiCliProvider.AdapterId);
        Assert.Equal("Gemini API", row.Name);
        // Idempotent: a second run touches nothing.
        Assert.Equal(0, await CliProviderSeed.EnsureGeminiCliNameAsync(db, default));
    }

    [Fact]
    public async Task Rename_backfill_leaves_operator_customised_name_untouched()
    {
        using var db = CreateDb();
        db.Providers.Add(new ProviderConfig
        {
            TenantId = Tenant,
            Name = "My Gemini",
            Adapter = GeminiCliProvider.AdapterId,
            Compliance = ProviderComplianceClass.PhiApproved,
            Enabled = true,
        });
        await db.SaveChangesAsync();

        var renamed = await CliProviderSeed.EnsureGeminiCliNameAsync(db, default);

        Assert.Equal(0, renamed);
        var row = await db.Providers.SingleAsync(p => p.Adapter == GeminiCliProvider.AdapterId);
        Assert.Equal("My Gemini", row.Name);
    }
}
