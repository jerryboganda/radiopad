using RadioPad.Api.Jobs;
using Xunit;

namespace RadioPad.Api.Tests.Jobs;

/// <summary>
/// PR-N1 — the Hangfire storage sniff must mirror the EF Core connection-string
/// sniff exactly: Host=/Server= (case-insensitive) => Postgres, everything else
/// (SQLite dev + the desktop sidecar) => InMemory.
/// </summary>
public class HangfireStorageSelectorTests
{
    [Theory]
    [InlineData("Host=localhost;Port=5432;Database=radiopad;Username=u;Password=p")]
    [InlineData("Server=db.internal;Port=5432;Database=radiopad")]
    [InlineData("HOST=localhost")]        // case-insensitive
    [InlineData("server=db;Database=x")]  // case-insensitive
    public void PostgresStyleConnectionStrings_SelectPostgres(string conn)
        => Assert.Equal(HangfireStorageSelector.Kind.Postgres, HangfireStorageSelector.Select(conn));

    [Theory]
    [InlineData("Data Source=radiopad.dev.db")]
    [InlineData("Data Source=:memory:")]
    [InlineData("Data Source=/tmp/radiopad-it.db")]
    [InlineData("")]
    [InlineData("   ")]
    public void SqliteOrBlankConnectionStrings_SelectInMemory(string conn)
        => Assert.Equal(HangfireStorageSelector.Kind.InMemory, HangfireStorageSelector.Select(conn));
}
