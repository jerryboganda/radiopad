using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace RadioPad.Api.Services;

/// <summary>
/// Sets SQLite's busy timeout on every connection this DbContext opens, so a writer
/// blocked by another connection's lock waits and retries internally instead of
/// throwing immediately.
///
/// Without this, two connections writing to the same SQLite file within the same
/// instant — an HTTP request and the background <c>AiJobCoordinator</c> runner, say —
/// produced a near-instant <c>SqliteException: database is locked</c>, wrapped in a
/// <c>DbUpdateException</c> that callers guarding specifically against
/// <c>DbUpdateConcurrencyException</c> do not catch. That turned a few milliseconds
/// of ordinary write contention into a permanently failed AI job.
///
/// <c>SqliteConnectionStringBuilder.DefaultTimeout</c> looks like the fix but is not:
/// its default (30s) never produced the expected multi-second wait before failure —
/// failures stayed single-digit milliseconds, which is only possible if no busy
/// handler was actually registered on the connection. <c>PRAGMA busy_timeout</c> is
/// the unambiguous, driver-independent way to set it.
/// </summary>
public sealed class SqliteBusyTimeoutInterceptor : DbConnectionInterceptor
{
    private const int BusyTimeoutMs = 15_000;

    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = $"PRAGMA busy_timeout = {BusyTimeoutMs};";
        cmd.ExecuteNonQuery();
    }

    public override async Task ConnectionOpenedAsync(
        DbConnection connection, ConnectionEndEventData eventData, CancellationToken cancellationToken = default)
    {
        var cmd = connection.CreateCommand();
        await using var _ = cmd.ConfigureAwait(false);
        cmd.CommandText = $"PRAGMA busy_timeout = {BusyTimeoutMs};";
        await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }
}
