using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using RadioPad.Domain.Entities;
using RadioPad.Infrastructure.Persistence;
using Xunit;

namespace RadioPad.Api.Tests.MariaDb;

[Trait("Category", "MySql")]
public class SqliteToMySqlCopierTests
{
    private static string Table<T>(DbContext db) => db.Model.FindEntityType(typeof(T))!.GetTableName()!;

    [MySqlFact]
    public async Task Copy_MovesEveryTable_KeepsValues_AndRefusesANonEmptyTarget()
    {
        var sqlitePath = Path.Combine(Path.GetTempPath(), $"radiopad-copy-{Guid.NewGuid():N}.db");
        await using var mysql = await MySqlTestDatabase.CreateAsync();
        try
        {
            // Microsecond-aligned: SQLite keeps 100 ns ticks, MySQL datetime(6) keeps microseconds.
            var stamp = new DateTimeOffset(2026, 11, 22, 9, 15, 30, TimeSpan.Zero).AddTicks(7654320);
            var tenantId = Guid.NewGuid();
            var userId = Guid.NewGuid();
            var reportId = Guid.NewGuid();

            // ---- seed the SQLite source
            await using (var src = new RadioPadDbContext(new DbContextOptionsBuilder<RadioPadDbContext>()
                             .UseSqlite($"Data Source={sqlitePath}").Options))
            {
                await src.Database.EnsureCreatedAsync();
                src.Tenants.Add(new Tenant { Id = tenantId, Slug = "copy", DisplayName = "Copy Tenant", CreatedAt = stamp });
                src.Users.Add(new User { Id = userId, TenantId = tenantId, Email = "radiologist@copy.test", DisplayName = "Dr Copy", CreatedAt = stamp });
                src.Providers.Add(new ProviderConfig
                {
                    TenantId = tenantId, Name = "P", Adapter = "mock", ApiKeySecretRef = "env:COPY_TEST_KEY",
                    CostPerInputKToken = 0.123456m, Quality = 0.75m,
                });
                src.Reports.Add(new Report
                {
                    Id = reportId, TenantId = tenantId, CreatedByUserId = userId,
                    RadiologyId = "RAD-1", PatientName = "Jane Doe", PatientAge = 54, PatientGender = "F",
                    Findings = "Résumé — naïve ✓ 日本語",
                    Study = { Modality = "CT", BodyPart = "Chest" },
                });
                src.ReportVersions.Add(new ReportVersion { ReportId = reportId, Sequence = 1, AuthorUserId = userId, SnapshotJson = "{\"a\":1}" });
                src.DictationAudios.Add(new DictationAudio
                {
                    ReportId = reportId, StoragePath = "/data/uploads/x.webm", DurationSeconds = 12.5, UploadedAt = stamp, TranscribedAt = stamp,
                });
                src.Lexicons.AddRange(
                    new TenantLexicon { TenantId = tenantId, Term = "aneurysm" },
                    new TenantLexicon { TenantId = tenantId, Term = "Aneurysm" });
                src.UserCorrections.AddRange(
                    new UserCorrection { TenantId = tenantId, UserId = userId, From = "hypo dense", To = "hypodense" },
                    new UserCorrection { TenantId = tenantId, UserId = userId, From = "Hypo dense", To = "Hypodense" });
                await src.SaveChangesAsync();
            }
            SqliteConnection.ClearAllPools(); // release the file handle before the copier reopens it read-only

            // ---- blank target (schema from the model; production would use the generated migrations)
            await using (var blank = mysql.CreateContext())
                await blank.Database.EnsureCreatedAsync();

            // ---- dry run: counts only, writes nothing
            var dry = await SqliteToMySqlCopier.RunAsync(sqlitePath, mysql.ConnectionString,
                new CopyOptions(DryRun: true, AllowInsecure: true), CancellationToken.None);
            Assert.True(dry.Ok);
            await using (var check = mysql.CreateContext())
                Assert.Equal(0, await check.Reports.CountAsync());

            // ---- the real copy (batch size 1 forces several flushes)
            var report = await SqliteToMySqlCopier.RunAsync(sqlitePath, mysql.ConnectionString,
                new CopyOptions(BatchSize: 1, AllowInsecure: true), CancellationToken.None);
            Assert.True(report.Ok, string.Join("; ", report.Tables.Where(t => !t.Ok).Select(t => $"{t.Table} {t.Source}->{t.Target}")));

            await using var dst = mysql.CreateContext();
            Assert.Contains(report.Tables, t => t.Table == Table<Report>(dst) && t is { Source: 1, Target: 1 });
            Assert.Contains(report.Tables, t => t.Table == Table<TenantLexicon>(dst) && t is { Source: 2, Target: 2 });

            var copiedReport = await dst.Reports.AsNoTracking().SingleAsync(r => r.Id == reportId);
            Assert.Equal("Jane Doe", copiedReport.PatientName);
            Assert.Equal(54, copiedReport.PatientAge);
            Assert.Equal("Résumé — naïve ✓ 日本語", copiedReport.Findings);
            Assert.Equal("CT", copiedReport.Study.Modality);

            var copiedProvider = await dst.Providers.AsNoTracking().SingleAsync();
            Assert.Equal("env:COPY_TEST_KEY", copiedProvider.ApiKeySecretRef); // decrypted on read, re-encrypted on write
            Assert.Equal(0.123456m, copiedProvider.CostPerInputKToken);

            var audio = await dst.DictationAudios.AsNoTracking().SingleAsync();
            Assert.Equal(stamp.UtcDateTime, audio.UploadedAt.UtcDateTime);
            Assert.Equal(stamp.UtcDateTime, audio.TranscribedAt!.Value.UtcDateTime);

            Assert.Equal(new[] { "Aneurysm", "aneurysm" }, (await dst.Lexicons.Select(l => l.Term).ToListAsync()).Order(StringComparer.Ordinal).ToArray());

            // ---- a second run must refuse: the target is no longer blank
            var refused = await Assert.ThrowsAsync<InvalidOperationException>(() => SqliteToMySqlCopier.RunAsync(
                sqlitePath, mysql.ConnectionString, new CopyOptions(AllowInsecure: true), CancellationToken.None));
            Assert.Contains("already holds", refused.Message);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            try { File.Delete(sqlitePath); } catch { /* best effort */ }
        }
    }

    [MySqlFact]
    public async Task Copy_RefusesWhenTheTargetSchemaIsMissing()
    {
        var sqlitePath = Path.Combine(Path.GetTempPath(), $"radiopad-copy-{Guid.NewGuid():N}.db");
        await using var mysql = await MySqlTestDatabase.CreateAsync(); // blank: no tables at all
        try
        {
            await using (var src = new RadioPadDbContext(new DbContextOptionsBuilder<RadioPadDbContext>()
                             .UseSqlite($"Data Source={sqlitePath}").Options))
                await src.Database.EnsureCreatedAsync();
            SqliteConnection.ClearAllPools();

            await Assert.ThrowsAsync<InvalidOperationException>(() => SqliteToMySqlCopier.RunAsync(
                sqlitePath, mysql.ConnectionString, new CopyOptions(AllowInsecure: true), CancellationToken.None));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            try { File.Delete(sqlitePath); } catch { /* best effort */ }
        }
    }
}
