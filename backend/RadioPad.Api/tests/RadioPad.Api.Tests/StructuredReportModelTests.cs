using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using RadioPad.Domain.Entities;
using RadioPad.Infrastructure.Persistence;
using Xunit;

namespace RadioPad.Api.Tests;

/// <summary>
/// Iter-0a (PRD v2 §14.12 / RPT-003 / RADS-001..008 / COMP-003/004) — proves the
/// structured report data model persists and round-trips: the flexible
/// <c>StructuredFieldsJson</c> column, plus first-class queryable
/// <see cref="RadsAssessment"/> and <see cref="ReportMeasurement"/> children.
/// Uses EnsureCreated (the same model the production migration builds).
/// </summary>
public class StructuredReportModelTests
{
    private static RadioPadDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<RadioPadDbContext>()
            .UseSqlite("Data Source=:memory:")
            .Options;
        var db = new RadioPadDbContext(options);
        db.Database.OpenConnection();
        db.Database.EnsureCreated();
        return db;
    }

    private static Report NewReport(Guid tenantId) => new()
    {
        TenantId = tenantId,
        CreatedByUserId = Guid.NewGuid(),
        Study = new StudyContext { Modality = "US", BodyPart = "Breast" },
        Findings = "2 cm spiculated mass, right breast upper outer quadrant.",
    };

    [Fact]
    public async Task StructuredFields_RadsAssessments_And_Measurements_RoundTrip()
    {
        var tenantId = Guid.NewGuid();
        using var db = CreateDb();

        var report = NewReport(tenantId);
        report.StructuredFieldsJson = """{"density":"heterogeneously dense","numberOfLesions":1}""";
        report.RadsAssessments.Add(new RadsAssessment
        {
            TenantId = tenantId,
            Family = "BI-RADS",
            Category = "4A",
            IsDerived = true,
            LesionKey = "lesion-1",
            Rationale = "Spiculated margin → suspicious.",
        });
        report.Measurements.Add(new ReportMeasurement
        {
            TenantId = tenantId,
            Label = "Right breast mass",
            Value = 20, Unit = "mm",
            AnatomicalLocation = "UOQ", Laterality = "right",
            Section = "Findings", LesionKey = "lesion-1", Source = "manual",
        });
        db.Reports.Add(report);
        await db.SaveChangesAsync();
        var reportId = report.Id;

        // Drop tracked instances so the assertions read from the database,
        // proving persistence + materialization (not just the in-memory graph).
        db.ChangeTracker.Clear();

        var loaded = await db.Reports
            .Include(r => r.RadsAssessments)
            .Include(r => r.Measurements)
            .SingleAsync(r => r.Id == reportId);

        Assert.Contains("heterogeneously dense", loaded.StructuredFieldsJson);
        var rads = Assert.Single(loaded.RadsAssessments);
        Assert.Equal("BI-RADS", rads.Family);
        Assert.Equal("4A", rads.Category);
        Assert.True(rads.IsDerived);
        var m = Assert.Single(loaded.Measurements);
        Assert.Equal(20, m.Value);
        Assert.Equal("right", m.Laterality);
        Assert.Equal("lesion-1", m.LesionKey);
    }

    [Fact]
    public async Task Rads_And_Lesions_Are_Queryable_By_Index_Keys()
    {
        var tenantId = Guid.NewGuid();
        using var db = CreateDb();

        var r1 = NewReport(tenantId);
        r1.RadsAssessments.Add(new RadsAssessment { TenantId = tenantId, Family = "BI-RADS", Category = "4A" });
        r1.Measurements.Add(new ReportMeasurement { TenantId = tenantId, Label = "mass", Value = 20, LesionKey = "L1" });
        var r2 = NewReport(tenantId);
        r2.RadsAssessments.Add(new RadsAssessment { TenantId = tenantId, Family = "BI-RADS", Category = "2" });
        r2.Measurements.Add(new ReportMeasurement { TenantId = tenantId, Label = "mass (follow-up)", Value = 14, LesionKey = "L1" });
        db.Reports.AddRange(r1, r2);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        // RADS analytics (RADS-008): query by (tenant, family, category).
        var birads4a = await db.RadsAssessments
            .CountAsync(a => a.TenantId == tenantId && a.Family == "BI-RADS" && a.Category == "4A");
        Assert.Equal(1, birads4a);

        // Longitudinal lesion tracking (COMP-003/004): follow a lesion across reports.
        var lesionTrack = await db.ReportMeasurements
            .Where(m => m.TenantId == tenantId && m.LesionKey == "L1")
            .OrderByDescending(m => m.Value)
            .Select(m => m.Value)
            .ToListAsync();
        Assert.Equal(new[] { 20d, 14d }, lesionTrack);
    }

    [Fact]
    public async Task Deleting_Report_Cascades_To_Structured_Children()
    {
        var tenantId = Guid.NewGuid();
        using var db = CreateDb();

        var report = NewReport(tenantId);
        report.RadsAssessments.Add(new RadsAssessment { TenantId = tenantId, Family = "LI-RADS", Category = "LR-4" });
        report.Measurements.Add(new ReportMeasurement { TenantId = tenantId, Label = "HCC", Value = 31 });
        db.Reports.Add(report);
        await db.SaveChangesAsync();

        db.Reports.Remove(report);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        Assert.Equal(0, await db.RadsAssessments.CountAsync());
        Assert.Equal(0, await db.ReportMeasurements.CountAsync());
    }
}
