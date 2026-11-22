using Hangfire;
using Microsoft.EntityFrameworkCore;
using RadioPad.Api.Jobs;
using RadioPad.Domain.Entities;
using Xunit;
using Xunit.Abstractions;

namespace RadioPad.Api.Tests.MariaDb;

/// <summary>
/// Runs against a real MariaDB (see <see cref="MySqlFactAttribute"/>). Nothing here can be exercised on SQLite:
/// it is the only automated check that the MySQL model shape, the Hangfire storage and the matched-row semantics
/// the app relies on actually work.
/// </summary>
[Trait("Category", "MySql")]
public class MySqlSmokeTests
{
    private readonly ITestOutputHelper _output;

    public MySqlSmokeTests(ITestOutputHelper output) => _output = output;

    /// <summary>Public + static so Hangfire can serialise a call to it.</summary>
    public static void Noop() { }

    [MySqlFact]
    public async Task Model_CreatesOnBlankDatabase_IncludingTablesSqliteGetsFromBridges()
    {
        await using var database = await MySqlTestDatabase.CreateAsync();
        await using var db = database.CreateContext();

        // Validates the DDL itself (index key lengths <= 3072 B, row sizes, collations) even before migrations exist.
        await db.Database.EnsureCreatedAsync();

        // On SQLite these are created by EnsureSchema bridges, not migrations; on MySQL the model must provide them.
        Assert.Equal(0, await db.GlobalUsers.CountAsync());
        Assert.Equal(0, await db.AuthSessions.CountAsync());
        Assert.Equal(0, await db.Snippets.CountAsync());
        Assert.Equal(0, await db.Reports.CountAsync());
    }

    [MySqlFact]
    public async Task Migrations_ApplyToBlankDatabase()
    {
        await using var database = await MySqlTestDatabase.CreateAsync();
        await using var db = database.CreateContext();

        if (!db.Database.GetMigrations().Any())
        {
            // A first CI run happens before the 'mysql-migrations' workflow has produced anything.
            _output.WriteLine("No MySQL migrations exist yet — run the 'mysql-migrations' workflow (Actions > mysql-migrations > Run workflow) and merge its branch.");
            return;
        }

        await db.Database.MigrateAsync();
        Assert.Empty(await db.Database.GetPendingMigrationsAsync());
        Assert.Equal(0, await db.Reports.CountAsync());
    }

    [MySqlFact]
    public async Task UniqueIndexes_AreCaseSensitive_LikeSqlite_ButStillRejectTrueDuplicates()
    {
        await using var database = await MySqlTestDatabase.CreateAsync();
        await using var db = database.CreateContext();
        await db.Database.EnsureCreatedAsync();

        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        // SQLite compares binary: "Hypo" and "hypo" are different keys. The default _ci collation would collide.
        db.UserCorrections.AddRange(
            new UserCorrection { TenantId = tenantId, UserId = userId, From = "hypo dense", To = "hypodense" },
            new UserCorrection { TenantId = tenantId, UserId = userId, From = "Hypo dense", To = "Hypodense" });
        db.Lexicons.AddRange(
            new TenantLexicon { TenantId = tenantId, Term = "aneurysm" },
            new TenantLexicon { TenantId = tenantId, Term = "Aneurysm" });
        await db.SaveChangesAsync();

        await using var again = database.CreateContext();
        again.UserCorrections.Add(new UserCorrection { TenantId = tenantId, UserId = userId, From = "hypo dense", To = "x" });
        await Assert.ThrowsAnyAsync<DbUpdateException>(() => again.SaveChangesAsync());

        await using var again2 = database.CreateContext();
        again2.Lexicons.Add(new TenantLexicon { TenantId = tenantId, Term = "aneurysm" });
        await Assert.ThrowsAnyAsync<DbUpdateException>(() => again2.SaveChangesAsync());
    }

    [MySqlFact]
    public async Task DateTimeOffset_RoundTripsAsTheSameInstant()
    {
        await using var database = await MySqlTestDatabase.CreateAsync();
        await using var db = database.CreateContext();
        await db.Database.EnsureCreatedAsync();

        // Microsecond precision (MySQL datetime(6)) and a non-UTC offset: the instant must survive, whatever the offset.
        var when = new DateTimeOffset(2026, 11, 22, 10, 30, 15, TimeSpan.Zero).AddTicks(1234560).ToOffset(TimeSpan.FromHours(5));
        var job = new AiJob
        {
            TenantId = Guid.NewGuid(), ReportId = Guid.NewGuid(), UserId = Guid.NewGuid(),
            Kind = "ai", Mode = "impression", StartedAt = when, CreatedAt = when,
        };
        db.AiJobs.Add(job);
        await db.SaveChangesAsync();

        await using var reader = database.CreateContext();
        var read = await reader.AiJobs.AsNoTracking().SingleAsync(j => j.Id == job.Id);
        Assert.Equal(when.UtcDateTime, read.StartedAt!.Value.UtcDateTime);
        Assert.Equal(when.UtcDateTime, read.CreatedAt.UtcDateTime);
    }

    [MySqlFact]
    public async Task AiJobStatusConcurrencyToken_ConflictsOnARealRace_ButNotOnAnUnchangedRow()
    {
        await using var database = await MySqlTestDatabase.CreateAsync();
        await using (var seed = database.CreateContext())
        {
            await seed.Database.EnsureCreatedAsync();
            seed.AiJobs.Add(new AiJob { Id = Guid.Parse("11111111-1111-1111-1111-111111111111"), Kind = "ai", Mode = "impression", Status = "queued" });
            await seed.SaveChangesAsync();
        }
        var id = Guid.Parse("11111111-1111-1111-1111-111111111111");

        // 1) A real race: the loser's UPDATE ... WHERE Status = 'queued' matches no row -> concurrency exception.
        await using (var a = database.CreateContext())
        await using (var b = database.CreateContext())
        {
            var winner = await a.AiJobs.SingleAsync(j => j.Id == id);
            var loser = await b.AiJobs.SingleAsync(j => j.Id == id);
            winner.Status = "running";
            await a.SaveChangesAsync();
            loser.Status = "cancelled";
            await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => b.SaveChangesAsync());
        }

        // 2) Matched-row semantics (UseAffectedRows=false): an UPDATE that rewrites a column with the value it
        //    already holds still MATCHES the row. With "affected rows" semantics this would be a false conflict.
        await using (var stale = database.CreateContext())
        await using (var other = database.CreateContext())
        {
            var staleJob = await stale.AiJobs.SingleAsync(j => j.Id == id);   // Error == null
            var otherJob = await other.AiJobs.SingleAsync(j => j.Id == id);
            otherJob.Error = "boom";
            await other.SaveChangesAsync();
            staleJob.Error = "boom";                                          // differs from ITS original, equals the DB value
            await stale.SaveChangesAsync();                                   // must not throw
        }
    }

    [MySqlFact]
    public async Task HangfireMySqlStorage_QueuedJobs_SurviveAStorageRestart()
    {
        await using var database = await MySqlTestDatabase.CreateAsync();

        // Built through the same factory HangfireSetup uses in production.
        JobStorage first = HangfireSetup.CreateMySqlStorage(database.ConnectionString);
        new BackgroundJobClient(first).Enqueue(() => Noop());
        Assert.Equal(1L, first.GetMonitoringApi().EnqueuedCount("default"));
        (first as IDisposable)?.Dispose();

        // A brand-new storage instance on the same database == a process restart. In-memory storage would show 0.
        JobStorage second = HangfireSetup.CreateMySqlStorage(database.ConnectionString);
        Assert.Equal(1L, second.GetMonitoringApi().EnqueuedCount("default"));
        (second as IDisposable)?.Dispose();
    }
}
