using Microsoft.EntityFrameworkCore;
using RadioPad.Application.Services;
using RadioPad.Domain.Entities;
using RadioPad.Domain.Enums;
using RadioPad.Infrastructure.Identity;
using RadioPad.Infrastructure.Persistence;
using RadioPad.Infrastructure.Seeding;
using Xunit;

namespace RadioPad.Api.Tests.Infrastructure;

/// <summary>
/// End-to-end proof that the bundled content library (rulebooks/*.yaml +
/// templates/*.json) parses with YamlDotNet, seeds as Approved, and auto-resolves
/// for the granular catalog selection keys — including the hybrid contrast model.
/// Seeds from the REAL repo content directories, so a malformed YAML / invalid
/// schema / unresolvable key fails CI here, not in production.
/// </summary>
public class ContentLibraryResolutionTests
{
    private static string RepoDir()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d is not null)
        {
            if (Directory.Exists(Path.Combine(d.FullName, "rulebooks")) &&
                Directory.Exists(Path.Combine(d.FullName, "templates")))
                return d.FullName;
            d = d.Parent;
        }
        throw new DirectoryNotFoundException("Could not locate repo root with rulebooks/ + templates/.");
    }

    private static async Task<(RadioPadDbContext db, Tenant tenant)> SeedFromRepoAsync()
    {
        var options = new DbContextOptionsBuilder<RadioPadDbContext>()
            .UseSqlite("Data Source=:memory:")
            .Options;
        var db = new RadioPadDbContext(options);
        db.Database.OpenConnection();
        await db.Database.MigrateAsync();
        await EnterpriseIdentityBridge.EnsureSchemaAsync(db, default);

        var repo = RepoDir();
        await DevSeed.EnsureSeededAsync(db, Path.Combine(repo, "rulebooks"), Path.Combine(repo, "templates"), default);
        var tenant = await db.Tenants.SingleAsync(t => t.Slug == "dev");
        return (db, tenant);
    }

    private static async Task<(List<ReportTemplate>, List<Rulebook>)> ApprovedAsync(RadioPadDbContext db, Tenant t)
    {
        var tpl = await db.Templates.Where(x => x.TenantId == t.Id && x.Status == TemplateStatus.Approved).ToListAsync();
        var rb = await db.Rulebooks.Where(x => x.TenantId == t.Id && x.Status == RulebookStatus.Approved).ToListAsync();
        return (tpl, rb);
    }

    [Fact]
    public async Task Entire_library_seeds_without_throwing_and_is_substantial()
    {
        using var db = (await SeedFromRepoAsync()).db;
        var tenant = await db.Tenants.SingleAsync(t => t.Slug == "dev");
        var (tpl, rb) = await ApprovedAsync(db, tenant);

        // The generated library is large; assert we are well past the legacy demo set.
        Assert.True(tpl.Count >= 140, $"expected >=140 approved templates, got {tpl.Count}");
        Assert.True(rb.Count >= 90, $"expected >=90 approved rulebooks, got {rb.Count}");
    }

    [Theory]
    // modality, bodyPart, contrast → both a template and a rulebook must resolve
    [InlineData("CT", "Abdomen & Pelvis", "With")]
    [InlineData("CT", "Abdomen & Pelvis", "None")]
    [InlineData("CT", "Abdomen & Pelvis", "WithAndWithout")]
    [InlineData("CT", "Chest", "None")]
    [InlineData("CT", "Pulmonary Arteries", "With")]
    [InlineData("MR", "Prostate", "WithAndWithout")]
    [InlineData("MR", "Lumbar Spine", "None")]
    [InlineData("MR", "Brain", "With")]
    [InlineData("US", "Thyroid", "")]
    [InlineData("US", "Breast", "")]
    [InlineData("MG", "Breast", "")]
    [InlineData("XR", "Chest", "")]
    [InlineData("CT", "KUB", "None")]
    public async Task Resolves_template_and_rulebook_for_catalog_selection(string modality, string bodyPart, string contrast)
    {
        using var db = (await SeedFromRepoAsync()).db;
        var tenant = await db.Tenants.SingleAsync(t => t.Slug == "dev");
        var (tpl, rb) = await ApprovedAsync(db, tenant);

        var (template, rulebook) = ReportingService.ResolveBindings(tpl, rb, modality, bodyPart, contrast);

        Assert.True(template is not null, $"no template resolved for {modality}/{bodyPart}/{contrast}");
        Assert.True(rulebook is not null, $"no rulebook resolved for {modality}/{bodyPart}/{contrast}");
    }

    [Fact]
    public async Task Production_backfill_seeds_library_into_existing_nondev_tenant()
    {
        // Mirrors prod: a real customer tenant exists but DevSeed never ran for it
        // (prod had 0 templates / 0 rulebooks). EnsureBundledContentForAllTenantsAsync
        // is the Production-safe path that backfills the curated library into every
        // existing tenant without creating a dev tenant.
        var options = new DbContextOptionsBuilder<RadioPadDbContext>()
            .UseSqlite("Data Source=:memory:").Options;
        using var db = new RadioPadDbContext(options);
        db.Database.OpenConnection();
        await db.Database.MigrateAsync();
        await EnterpriseIdentityBridge.EnsureSchemaAsync(db, default);

        var tenant = new Tenant { Slug = "dhqgujranwala", DisplayName = "DHQ Gujranwala", RequirePhiApprovedProvider = false };
        db.Tenants.Add(tenant);
        await db.SaveChangesAsync();

        var repo = RepoDir();
        await DevSeed.EnsureBundledContentForAllTenantsAsync(
            db, Path.Combine(repo, "rulebooks"), Path.Combine(repo, "templates"), default);

        // No dev tenant should have been created.
        Assert.False(await db.Tenants.AnyAsync(t => t.Slug == "dev"));

        var (tpl, rb) = await ApprovedAsync(db, tenant);
        Assert.True(tpl.Count >= 140, $"expected the real tenant to get >=140 templates, got {tpl.Count}");
        Assert.True(rb.Count >= 90, $"expected the real tenant to get >=90 rulebooks, got {rb.Count}");
        Assert.True(await db.BodyParts.CountAsync(b => b.TenantId == tenant.Id) >= 60, "catalog should be expanded for the tenant");

        var (template, rulebook) = ReportingService.ResolveBindings(tpl, rb, "CT", "Abdomen & Pelvis", "With");
        Assert.NotNull(template);
        Assert.NotNull(rulebook);
    }

    [Fact]
    public async Task Contrast_selects_the_matching_template_variant()
    {
        using var db = (await SeedFromRepoAsync()).db;
        var tenant = await db.Tenants.SingleAsync(t => t.Slug == "dev");
        var (tpl, rb) = await ApprovedAsync(db, tenant);

        var withC = ReportingService.ResolveBindings(tpl, rb, "CT", "Abdomen & Pelvis", "With").template;
        var noneC = ReportingService.ResolveBindings(tpl, rb, "CT", "Abdomen & Pelvis", "None").template;

        Assert.NotNull(withC);
        Assert.NotNull(noneC);
        // Different contrast selections must yield different (contrast-specific) templates.
        Assert.Equal("With", withC!.Contrast);
        Assert.Equal("None", noneC!.Contrast);
        Assert.NotEqual(withC.Id, noneC.Id);
    }
}
