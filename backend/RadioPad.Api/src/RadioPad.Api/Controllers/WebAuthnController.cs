using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RadioPad.Api.Auth;
using RadioPad.Application.Abstractions;
using RadioPad.Application.Services.WebAuthn;
using RadioPad.Domain.Entities;
using RadioPad.Domain.Enums;
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

    public WebAuthnController(
        RadioPadDbContext db,
        IAuditLog audit,
        RadioPad.Api.Auth.LockoutPolicy lockout,
        IWebHostEnvironment env,
        RadioPad.Application.Services.WebAuthn.IFidoMdsMetadataSource? mdsRoots = null)
    { _db = db; _audit = audit; _lockout = lockout; _env = env; _mdsRoots = mdsRoots; }

    private static string Rp() => Environment.GetEnvironmentVariable("RADIOPAD_WEBAUTHN_RP_ID") ?? "localhost";
    private static string RpName() => Environment.GetEnvironmentVariable("RADIOPAD_WEBAUTHN_RP_NAME") ?? "RadioPad";

    public record RegisterOptionsDto(string? Label);
    public record RegisterDto(string AttestationObject, string ClientDataJson, string? Label);
    public record SignInOptionsDto();
    public record SignInDto(string CredentialId, string ClientDataJson, string AuthenticatorData, string Signature, uint SignCount);

    [HttpPost("register-options")]
    public async Task<IActionResult> RegisterOptions([FromBody] RegisterOptionsDto _, CancellationToken ct)
    {
        var (tenant, user) = await ResolveContextAsync(_db, ct);
        var challenge = NewChallenge();
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
    public async Task<IActionResult> SignInOptions([FromBody] SignInOptionsDto _, CancellationToken ct)
    {
        var (tenant, user) = await ResolveContextAsync(_db, ct);
        var creds = await _db.WebAuthnCredentials
            .Where(c => c.TenantId == tenant.Id && c.UserId == user.Id)
            .Select(c => c.CredentialId)
            .ToListAsync(ct);
        return Ok(new
        {
            challenge = NewChallenge(),
            rpId = Rp(),
            timeout = 60_000,
            userVerification = "preferred",
            allowCredentials = creds.Select(id => new { type = "public-key", id }).ToArray(),
        });
    }

    [HttpPost("signin")]
    public async Task<IActionResult> SignIn([FromBody] SignInDto dto, CancellationToken ct)
    {
        var (tenant, user) = await ResolveContextAsync(_db, ct);
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
        if (dto.SignCount != 0 && dto.SignCount <= cred.SignCount)
        {
            // Authenticator counter regression — possible cloned key.
            await _lockout.OnFailureAsync(user, "webauthn", ct);
            return Unauthorized(new { error = "Authenticator counter regression.", kind = "unauthenticated" });
        }

        cred.SignCount = dto.SignCount;
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
