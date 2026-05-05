using System.Text.Json;
using RadioPad.Application.Abstractions;
using RadioPad.Domain.Entities;
using RadioPad.Domain.Enums;
using RadioPad.Infrastructure.Persistence;

namespace RadioPad.Api.Auth;

/// <summary>
/// Iter-32 AUTH-006 — sliding-window account-lockout policy. Failed
/// credential attempts (TOTP verify, magic-link consume, WebAuthn assert,
/// SAML/OIDC translation) within a 15-minute window count toward the
/// threshold; on the 5th failure the account is locked for 15 minutes by
/// stamping <see cref="User.LockedUntil"/>. The middleware-level
/// <c>SuspensionGuardMiddleware</c> + the controllers' explicit
/// <c>IsActive</c> checks reject sign-in while a lock is active.
///
/// On a successful sign-in <see cref="OnSuccessAsync"/> resets the counter
/// and clears the lock.
/// </summary>
public sealed class LockoutPolicy
{
    public const int MaxAttempts = 5;
    public static readonly TimeSpan Window = TimeSpan.FromMinutes(15);
    public static readonly TimeSpan LockDuration = TimeSpan.FromMinutes(15);

    private readonly RadioPadDbContext _db;
    private readonly IAuditLog _audit;

    public LockoutPolicy(RadioPadDbContext db, IAuditLog audit)
    { _db = db; _audit = audit; }

    /// <summary>True when <paramref name="user"/> is currently locked out.</summary>
    public static bool IsLocked(User user) =>
        user.LockedUntil is not null && user.LockedUntil > DateTimeOffset.UtcNow;

    public async Task OnFailureAsync(User user, string method, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        if (user.FailedLoginWindowStart is null
            || (now - user.FailedLoginWindowStart.Value) > Window)
        {
            user.FailedLoginWindowStart = now;
            user.FailedLoginCount = 0;
        }
        user.FailedLoginCount += 1;
        if (user.FailedLoginCount >= MaxAttempts && !IsLocked(user))
        {
            user.LockedUntil = now.Add(LockDuration);
            await _db.SaveChangesAsync(ct);
            await _audit.AppendAsync(new AuditEvent
            {
                TenantId = user.TenantId,
                UserId = user.Id,
                Action = AuditAction.UserLockedOut,
                DetailsJson = JsonSerializer.Serialize(new
                {
                    method,
                    failures = user.FailedLoginCount,
                    until = user.LockedUntil,
                    reason = "rate_limited",
                }),
            }, ct);
            return;
        }
        await _db.SaveChangesAsync(ct);
    }

    public async Task OnSuccessAsync(User user, CancellationToken ct)
    {
        var dirty = false;
        if (user.FailedLoginCount != 0) { user.FailedLoginCount = 0; dirty = true; }
        if (user.FailedLoginWindowStart is not null) { user.FailedLoginWindowStart = null; dirty = true; }
        if (user.LockedUntil is not null) { user.LockedUntil = null; dirty = true; }
        if (dirty) await _db.SaveChangesAsync(ct);
    }
}
