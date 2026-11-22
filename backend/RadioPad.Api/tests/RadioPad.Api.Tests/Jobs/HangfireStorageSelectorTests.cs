using RadioPad.Api.Jobs;
using Xunit;

namespace RadioPad.Api.Tests.Jobs;

/// <summary>
/// PR-N1 — the Hangfire storage sniff must mirror the EF Core connection-string
/// sniff exactly: Host=/Server= (case-insensitive) => Postgres, everything else
/// (SQLite dev + the desktop sidecar) => InMemory. RADIOPAD_DB_PROVIDER=mysql => MySql.
/// </summary>
public class HangfireStorageSelectorTests
{
    [Theory]
    [InlineData("Host=localhost;Port=5432;Database=radiopad;Username=u;Password=p")]
    [InlineData("Server=db.internal;Port=5432;Database=radiopad")]
    [InlineData("HOST=localhost")]        // case-insensitive
    [InlineData("server=db;Database=x")]  // case-insensitive
    public void PostgresStyleConnectionStrings_SelectPostgres(string conn)
        => Assert.Equal(HangfireStorageSelector.Kind.Postgres, HangfireStorageSelector.Select(conn, ""));

    [Theory]
    [InlineData("Data Source=radiopad.dev.db")]
    [InlineData("Data Source=:memory:")]
    [InlineData("Data Source=/tmp/radiopad-it.db")]
    [InlineData("")]
    [InlineData("   ")]
    public void SqliteOrBlankConnectionStrings_SelectInMemory(string conn)
        => Assert.Equal(HangfireStorageSelector.Kind.InMemory, HangfireStorageSelector.Select(conn, ""));

    [Theory]
    [InlineData("mysql")]
    [InlineData("MariaDB")]
    public void ExplicitMySqlProvider_SelectsMySql_EvenThoughServerEqualsLooksLikePostgres(string providerEnv)
        => Assert.Equal(HangfireStorageSelector.Kind.MySql,
            HangfireStorageSelector.Select("Server=db.example;Port=3306;Database=radiopad", providerEnv));

    [Fact]
    public void BlankConnectionString_IsInMemory_EvenWithMySqlProvider()
        => Assert.Equal(HangfireStorageSelector.Kind.InMemory, HangfireStorageSelector.Select("", "mysql"));
}
