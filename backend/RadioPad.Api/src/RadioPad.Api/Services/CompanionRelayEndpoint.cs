using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RadioPad.Api.Auth;
using RadioPad.Domain.Entities;
using RadioPad.Domain.Enums;
using RadioPad.Infrastructure.Identity;
using RadioPad.Infrastructure.Persistence;

namespace RadioPad.Api.Services;

/// <summary>
/// Raw-WebSocket relay terminal for <c>/ws/companion</c>. The desktop (host) and
/// phone (companion) each open a socket, authenticate with the RadioPad bearer in
/// the <c>access_token</c> query parameter (browsers/webviews cannot set WS
/// headers), then send a <c>hello</c> naming their role + session. Once both are
/// registered, JSON frames carrying a <c>type</c> field are forwarded verbatim to
/// the opposite peer. Dictation text is never persisted — this is a transient
/// relay only.
/// </summary>
public static class CompanionRelayEndpoint
{
    private const int MaxMessageBytes = 256 * 1024;
    private static readonly TimeSpan HelloTimeout = TimeSpan.FromSeconds(15);

    /// <summary>Wall-clock cadence for re-validating an already-open socket's auth +
    /// session liveness (Finding #13). Cheap: one indexed AuthSession lookup + one
    /// indexed CompanionSession lookup per tick.</summary>
    private static readonly TimeSpan RevalidateInterval = TimeSpan.FromSeconds(60);

    /// <summary>Hard cap on how long a companion session may carry relay traffic,
    /// enforced at connect and by the liveness watchdog (Finding #11). Matches the
    /// bearer's own 12h TTL so a paired session cannot outlive its credential.</summary>
    private static readonly TimeSpan MaxSessionLifetime = TimeSpan.FromHours(12);

    public static async Task HandleAsync(HttpContext context)
    {
        if (!context.WebSockets.IsWebSocketRequest)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }

        var db = context.RequestServices.GetRequiredService<RadioPadDbContext>();
        var env = context.RequestServices.GetRequiredService<IWebHostEnvironment>();
        var token = context.Request.Query["access_token"].FirstOrDefault();

        var auth = await ValidateBearerAsync(db, env, token, context.RequestAborted);
        if (auth is null)
        {
            // Reject the upgrade before accepting the socket — the browser surfaces
            // this as a failed WebSocket handshake.
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }

        var (tenant, user) = auth.Value;
        var registry = context.RequestServices.GetRequiredService<CompanionRelayRegistry>();
        var scopeFactory = context.RequestServices.GetRequiredService<IServiceScopeFactory>();
        var loggerFactory = context.RequestServices.GetRequiredService<ILoggerFactory>();
        var logger = loggerFactory.CreateLogger("CompanionRelay");

        using var socket = await context.WebSockets.AcceptWebSocketAsync();
        var ct = context.RequestAborted;

        // 1) Handshake: first frame must be a valid hello for a session this
        //    (tenant, user) owns and that is still live.
        var hello = await ReadHelloAsync(socket, ct);
        if (hello is null)
        {
            await AckFailAndCloseAsync(socket, "expected a hello message", ct);
            return;
        }

        var scope = context.RequestServices;
        var sessions = scope.GetRequiredService<CompanionSessionService>();
        var session = await sessions.GetAsync(tenant.Id, user.Id, hello.SessionId, ct);
        if (session is null)
        {
            await AckFailAndCloseAsync(socket, "unknown session", ct);
            return;
        }

        var now = DateTimeOffset.UtcNow;
        // Finding #11: a Paired session must still honour a hard maximum lifetime so
        // a relay cannot live forever (the credential it was created under expires).
        var live = (session.Status == CompanionSessionStatus.Paired
                || (session.Status == CompanionSessionStatus.Advertising && session.ExpiresAt > now))
            && (now - session.CreatedAt) < MaxSessionLifetime;
        if (!live)
        {
            await AckFailAndCloseAsync(socket, "session is not active", ct);
            return;
        }

        var deviceName = hello.Role == CompanionRelayRegistry.HostRole
            ? session.HostDeviceName
            : (session.CompanionDeviceName ?? "Phone");

        var peer = new CompanionRelayRegistry.Peer(hello.Role, socket, deviceName);
        var other = registry.AddPeer(session.Id, peer);

        // Finding #12: the owner may have ended the session (REST) in the race
        // window between the liveness check above and AddPeer. Re-read it; if it
        // was ended/expired meanwhile, do not leave an orphan peer on a dead
        // session — notify + tear this socket down.
        var recheck = await sessions.GetAsync(tenant.Id, user.Id, session.Id, ct);
        if (recheck is null
            || recheck.Status is CompanionSessionStatus.Ended or CompanionSessionStatus.Expired)
        {
            registry.RemovePeer(session.Id, peer);
            await peer.SendTextAsync(SessionEnded(), ct);
            await peer.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "session_ended");
            return;
        }

        await peer.SendTextAsync(Ack(true, null), ct);

        // Notify the peer already present (spec), and let this newcomer learn about
        // an already-connected peer too so both ends render the paired state.
        if (other is not null)
        {
            await other.SendTextAsync(PeerJoined(peer.DeviceName), ct);
            await peer.SendTextAsync(PeerJoined(other.DeviceName), ct);
        }

        // Findings #13 + #11: while the socket is open, a watchdog periodically
        // re-validates the bearer (catches logout / per-session revoke / user
        // deprovision / SessionEpoch bump) and the session (ended / expired / over
        // max lifetime). On failure it sends `session_ended` and cancels the read
        // loop, so an open socket can no longer outlive its credential.
        using var relayCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var watchdog = RevalidateLoopAsync(scopeFactory, env, token!, session.Id, peer, relayCts);

        // 2) Relay loop: forward any JSON object carrying a `type` field to the
        //    opposite peer, verbatim.
        try
        {
            while (socket.State == WebSocketState.Open && !relayCts.IsCancellationRequested)
            {
                var message = await ReadMessageAsync(socket, relayCts.Token);
                if (message is null)
                    break; // close frame or transport gone

                if (!IsRelayableJson(message))
                    continue; // ignore malformed / non-object / typeless frames

                var current = registry.GetPeers(session.Id);
                var target = peer.Role == CompanionRelayRegistry.HostRole ? current.Companion : current.Host;
                if (target is not null)
                    await target.SendTextAsync(message, relayCts.Token);
            }
        }
        catch (OperationCanceledException)
        {
            // client/host went away, or the watchdog revoked the session — teardown
        }
        catch (WebSocketException ex)
        {
            logger.LogDebug(ex, "Companion relay socket closed abnormally for session {SessionId}", session.Id);
        }
        finally
        {
            relayCts.Cancel();
            try { await watchdog; } catch { /* watchdog is best-effort */ }
            var remaining = registry.RemovePeer(session.Id, peer);
            if (remaining is not null)
                await SafeSendAsync(remaining, PeerLeft());
            await CloseAsync(socket, WebSocketCloseStatus.NormalClosure, "bye");
        }
    }

    /// <summary>
    /// Watchdog that re-validates an open relay socket every
    /// <see cref="RevalidateInterval"/> (Findings #13 + #11). Uses a fresh DI scope
    /// per tick (the request-scoped DbContext must not be reused across a long-lived
    /// socket), re-running the SAME bearer validation as connect plus a session
    /// liveness + max-lifetime check. On any failure it notifies the peer with
    /// <c>session_ended</c> and cancels the relay so the read loop unwinds.
    /// </summary>
    private static async Task RevalidateLoopAsync(
        IServiceScopeFactory scopeFactory,
        IWebHostEnvironment env,
        string token,
        Guid sessionId,
        CompanionRelayRegistry.Peer peer,
        CancellationTokenSource relayCts)
    {
        try
        {
            using var timer = new PeriodicTimer(RevalidateInterval);
            while (await timer.WaitForNextTickAsync(relayCts.Token))
            {
                using var scope = scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<RadioPadDbContext>();

                var auth = await ValidateBearerAsync(db, env, token, relayCts.Token);
                var ok = auth is not null;
                if (ok)
                {
                    var (t, u) = auth!.Value;
                    var sessions = scope.ServiceProvider.GetRequiredService<CompanionSessionService>();
                    var s = await sessions.GetAsync(t.Id, u.Id, sessionId, relayCts.Token);
                    var now = DateTimeOffset.UtcNow;
                    ok = s is not null
                        && (s.Status == CompanionSessionStatus.Paired
                            || (s.Status == CompanionSessionStatus.Advertising && s.ExpiresAt > now))
                        && (now - s.CreatedAt) < MaxSessionLifetime;
                }

                if (!ok)
                {
                    try { await peer.SendTextAsync(SessionEnded(), CancellationToken.None); }
                    catch { /* peer may already be gone */ }
                    relayCts.Cancel();
                    return;
                }
            }
        }
        catch (OperationCanceledException)
        {
            // relay ended — normal
        }
        catch
        {
            // never let the watchdog itself crash the connection
        }
    }

    /// <summary>
    /// Validates a RadioPad bearer supplied in the <c>access_token</c> query
    /// parameter, mirroring EVERY check <c>RadioPadBearerMiddleware</c> performs for
    /// <c>/api</c> — including the AuthSession revocation/expiry lookup (Finding #3),
    /// which <c>TryValidate</c> alone does not cover because per-session logout/revoke
    /// sets <c>RevokedAt</c> without bumping <c>SessionEpoch</c>. Returns the resolved
    /// (tenant, user) or <c>null</c> when the token is missing/invalid/revoked.
    /// Static + dependency-injected so the connect path and the re-validation
    /// watchdog share one implementation.
    /// </summary>
    private static async Task<(Tenant tenant, User user)?> ValidateBearerAsync(
        RadioPadDbContext db, IWebHostEnvironment env, string? token, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(token) || !token.StartsWith(RadioPadBearerTokens.Prefix, StringComparison.Ordinal))
            return null;

        if (!RadioPadBearerTokens.TryReadUnvalidatedContext(token, out var slug, out var email))
            return null;

        var tenant = await db.Tenants.FirstOrDefaultAsync(t => t.Slug == slug, ct);
        if (tenant is null)
            return null;

        var user = await db.Users.FirstOrDefaultAsync(u => u.Email == email && u.TenantId == tenant.Id, ct);
        if (user is null || !user.IsActive || user.LockedUntil > DateTimeOffset.UtcNow)
            return null;

        if (!RadioPadBearerTokens.TryValidate(token, slug, email, user.SessionEpoch, env, out _))
            return null;

        // Finding #3: reject a token whose AuthSession has been revoked or expired.
        var tokenHash = EnterpriseIdentityBridge.Sha256Hex(token);
        var session = await db.AuthSessions.AsNoTracking()
            .Where(s => s.TokenHash == tokenHash && s.TenantId == tenant.Id && s.UserId == user.Id)
            .Select(s => new { s.ExpiresAt, s.RevokedAt, s.SessionEpochAtIssue })
            .FirstOrDefaultAsync(ct);
        if (session is not null)
        {
            if (session.RevokedAt is not null)
                return null;
            if (session.ExpiresAt <= DateTimeOffset.UtcNow || session.SessionEpochAtIssue != user.SessionEpoch)
                return null;
        }

        return (tenant, user);
    }

    private sealed record Hello(string Role, Guid SessionId);

    private static async Task<Hello?> ReadHelloAsync(WebSocket socket, CancellationToken ct)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(HelloTimeout);

        string? raw;
        try
        {
            raw = await ReadMessageAsync(socket, timeoutCts.Token);
        }
        catch (OperationCanceledException)
        {
            return null;
        }

        if (raw is null)
            return null;

        try
        {
            using var doc = JsonDocument.Parse(raw);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return null;
            if (!root.TryGetProperty("type", out var type) || type.GetString() != "hello")
                return null;
            if (!root.TryGetProperty("role", out var roleEl))
                return null;

            var role = roleEl.GetString();
            if (role != CompanionRelayRegistry.HostRole && role != CompanionRelayRegistry.CompanionRole)
                return null;

            if (!root.TryGetProperty("sessionId", out var sidEl)
                || !Guid.TryParse(sidEl.GetString(), out var sessionId))
                return null;

            return new Hello(role, sessionId);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Reads one full text message, reassembling fragments. Returns null on
    /// a close frame, a binary frame, or when the size cap is exceeded.</summary>
    private static async Task<string?> ReadMessageAsync(WebSocket socket, CancellationToken ct)
    {
        var buffer = new byte[8192];
        using var ms = new MemoryStream();
        while (true)
        {
            WebSocketReceiveResult result;
            try
            {
                result = await socket.ReceiveAsync(buffer, ct);
            }
            catch (WebSocketException)
            {
                return null;
            }

            if (result.MessageType == WebSocketMessageType.Close)
                return null;
            if (result.MessageType != WebSocketMessageType.Text)
                return null;

            ms.Write(buffer, 0, result.Count);
            if (ms.Length > MaxMessageBytes)
                return null;

            if (result.EndOfMessage)
                break;
        }

        return Encoding.UTF8.GetString(ms.ToArray());
    }

    private static bool IsRelayableJson(string message)
    {
        try
        {
            using var doc = JsonDocument.Parse(message);
            return doc.RootElement.ValueKind == JsonValueKind.Object
                && doc.RootElement.TryGetProperty("type", out var type)
                && type.ValueKind == JsonValueKind.String;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static async Task AckFailAndCloseAsync(WebSocket socket, string message, CancellationToken ct)
    {
        try
        {
            var bytes = Encoding.UTF8.GetBytes(Ack(false, message));
            if (socket.State == WebSocketState.Open)
                await socket.SendAsync(bytes, WebSocketMessageType.Text, true, ct);
        }
        catch
        {
            // best effort — closing anyway
        }
        await CloseAsync(socket, WebSocketCloseStatus.PolicyViolation, message);
    }

    private static async Task SafeSendAsync(CompanionRelayRegistry.Peer peer, string json)
    {
        try
        {
            await peer.SendTextAsync(json, CancellationToken.None);
        }
        catch
        {
            // peer may have gone away concurrently — nothing to do
        }
    }

    private static async Task CloseAsync(WebSocket socket, WebSocketCloseStatus status, string description)
    {
        try
        {
            if (socket.State == WebSocketState.Open || socket.State == WebSocketState.CloseReceived)
                await socket.CloseAsync(status, description, CancellationToken.None);
        }
        catch
        {
            // socket already torn down
        }
    }

    // --- control-frame builders (kept tiny + allocation-cheap) ---

    private static string Ack(bool ok, string? message) =>
        message is null
            ? $"{{\"type\":\"ack\",\"ok\":{(ok ? "true" : "false")}}}"
            : $"{{\"type\":\"ack\",\"ok\":{(ok ? "true" : "false")},\"message\":{JsonSerializer.Serialize(message)}}}";

    private static string PeerJoined(string deviceName) =>
        $"{{\"type\":\"peer_joined\",\"deviceName\":{JsonSerializer.Serialize(deviceName)}}}";

    private static string PeerLeft() => "{\"type\":\"peer_left\"}";

    public static string SessionEnded() => "{\"type\":\"session_ended\"}";
}
