namespace RadioPad.Api.Jobs;

/// <summary>
/// PR-N1 — pure connection-string sniff that decides which Hangfire storage a
/// deployment gets. It mirrors the EF Core sniff in <c>Program.cs</c> exactly
/// (<c>Host=</c> / <c>Server=</c> =&gt; Postgres, anything else =&gt; the
/// SQLite / desktop-sidecar case) so Hangfire and EF never disagree about the
/// backing database. Kept as a stateless static so the decision is unit-testable
/// without spinning up a host.
/// </summary>
public static class HangfireStorageSelector
{
    public enum Kind
    {
        /// <summary>Postgres deployment — Hangfire.PostgreSql in its own <c>hangfire</c> schema.</summary>
        Postgres,

        /// <summary>SQLite dev workstation or the desktop sidecar's bundled backend — Hangfire.InMemory.</summary>
        InMemory,
    }

    /// <summary>
    /// Returns <see cref="Kind.Postgres"/> when the connection string looks like a
    /// PostgreSQL/SQL-server-style string (<c>Host=</c> or <c>Server=</c>,
    /// case-insensitive), otherwise <see cref="Kind.InMemory"/>. A null/blank
    /// string resolves to InMemory (the throwaway-database default).
    /// </summary>
    public static Kind Select(string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
            return Kind.InMemory;

        if (connectionString.Contains("Host=", StringComparison.OrdinalIgnoreCase) ||
            connectionString.Contains("Server=", StringComparison.OrdinalIgnoreCase))
            return Kind.Postgres;

        return Kind.InMemory;
    }
}
