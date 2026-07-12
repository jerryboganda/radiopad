using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;

namespace RadioPad.Api.Services;

/// <summary>
/// In-memory registry of live companion relay sockets, keyed by session id. Each
/// session has one <c>host</c> slot (the desktop) and one <c>companion</c> slot
/// (the phone); the relay forwards frames from one slot to the other. Deliberately
/// process-local — the dictation stream is transient, a single backend instance
/// serves the relay, and a restart simply drops connections (both peers reconnect).
/// Nothing here is persisted; the durable session lives in the database via
/// <see cref="CompanionSessionService"/>.
/// </summary>
public sealed class CompanionRelayRegistry
{
    public const string HostRole = "host";
    public const string CompanionRole = "companion";

    /// <summary>One connected endpoint of a session. <see cref="SendAsync"/> is
    /// serialized because <see cref="WebSocket.SendAsync"/> forbids overlapping
    /// sends on the same socket.</summary>
    public sealed class Peer
    {
        private readonly SemaphoreSlim _sendLock = new(1, 1);

        public Peer(string role, WebSocket socket, string deviceName)
        {
            Role = role;
            Socket = socket;
            DeviceName = deviceName;
        }

        public string Role { get; }
        public WebSocket Socket { get; }
        public string DeviceName { get; }

        public async Task SendTextAsync(string json, CancellationToken ct)
        {
            if (Socket.State != WebSocketState.Open)
                return;
            await _sendLock.WaitAsync(ct);
            try
            {
                if (Socket.State != WebSocketState.Open)
                    return;
                var bytes = Encoding.UTF8.GetBytes(json);
                await Socket.SendAsync(bytes, WebSocketMessageType.Text, endOfMessage: true, ct);
            }
            finally
            {
                _sendLock.Release();
            }
        }

        /// <summary>Sends a close frame under the same lock as data sends (a close
        /// frame is still a send, so it must not overlap a concurrent
        /// <see cref="SendTextAsync"/>). Uses the output-only close so it never
        /// blocks waiting for the peer's acknowledgement.</summary>
        public async Task CloseOutputAsync(WebSocketCloseStatus status, string description)
        {
            await _sendLock.WaitAsync();
            try
            {
                if (Socket.State == WebSocketState.Open)
                    await Socket.CloseOutputAsync(status, description, CancellationToken.None);
            }
            catch
            {
                // socket already faulted/closing — nothing to recover
            }
            finally
            {
                _sendLock.Release();
            }
        }
    }

    private sealed class SessionSlots
    {
        public Peer? Host;
        public Peer? Companion;
    }

    private readonly ConcurrentDictionary<Guid, SessionSlots> _sessions = new();

    /// <summary>
    /// Registers <paramref name="peer"/> in its role slot, evicting any stale peer
    /// already there (a reconnect supersedes the old socket). Returns the peer
    /// currently occupying the OTHER slot, if any, so the caller can notify it.
    /// </summary>
    public Peer? AddPeer(Guid sessionId, Peer peer)
    {
        var slots = _sessions.GetOrAdd(sessionId, _ => new SessionSlots());
        Peer? evicted = null;
        Peer? other;
        lock (slots)
        {
            if (peer.Role == HostRole)
            {
                evicted = slots.Host;
                slots.Host = peer;
                other = slots.Companion;
            }
            else
            {
                evicted = slots.Companion;
                slots.Companion = peer;
                other = slots.Host;
            }
        }

        if (evicted is not null && !ReferenceEquals(evicted, peer))
            _ = CloseQuietlyAsync(evicted);

        return other;
    }

    /// <summary>
    /// Removes <paramref name="peer"/> from its slot (only if it is still the
    /// occupant — a superseded socket must not clear its replacement). Returns the
    /// peer in the other slot, if any, so the caller can send it <c>peer_left</c> —
    /// but ONLY when <paramref name="peer"/> was still the current occupant. A peer
    /// that had already been superseded by a reconnect is not a real disconnect, so
    /// it returns <c>null</c> and the surviving peer sees no spurious <c>peer_left</c>.
    /// </summary>
    public Peer? RemovePeer(Guid sessionId, Peer peer)
    {
        if (!_sessions.TryGetValue(sessionId, out var slots))
            return null;

        Peer? other = null;
        var empty = false;
        var wasOccupant = false;
        lock (slots)
        {
            if (peer.Role == HostRole && ReferenceEquals(slots.Host, peer))
            {
                slots.Host = null;
                wasOccupant = true;
            }
            else if (peer.Role == CompanionRole && ReferenceEquals(slots.Companion, peer))
            {
                slots.Companion = null;
                wasOccupant = true;
            }

            other = peer.Role == HostRole ? slots.Companion : slots.Host;
            empty = slots.Host is null && slots.Companion is null;
        }

        if (empty)
            _sessions.TryRemove(sessionId, out _);

        // A superseded peer (no longer its slot's occupant) was already replaced by a
        // reconnect — its teardown must not notify the surviving peer.
        return wasOccupant ? other : null;
    }

    /// <summary>Snapshots both peers of a session (either may be null).</summary>
    public (Peer? Host, Peer? Companion) GetPeers(Guid sessionId)
    {
        if (!_sessions.TryGetValue(sessionId, out var slots))
            return (null, null);
        lock (slots)
        {
            return (slots.Host, slots.Companion);
        }
    }

    /// <summary>
    /// Drops a session entirely — used when the owner ends it via REST. Returns the
    /// peers that were connected so the caller can notify + close them.
    /// </summary>
    public IReadOnlyList<Peer> RemoveSession(Guid sessionId)
    {
        if (!_sessions.TryRemove(sessionId, out var slots))
            return Array.Empty<Peer>();

        var peers = new List<Peer>(2);
        lock (slots)
        {
            if (slots.Host is not null) peers.Add(slots.Host);
            if (slots.Companion is not null) peers.Add(slots.Companion);
            slots.Host = null;
            slots.Companion = null;
        }
        return peers;
    }

    private static async Task CloseQuietlyAsync(Peer peer)
    {
        try
        {
            if (peer.Socket.State == WebSocketState.Open)
                await peer.Socket.CloseAsync(
                    WebSocketCloseStatus.NormalClosure, "superseded", CancellationToken.None);
        }
        catch
        {
            // A superseded socket may already be faulted; nothing to recover.
        }
    }
}
