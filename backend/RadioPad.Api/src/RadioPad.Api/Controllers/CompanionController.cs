using System.Collections.Concurrent;
using System.Globalization;
using System.Net.WebSockets;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RadioPad.Api.Auth;
using RadioPad.Api.Services;
using RadioPad.Application.Abstractions;
using RadioPad.Domain.Entities;
using RadioPad.Domain.Enums;
using RadioPad.Infrastructure.Identity;
using RadioPad.Infrastructure.Persistence;

namespace RadioPad.Api.Controllers;

/// <summary>
/// REST surface for the desktop↔phone companion relay. The desktop advertises a
/// pairing session, the phone joins by its short code, and both then open a
/// <c>/ws/companion</c> socket to stream dictation + commands through the cloud
/// backend (the phone cannot reach the desktop's loopback sidecar directly).
///
/// Every action is tenant + user scoped via <see cref="TenantedController.ResolveContextAsync"/>:
/// a phone must authenticate as the SAME radiologist that opened the session.
/// </summary>
[ApiController]
[Route("api/companion")]
public class CompanionController : TenantedController
{
    /// <summary>
    /// Lifetime of the companion bearer embedded in the desktop's pairing QR. It
    /// authenticates the phone as the SAME (tenant, user) that advertised the
    /// session, so it is deliberately SHORT — long enough for a realistic
    /// dictation session but bounding the exposure if the QR is photographed off
    /// the desktop screen. Ending / unpairing the session revokes it immediately
    /// (see <see cref="End"/>); the 12h WS relay cap is the hard backstop.
    /// </summary>
    private static readonly TimeSpan CompanionTokenLifetime = TimeSpan.FromHours(2);
    private static readonly ConcurrentDictionary<Guid, SemaphoreSlim> PairAuditLocks = new();

    /// <summary><see cref="AuthSession.Method"/> tag for companion bearers, so
    /// ending a session can revoke exactly these (and not the radiologist's
    /// desktop/web sessions).</summary>
    private const string CompanionAuthMethod = "companion";

    private readonly RadioPadDbContext _db;
    private readonly CompanionSessionService _sessions;
    private readonly CompanionRelayRegistry _relay;
    private readonly IAuditLog _audit;
    private readonly IWebHostEnvironment _env;

    public CompanionController(
        RadioPadDbContext db,
        CompanionSessionService sessions,
        CompanionRelayRegistry relay,
        IAuditLog audit,
        IWebHostEnvironment env)
    {
        _db = db;
        _sessions = sessions;
        _relay = relay;
        _audit = audit;
        _env = env;
    }

    public record CreateSessionDto(string? DeviceName);
    public record PairDto(string? PairingCode, string? DeviceName);

    /// <summary>Desktop advertises a new pairing session and gets back a short code
    /// plus a short-lived <c>companionToken</c> the desktop embeds in its pairing QR.
    /// The phone scans the QR, adopts the token as its bearer, and pairs — so it never
    /// needs a separate phone login. The token authenticates as THIS (tenant, user)
    /// only, expires in <see cref="CompanionTokenLifetime"/>, and is revoked when the
    /// session ends.</summary>
    [HttpPost("sessions")]
    public async Task<IActionResult> Create([FromBody] CreateSessionDto dto, CancellationToken ct)
    {
        var (tenant, user) = await ResolveContextAsync(_db, ct);
        var session = await _sessions.CreateAsync(tenant.Id, user.Id, dto.DeviceName ?? "Desktop", ct);

        // Mint the pairing credential the phone will adopt off the QR. It is a
        // normal rp_ bearer for this same identity (so the existing bearer
        // middleware + WS relay validate it with no special-casing), just
        // short-lived and recorded as a revocable AuthSession so ending the
        // session can kill it (see End()).
        var now = DateTimeOffset.UtcNow;
        var companionToken = RadioPadBearerTokens.Mint(
            tenant.Slug, user.Email, user.SessionEpoch, _env, now, CompanionTokenLifetime);
        await EnterpriseIdentityBridge.RecordAuthSessionAsync(
            _db, user, companionToken, CompanionAuthMethod, now.Add(CompanionTokenLifetime), ct);
        session.CompanionTokenHash = EnterpriseIdentityBridge.Sha256Hex(companionToken);
        await _db.SaveChangesAsync(ct);

        return Ok(new
        {
            sessionId = session.Id.ToString(),
            pairingCode = session.PairingCode,
            expiresAt = session.ExpiresAt.ToString("o", CultureInfo.InvariantCulture),
            wsUrl = "/ws/companion",
            // The phone adopts these to authenticate + address the cloud relay off
            // the QR. tenantSlug/userEmail are the bearer's context headers.
            companionToken,
            tenantSlug = tenant.Slug,
            userEmail = user.Email,
        });
    }

    /// <summary>Phone pairs to an advertised session by its code. Unknown / expired /
    /// cross-user codes and retries using a different bearer return 404 so a prober
    /// cannot tell them apart; a retry using the same QR bearer is idempotent.</summary>
    [HttpPost("pair")]
    public async Task<IActionResult> Pair([FromBody] PairDto dto, CancellationToken ct)
    {
        var (tenant, user) = await ResolveContextAsync(_db, ct);

        var companionToken = RadioPadSessionCookies.ExtractBearer(Request);
        var companionTokenHash = string.IsNullOrWhiteSpace(companionToken)
            ? null
            : EnterpriseIdentityBridge.Sha256Hex(companionToken);
        var paired = await _sessions.PairWithResultAsync(
            tenant.Id,
            user.Id,
            dto.PairingCode ?? "",
            dto.DeviceName ?? "Phone",
            companionTokenHash,
            ct);
        if (paired is null)
            return NotFound(new { error = "Pairing code is invalid or expired.", kind = "not_found" });

        // Append-only audit. Records the session id only — never device names or any
        // dictation/report content (transient relay carries no PHI to the log). Check
        // the durable log rather than relying only on WasRetry: if the pair commit
        // succeeded but the first response/audit append was interrupted, a retry
        // must be able to complete the missing audit event.
        var auditGate = PairAuditLocks.GetOrAdd(paired.Session.Id, _ => new SemaphoreSlim(1, 1));
        await auditGate.WaitAsync(ct);
        try
        {
            var auditDetails = JsonSerializer.Serialize(new { sessionId = paired.Session.Id });
            var alreadyAudited = await _db.AuditEvents
                .AsNoTracking()
                .AnyAsync(a => a.TenantId == tenant.Id
                    && a.UserId == user.Id
                    && a.Action == AuditAction.CompanionPaired
                    && a.DetailsJson == auditDetails, ct);
            if (!alreadyAudited)
            {
                await _audit.AppendAsync(new AuditEvent
                {
                    TenantId = tenant.Id,
                    UserId = user.Id,
                    Action = AuditAction.CompanionPaired,
                    DetailsJson = auditDetails,
                }, ct);
            }
        }
        finally
        {
            auditGate.Release();
        }

        return Ok(new
        {
            sessionId = paired.Session.Id.ToString(),
            hostDeviceName = paired.Session.HostDeviceName,
        });
    }

    /// <summary>Ends a session the caller owns (idempotent) and tears down any live
    /// relay sockets with a <c>session_ended</c> frame.</summary>
    [HttpDelete("sessions/{sessionId:guid}")]
    public async Task<IActionResult> End(Guid sessionId, CancellationToken ct)
    {
        var (tenant, user) = await ResolveContextAsync(_db, ct);
        await _sessions.EndAsync(tenant.Id, user.Id, sessionId, ct);

        // Revoke the bearer bound to this durable session so a phone paired off the
        // QR loses API access the moment the radiologist unpairs — not just when the
        // 2h token would have expired. Desktop/web sessions and newer companion QR
        // sessions for this user are untouched.
        var now = DateTimeOffset.UtcNow;
        var ended = await _sessions.GetAsync(tenant.Id, user.Id, sessionId, ct);
        var companionTokenHash = ended?.CompanionTokenHash;
        var live = string.IsNullOrWhiteSpace(companionTokenHash)
            ? new List<AuthSession>()
            : await _db.AuthSessions
                .Where(s => s.TenantId == tenant.Id
                    && s.UserId == user.Id
                    && s.Method == CompanionAuthMethod
                    && s.TokenHash == companionTokenHash
                    && s.RevokedAt == null)
                .ToListAsync(ct);
        foreach (var s in live)
        {
            s.RevokedAt = now;
            s.RevocationReason = "companion_session_ended";
        }
        if (live.Count > 0)
            await _db.SaveChangesAsync(ct);

        // Notify + close both peers if they are connected (best effort — the relay
        // is in-memory and process-local).
        foreach (var peer in _relay.RemoveSession(sessionId))
        {
            await peer.SendTextAsync(CompanionRelayEndpoint.SessionEnded(), ct);
            await peer.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "session_ended");
        }

        return NoContent();
    }

    /// <summary>Reads the current state of a session the caller owns.</summary>
    [HttpGet("sessions/{sessionId:guid}")]
    public async Task<IActionResult> Get(Guid sessionId, CancellationToken ct)
    {
        var (tenant, user) = await ResolveContextAsync(_db, ct);
        var session = await _sessions.GetAsync(tenant.Id, user.Id, sessionId, ct);
        if (session is null)
            return NotFound(new { error = "Companion session not found.", kind = "not_found" });

        return Ok(new
        {
            sessionId = session.Id.ToString(),
            status = session.Status.ToString(),
            hostDeviceName = session.HostDeviceName,
            companionDeviceName = session.CompanionDeviceName,
        });
    }
}
