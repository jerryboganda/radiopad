using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using RadioPad.Domain.Entities;
using RadioPad.Domain.Enums;
using RadioPad.Infrastructure.Persistence;

namespace RadioPad.Api.Services;

public sealed record CompanionPairResult(CompanionSession Session, bool WasRetry);

/// <summary>
/// Coordinates the durable side of the desktop↔phone companion handshake: minting
/// a pairing session with a short code, pairing a phone by that code, tearing a
/// session down, and reading its state. All operations are tenant + user scoped —
/// the phone must authenticate as the SAME (tenant, user) that advertised the
/// session — so a code leaking to another tenant is inert.
///
/// The live dictation/command stream is relayed in-memory by
/// <see cref="CompanionRelayRegistry"/> / the <c>/ws/companion</c> endpoint and is
/// never written here; this service only manages the coordination record.
/// </summary>
public sealed class CompanionSessionService
{
    /// <summary>Advertised codes are pairable for this long before they expire.</summary>
    public static readonly TimeSpan PairingWindow = TimeSpan.FromMinutes(15);

    // Uppercase alphanumerics. Ambiguous glyphs are dropped so a code read off a
    // desktop screen and typed on a phone is unambiguous (no O/0, I/1, etc.).
    private const string CodeAlphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
    private const int CodeLength = 6;
    private const int MaxCodeAttempts = 12;

    private readonly RadioPadDbContext _db;

    public CompanionSessionService(RadioPadDbContext db) => _db = db;

    /// <summary>
    /// Creates a new <see cref="CompanionSessionStatus.Advertising"/> session for the
    /// caller with a freshly minted, collision-free pairing code.
    /// </summary>
    public async Task<CompanionSession> CreateAsync(
        Guid tenantId, Guid userId, string hostDeviceName, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var session = new CompanionSession
        {
            TenantId = tenantId,
            UserId = userId,
            PairingCode = await GenerateUniqueCodeAsync(ct),
            HostDeviceName = string.IsNullOrWhiteSpace(hostDeviceName) ? "Desktop" : hostDeviceName.Trim(),
            Status = CompanionSessionStatus.Advertising,
            CreatedAt = now,
            UpdatedAt = now,
            ExpiresAt = now.Add(PairingWindow),
        };
        _db.CompanionSessions.Add(session);
        await _db.SaveChangesAsync(ct);
        return session;
    }

    /// <summary>
    /// Pairs a phone to an advertising session by its code. Returns the paired
    /// session, or <c>null</c> when the code is unknown, expired, paired to a
    /// different bearer, or belongs to a different tenant/user. A retry using the
    /// same QR bearer returns the already-paired session.
    /// </summary>
    public Task<CompanionSession?> PairAsync(
        Guid tenantId,
        Guid userId,
        string pairingCode,
        string companionDeviceName,
        CancellationToken ct) =>
        PairAsync(tenantId, userId, pairingCode, companionDeviceName, null, ct);

    public async Task<CompanionSession?> PairAsync(
        Guid tenantId,
        Guid userId,
        string pairingCode,
        string companionDeviceName,
        string? companionTokenHash,
        CancellationToken ct)
    {
        var result = await PairWithResultAsync(
            tenantId,
            userId,
            pairingCode,
            companionDeviceName,
            companionTokenHash,
            ct);
        return result?.Session;
    }

    /// <summary>
    /// Pairs a phone to an advertising session and optionally binds the QR bearer
    /// so a retry of the same request can be completed idempotently.
    /// </summary>
    public async Task<CompanionPairResult?> PairWithResultAsync(
        Guid tenantId,
        Guid userId,
        string pairingCode,
        string companionDeviceName,
        string? companionTokenHash,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(pairingCode))
            return null;

        var code = pairingCode.Trim().ToUpperInvariant();
        var now = DateTimeOffset.UtcNow;

        var session = await _db.CompanionSessions
            .FirstOrDefaultAsync(
                s => s.PairingCode == code && s.TenantId == tenantId && s.UserId == userId,
                ct);

        if (session is null)
            return null;

        // A client may retry after the pair response was lost in transit. The QR
        // bearer is bound to this row, so the retry is safe and idempotent while a
        // different bearer still receives the normal single-use failure.
        if (session.Status == CompanionSessionStatus.Paired)
        {
            return !string.IsNullOrWhiteSpace(companionTokenHash)
                && string.Equals(session.CompanionTokenHash, companionTokenHash, StringComparison.Ordinal)
                ? new CompanionPairResult(session, WasRetry: true)
                : null;
        }

        if (session.Status != CompanionSessionStatus.Advertising)
            return null;

        if (session.ExpiresAt <= now)
        {
            // Lazily mark a stale advertisement expired so a later GET is truthful.
            session.Status = CompanionSessionStatus.Expired;
            session.UpdatedAt = now;
            await _db.SaveChangesAsync(ct);
            return null;
        }

        var deviceName = string.IsNullOrWhiteSpace(companionDeviceName)
            ? "Phone"
            : companionDeviceName.Trim();
        var updated = await _db.CompanionSessions
            .Where(s => s.Id == session.Id
                && s.Status == CompanionSessionStatus.Advertising
                && s.ExpiresAt > now)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(s => s.Status, CompanionSessionStatus.Paired)
                .SetProperty(s => s.CompanionDeviceName, deviceName)
                .SetProperty(s => s.PairedAt, now)
                .SetProperty(s => s.UpdatedAt, now), ct);
        if (updated == 0)
        {
            var raced = await _db.CompanionSessions
                .AsNoTracking()
                .FirstOrDefaultAsync(s => s.Id == session.Id, ct);
            return raced?.Status == CompanionSessionStatus.Paired
                && !string.IsNullOrWhiteSpace(companionTokenHash)
                && string.Equals(raced.CompanionTokenHash, companionTokenHash, StringComparison.Ordinal)
                ? new CompanionPairResult(raced, WasRetry: true)
                : null;
        }

        await _db.Entry(session).ReloadAsync(ct);
        return new CompanionPairResult(session, WasRetry: false);
    }

    /// <summary>
    /// Ends a session the caller owns (idempotent). Returns the ended session, or
    /// <c>null</c> if it does not exist for this (tenant, user).
    /// </summary>
    public async Task<CompanionSession?> EndAsync(
        Guid tenantId, Guid userId, Guid sessionId, CancellationToken ct)
    {
        var session = await FindOwnedAsync(tenantId, userId, sessionId, ct);
        if (session is null)
            return null;

        if (session.Status != CompanionSessionStatus.Ended)
        {
            session.Status = CompanionSessionStatus.Ended;
            session.UpdatedAt = DateTimeOffset.UtcNow;
            await _db.SaveChangesAsync(ct);
        }
        return session;
    }

    /// <summary>Reads a session the caller owns, or <c>null</c>.</summary>
    public Task<CompanionSession?> GetAsync(
        Guid tenantId, Guid userId, Guid sessionId, CancellationToken ct) =>
        FindOwnedAsync(tenantId, userId, sessionId, ct);

    private Task<CompanionSession?> FindOwnedAsync(
        Guid tenantId, Guid userId, Guid sessionId, CancellationToken ct) =>
        _db.CompanionSessions.FirstOrDefaultAsync(
            s => s.Id == sessionId && s.TenantId == tenantId && s.UserId == userId, ct);

    private async Task<string> GenerateUniqueCodeAsync(CancellationToken ct)
    {
        for (var attempt = 0; attempt < MaxCodeAttempts; attempt++)
        {
            var code = GenerateCode();
            if (!await _db.CompanionSessions.AnyAsync(s => s.PairingCode == code, ct))
                return code;
        }
        throw new InvalidOperationException("Unable to allocate a unique companion pairing code.");
    }

    private static string GenerateCode()
    {
        var chars = new char[CodeLength];
        for (var i = 0; i < CodeLength; i++)
            chars[i] = CodeAlphabet[RandomNumberGenerator.GetInt32(CodeAlphabet.Length)];
        return new string(chars);
    }
}
