using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using RadioPad.Api.Auth;
using RadioPad.Application.Abstractions;
using RadioPad.Application.Services.WebAuthn;
using RadioPad.Domain.Entities;
using RadioPad.Domain.Enums;
using RadioPad.Infrastructure.Identity;
using RadioPad.Infrastructure.Persistence;

namespace RadioPad.Api.Controllers;

/// <summary>
/// PRD AUTH-001 — WebAuthn / FIDO2 passkey registration + assertion. Four
/// endpoints, scoped to the active dev tenant context (or the OIDC-projected
/// principal once an IdP is wired):
///
/// <list type="bullet">
///   <item><c>POST /api/auth/webauthn/register-options</c> — returns a
///   PublicKeyCredentialCreationOptions-shaped envelope (challenge + RP id
///   + user handle).</item>
///   <item><c>POST /api/auth/webauthn/register</c> — accepts the
///   browser/authenticator response (credential id + COSE_Key public key)
///   and persists a new <see cref="WebAuthnCredential"/>.</item>
///   <item><c>POST /api/auth/webauthn/signin-options</c> — returns
///   PublicKeyCredentialRequestOptions (challenge + the user's allowed
///   credential ids).</item>
///   <item><c>POST /api/auth/webauthn/signin</c> — accepts the
///   authenticator assertion, validates the signature, bumps
///   <see cref="WebAuthnCredential.SignCount"/>, and mints a session
///   bearer.</item>
/// </list>
///
/// The implementation is dependency-free (no Fido2NetLib runtime
/// dependency) so it does not collide with the existing dev-header pipeline.
/// Registration verifies the attestation object via
/// <see cref="AttestationParser"/>, which currently supports the
/// <c>none</c>, <c>packed</c> (with optional <c>x5c</c> chain or
/// self-attestation), and <c>fido-u2f</c> formats; any other format is
/// rejected as <c>400 Unsupported attestation format</c>. Signin verifies
/// the assertion signature against the stored COSE_Key public key and
/// bumps the authenticator counter, refusing counter regressions as a
/// cloned-key signal.
/// </summary>
[ApiController]
[Route("api/auth/webauthn")]
public class WebAuthnController : TenantedController
{
    private readonly RadioPadDbContext _db;
    private readonly IAuditLog _audit;
    private readonly RadioPad.Api.Auth.LockoutPolicy _lockout;
    private readonly RadioPad.Application.Services.WebAuthn.IFidoMdsMetadataSource? _mdsRoots;
    private readonly IWebHostEnvironment _env;
    private readonly IMemoryCache _cache;

    public WebAuthnController(
        RadioPadDbContext db,
        IAuditLog audit,
        RadioPad.Api.Auth.LockoutPolicy lockout,
        IWebHostEnvironment env,
        IMemoryCache cache,
        RadioPad.Application.Services.WebAuthn.IFidoMdsMetadataSource? mdsRoots = null)
    { _db = db; _audit = audit; _lockout = lockout; _env = env; _cache = cache; _mdsRoots = mdsRoots; }

    private static string Rp() => Environment.GetEnvironmentVariable("RADIOPAD_WEBAUTHN_RP_ID") ?? "localhost";
    private static string RpName() => Environment.GetEnvironmentVariable("RADIOPAD_WEBAUTHN_RP_NAME") ?? "RadioPad";

    /// <summary>Explicit origin allow-list (comma-separated). Empty ⇒ derive from the RP id.</summary>
    private static IReadOnlyCollection<string> AllowedOrigins() =>
        (Environment.GetEnvironmentVariable("RADIOPAD_WEBAUTHN_ORIGINS") ?? "")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    /// <summary>When set, the authenticator must report User Verification (Windows Hello PIN/biometric).</summary>
    private static bool RequireUserVerification() =>
        Environment.GetEnvironmentVariable("RADIOPAD_WEBAUTHN_REQUIRE_UV") == "1";

    // Short-lived, single-use challenge store (AUTH-001). Keyed per
    // tenant+user+ceremony so a register challenge can't be replayed at sign-in.
    private static readonly TimeSpan ChallengeTtl = TimeSpan.FromMinutes(2);
    private static string ChallengeKey(string purpose, Guid tenantId, Guid userId) =>
        $"webauthn:{purpose}:{tenantId:N}:{userId:N}";
    private void StoreChallenge(string purpose, Guid tenantId, Guid userId, string challenge) =>
        _cache.Set(ChallengeKey(purpose, tenantId, userId), challenge, ChallengeTtl);
    private string? ConsumeChallenge(string purpose, Guid tenantId, Guid userId)
    {
        var key = ChallengeKey(purpose, tenantId, userId);
        if (_cache.TryGetValue(key, out string? value) && !string.IsNullOrEmpty(value))
        {
            _cache.Remove(key); // single use — replays after this fail
            return value;
        }
        return null;
    }

    /// <summary>
    /// Resolves the principal for a sign-in ceremony. When a verified session is
    /// already present (post-login passkey use) the token identity wins. On the
    /// login screen there is no token, so the caller-supplied tenant slug + email
    /// identify whose credentials to offer — the assertion signature, not this
    /// lookup, is what actually authenticates the user.
    /// </summary>
    private async Task<(Tenant tenant, User user)> ResolveSignInPrincipalAsync(string? tenantSlug, string? email, CancellationToken ct)
    {
        if (!RadioPadRequestIdentity.TryGet(HttpContext, out _)
            && !string.IsNullOrWhiteSpace(tenantSlug) && !string.IsNullOrWhiteSpace(email))
        {
            var tenant = await _db.Tenants.FirstOrDefaultAsync(t => t.Slug == tenantSlug, ct)
                ?? throw new UnauthorizedAccessException("Unknown tenant or credential.");
            var user = await _db.Users.FirstOrDefaultAsync(u => u.TenantId == tenant.Id && u.Email == email, ct)
                ?? throw new UnauthorizedAccessException("Unknown tenant or credential.");
            if (!user.IsActive)
                throw new UnauthorizedAccessException("User has been deprovisioned.");
            return (tenant, user);
        }
        return await ResolveContextAsync(_db, ct);
    }

    public record RegisterOptionsDto(string? Label);
    public record RegisterDto(string AttestationObject, string ClientDataJson, string? Label);
    // Tenant/User are supplied only for pre-authentication sign-in (the login
    // screen, before any session token exists). When a verified session is
    // already present they are ignored in favour of the token identity.
    public record SignInOptionsDto(string? Tenant = null, string? User = null);
    public record SignInDto(
        string CredentialId, string ClientDataJson, string AuthenticatorData, string Signature, uint SignCount,
        string? Tenant = null, string? User = null);

    [HttpPost("register-options")]
    public async Task<IActionResult> RegisterOptions([FromBody] RegisterOptionsDto _, CancellationToken ct)
    {
        var (tenant, user) = await ResolveContextAsync(_db, ct);
        var challenge = NewChallenge();
        StoreChallenge("register", tenant.Id, user.Id, challenge);
        var existing = await _db.WebAuthnCredentials
            .Where(c => c.TenantId == tenant.Id && c.UserId == user.Id)
            .Select(c => c.CredentialId)
            .ToListAsync(ct);
        return Ok(new
        {
            rp = new { id = Rp(), name = RpName() },
            user = new
            {
                id = Base64Url(Encoding.UTF8.GetBytes(user.Id.ToString())),
                name = user.Email,
                displayName = string.IsNullOrEmpty(user.DisplayName) ? user.Email : user.DisplayName,
            },
            challenge,
            pubKeyCredParams = new[]
            {
                new { type = "public-key", alg = -7  },  // ES256
                new { type = "public-key", alg = -257 }, // RS256
            },
            authenticatorSelection = new
            {
                userVerification = "preferred",
                residentKey = "preferred",
            },
            attestation = "none",
            timeout = 60_000,
            excludeCredentials = existing.Select(id => new { type = "public-key", id }).ToArray(),
        });
    }

    [HttpPost("register")]
    public async Task<IActionResult> Register([FromBody] RegisterDto dto, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(dto.AttestationObject) || string.IsNullOrWhiteSpace(dto.ClientDataJson))
            return BadRequest(new { error = "attestationObject and clientDataJson required.", kind = "validation" });

        var (tenant, user) = await ResolveContextAsync(_db, ct);
        if (RadioPad.Api.Auth.LockoutPolicy.IsLocked(user))
            return Unauthorized(new { error = "Account locked.", kind = "unauthenticated" });

        // Bind the response to the single-use challenge we issued in
        // register-options, and confirm the ceremony type + origin before
        // trusting the attestation. A missing challenge means the enrollment
        // window lapsed or this is a replay.
        var expectedChallenge = ConsumeChallenge("register", tenant.Id, user.Id);
        if (expectedChallenge is null)
            return Unauthorized(new { error = "Registration challenge expired or already used. Restart enrollment.", kind = "unauthenticated" });
        try
        {
            AttestationParser.VerifyClientData(
                FromBase64Url(dto.ClientDataJson), "webauthn.create", expectedChallenge, AllowedOrigins(), Rp());
        }
        catch (AttestationParser.AttestationException ex)
        {
            await _lockout.OnFailureAsync(user, "webauthn", ct);
            return BadRequest(new { error = ex.Message, kind = ex.Kind });
        }
        catch (FormatException)
        {
            return BadRequest(new { error = "clientDataJson must be base64url.", kind = "validation" });
        }

        AttestationParser.Result parsed;
        try
        {
            var attBytes = FromBase64Url(dto.AttestationObject);
            var clientBytes = FromBase64Url(dto.ClientDataJson);
            parsed = AttestationParser.Verify(attBytes, clientBytes, _mdsRoots);
        }
        catch (AttestationParser.AttestationException ex) when (ex.Kind == "unsupported_format")
        {
            return BadRequest(new { error = "Unsupported attestation format", kind = "validation" });
        }
        catch (AttestationParser.AttestationException ex) when (ex.Kind == "attestation_root")
        {
            // Iter-35 AUTH-001 — packed-with-x5c chain did not terminate in
            // a FIDO MDS3 trusted root. Audit a PolicyViolation row before
            // returning 400 so SIEM / anomaly detection sees it.
            await _audit.AppendAsync(new AuditEvent
            {
                TenantId = tenant.Id,
                UserId = user.Id,
                Action = AuditAction.PolicyViolation,
                DetailsJson = JsonSerializer.Serialize(new
                {
                    kind = "webauthn_attestation_root",
                    op = "register",
                    message = ex.Message,
                }),
            }, ct);
            await _lockout.OnFailureAsync(user, "webauthn", ct);
            return BadRequest(new { error = ex.Message, kind = ex.Kind });
        }
        catch (AttestationParser.AttestationException ex)
        {
            await _lockout.OnFailureAsync(user, "webauthn", ct);
            return BadRequest(new { error = ex.Message, kind = ex.Kind });
        }
        catch (FormatException)
        {
            return BadRequest(new { error = "attestationObject/clientDataJson must be base64url.", kind = "validation" });
        }

        var credentialIdB64 = Base64Url(parsed.CredentialId);
        var publicKeyB64 = Convert.ToBase64String(parsed.CosePublicKey);
        var hash = Sha256Hex(credentialIdB64);
        var collision = await _db.WebAuthnCredentials
            .FirstOrDefaultAsync(c => c.TenantId == tenant.Id && c.CredentialIdHash == hash, ct);
        if (collision is not null)
            return Conflict(new { error = "Credential already registered.", kind = "validation" });

        var row = new WebAuthnCredential
        {
            TenantId = tenant.Id,
            UserId = user.Id,
            CredentialId = credentialIdB64,
            CredentialIdHash = hash,
            PublicKey = publicKeyB64,
            SignCount = parsed.SignCount,
            Label = dto.Label?.Trim() ?? "",
            AttestationFormat = parsed.Format,
        };
        _db.WebAuthnCredentials.Add(row);
        await _db.SaveChangesAsync(ct);
        await _audit.AppendAsync(new AuditEvent
        {
            TenantId = tenant.Id,
            UserId = user.Id,
            Action = AuditAction.UserLogin,
            DetailsJson = JsonSerializer.Serialize(new
            {
                method = "webauthn",
                op = "register",
                credentialId = hash,
                attestationFormat = parsed.Format,
            }),
        }, ct);
        return Ok(new { id = row.Id, credentialIdHash = hash, label = row.Label, attestationFormat = parsed.Format });
    }

    [HttpPost("signin-options")]
    public async Task<IActionResult> SignInOptions([FromBody] SignInOptionsDto dto, CancellationToken ct)
    {
        var (tenant, user) = await ResolveSignInPrincipalAsync(dto.Tenant, dto.User, ct);
        var creds = await _db.WebAuthnCredentials
            .Where(c => c.TenantId == tenant.Id && c.UserId == user.Id)
            .Select(c => c.CredentialId)
            .ToListAsync(ct);
        var challenge = NewChallenge();
        StoreChallenge("signin", tenant.Id, user.Id, challenge);
        return Ok(new
        {
            challenge,
            rpId = Rp(),
            timeout = 60_000,
            userVerification = "preferred",
            allowCredentials = creds.Select(id => new { type = "public-key", id }).ToArray(),
        });
    }

    [HttpPost("signin")]
    public async Task<IActionResult> SignIn([FromBody] SignInDto dto, CancellationToken ct)
    {
        var (tenant, user) = await ResolveSignInPrincipalAsync(dto.Tenant, dto.User, ct);
        if (RadioPad.Api.Auth.LockoutPolicy.IsLocked(user))
            return Unauthorized(new { error = "Account locked.", kind = "unauthenticated" });

        var hash = Sha256Hex(dto.CredentialId);
        var cred = await _db.WebAuthnCredentials.FirstOrDefaultAsync(
            c => c.TenantId == tenant.Id && c.UserId == user.Id && c.CredentialIdHash == hash, ct);
        if (cred is null)
        {
            await _lockout.OnFailureAsync(user, "webauthn", ct);
            return Unauthorized(new { error = "Unknown credential.", kind = "unauthenticated" });
        }

        // Bind to the single-use challenge from signin-options, then verify the
        // ceremony type/origin, the authenticatorData (rpIdHash + UP/UV flags),
        // and finally the assertion signature against the stored COSE public
        // key. Any failure is a failed auth attempt (lockout-counted, 401).
        var expectedChallenge = ConsumeChallenge("signin", tenant.Id, user.Id);
        if (expectedChallenge is null)
        {
            await _lockout.OnFailureAsync(user, "webauthn", ct);
            return Unauthorized(new { error = "Sign-in challenge expired or already used. Try again.", kind = "unauthenticated" });
        }
        // The signature-protected counter parsed from authenticatorData — used
        // for clone detection instead of the unauthenticated dto.SignCount.
        uint assertedSignCount = 0;
        try
        {
            var clientBytes = FromBase64Url(dto.ClientDataJson);
            var authBytes = FromBase64Url(dto.AuthenticatorData);
            var sigBytes = FromBase64Url(dto.Signature);
            AttestationParser.VerifyClientData(
                clientBytes, "webauthn.get", expectedChallenge, AllowedOrigins(), Rp());
            (_, _, assertedSignCount) = AttestationParser.VerifyAssertionAuthData(authBytes, Rp(), RequireUserVerification());
            AttestationParser.VerifyAssertionSignature(
                Convert.FromBase64String(cred.PublicKey), authBytes, clientBytes, sigBytes);
        }
        catch (AttestationParser.AttestationException ex)
        {
            await _lockout.OnFailureAsync(user, "webauthn", ct);
            return Unauthorized(new { error = ex.Message, kind = "unauthenticated" });
        }
        catch (FormatException)
        {
            await _lockout.OnFailureAsync(user, "webauthn", ct);
            return BadRequest(new { error = "clientDataJson, authenticatorData and signature must be base64url.", kind = "validation" });
        }

        if (assertedSignCount != 0 && assertedSignCount <= cred.SignCount)
        {
            // Authenticator counter regression — possible cloned key.
            await _lockout.OnFailureAsync(user, "webauthn", ct);
            return Unauthorized(new { error = "Authenticator counter regression.", kind = "unauthenticated" });
        }

        cred.SignCount = assertedSignCount;
        cred.LastUsedAt = DateTimeOffset.UtcNow;
        await _lockout.OnSuccessAsync(user, ct);
        await _db.SaveChangesAsync(ct);
        await _audit.AppendAsync(new AuditEvent
        {
            TenantId = tenant.Id,
            UserId = user.Id,
            Action = AuditAction.UserLogin,
            DetailsJson = JsonSerializer.Serialize(new { method = "webauthn", credentialId = hash }),
        }, ct);

        var issuedAt = DateTimeOffset.UtcNow;
        return Ok(new
        {
            token = RadioPadBearerTokens.Mint(tenant.Slug, user.Email, user.SessionEpoch, _env, issuedAt),
            tenant = tenant.Slug,
            user = user.Email,
            expiresAt = RadioPadBearerTokens.ExpiresAt(issuedAt),
        });
    }

    [HttpGet("credentials")]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        var (tenant, user) = await ResolveContextAsync(_db, ct);
        var rows = await _db.WebAuthnCredentials
            .Where(c => c.TenantId == tenant.Id && c.UserId == user.Id)
            .OrderByDescending(c => c.CreatedAt)
            .Select(c => new { c.Id, c.Label, c.SignCount, c.CreatedAt, c.LastUsedAt })
            .ToListAsync(ct);
        return Ok(rows);
    }

    [HttpDelete("credentials/{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var (tenant, user) = await ResolveContextAsync(_db, ct);
        var row = await _db.WebAuthnCredentials.FirstOrDefaultAsync(
            c => c.Id == id && c.TenantId == tenant.Id && c.UserId == user.Id, ct);
        if (row is null) return NotFound();
        _db.WebAuthnCredentials.Remove(row);
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    private static string NewChallenge() =>
        Base64Url(RandomNumberGenerator.GetBytes(32));

    private static string Base64Url(byte[] b) =>
        Convert.ToBase64String(b).Replace("+", "-").Replace("/", "_").TrimEnd('=');

    private static byte[] FromBase64Url(string s)
    {
        var pad = s.Replace('-', '+').Replace('_', '/');
        switch (pad.Length % 4) { case 2: pad += "=="; break; case 3: pad += "="; break; }
        return Convert.FromBase64String(pad);
    }

    private static string Sha256Hex(string s) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(s))).ToLowerInvariant();
}
