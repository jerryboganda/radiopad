using System.Security.Cryptography;
using System.Text;
using RadioPad.Application.Abstractions;
using RadioPad.Application.Services.Kms;
using RadioPad.Domain.Entities;

namespace RadioPad.Application.Services;

/// <summary>
/// Iter-35 PROV-007 — encrypts, persists, rotates, and deletes per-provider
/// OAuth refresh tokens using envelope encryption. The plaintext token is
/// encrypted with a fresh AES-256-GCM data-encryption key (DEK) per save;
/// the DEK is wrapped under the tenant's KMS-managed key-encryption key
/// (KEK). Only the wrapped DEK + ciphertext + IV + tag are written to the
/// database; the plaintext DEK is zeroed immediately after use.
///
/// The vault never logs and never returns ciphertext, IV, tag or wrapped
/// DEK bytes from any public method. Audit rows record the action kind and
/// the provider id, never the token bytes.
///
/// Caller resolves the per-tenant KEK reference (typically
/// <c>TenantSettings.CmkKeyRef</c> with an env-var fallback) and passes it
/// to every vault call so the vault stays decoupled from the persistence
/// layer.
/// </summary>
public sealed class OAuthRefreshVault
{
    /// <summary>
    /// Operator-supplied env-var fallback used by callers that have no
    /// per-tenant KEK configured. Documented in
    /// <c>docs/04-security/security-architecture.md</c>.
    /// </summary>
    public const string FallbackKekEnvRef = "env:RADIOPAD_TENANT_KEK_DEFAULT";

    private readonly IKmsResolver _kms;

    public OAuthRefreshVault(IKmsResolver kms)
    {
        _kms = kms ?? throw new ArgumentNullException(nameof(kms));
    }

    /// <summary>
    /// Resolve a usable KEK reference for <paramref name="tenantCmkKeyRef"/>.
    /// Returns the per-tenant ref when set; otherwise the operator fallback
    /// when the env var is populated; otherwise throws so callers fail
    /// closed (per security boundaries).
    /// </summary>
    public static string ResolveKekRef(string? tenantCmkKeyRef)
    {
        if (!string.IsNullOrWhiteSpace(tenantCmkKeyRef)) return tenantCmkKeyRef;
        var fallback = Environment.GetEnvironmentVariable("RADIOPAD_TENANT_KEK_DEFAULT");
        if (string.IsNullOrEmpty(fallback))
            throw new KmsUnavailableException(
                "No tenant KEK configured. Set TenantSettings.CmkKeyRef or RADIOPAD_TENANT_KEK_DEFAULT.");
        return FallbackKekEnvRef;
    }

    /// <summary>
    /// Encrypt <paramref name="refreshToken"/> and stamp the four ciphertext
    /// columns + timestamps onto <paramref name="provider"/>. Does not call
    /// SaveChangesAsync; the caller persists the change.
    /// </summary>
    public async Task SaveAsync(
        Tenant tenant,
        ProviderConfig provider,
        string keyRef,
        string refreshToken,
        DateTimeOffset? expiresAt,
        string? rotationPolicy,
        CancellationToken ct)
    {
        if (tenant is null) throw new ArgumentNullException(nameof(tenant));
        if (provider is null) throw new ArgumentNullException(nameof(provider));
        if (string.IsNullOrEmpty(refreshToken))
            throw new ArgumentException("Refresh token must not be empty.", nameof(refreshToken));
        if (string.IsNullOrWhiteSpace(keyRef))
            throw new ArgumentException("KEK reference must be supplied.", nameof(keyRef));
        if (provider.TenantId != tenant.Id)
            throw new InvalidOperationException("Provider does not belong to the supplied tenant.");

        var kms = _kms.Resolve(keyRef);

        var dek = RandomNumberGenerator.GetBytes(32);
        try
        {
            var iv = RandomNumberGenerator.GetBytes(12);
            var plaintext = Encoding.UTF8.GetBytes(refreshToken);
            var ciphertext = new byte[plaintext.Length];
            var tag = new byte[16];
            using (var aes = new AesGcm(dek, 16))
            {
                aes.Encrypt(iv, plaintext, ciphertext, tag);
            }
            CryptographicOperations.ZeroMemory(plaintext);

            var wrappedDek = await kms.WrapAsync(keyRef, dek, tenant.Id.ToString(), ct).ConfigureAwait(false);

            provider.OAuthRefreshTokenEnc = ciphertext;
            provider.OAuthRefreshTokenIv = iv;
            provider.OAuthRefreshTokenTag = tag;
            provider.OAuthRefreshTokenWrappedDek = wrappedDek;
            provider.OAuthRefreshTokenUpdatedAt = DateTimeOffset.UtcNow;
            provider.OAuthRefreshTokenExpiresAt = expiresAt;
            if (!string.IsNullOrWhiteSpace(rotationPolicy))
            {
                provider.OAuthRefreshTokenRotationPolicy = NormalizePolicy(rotationPolicy);
            }
            else if (string.IsNullOrEmpty(provider.OAuthRefreshTokenRotationPolicy))
            {
                provider.OAuthRefreshTokenRotationPolicy = "before_expiry";
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(dek);
        }
    }

    /// <summary>
    /// Decrypt and return the stored refresh token, or null when no token
    /// is stored. Throws <see cref="KmsUnavailableException"/> when the KEK
    /// cannot be resolved.
    /// </summary>
    public async Task<string?> LoadAsync(
        Tenant tenant,
        ProviderConfig provider,
        string keyRef,
        CancellationToken ct)
    {
        if (provider.OAuthRefreshTokenEnc is null
            || provider.OAuthRefreshTokenIv is null
            || provider.OAuthRefreshTokenTag is null
            || provider.OAuthRefreshTokenWrappedDek is null)
        {
            return null;
        }
        if (provider.TenantId != tenant.Id)
            throw new InvalidOperationException("Provider does not belong to the supplied tenant.");
        if (string.IsNullOrWhiteSpace(keyRef))
            throw new ArgumentException("KEK reference must be supplied.", nameof(keyRef));

        var kms = _kms.Resolve(keyRef);
        var dek = await kms.UnwrapAsync(keyRef, provider.OAuthRefreshTokenWrappedDek, tenant.Id.ToString(), ct)
            .ConfigureAwait(false);
        if (dek.Length != 32)
        {
            CryptographicOperations.ZeroMemory(dek);
            throw new InvalidOperationException("Unwrapped OAuth refresh-token DEK must be 32 bytes.");
        }

        try
        {
            var plain = new byte[provider.OAuthRefreshTokenEnc.Length];
            using var aes = new AesGcm(dek, 16);
            aes.Decrypt(
                provider.OAuthRefreshTokenIv,
                provider.OAuthRefreshTokenEnc,
                provider.OAuthRefreshTokenTag,
                plain);
            try { return Encoding.UTF8.GetString(plain); }
            finally { CryptographicOperations.ZeroMemory(plain); }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(dek);
        }
    }

    /// <summary>
    /// Rotate the stored refresh token using <paramref name="issuer"/>.
    /// Returns true on success, false when the issuer cannot refresh, no
    /// token is stored, or the upstream call fails. The caller decides
    /// whether to audit + persist.
    /// </summary>
    public async Task<bool> RotateAsync(
        Tenant tenant,
        ProviderConfig provider,
        string keyRef,
        IOAuthTokenIssuer issuer,
        CancellationToken ct)
    {
        if (issuer is null || !issuer.CanRefresh) return false;
        var current = await LoadAsync(tenant, provider, keyRef, ct).ConfigureAwait(false);
        if (current is null) return false;

        var (next, expiresAt) = await issuer.RefreshAsync(provider, current, ct).ConfigureAwait(false);
        if (string.IsNullOrEmpty(next)) return false;
        await SaveAsync(tenant, provider, keyRef, next, expiresAt, provider.OAuthRefreshTokenRotationPolicy, ct)
            .ConfigureAwait(false);
        return true;
    }

    /// <summary>
    /// Clear all OAuth refresh-token columns on <paramref name="provider"/>.
    /// Caller persists the change.
    /// </summary>
    public void Delete(ProviderConfig provider)
    {
        if (provider is null) throw new ArgumentNullException(nameof(provider));
        if (provider.OAuthRefreshTokenEnc is { } e) CryptographicOperations.ZeroMemory(e);
        if (provider.OAuthRefreshTokenIv is { } i) CryptographicOperations.ZeroMemory(i);
        if (provider.OAuthRefreshTokenTag is { } t) CryptographicOperations.ZeroMemory(t);
        if (provider.OAuthRefreshTokenWrappedDek is { } w) CryptographicOperations.ZeroMemory(w);
        provider.OAuthRefreshTokenEnc = null;
        provider.OAuthRefreshTokenIv = null;
        provider.OAuthRefreshTokenTag = null;
        provider.OAuthRefreshTokenWrappedDek = null;
        provider.OAuthRefreshTokenUpdatedAt = null;
        provider.OAuthRefreshTokenExpiresAt = null;
        // Rotation policy is preserved so a re-saved token follows the same cadence.
    }

    /// <summary>
    /// Returns true when the provider's rotation policy + stored expiry
    /// indicate the token should be rotated now. Used by the rotation
    /// background service.
    /// </summary>
    public static bool ShouldRotate(ProviderConfig provider, DateTimeOffset now)
    {
        if (provider.OAuthRefreshTokenEnc is null) return false;
        var policy = NormalizePolicy(provider.OAuthRefreshTokenRotationPolicy);
        return policy switch
        {
            "never" => false,
            "every_24h" => provider.OAuthRefreshTokenUpdatedAt is null
                || now - provider.OAuthRefreshTokenUpdatedAt.Value >= TimeSpan.FromHours(24),
            // "before_expiry" (default) — rotate when expiry is within 1h.
            _ => provider.OAuthRefreshTokenExpiresAt is { } exp
                && exp <= now + TimeSpan.FromHours(1),
        };
    }

    private static string NormalizePolicy(string? policy)
    {
        if (string.IsNullOrWhiteSpace(policy)) return "before_expiry";
        return policy.Trim().ToLowerInvariant() switch
        {
            "never" => "never",
            "every_24h" => "every_24h",
            "before_expiry" => "before_expiry",
            _ => "before_expiry",
        };
    }
}
