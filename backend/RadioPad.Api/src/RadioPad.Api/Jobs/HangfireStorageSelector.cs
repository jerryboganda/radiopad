using RadioPad.Infrastructure.Persistence;

namespace RadioPad.Api.Jobs;

/// <summary>
/// PR-N1 — pure decision that picks which Hangfire storage a deployment gets. It defers to
/// <see cref="DbProviderSelector"/> (the same call EF Core uses in <c>Program.cs</c>) so Hangfire and EF can
/// never disagree about the backing database: <c>RADIOPAD_DB_PROVIDER</c> wins, else the legacy sniff
/// (<c>Host=</c> / <c>Server=</c> =&gt; Postgres, anything else =&gt; the SQLite / desktop-sidecar case).
/// Kept as a stateless static so the decision is unit-testable without spinning up a host.
/// </summary>
public static class HangfireStorageSelector
{
    public enum Kind
    {
        /// <summary>Postgres deployment — Hangfire.PostgreSql in its own <c>hangfire</c> schema.</summary>
        Postgres,

        /// <summary>SQLite dev workstation or the desktop sidecar's bundled backend — Hangfire.InMemory.</summary>
        InMemory,

        /// <summary>MySQL/MariaDB deployment — Hangfire.MySqlStorage (jobs survive a restart).</summary>
        MySql,
    }

    /// <summary>
    /// A null/blank connection string resolves to <see cref="Kind.InMemory"/> (the throwaway-database
    /// default); otherwise the provider chosen by <see cref="DbProviderSelector.Resolve"/> maps 1:1.
    /// </summary>
    /// <param name="providerEnv">Test seam, passed straight to <see cref="DbProviderSelector.Resolve"/>
    /// (<c>null</c> reads <c>RADIOPAD_DB_PROVIDER</c>).</param>
    public static Kind Select(string connectionString, string? providerEnv = null)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
            return Kind.InMemory;

        return DbProviderSelector.Resolve(connectionString, providerEnv) switch
        {
            DbProvider.Postgres => Kind.Postgres,
            DbProvider.MySql => Kind.MySql,
            _ => Kind.InMemory,
        };
    }
}
