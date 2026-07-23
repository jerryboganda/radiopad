using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using RadioPad.Api.Jobs;
using RadioPad.Domain.Entities;
using RadioPad.Domain.Enums;
using RadioPad.Infrastructure.Persistence;
using Xunit;

namespace RadioPad.Api.Tests.Integration;

/// <summary>
/// PR-N2 — <see cref="OrphanedDraftCleanupJob"/>. Drives <c>SweepAsync</c> directly. Confirms
/// only opt-in tenants' stale Drafts are archived (ArchivedAt set, Status untouched), that
/// non-Draft / recent / already-archived reports are left alone, that a per-report audit row is
/// written, that a disabled (days=0) tenant is skipped, and that the per-tenant batch cap holds.
/// </summary>
public class OrphanedDraftCleanupJobTests : IClassFixture<RadioPadAppFactory>
{
    private readonly RadioPadAppFactory _factory;
    public OrphanedDraftCleanupJobTests(RadioPadAppFactory f) => _factory = f;

    private static Report MakeReport(Guid tenantId, DateTimeOffset updatedAt, ReportStatus status = ReportStatus.Draft, DateTimeOffset? archivedAt = null) => new()
    {
        TenantId = tenantId,
        CreatedByUserId = Guid.NewGuid(),
        Status = status,
        Study = new StudyContext(),
        CreatedAt = updatedAt,
        UpdatedAt = updatedAt,
        ArchivedAt = archivedAt,
    };

    private async Task<Guid> NewTenantAsync(int draftAutoArchiveDays)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RadioPadDbContext>();
        var t = new Tenant
        {
            Slug = $"cleanup-{Guid.NewGuid():N}",
            DisplayName = "Cleanup",
            DraftAutoArchiveDays = draftAutoArchiveDays,
        };
        db.Tenants.Add(t);
        await db.SaveChangesAsync();
        return t.Id;
    }

    [Fact]
    public async Task Sweep_ArchivesOnlyStaleDrafts_ForOptInTenant_StatusUntouched_AndAudits()
    {
        var now = DateTimeOffset.UtcNow;
        var tenantId = await NewTenantAsync(7);

        Guid staleId, recentId, validatedId, alreadyArchivedId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RadioPadDbContext>();
            var stale = MakeReport(tenantId, now.AddDays(-10));
            var recent = MakeReport(tenantId, now.AddDays(-2));
            var validated = MakeReport(tenantId, now.AddDays(-10), ReportStatus.Validated);
            var alreadyArchived = MakeReport(tenantId, now.AddDays(-10), ReportStatus.Draft, now.AddDays(-1));
            db.Reports.AddRange(stale, recent, validated, alreadyArchived);
            await db.SaveChangesAsync();
            staleId = stale.Id;
            recentId = recent.Id;
            validatedId = validated.Id;
            alreadyArchivedId = alreadyArchived.Id;
        }

        var job = _factory.Services.GetRequiredService<OrphanedDraftCleanupJob>();
        await job.SweepAsync(now, CancellationToken.None);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RadioPadDbContext>();
            var stale = await db.Reports.SingleAsync(r => r.Id == staleId);
            Assert.NotNull(stale.ArchivedAt);
            Assert.Equal(ReportStatus.Draft, stale.Status); // status untouched

            Assert.Null((await db.Reports.SingleAsync(r => r.Id == recentId)).ArchivedAt);
            Assert.Null((await db.Reports.SingleAsync(r => r.Id == validatedId)).ArchivedAt);
            // Already-archived draft is left with its original ArchivedAt (not re-touched).
            var already = await db.Reports.SingleAsync(r => r.Id == alreadyArchivedId);
            Assert.NotNull(already.ArchivedAt);
            Assert.True(already.ArchivedAt < now.AddHours(-1));

            var auditedStale = await db.AuditEvents.AnyAsync(a =>
                a.Action == AuditAction.ReportDraftArchived && a.ReportId == staleId);
            Assert.True(auditedStale);
            var auditedRecent = await db.AuditEvents.AnyAsync(a =>
                a.Action == AuditAction.ReportDraftArchived && a.ReportId == recentId);
            Assert.False(auditedRecent);
        }
    }

    [Fact]
    public async Task Sweep_SkipsDisabledTenant()
    {
        var now = DateTimeOffset.UtcNow;
        var tenantId = await NewTenantAsync(0); // opt-out

        Guid staleId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RadioPadDbContext>();
            var stale = MakeReport(tenantId, now.AddDays(-100));
            db.Reports.Add(stale);
            await db.SaveChangesAsync();
            staleId = stale.Id;
        }

        var job = _factory.Services.GetRequiredService<OrphanedDraftCleanupJob>();
        await job.SweepAsync(now, CancellationToken.None);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RadioPadDbContext>();
            Assert.Null((await db.Reports.SingleAsync(r => r.Id == staleId)).ArchivedAt);
        }
    }

    [Fact]
    public async Task Sweep_RespectsPerTenantBatchCap()
    {
        var now = DateTimeOffset.UtcNow;
        var tenantId = await NewTenantAsync(1);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RadioPadDbContext>();
            var batch = Enumerable.Range(0, OrphanedDraftCleanupJob.MaxPerTenantPerRun + 1)
                .Select(_ => MakeReport(tenantId, now.AddDays(-30)))
                .ToList();
            db.Reports.AddRange(batch);
            await db.SaveChangesAsync();
        }

        var job = _factory.Services.GetRequiredService<OrphanedDraftCleanupJob>();
        await job.SweepAsync(now, CancellationToken.None);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RadioPadDbContext>();
            var archived = await db.Reports.CountAsync(r => r.TenantId == tenantId && r.ArchivedAt != null);
            var remaining = await db.Reports.CountAsync(r =>
                r.TenantId == tenantId && r.Status == ReportStatus.Draft && r.ArchivedAt == null);
            Assert.Equal(OrphanedDraftCleanupJob.MaxPerTenantPerRun, archived);
            Assert.Equal(1, remaining);
        }
    }
}
