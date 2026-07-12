using System.Globalization;
using System.Net.WebSockets;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using RadioPad.Api.Services;
using RadioPad.Application.Abstractions;
using RadioPad.Domain.Entities;
using RadioPad.Domain.Enums;
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
    private readonly RadioPadDbContext _db;
    private readonly CompanionSessionService _sessions;
    private readonly CompanionRelayRegistry _relay;
    private readonly IAuditLog _audit;

    public CompanionController(
        RadioPadDbContext db,
        CompanionSessionService sessions,
        CompanionRelayRegistry relay,
        IAuditLog audit)
    {
        _db = db;
        _sessions = sessions;
        _relay = relay;
        _audit = audit;
    }

    public record CreateSessionDto(string? DeviceName);
    public record PairDto(string? PairingCode, string? DeviceName);

    /// <summary>Desktop advertises a new pairing session and gets back a short code
    /// plus the relative relay URL the frontend resolves against its own host.</summary>
    [HttpPost("sessions")]
    public async Task<IActionResult> Create([FromBody] CreateSessionDto dto, CancellationToken ct)
    {
        var (tenant, user) = await ResolveContextAsync(_db, ct);
        var session = await _sessions.CreateAsync(tenant.Id, user.Id, dto.DeviceName ?? "Desktop", ct);

        return Ok(new
        {
            sessionId = session.Id.ToString(),
            pairingCode = session.PairingCode,
            expiresAt = session.ExpiresAt.ToString("o", CultureInfo.InvariantCulture),
            wsUrl = "/ws/companion",
        });
    }

    /// <summary>Phone pairs to an advertised session by its code. Unknown / expired /
    /// already-paired / cross-user codes all return 404 so a prober cannot tell them
    /// apart.</summary>
    [HttpPost("pair")]
    public async Task<IActionResult> Pair([FromBody] PairDto dto, CancellationToken ct)
    {
        var (tenant, user) = await ResolveContextAsync(_db, ct);

        var session = await _sessions.PairAsync(tenant.Id, user.Id, dto.PairingCode ?? "", dto.DeviceName ?? "Phone", ct);
        if (session is null)
            return NotFound(new { error = "Pairing code is invalid or expired.", kind = "not_found" });

        // Append-only audit. Records the session id only — never device names or any
        // dictation/report content (transient relay carries no PHI to the log).
        await _audit.AppendAsync(new AuditEvent
        {
            TenantId = tenant.Id,
            UserId = user.Id,
            Action = AuditAction.CompanionPaired,
            DetailsJson = JsonSerializer.Serialize(new { sessionId = session.Id }),
        }, ct);

        return Ok(new
        {
            sessionId = session.Id.ToString(),
            hostDeviceName = session.HostDeviceName,
        });
    }

    /// <summary>Ends a session the caller owns (idempotent) and tears down any live
    /// relay sockets with a <c>session_ended</c> frame.</summary>
    [HttpDelete("sessions/{sessionId:guid}")]
    public async Task<IActionResult> End(Guid sessionId, CancellationToken ct)
    {
        var (tenant, user) = await ResolveContextAsync(_db, ct);
        await _sessions.EndAsync(tenant.Id, user.Id, sessionId, ct);

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
