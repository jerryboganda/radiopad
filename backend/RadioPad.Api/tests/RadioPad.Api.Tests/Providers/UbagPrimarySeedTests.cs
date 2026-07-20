using Microsoft.EntityFrameworkCore;
using RadioPad.Domain.Entities;
using RadioPad.Domain.Enums;
using RadioPad.Infrastructure.Persistence;
using RadioPad.Infrastructure.Seeding;
using Xunit;

namespace RadioPad.Api.Tests.Providers;

/// <summary>
/// The curated UBAG primaries (Gemini + DeepSeek) must be seedable for ANY
/// tenant — not just the DevSeed "dev" org — so production orgs created via
/// bootstrap-org / registration (and existing orgs backfilled at startup) surface
/// the UBAG models on the AI-models page. <see cref="UbagPrimarySeed"/> is the single
/// idempotent source of truth those paths share.
/// </summary>
public class UbagPrimarySeedTests
{
    private static readonly Guid Tenant = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private static RadioPadDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<RadioPadDbContext>()
            .UseSqlite("Data Source=:memory:")
            .Options;
        var db = new RadioPadDbContext(options);
        db.Database.OpenConnection();
        db.Database.EnsureCreated();
        // ProviderConfig.TenantId is an enforced FK; seed the tenant first.
        db.Tenants.Add(new Tenant { Id = Tenant, Slug = "seed-test", DisplayName = "Seed Test" });
        db.SaveChanges();
        return db;
    }

    [Fact]
    public async Task Seeds_both_curated_primaries_for_a_fresh_tenant()
    {
        using var db = CreateDb();

        var added = await UbagPrimarySeed.EnsureCuratedPrimariesAsync(db, Tenant, default);

        Assert.Equal(2, added);
        var rows = await db.Providers
            .Where(p => p.TenantId == Tenant && p.Adapter == "ubag")
            .OrderBy(p => p.Priority)
            .ToListAsync();
        Assert.Equal(2, rows.Count);

        var gemini = rows[0];
        Assert.Equal("UBAG (Gemini)", gemini.Name);
        Assert.Equal("gemini_web", gemini.Model);
        // Policy change (2026-06-27): UBAG is PHI-approved so AI features are not
        // blocked by the PHI gates.
        Assert.Equal(ProviderComplianceClass.PhiApproved, gemini.Compliance);
        Assert.True(gemini.Enabled);

        var deepseek = rows[1];
        Assert.Equal("UBAG (DeepSeek)", deepseek.Name);
        Assert.Equal("deepseek_web", deepseek.Model);
        Assert.True(deepseek.Enabled);
    }

    [Fact]
    public async Task Is_idempotent_a_second_call_adds_nothing()
    {
        using var db = CreateDb();

        await UbagPrimarySeed.EnsureCuratedPrimariesAsync(db, Tenant, default);
        var addedSecond = await UbagPrimarySeed.EnsureCuratedPrimariesAsync(db, Tenant, default);

        Assert.Equal(0, addedSecond);
        Assert.Equal(2, await db.Providers.CountAsync(p => p.TenantId == Tenant && p.Adapter == "ubag"));
    }

    [Fact]
    public async Task Adds_only_the_missing_primary()
    {
        using var db = CreateDb();
        db.Providers.Add(new ProviderConfig
        {
            TenantId = Tenant,
            Name = "UBAG (Gemini)",
            Adapter = "ubag",
            Model = "gemini_web",
            Compliance = ProviderComplianceClass.Sandbox,
            Enabled = true,
        });
        await db.SaveChangesAsync();

        var added = await UbagPrimarySeed.EnsureCuratedPrimariesAsync(db, Tenant, default);

        Assert.Equal(1, added); // only deepseek_web
        Assert.Equal(1, await db.Providers.CountAsync(p => p.TenantId == Tenant && p.Model == "gemini_web"));
        Assert.Equal(1, await db.Providers.CountAsync(p => p.TenantId == Tenant && p.Model == "deepseek_web"));
    }

    [Fact]
    public async Task Compliance_backfill_promotes_legacy_sandbox_rows_to_phi_approved()
    {
        using var db = CreateDb();
        // Two pre-existing rows as production seeded them before the policy change.
        db.Providers.AddRange(
            new ProviderConfig { TenantId = Tenant, Name = "UBAG (Gemini)", Adapter = "ubag", Model = "gemini_web", Compliance = ProviderComplianceClass.Sandbox, Enabled = true },
            new ProviderConfig { TenantId = Tenant, Name = "UBAG (DeepSeek)", Adapter = "ubag", Model = "deepseek_web", Compliance = ProviderComplianceClass.PhiApproved, Enabled = true });
        await db.SaveChangesAsync();

        var promoted = await UbagPrimarySeed.EnsureCuratedComplianceAsync(db, default);

        Assert.Equal(1, promoted); // only the Sandbox row needed promotion
        Assert.All(
            await db.Providers.Where(p => p.Adapter == "ubag").ToListAsync(),
            p => Assert.Equal(ProviderComplianceClass.PhiApproved, p.Compliance));
        // Idempotent: a second run touches nothing.
        Assert.Equal(0, await UbagPrimarySeed.EnsureCuratedComplianceAsync(db, default));
    }

    [Fact]
    public async Task Name_backfill_strips_web_suffix_from_curated_and_discovered_rows()
    {
        using var db = CreateDb();
        // A curated primary and an auto-discovered row, both seeded before the
        // 2026-07-21 "never say Web" decision.
        db.Providers.AddRange(
            new ProviderConfig { TenantId = Tenant, Name = "UBAG (Gemini Web)", Adapter = "ubag", Model = "gemini_web", Compliance = ProviderComplianceClass.PhiApproved, Enabled = true },
            new ProviderConfig { TenantId = Tenant, Name = "UBAG (ChatGPT Web)", Adapter = "ubag", Model = "chatgpt_web", Compliance = ProviderComplianceClass.Sandbox, Enabled = true },
            new ProviderConfig { TenantId = Tenant, Name = "My Custom Web Thing", Adapter = "ubag", Model = "custom", Compliance = ProviderComplianceClass.Sandbox, Enabled = true });
        await db.SaveChangesAsync();

        var renamed = await UbagPrimarySeed.EnsureCuratedNamesAsync(db, default);

        Assert.Equal(2, renamed);
        Assert.Equal("UBAG (Gemini)", (await db.Providers.SingleAsync(p => p.Model == "gemini_web")).Name);
        Assert.Equal("UBAG (ChatGPT)", (await db.Providers.SingleAsync(p => p.Model == "chatgpt_web")).Name);
        // Doesn't end in " Web)" so an operator's own naming is left alone.
        Assert.Equal("My Custom Web Thing", (await db.Providers.SingleAsync(p => p.Model == "custom")).Name);

        // Idempotent: a second run touches nothing.
        Assert.Equal(0, await UbagPrimarySeed.EnsureCuratedNamesAsync(db, default));
    }

    [Fact]
    public async Task Does_not_overwrite_an_existing_operator_customised_row()
    {
        using var db = CreateDb();
        db.Providers.Add(new ProviderConfig
        {
            TenantId = Tenant,
            Name = "My Gemini",
            Adapter = "ubag",
            Model = "gemini_web",
            Compliance = ProviderComplianceClass.Sandbox,
            Enabled = false, // operator deliberately disabled it
        });
        await db.SaveChangesAsync();

        await UbagPrimarySeed.EnsureCuratedPrimariesAsync(db, Tenant, default);

        var gemini = await db.Providers.SingleAsync(p => p.TenantId == Tenant && p.Model == "gemini_web");
        Assert.Equal("My Gemini", gemini.Name); // untouched
        Assert.False(gemini.Enabled);           // still disabled
    }
}
