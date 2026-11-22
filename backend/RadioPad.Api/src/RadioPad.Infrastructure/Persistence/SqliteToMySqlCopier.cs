using System.Data.Common;
using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using RadioPad.Application.Services.Kms;
using RadioPad.Infrastructure.Security;

namespace RadioPad.Infrastructure.Persistence;

/// <param name="DryRun">Count the source tables only; touch nothing on the target.</param>
/// <param name="AllowInsecure">Test seam for <see cref="DbProviderSelector.PrepareMySqlConnectionString"/>;
/// <c>null</c> reads <c>RADIOPAD_DB_ALLOW_INSECURE</c>.</param>
/// <param name="Log">Progress sink. Receives table names and counts only — never row contents (PHI).</param>
public sealed record CopyOptions(bool DryRun = false, int BatchSize = 500, bool? AllowInsecure = null, Action<string>? Log = null);

public sealed record CopyTableResult(string Table, long Source, long Target, bool Ok);

public sealed record CopyReport(bool DryRun, IReadOnlyList<CopyTableResult> Tables)
{
    public bool Ok => Tables.All(t => t.Ok);
}

/// <summary>
/// One-shot SQLite -> MySQL/MariaDB data copy for the cutover. EF Core does the type mapping, so anything the
/// running app can read, the copier can read (no raw-SQL value formats to get wrong).
///
/// <para><b>Key material:</b> the AES-GCM column converters decrypt on read and re-encrypt on write, so the
/// process must see the SAME <c>RADIOPAD_COLUMN_KEY_REF</c> / <c>RADIOPAD_COLUMN_KEY_WRAPPED</c> /
/// <c>RADIOPAD_COLUMN_KEK</c> as the API that wrote the SQLite file — otherwise secrets are copied as garbage.</para>
///
/// <para><b>Source:</b> opened read-only. Run it on a COPY of the database taken while the API is stopped; a WAL-mode
/// file also needs its <c>-wal</c>/<c>-shm</c> siblings copied or checkpointed first.</para>
///
/// <para><b>Target:</b> must already have its schema (migrations applied, or created from the model) and every table
/// empty — the copier refuses otherwise rather than merging into live data.</para>
/// </summary>
public static class SqliteToMySqlCopier
{
    private static readonly MethodInfo CountMethod =
        typeof(SqliteToMySqlCopier).GetMethod(nameof(CountAsync), BindingFlags.NonPublic | BindingFlags.Static)!;
    private static readonly MethodInfo CopyMethod =
        typeof(SqliteToMySqlCopier).GetMethod(nameof(CopyAsync), BindingFlags.NonPublic | BindingFlags.Static)!;

    public static async Task<CopyReport> RunAsync(
        string sqlitePath, string mysqlConnectionString, CopyOptions options, CancellationToken ct)
    {
        if (!File.Exists(sqlitePath))
            throw new FileNotFoundException("SQLite source database not found.", sqlitePath);
        var batchSize = Math.Max(1, options.BatchSize);
        var log = options.Log ?? (_ => { });

        await using var src = new RadioPadDbContext(new DbContextOptionsBuilder<RadioPadDbContext>()
            .UseSqlite($"Data Source={sqlitePath};Mode=ReadOnly").Options);

        // isProduction: true — a cutover tool must never ship patient data over an unencrypted link by default.
        var prepared = DbProviderSelector.PrepareMySqlConnectionString(mysqlConnectionString, isProduction: true, options.AllowInsecure);
        await using var dst = new MySqlRadioPadDbContext(new DbContextOptionsBuilder<MySqlRadioPadDbContext>()
            .UseMySql(prepared, DbProviderSelector.MySqlServerVersion).Options);
        dst.ChangeTracker.AutoDetectChangesEnabled = false;

        var (order, cyclic) = OrderByDependency(dst.Model.GetEntityTypes()
            .Where(t => !t.IsOwned() && t.BaseType is null && !t.HasSharedClrType
                        && t.FindPrimaryKey() is not null && t.GetTableName() is not null)
            .ToList());

        if (!options.DryRun)
        {
            InitializeColumnEncryption();
            if (dst.Database.GetMigrations().Any() && (await dst.Database.GetPendingMigrationsAsync(ct)).Any())
                throw new InvalidOperationException(
                    "The target database has unapplied migrations. Start the API once against it (it migrates on boot), stop it, then run the copy again.");

            foreach (var t in order)
            {
                long existing;
                try { existing = await InvokeAsync(CountMethod, t.ClrType, dst, ct); }
                catch (DbException ex)
                {
                    throw new InvalidOperationException(
                        $"The target database has no table '{t.GetTableName()}'. Apply the MySQL migrations first (start the API once against it, then stop it).", ex);
                }
                if (existing > 0)
                    throw new InvalidOperationException(
                        $"Refusing to copy: target table '{t.GetTableName()}' already holds {existing} row(s). The target must be a blank, freshly migrated database.");
            }
        }

        var results = new List<CopyTableResult>();
        // Self-references and FK cycles cannot be ordered, so for those the session skips FK checks; the row-count
        // comparison below still catches a short copy.
        var fkOff = !options.DryRun && cyclic;
        if (fkOff)
        {
            await dst.Database.OpenConnectionAsync(ct); // FOREIGN_KEY_CHECKS is per connection: keep this one open
            await dst.Database.ExecuteSqlRawAsync("SET FOREIGN_KEY_CHECKS=0", ct);
        }
        try
        {
            foreach (var t in order)
            {
                var table = t.GetTableName()!;
                var sourceCount = await InvokeAsync(CountMethod, t.ClrType, src, ct);
                if (options.DryRun)
                {
                    log($"{table}: {sourceCount} row(s) would be copied");
                    results.Add(new CopyTableResult(table, sourceCount, 0, true));
                    continue;
                }

                if (sourceCount > 0)
                    await InvokeAsync(CopyMethod, t.ClrType, src, dst, batchSize, ct);
                var targetCount = await InvokeAsync(CountMethod, t.ClrType, dst, ct);
                var ok = sourceCount == targetCount;
                log($"{table}: {sourceCount} -> {targetCount} {(ok ? "OK" : "MISMATCH")}");
                results.Add(new CopyTableResult(table, sourceCount, targetCount, ok));
            }
        }
        finally
        {
            if (fkOff)
            {
                await dst.Database.ExecuteSqlRawAsync("SET FOREIGN_KEY_CHECKS=1", CancellationToken.None);
                await dst.Database.CloseConnectionAsync();
            }
        }

        return new CopyReport(options.DryRun, results);
    }

    // Copy mode runs before the web host exists (no DI), so build the column encryptor the way Program.cs does.
    // ponytail: only the env: and local: KMS schemes work here; aws:/azkv:/gcp: need the host's DI — extend if used.
    private static void InitializeColumnEncryption()
    {
        var keyRef = Environment.GetEnvironmentVariable("RADIOPAD_COLUMN_KEY_REF");
        var wrapped = Environment.GetEnvironmentVariable("RADIOPAD_COLUMN_KEY_WRAPPED");
        if (string.IsNullOrWhiteSpace(keyRef) || string.IsNullOrWhiteSpace(wrapped))
        {
            if (string.Equals(Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT"), "Production", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException(
                    "RADIOPAD_COLUMN_KEY_REF and RADIOPAD_COLUMN_KEY_WRAPPED must be set to the same values as the API, or encrypted columns would be copied with the wrong key.");
            return; // dev/test: the accessor's deterministic dev key applies on both sides
        }
        var resolver = new DefaultKmsResolver(new IKmsProvider[] { new EnvKmsProvider(), new LocalKmsProvider() });
        ColumnEncryptorAccessor.Current = AesGcmColumnEncryptor
            .CreateAsync(resolver, keyRef, wrapped, CancellationToken.None).GetAwaiter().GetResult();
    }

    private static Task<long> InvokeAsync(MethodInfo method, Type entityClrType, params object[] args) =>
        (Task<long>)method.MakeGenericMethod(entityClrType).Invoke(null, args)!;

    private static Task<long> CountAsync<T>(DbContext db, CancellationToken ct) where T : class =>
        db.Set<T>().IgnoreQueryFilters().LongCountAsync(ct);

    private static async Task<long> CopyAsync<T>(DbContext src, DbContext dst, int batchSize, CancellationToken ct) where T : class
    {
        long copied = 0;
        var buffer = new List<T>(batchSize);

        async Task FlushAsync()
        {
            dst.Set<T>().AddRange(buffer);
            await dst.SaveChangesAsync(ct);
            dst.ChangeTracker.Clear();
            copied += buffer.Count;
            buffer.Clear();
        }

        // Streamed, untracked, no Include: owned types (Report.Study) come with their owner; FK columns are plain values.
        await foreach (var row in src.Set<T>().IgnoreQueryFilters().AsNoTracking().AsAsyncEnumerable().WithCancellation(ct))
        {
            buffer.Add(row);
            if (buffer.Count >= batchSize) await FlushAsync();
        }
        if (buffer.Count > 0) await FlushAsync();
        return copied;
    }

    /// <summary>Principals before dependents. <c>Cyclic</c> is true when a self-reference or FK cycle makes a strict order impossible.</summary>
    private static (List<IEntityType> Order, bool Cyclic) OrderByDependency(List<IEntityType> types)
    {
        var ordered = new List<IEntityType>();
        var state = new Dictionary<IEntityType, int>(); // 1 = visiting, 2 = done
        var cyclic = false;

        void Visit(IEntityType t)
        {
            if (state.TryGetValue(t, out var s))
            {
                if (s == 1) cyclic = true;
                return;
            }
            state[t] = 1;
            foreach (var fk in t.GetForeignKeys())
            {
                var principal = fk.PrincipalEntityType;
                if (principal == t) cyclic = true;
                else if (types.Contains(principal)) Visit(principal);
            }
            state[t] = 2;
            ordered.Add(t);
        }

        foreach (var t in types) Visit(t);
        return (ordered, cyclic);
    }
}
