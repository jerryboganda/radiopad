using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MailKit.Net.Smtp;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MimeKit;
using RadioPad.Api.Auth;
using RadioPad.Api.Middleware;
using RadioPad.Application.Abstractions;
using RadioPad.Domain.Entities;
using RadioPad.Domain.Enums;
using RadioPad.Infrastructure.Identity;
using RadioPad.Infrastructure.Persistence;

namespace RadioPad.Api.Controllers;

/// <summary>
/// PRD AUTH-003 — TOTP (RFC 6238) MFA enrollment + verification. The shared
/// secret is stored base32 on <see cref="User.MfaSecret"/>; verification
/// accepts a 6-digit code with a ±1 window (30 s skew). Audited via
/// <see cref="AuditAction.UserLogin"/> with <c>{ method = "totp" }</c>.
/// </summary>
[ApiController]
[Route("api/auth/mfa")]
public class MfaController : TenantedController
{
    private readonly RadioPadDbContext _db;
    private readonly IAuditLog _audit;
    private readonly RadioPad.Api.Auth.LockoutPolicy _lockout;
    public MfaController(RadioPadDbContext db, IAuditLog audit, RadioPad.Api.Auth.LockoutPolicy lockout)
    { _db = db; _audit = audit; _lockout = lockout; }

    public record EnrollDto(string Tenant, string Email);
    public record VerifyDto(string Tenant, string Email, string Code);

    [HttpPost("enroll")]
    public async Task<IActionResult> Enroll([FromBody] EnrollDto dto, CancellationToken ct)
    {
        var (tenant, user) = await ResolveContextAsync(_db, ct);
        if (!RequestedIdentityMatches(dto.Tenant, dto.Email, tenant, user))
            return ForbiddenIdentityMismatch();

        var secret = RandomNumberGenerator.GetBytes(20); // 160-bit per RFC 4226
        user.MfaSecret = Base32Encode(secret);
        user.MfaEnabled = false;
        await _db.SaveChangesAsync(ct);

        var issuer = "RadioPad";
        var label = Uri.EscapeDataString($"{issuer}:{user.Email}");
        var otpauth = $"otpauth://totp/{label}?secret={user.MfaSecret}&issuer={issuer}&period=30&digits=6&algorithm=SHA1";
        return Ok(new { secret = user.MfaSecret, otpauth });
    }

    [HttpPost("verify")]
    public async Task<IActionResult> Verify([FromBody] VerifyDto dto, CancellationToken ct)
    {
        var (tenant, user) = await ResolveContextAsync(_db, ct);
        if (!RequestedIdentityMatches(dto.Tenant, dto.Email, tenant, user))
            return ForbiddenIdentityMismatch();
        if (string.IsNullOrEmpty(user.MfaSecret))
            return BadRequest(new { error = "MFA is not enrolled.", kind = "validation" });
        if (RadioPad.Api.Auth.LockoutPolicy.IsLocked(user))
            return Unauthorized(new { error = "Account locked.", kind = "unauthenticated", until = user.LockedUntil });
        if (!TotpVerify(user.MfaSecret, dto.Code))
        {
            await _lockout.OnFailureAsync(user, method: "totp", ct);
            return Unauthorized(new { error = "Invalid TOTP code.", kind = "unauthenticated" });
        }

        user.MfaEnabled = true;
        await _lockout.OnSuccessAsync(user, ct);
        await _db.SaveChangesAsync(ct);
        await _audit.AppendAsync(new AuditEvent
        {
            TenantId = tenant.Id,
            UserId = user.Id,
            Action = AuditAction.UserLogin,
            DetailsJson = JsonSerializer.Serialize(new { method = "totp" }),
        }, ct);
        return Ok(new { ok = true, mfaEnabled = true });
    }

    private static bool RequestedIdentityMatches(string? slug, string? email, Tenant tenant, User user) =>
        (string.IsNullOrWhiteSpace(slug) || string.Equals(slug, tenant.Slug, StringComparison.Ordinal))
        && (string.IsNullOrWhiteSpace(email) || string.Equals(email, user.Email, StringComparison.OrdinalIgnoreCase));

    private IActionResult ForbiddenIdentityMismatch()
    {
        return StatusCode(StatusCodes.Status403Forbidden, new
        {
            error = "Requested identity does not match the authenticated user.",
            kind = "forbidden",
        });
    }

    // --- RFC 6238 TOTP ---
    internal static bool TotpVerify(string base32Secret, string code, int window = 1)
    {
        if (!int.TryParse(code, out _) || code.Length != 6) return false;
        var key = Base32Decode(base32Secret);
        var counter = DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 30;
        for (int w = -window; w <= window; w++)
        {
            var candidate = HotpAt(key, counter + w);
            if (CryptographicOperations.FixedTimeEquals(
                Encoding.ASCII.GetBytes(candidate),
                Encoding.ASCII.GetBytes(code))) return true;
        }
        return false;
    }

    internal static string HotpAt(byte[] key, long counter)
    {
        var counterBytes = BitConverter.GetBytes(counter);
        if (BitConverter.IsLittleEndian) Array.Reverse(counterBytes);
        using var hmac = new HMACSHA1(key);
        var hash = hmac.ComputeHash(counterBytes);
        int offset = hash[^1] & 0x0F;
        int bin = ((hash[offset] & 0x7F) << 24)
                | ((hash[offset + 1] & 0xFF) << 16)
                | ((hash[offset + 2] & 0xFF) << 8)
                | (hash[offset + 3] & 0xFF);
        int otp = bin % 1_000_000;
        return otp.ToString("D6");
    }

    private const string B32 = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
    internal static string Base32Encode(byte[] bytes)
    {
        var sb = new StringBuilder();
        int buffer = 0, bits = 0;
        foreach (var b in bytes)
        {
            buffer = (buffer << 8) | b;
            bits += 8;
            while (bits >= 5)
            {
                bits -= 5;
                sb.Append(B32[(buffer >> bits) & 0x1F]);
            }
        }
        if (bits > 0) sb.Append(B32[(buffer << (5 - bits)) & 0x1F]);
        return sb.ToString();
    }

    internal static byte[] Base32Decode(string s)
    {
        s = s.Trim().TrimEnd('=').ToUpperInvariant();
        var bytes = new List<byte>();
        int buffer = 0, bits = 0;
        foreach (var c in s)
        {
            int v = B32.IndexOf(c);
            if (v < 0) continue;
            buffer = (buffer << 5) | v;
            bits += 5;
            if (bits >= 8)
            {
                bits -= 8;
                bytes.Add((byte)((buffer >> bits) & 0xFF));
            }
        }
        return bytes.ToArray();
    }
}

/// <summary>
/// PRD AUTH-004 — passwordless magic-link sign-in. Token is a 32-byte
/// random value stored hashed (SHA-256). Mailing is best-effort via MailKit
/// when SMTP env vars are configured; in dev (no SMTP) the raw link is
/// returned in the response so tests and local users can proceed.
/// </summary>
[ApiController]
[Route("api/auth/magic-link")]
public class MagicLinkController : ControllerBase
{
    private readonly RadioPadDbContext _db;
    private readonly IAuditLog _audit;
    private readonly ILogger<MagicLinkController> _log;
    public MagicLinkController(RadioPadDbContext db, IAuditLog audit, ILogger<MagicLinkController> log)
    { _db = db; _audit = audit; _log = log; }

    public record RequestDto(string Tenant, string Email, string? CallbackUrl);
    public record ConsumeDto(string Token);

    [HttpPost("request")]
    public async Task<IActionResult> Request([FromBody] RequestDto dto, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(dto.Tenant) || string.IsNullOrWhiteSpace(dto.Email))
            return BadRequest(new { error = "tenant and email required.", kind = "validation" });

        // Iter-33 AUTH-004 — chained per-email + per-IP fixed-window rate limit.
        // Uses System.Threading.RateLimiting fixed-window primitives directly
        // (the framework's RateLimiterOptions.AddPolicy can only return a
        // single partition per request, which can't enforce two independent
        // dimensions). The limiter and audit row both fire BEFORE any tenant /
        // user lookup so we don't leak account existence to a flood.
        var emailKey = dto.Email.Trim().ToLowerInvariant();
        var ipKey = IpAllowlistMiddleware.ResolveRemoteIp(HttpContext)?.ToString() ?? "__no_ip";
        var rl = MagicLinkRateLimiter.TryAcquire(emailKey, ipKey);
        if (!rl.Acquired)
        {
            // Best-effort audit. If the tenant slug doesn't resolve we still
            // log a "tenant-less" RateLimited row keyed off the dev fallback
            // (Guid.Empty) so operators can see flood activity without us
            // confirming the tenant existed.
            var tenantForAudit = await _db.Tenants.FirstOrDefaultAsync(x => x.Slug == dto.Tenant, ct);
            try
            {
                await _audit.AppendAsync(new AuditEvent
                {
                    TenantId = tenantForAudit?.Id ?? Guid.Empty,
                    Action = AuditAction.RateLimited,
                    DetailsJson = JsonSerializer.Serialize(new
                    {
                        endpoint = "magic-link/request",
                        scope = rl.Scope,
                        emailHash = Sha256Hex(emailKey),
                        retryAfterSeconds = rl.RetryAfterSeconds,
                    }),
                }, ct);
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "Magic-link rate-limit audit append failed.");
            }
            HttpContext.Response.Headers["Retry-After"] = rl.RetryAfterSeconds.ToString();
            return StatusCode(StatusCodes.Status429TooManyRequests, new
            {
                error = "Too many magic-link requests.",
                kind = "rate-limit",
            });
        }

        var t = await _db.Tenants.FirstOrDefaultAsync(x => x.Slug == dto.Tenant, ct);
        if (t is null) return Ok(new { ok = true }); // Don't leak existence.
        var u = await _db.Users.FirstOrDefaultAsync(x => x.TenantId == t.Id && x.Email == dto.Email && x.IsActive, ct);
        if (u is null) return Ok(new { ok = true });

        var raw = "ml_" + Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .Replace("+", "-").Replace("/", "_").TrimEnd('=');
        _db.MagicLinks.Add(new MagicLinkToken
        {
            TenantId = t.Id,
            UserId = u.Id,
            TokenHash = Sha256Hex(raw),
            ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(15),
        });
        await _db.SaveChangesAsync(ct);

        var callback = dto.CallbackUrl ?? "http://localhost:3000/login";
        var link = $"{callback}?magic={Uri.EscapeDataString(raw)}";
        var sent = await TrySendAsync(dto.Email, link, ct);
        if (sent || !RadioPadRequestIdentity.DevHeadersEnabled(HttpContext))
            return Ok(new { ok = true });
        // In explicit dev/test mode (no SMTP) surface the link so QA/tests can complete the flow.
        return Ok(new { ok = true, devLink = link });
    }

    [HttpPost("consume")]
    public async Task<IActionResult> Consume([FromBody] ConsumeDto dto, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(dto.Token))
            return BadRequest(new { error = "token required.", kind = "validation" });
        var hash = Sha256Hex(dto.Token);
        var row = await _db.MagicLinks.FirstOrDefaultAsync(m => m.TokenHash == hash, ct);
        var consumedAt = DateTimeOffset.UtcNow;
        if (row is null || row.ConsumedAt is not null || row.ExpiresAt < consumedAt)
            return Unauthorized(new { error = "Invalid or expired link.", kind = "unauthenticated" });

        var consumed = await _db.MagicLinks
            .Where(m => m.Id == row.Id && m.ConsumedAt == null && m.ExpiresAt >= consumedAt)
            .ExecuteUpdateAsync(setters => setters.SetProperty(m => m.ConsumedAt, consumedAt), ct);
        if (consumed != 1)
            return Unauthorized(new { error = "Invalid or expired link.", kind = "unauthenticated" });

        var tenant = await _db.Tenants.FirstAsync(t => t.Id == row.TenantId, ct);
        var user = await _db.Users.FirstAsync(u => u.Id == row.UserId, ct);
        if (!user.IsActive || user.LockedUntil is not null && user.LockedUntil > DateTimeOffset.UtcNow)
            return Unauthorized(new { error = "User cannot sign in.", kind = "unauthenticated" });

        await _audit.AppendAsync(new AuditEvent
        {
            TenantId = tenant.Id,
            UserId = user.Id,
            Action = AuditAction.UserLogin,
            DetailsJson = JsonSerializer.Serialize(new { method = "magic-link" }),
        }, ct);

        var token = MintBearer(tenant.Slug, user.Email, user.SessionEpoch);
        var expiresAt = DateTimeOffset.UtcNow.Add(RadioPadBearerToken.Lifetime);
        await EnterpriseIdentityBridge.RecordAuthSessionAsync(_db, user, token, "magic-link", expiresAt, ct,
            ip: HttpContext.Connection.RemoteIpAddress?.ToString(),
            userAgent: HttpContext.Request.Headers.UserAgent.FirstOrDefault());

        return Ok(new
        {
            token,
            tenant = tenant.Slug,
            user = user.Email,
            expiresAt,
        });
    }

    /// <summary>
    /// Iter-32 AUTH-006 — when <paramref name="sessionEpoch"/> is non-zero it
    /// is folded into the HMAC seed so that calling
    /// <c>POST /api/users/{id}/revoke-sessions</c> (which increments the
    /// epoch) invalidates every token previously minted for that user.
    /// </summary>
    internal static string MintBearer(string tenant, string email, int sessionEpoch = 0)
    {
        return RadioPadBearerToken.Mint(tenant, email, sessionEpoch);
    }

    private async Task<bool> TrySendAsync(string to, string link, CancellationToken ct)
    {
        var host = Environment.GetEnvironmentVariable("RADIOPAD_SMTP_HOST");
        if (string.IsNullOrWhiteSpace(host)) return false;
        try
        {
            int port = int.TryParse(Environment.GetEnvironmentVariable("RADIOPAD_SMTP_PORT"), out var p) ? p : 587;
            var user = Environment.GetEnvironmentVariable("RADIOPAD_SMTP_USER");
            var pass = Environment.GetEnvironmentVariable("RADIOPAD_SMTP_PASS");
            var from = Environment.GetEnvironmentVariable("RADIOPAD_SMTP_FROM") ?? "no-reply@radiopad.local";

            var msg = new MimeMessage();
            msg.From.Add(MailboxAddress.Parse(from));
            msg.To.Add(MailboxAddress.Parse(to));
            msg.Subject = "Your RadioPad sign-in link";
            msg.Body = new TextPart("plain") { Text = $"Click to sign in (expires in 15 minutes):\n\n{link}\n" };

            using var client = new SmtpClient();
            await client.ConnectAsync(host, port, MailKit.Security.SecureSocketOptions.StartTlsWhenAvailable, ct);
            if (!string.IsNullOrEmpty(user)) await client.AuthenticateAsync(user, pass, ct);
            await client.SendAsync(msg, ct);
            await client.DisconnectAsync(true, ct);
            return true;
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Magic-link SMTP send failed.");
            return false;
        }
    }

    internal static string Sha256Hex(string s)
    {
        var b = SHA256.HashData(Encoding.UTF8.GetBytes(s));
        return Convert.ToHexString(b);
    }
}

/// <summary>
/// Iter-33 AUTH-004 — chained per-email + per-IP fixed-window rate limiter
/// for <c>POST /api/auth/magic-link/request</c>. Two independent
/// <see cref="System.Threading.RateLimiting.FixedWindowRateLimiter"/>
/// partitions are consulted on every call:
///
/// <list type="bullet">
///   <item><description>Per-email — <c>5 req / 15 min</c>, keyed by
///   <c>email.ToLowerInvariant()</c>.</description></item>
///   <item><description>Per-IP — <c>20 req / 15 min</c>, keyed by the
///   resolved client IP (TCP peer, or left-most <c>X-Forwarded-For</c>
///   when <c>RADIOPAD_TRUST_FORWARDED_FOR=1</c>).</description></item>
/// </list>
///
/// A request is rejected when either dimension is exhausted; the controller
/// surfaces <c>429 Too Many Requests</c> with <c>Retry-After</c> and audits
/// <see cref="AuditAction.RateLimited"/>.
/// </summary>
internal static class MagicLinkRateLimiter
{
    private const int EmailPerWindow = 5;
    private const int IpPerWindow = 20;
    private static readonly TimeSpan Window = TimeSpan.FromMinutes(15);

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, System.Threading.RateLimiting.FixedWindowRateLimiter> _byEmail = new();
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, System.Threading.RateLimiting.FixedWindowRateLimiter> _byIp = new();

    internal readonly record struct Decision(bool Acquired, string Scope, int RetryAfterSeconds);

    public static Decision TryAcquire(string emailKey, string ipKey)
    {
        var emailLimiter = _byEmail.GetOrAdd(emailKey, _ => Make(EmailPerWindow));
        var ipLimiter = _byIp.GetOrAdd(ipKey, _ => Make(IpPerWindow));

        using var emailLease = emailLimiter.AttemptAcquire(1);
        if (!emailLease.IsAcquired)
            return new Decision(false, "email", (int)Window.TotalSeconds);

        using var ipLease = ipLimiter.AttemptAcquire(1);
        if (!ipLease.IsAcquired)
            return new Decision(false, "ip", (int)Window.TotalSeconds);

        return new Decision(true, string.Empty, 0);
    }

    private static System.Threading.RateLimiting.FixedWindowRateLimiter Make(int permits) =>
        new(new System.Threading.RateLimiting.FixedWindowRateLimiterOptions
        {
            PermitLimit = permits,
            Window = Window,
            QueueLimit = 0,
            AutoReplenishment = true,
        });

    /// <summary>Test-only — clears every window so partition state from a
    /// previous test does not bleed into the next. Not exposed publicly.</summary>
    internal static void ResetForTesting()
    {
        foreach (var kv in _byEmail) kv.Value.Dispose();
        foreach (var kv in _byIp) kv.Value.Dispose();
        _byEmail.Clear();
        _byIp.Clear();
    }
}

/// <summary>
/// PRD AUTH-007 — RFC 8628 OAuth 2.0 Device Authorization Grant for the CLI
/// and desktop shells. Implements the four roles:
///   <list type="bullet">
///   <item><c>POST /api/auth/device/authorize</c> — device requests a code pair.</item>
///   <item><c>POST /api/auth/device/approve</c> — human approves a <c>user_code</c> while signed in via headers.</item>
///   <item><c>POST /api/auth/device/token</c> — device polls (returns <c>authorization_pending</c>, <c>slow_down</c>, <c>access_denied</c>, <c>expired_token</c>, or 200 with token).</item>
///   <item><c>POST /api/auth/device/deny</c> — operator-initiated reject.</item>
///   </list>
/// </summary>
[ApiController]
[Route("api/auth/device")]
public class DeviceAuthController : TenantedController
{
    private readonly RadioPadDbContext _db;
    private readonly IAuditLog _audit;
    public DeviceAuthController(RadioPadDbContext db, IAuditLog audit) { _db = db; _audit = audit; }

    public record AuthorizeDto(string ClientId, string? DeviceFingerprint = null);
    public record ApproveDto(string Tenant, string Email, string UserCode);
    public record TokenDto(string DeviceCode, string GrantType);
    public record DenyDto(string Tenant, string Email, string UserCode);

    [HttpPost("authorize")]
    public async Task<IActionResult> Authorize([FromBody] AuthorizeDto dto, CancellationToken ct)
    {
        var deviceCode = "dc_" + Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .Replace("+", "-").Replace("/", "_").TrimEnd('=');
        var userCode = GenerateUserCode();
        _db.DeviceAuth.Add(new DeviceAuthRequest
        {
            DeviceCodeHash = MagicLinkController.Sha256Hex(deviceCode),
            UserCode = userCode,
            Status = "pending",
            ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(10),
            IntervalSeconds = 5,
            DeviceFingerprint = string.IsNullOrWhiteSpace(dto.DeviceFingerprint)
                ? null
                : dto.DeviceFingerprint.Trim(),
        });
        await _db.SaveChangesAsync(ct);
        return Ok(new
        {
            deviceCode,
            userCode,
            verificationUri = "/devices",
            verificationUriComplete = $"/devices?code={userCode}",
            expiresIn = 600,
            interval = 5,
        });
    }

    [HttpPost("approve")]
    public async Task<IActionResult> Approve([FromBody] ApproveDto dto, CancellationToken ct)
    {
        var (tenant, user) = await ResolveContextAsync(_db, ct);
        if (!RequestedIdentityMatches(dto.Tenant, dto.Email, tenant, user))
            return ForbiddenIdentityMismatch();

        var row = await _db.DeviceAuth.FirstOrDefaultAsync(d => d.UserCode == dto.UserCode && d.Status == "pending", ct);
        if (row is null) return NotFound(new { error = "user_code", kind = "not_found" });
        if (row.ExpiresAt < DateTimeOffset.UtcNow)
        {
            row.Status = "expired";
            await _db.SaveChangesAsync(ct);
            return BadRequest(new { error = "expired_token", kind = "validation" });
        }
        row.Status = "approved";
        row.TenantId = tenant.Id;
        row.UserId = user.Id;
        await _db.SaveChangesAsync(ct);
        return Ok(new { ok = true });
    }

    [HttpPost("deny")]
    public async Task<IActionResult> Deny([FromBody] DenyDto dto, CancellationToken ct)
    {
        var (tenant, user) = await ResolveContextAsync(_db, ct);
        if (!RequestedIdentityMatches(dto.Tenant, dto.Email, tenant, user))
            return ForbiddenIdentityMismatch();

        var row = await _db.DeviceAuth.FirstOrDefaultAsync(d => d.UserCode == dto.UserCode && d.Status == "pending", ct);
        if (row is null) return NotFound(new { error = "user_code", kind = "not_found" });
        row.Status = "denied";
        await _db.SaveChangesAsync(ct);
        return Ok(new { ok = true });
    }

    [HttpPost("token")]
    public async Task<IActionResult> Token([FromBody] TokenDto dto, CancellationToken ct)
    {
        if (dto.GrantType != "urn:ietf:params:oauth:grant-type:device_code")
            return BadRequest(new { error = "unsupported_grant_type", kind = "validation" });

        var hash = MagicLinkController.Sha256Hex(dto.DeviceCode ?? "");
        var row = await _db.DeviceAuth.AsNoTracking().FirstOrDefaultAsync(d => d.DeviceCodeHash == hash, ct);
        if (row is null) return BadRequest(new { error = "invalid_grant" });

        // RFC 8628 §3.5 polling guards.
        var now = DateTimeOffset.UtcNow;
        if (row.LastPolledAt is not null && (now - row.LastPolledAt.Value).TotalSeconds < row.IntervalSeconds)
        {
            return BadRequest(new { error = "slow_down", interval = row.IntervalSeconds });
        }
        if (row.ExpiresAt < now)
        {
            await UpdateDevicePollStateAsync(row.Id, now, "expired", ct);
            return BadRequest(new { error = "expired_token" });
        }
        if (row.Status == "denied")
        {
            await UpdateDevicePollStateAsync(row.Id, now, null, ct);
            return BadRequest(new { error = "access_denied" });
        }
        if (row.Status == "pending")
        {
            await UpdateDevicePollStateAsync(row.Id, now, null, ct);
            return BadRequest(new { error = "authorization_pending" });
        }
        if (row.Status != "approved")
        {
            await UpdateDevicePollStateAsync(row.Id, now, null, ct);
            return BadRequest(new { error = "invalid_grant" });
        }

        var tenant = await _db.Tenants.FirstAsync(t => t.Id == row.TenantId, ct);
        var user = await _db.Users.FirstAsync(u => u.Id == row.UserId, ct);
        if (!user.IsActive || user.LockedUntil is not null && user.LockedUntil > DateTimeOffset.UtcNow)
        {
            await UpdateDevicePollStateAsync(row.Id, now, "denied", ct);
            return BadRequest(new { error = "access_denied" });
        }
        var consumed = await _db.DeviceAuth
            .Where(d => d.Id == row.Id && d.Status == "approved")
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(d => d.Status, "consumed")
                .SetProperty(d => d.LastPolledAt, now), ct);
        if (consumed != 1)
            return BadRequest(new { error = "invalid_grant" });

        await _audit.AppendAsync(new AuditEvent
        {
            TenantId = tenant.Id,
            UserId = user.Id,
            Action = AuditAction.UserLogin,
            DetailsJson = JsonSerializer.Serialize(new { method = "device-flow" }),
        }, ct);

        var token = MagicLinkController.MintBearer(tenant.Slug, user.Email, user.SessionEpoch);
        var expiresAt = DateTimeOffset.UtcNow.Add(RadioPadBearerToken.Lifetime);
        await EnterpriseIdentityBridge.RecordAuthSessionAsync(_db, user, token, "device-flow", expiresAt, ct,
            deviceFingerprint: row.DeviceFingerprint,
            ip: HttpContext.Connection.RemoteIpAddress?.ToString(),
            userAgent: HttpContext.Request.Headers.UserAgent.FirstOrDefault());

        return Ok(new
        {
            accessToken = token,
            tokenType = "Bearer",
            expiresIn = (int)RadioPadBearerToken.Lifetime.TotalSeconds,
            tenant = tenant.Slug,
            user = user.Email,
        });
    }

    private static bool RequestedIdentityMatches(string? slug, string? email, Tenant tenant, User user) =>
        (string.IsNullOrWhiteSpace(slug) || string.Equals(slug, tenant.Slug, StringComparison.Ordinal))
        && (string.IsNullOrWhiteSpace(email) || string.Equals(email, user.Email, StringComparison.OrdinalIgnoreCase));

    private IActionResult ForbiddenIdentityMismatch()
    {
        return StatusCode(StatusCodes.Status403Forbidden, new
        {
            error = "Requested identity does not match the authenticated user.",
            kind = "forbidden",
        });
    }

    private Task<int> UpdateDevicePollStateAsync(
        Guid id,
        DateTimeOffset lastPolledAt,
        string? status,
        CancellationToken ct)
    {
        var query = _db.DeviceAuth.Where(d => d.Id == id);
        return status is null
            ? query.ExecuteUpdateAsync(setters => setters.SetProperty(d => d.LastPolledAt, lastPolledAt), ct)
            : query.ExecuteUpdateAsync(setters => setters
                .SetProperty(d => d.LastPolledAt, lastPolledAt)
                .SetProperty(d => d.Status, status), ct);
    }

    private static string GenerateUserCode()
    {
        // 8 chars, upper alpha-num, no ambiguous (1, I, 0, O).
        const string alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        Span<byte> bytes = stackalloc byte[8];
        RandomNumberGenerator.Fill(bytes);
        var chars = new char[9];
        for (int i = 0; i < 8; i++) chars[i < 4 ? i : i + 1] = alphabet[bytes[i] % alphabet.Length];
        chars[4] = '-';
        return new string(chars);
    }
}
