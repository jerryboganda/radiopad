using Microsoft.EntityFrameworkCore;
using RadioPad.Infrastructure.Persistence;

namespace RadioPad.Api.Services;

/// <summary>
/// Keeps a few pooled MySQL connections warm. The hosted database closes idle connections after 20 s and
/// opening a new TLS connection to it costs ~0.75 s, so the first request after a short pause paid 3-5 s.
/// Every 10 s this checks out N connections at once (so N distinct pooled connections are refreshed) and
/// runs a trivial statement. Registered for the MySQL provider only.
/// </summary>
public sealed class DbKeepAliveService : BackgroundService
{
    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<DbKeepAliveService> _log;
    private readonly int _connections;
    private readonly TimeSpan _interval;

    public DbKeepAliveService(IServiceScopeFactory scopes, ILogger<DbKeepAliveService> log)
    {
        _scopes = scopes;
        _log = log;
        _connections = Math.Clamp(int.TryParse(Environment.GetEnvironmentVariable("RADIOPAD_DB_KEEPALIVE_CONNECTIONS"), out var n) ? n : 4, 1, 10);
        _interval = TimeSpan.FromSeconds(Math.Clamp(int.TryParse(Environment.GetEnvironmentVariable("RADIOPAD_DB_KEEPALIVE_SEC"), out var s) ? s : 10, 2, 15));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try { await Task.Delay(_interval, stoppingToken); }
            catch (OperationCanceledException) { return; }

            var pings = Enumerable.Range(0, _connections).Select(_ => PingAsync(stoppingToken)).ToArray();
            try { await Task.WhenAll(pings); }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _log.LogDebug(ex, "DB keep-alive ping failed; the pool will reconnect on demand.");
            }
        }
    }

    private async Task PingAsync(CancellationToken ct)
    {
        using var scope = _scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RadioPadDbContext>();
        await db.Database.OpenConnectionAsync(ct);
        try
        {
            await Task.Delay(50, ct); // hold the connection briefly so the N pings land on N different pooled connections
            await db.Database.ExecuteSqlRawAsync("SELECT 1", ct);
        }
        finally { await db.Database.CloseConnectionAsync(); }
    }
}
