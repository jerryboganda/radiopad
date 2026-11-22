using Microsoft.EntityFrameworkCore;

namespace RadioPad.Infrastructure.Persistence;

public enum DbProvider { Sqlite, Postgres, MySql }

/// <summary>
/// The one place that decides which database provider a deployment uses, shared by EF Core and Hangfire
/// so they can never disagree. <c>RADIOPAD_DB_PROVIDER</c> (sqlite | postgres | mysql | mariadb) wins; when it
/// is unset the legacy connection-string sniff applies unchanged (<c>Host=</c> / <c>Server=</c> => Postgres,
/// anything else => SQLite — the desktop sidecar's <c>Data Source=</c> path).
/// </summary>
public static class DbProviderSelector
{
    public const string ProviderEnvVar = "RADIOPAD_DB_PROVIDER";

    /// <summary>UNSAFE escape hatch: set to <c>1</c> to allow an unencrypted MySQL connection in production.</summary>
    public const string AllowInsecureEnvVar = "RADIOPAD_DB_ALLOW_INSECURE";

    /// <summary>Hostinger serves MariaDB 10.11+. A fixed version avoids <c>ServerVersion.AutoDetect</c>'s network call.</summary>
    public static readonly ServerVersion MySqlServerVersion = new MariaDbServerVersion(new Version(10, 11, 0));

    // Hostinger caps a database user at 75 connections and Hangfire adds its own workers, so EF and
    // Hangfire (which share this string) each stay at 20. MySqlConnector's library default is 100.
    private const uint DefaultMaxPoolSize = 20;

    /// <summary>
    /// <paramref name="providerEnv"/> is the value of <c>RADIOPAD_DB_PROVIDER</c>; <c>null</c> reads the real
    /// environment variable, an empty string means "unset" (use the legacy sniff). An unknown value throws —
    /// a typo must not silently fall back to SQLite in production.
    /// </summary>
    public static DbProvider Resolve(string? connectionString, string? providerEnv = null)
    {
        providerEnv ??= Environment.GetEnvironmentVariable(ProviderEnvVar);
        if (!string.IsNullOrWhiteSpace(providerEnv))
        {
            return providerEnv.Trim().ToLowerInvariant() switch
            {
                "sqlite" => DbProvider.Sqlite,
                "postgres" or "postgresql" or "npgsql" => DbProvider.Postgres,
                "mysql" or "mariadb" => DbProvider.MySql,
                _ => throw new InvalidOperationException(
                    $"{ProviderEnvVar}='{providerEnv}' is not valid. Use sqlite, postgres, mysql or mariadb."),
            };
        }

        // Legacy sniff, unchanged: Host= / Server= => Postgres, everything else (incl. blank) => SQLite.
        if (!string.IsNullOrWhiteSpace(connectionString) &&
            (connectionString.Contains("Host=", StringComparison.OrdinalIgnoreCase) ||
             connectionString.Contains("Server=", StringComparison.OrdinalIgnoreCase)))
            return DbProvider.Postgres;

        return DbProvider.Sqlite;
    }

    /// <summary>
    /// Validates and normalises a MySQL/MariaDB connection string. In production it must encrypt the link
    /// (SslMode Required / VerifyCA / VerifyFull — the database holds PHI and may be reached over the public
    /// internet) unless <c>RADIOPAD_DB_ALLOW_INSECURE=1</c>. Always forces <c>UseAffectedRows=false</c>: the
    /// AiJob concurrency token needs MATCHED-row counts. Caps the pool at 20 unless the string sets its own.
    /// </summary>
    /// <param name="allowInsecure">Test seam: <c>null</c> reads <c>RADIOPAD_DB_ALLOW_INSECURE</c>.</param>
    public static string PrepareMySqlConnectionString(string connectionString, bool isProduction, bool? allowInsecure = null)
    {
        allowInsecure ??= Environment.GetEnvironmentVariable(AllowInsecureEnvVar) == "1";
        var b = new MySqlConnector.MySqlConnectionStringBuilder(connectionString);

        var encrypted = b.SslMode is MySqlConnector.MySqlSslMode.Required or MySqlConnector.MySqlSslMode.VerifyCA or MySqlConnector.MySqlSslMode.VerifyFull;
        if (isProduction && !encrypted && allowInsecure != true)
            throw new InvalidOperationException(
                "The MySQL connection string must set SslMode=Required, VerifyCA or VerifyFull in production " +
                "(patient data must not cross the network unencrypted). " +
                $"Setting {AllowInsecureEnvVar}=1 overrides this and is UNSAFE.");

        b.UseAffectedRows = false;

        // ponytail: "unset" is detected as "still the library default (100)", so an explicit 100 is also
        // capped to 20. Set any other number to keep it.
        if (b.MaximumPoolSize == new MySqlConnector.MySqlConnectionStringBuilder().MaximumPoolSize)
            b.MaximumPoolSize = DefaultMaxPoolSize;

        return b.ConnectionString;
    }
}
