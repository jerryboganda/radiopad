using Microsoft.EntityFrameworkCore;
using MySqlConnector;
using RadioPad.Infrastructure.Persistence;

namespace RadioPad.Api.Tests.MariaDb;

/// <summary>
/// A throw-away MariaDB database (<c>radiopad_test_&lt;guid&gt;</c>) on the server named by
/// <see cref="MySqlFactAttribute.EnvVar"/>. Created per test so tests never see each other's rows; dropped on dispose.
/// </summary>
public sealed class MySqlTestDatabase : IAsyncDisposable
{
    private readonly string _adminConnectionString;

    public string Name { get; }

    /// <summary>The connection string for THIS database, already normalised like the app does (UseAffectedRows=false, pool cap).</summary>
    public string ConnectionString { get; }

    private MySqlTestDatabase(string adminConnectionString, string name, string connectionString)
    {
        _adminConnectionString = adminConnectionString;
        Name = name;
        ConnectionString = connectionString;
    }

    public static async Task<MySqlTestDatabase> CreateAsync()
    {
        var admin = Environment.GetEnvironmentVariable(MySqlFactAttribute.EnvVar)
            ?? throw new InvalidOperationException($"{MySqlFactAttribute.EnvVar} is not set.");
        var name = $"radiopad_test_{Guid.NewGuid():N}";

        await using (var connection = new MySqlConnection(admin))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = $"CREATE DATABASE `{name}` CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci";
            await command.ExecuteNonQueryAsync();
        }

        var own = new MySqlConnectionStringBuilder(admin) { Database = name }.ConnectionString;
        return new MySqlTestDatabase(admin, name, DbProviderSelector.PrepareMySqlConnectionString(own, isProduction: false));
    }

    /// <summary>A new context on this database. The caller disposes it; the schema is NOT created.</summary>
    public MySqlRadioPadDbContext CreateContext() => new(
        new DbContextOptionsBuilder<MySqlRadioPadDbContext>()
            .UseMySql(ConnectionString, DbProviderSelector.MySqlServerVersion).Options);

    public async ValueTask DisposeAsync()
    {
        MySqlConnection.ClearAllPools(); // no pooled connection may keep the dropped database alive
        try
        {
            await using var connection = new MySqlConnection(_adminConnectionString);
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = $"DROP DATABASE IF EXISTS `{Name}`";
            await command.ExecuteNonQueryAsync();
        }
        catch (MySqlException)
        {
            // best effort: the CI service container is thrown away anyway
        }
    }
}
