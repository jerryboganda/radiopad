using Microsoft.EntityFrameworkCore;
using RadioPad.Domain.Enums;
using RadioPad.Infrastructure.Identity;
using RadioPad.Infrastructure.Persistence;
using RadioPad.Infrastructure.Seeding;
using Xunit;

namespace RadioPad.Api.Tests.Infrastructure;

/// <summary>
/// v0.1.15 — DevSeed seeds the bundled report templates (templates/*.json) the
/// same way it seeds rulebooks, so a freshly installed desktop lists them on the
/// Templates page and offers them in the editor's "apply scaffolding" dropdown.
/// </summary>
public class DevSeedTemplateTests
{
    private static async Task<RadioPadDbContext> NewDbAsync()
    {
        var options = new DbContextOptionsBuilder<RadioPadDbContext>()
            .UseSqlite("Data Source=:memory:")
            .Options;
        var db = new RadioPadDbContext(options);
        db.Database.OpenConnection();
        // Mirror Program.cs startup ordering: migrate, materialise the enterprise
        // -identity tables (not created by an EF migration), then seed.
        await db.Database.MigrateAsync();
        await EnterpriseIdentityBridge.EnsureSchemaAsync(db, default);
        return db;
    }

    // rulebooksDir intentionally points at a non-existent path so only template
    // seeding is exercised here.
    private static Task SeedAsync(RadioPadDbContext db, string templatesDir)
        => DevSeed.EnsureSeededAsync(db, TempPath("rp-no-rulebooks"), templatesDir, default);

    private static string TempPath(string prefix)
        => Path.Combine(Path.GetTempPath(), $"{prefix}-{Guid.NewGuid():N}");

    private static string NewTemplatesDir()
    {
        var dir = TempPath("rp-templates");
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static Task WriteTemplateAsync(string dir, string file, string id) =>
        File.WriteAllTextAsync(Path.Combine(dir, file), $$"""
            {
              "templateId": "{{id}}",
              "name": "Template {{id}}",
              "modality": "CT",
              "bodyPart": "Chest",
              "subspecialty": "Thoracic",
              "sections": [
                { "key": "Indication", "required": true },
                { "key": "Impression", "required": true, "format": "bullets" }
              ]
            }
            """);

    [Fact]
    public async Task Seeds_bundled_template_into_a_report_template_row()
    {
        var dir = NewTemplatesDir();
        await WriteTemplateAsync(dir, "chest-ct.json", "chest-ct");
        using var db = await NewDbAsync();

        await SeedAsync(db, dir);

        var t = await db.Templates.SingleAsync(x => x.TemplateId == "chest-ct");
        Assert.Equal("Template chest-ct", t.Name);
        Assert.Equal("CT", t.Modality);
        Assert.Equal("Chest", t.BodyPart);
        Assert.Equal("Thoracic", t.Subspecialty);
        // The whole "sections" array is preserved verbatim as the SectionsJson blob.
        Assert.Contains("Indication", t.SectionsJson);
        Assert.Contains("Impression", t.SectionsJson);
        // No status/variant in the JSON → entity defaults; both the Templates page
        // and the editor scaffolding dropdown list every template regardless.
        Assert.Equal(TemplateStatus.Draft, t.Status);
        Assert.Equal(TemplateVariant.Normal, t.Variant);
    }

    [Fact]
    public async Task Reseeding_does_not_duplicate_an_existing_template()
    {
        var dir = NewTemplatesDir();
        await WriteTemplateAsync(dir, "chest-ct.json", "chest-ct");
        using var db = await NewDbAsync();

        await SeedAsync(db, dir);
        await SeedAsync(db, dir);

        Assert.Equal(1, await db.Templates.CountAsync(t => t.TemplateId == "chest-ct"));
    }

    [Fact]
    public async Task Malformed_template_file_is_skipped_without_aborting_seeding()
    {
        var dir = NewTemplatesDir();
        await File.WriteAllTextAsync(Path.Combine(dir, "broken.json"), "{ this is not valid json");
        await WriteTemplateAsync(dir, "brain-mri.json", "brain-mri");
        using var db = await NewDbAsync();

        // Must not throw: a single bad bundled template can't abort sidecar startup.
        await SeedAsync(db, dir);

        Assert.True(await db.Templates.AnyAsync(t => t.TemplateId == "brain-mri"));
        Assert.False(await db.Templates.AnyAsync(t => t.TemplateId == "broken"));
    }
}
