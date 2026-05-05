using System.Security.Cryptography;

namespace RadioPad.Application.Services.Kms;

/// <summary>
/// PRD SEC-003 — pluggable KMS abstraction for envelope encryption of
/// tenant data keys. RadioPad never holds the master key in-process: it
/// asks the configured <see cref="IKmsProvider"/> to wrap/unwrap a per-tenant
/// data key, then uses that data key with AES-GCM at the application layer.
///
/// The reference is an opaque scheme-prefixed string (e.g.
/// <c>"aws-kms:arn:aws:kms:..."</c>, <c>"azure-kv:https://vault/.../keys/..."</c>,
/// <c>"gcp-kms:projects/.../cryptoKeys/..."</c>, <c>"env:RADIOPAD_TENANT_KEY"</c>,
/// <c>"local:/var/lib/radiopad/keys/&lt;tenant&gt;.key"</c>). The provider that
/// owns the scheme is selected by <see cref="IKmsResolver"/>.
///
/// Adapters MUST NOT log the unwrapped data key. Adapters MUST surface a
/// permission/availability failure as <see cref="KmsUnavailableException"/>
/// so callers can fail closed.
/// </summary>
public interface IKmsProvider
{
    /// <summary>Scheme prefix this provider handles (e.g. <c>"aws-kms"</c>).</summary>
    string Scheme { get; }

    /// <summary>Wrap a fresh 32-byte data key under the master key referenced by <paramref name="keyRef"/>.</summary>
    Task<byte[]> WrapAsync(string keyRef, byte[] dataKey, CancellationToken ct);

    /// <summary>Unwrap a previously wrapped data key.</summary>
    Task<byte[]> UnwrapAsync(string keyRef, byte[] wrappedKey, CancellationToken ct);

    /// <summary>
    /// Iter-32 SEC-003 — tenant-aware wrap. Cloud providers MUST bind
    /// <paramref name="tenantId"/> into their AAD / encryption-context so a
    /// wrapped DEK from one tenant cannot be unwrapped while masquerading as
    /// another. <c>env:</c> and <c>local:</c> ignore the parameter (tenant
    /// isolation is provided by the per-tenant master key file/var). Default
    /// implementation forwards to the legacy overload for back-compat.
    /// </summary>
    Task<byte[]> WrapAsync(string keyRef, byte[] dataKey, string? tenantId, CancellationToken ct)
        => WrapAsync(keyRef, dataKey, ct);

    /// <summary>Tenant-aware unwrap. See <see cref="WrapAsync(string, byte[], string?, CancellationToken)"/>.</summary>
    Task<byte[]> UnwrapAsync(string keyRef, byte[] wrappedKey, string? tenantId, CancellationToken ct)
        => UnwrapAsync(keyRef, wrappedKey, ct);

    /// <summary>
    /// Cheap health check used by `keys verify` and the tenant-settings UI.
    /// Returns true iff the provider can reach the master key referenced.
    /// MUST NOT throw on permission errors — return false instead so the
    /// admin UI can present the failure as a banner rather than a 5xx.
    /// </summary>
    Task<bool> VerifyAsync(string keyRef, CancellationToken ct);

    /// <summary>
    /// Iter-31 SEC-002 — rotate the data key wrapped under <paramref name="keyRef"/>.
    /// Implementations re-wrap the supplied <paramref name="oldWrappedKey"/> under
    /// the (potentially rolled) master key and return the freshly wrapped blob.
    /// Default implementation is unwrap-then-wrap, which is correct for static
    /// master keys; cloud KMS adapters may override with native rotation.
    /// </summary>
    async Task<byte[]> RotateAsync(string keyRef, byte[] oldWrappedKey, CancellationToken ct)
    {
        var dek = await UnwrapAsync(keyRef, oldWrappedKey, ct);
        try { return await WrapAsync(keyRef, dek, ct); }
        finally { Array.Clear(dek, 0, dek.Length); }
    }
}

/// <summary>Routes a `scheme:rest` reference to the matching provider.</summary>
public interface IKmsResolver
{
    IKmsProvider Resolve(string keyRef);
}

public sealed class DefaultKmsResolver : IKmsResolver
{
    private readonly Dictionary<string, IKmsProvider> _providers;
    public DefaultKmsResolver(IEnumerable<IKmsProvider> providers)
    {
        _providers = providers.ToDictionary(p => p.Scheme, StringComparer.OrdinalIgnoreCase);
    }
    public IKmsProvider Resolve(string keyRef)
    {
        var idx = keyRef.IndexOf(':');
        if (idx <= 0) throw new ArgumentException($"keyRef must be in the form 'scheme:rest'. Got: {keyRef}");
        var scheme = keyRef[..idx];
        if (!_providers.TryGetValue(scheme, out var p))
            throw new KmsUnavailableException($"No KMS provider registered for scheme '{scheme}'.");
        return p;
    }
}

public sealed class KmsUnavailableException : Exception
{
    public KmsUnavailableException(string message) : base(message) { }
    public KmsUnavailableException(string message, Exception inner) : base(message, inner) { }
}

/// <summary>
/// Dev / on-prem KMS where the master key is supplied as a base64-encoded
/// 32-byte AES-256 key in the named environment variable. The reference is
/// <c>"env:NAME"</c>. Wrapping is AES-GCM with a random 12-byte nonce
/// prefixed to the ciphertext + tag.
/// </summary>
public sealed class EnvKmsProvider : IKmsProvider
{
    public string Scheme => "env";

    public Task<byte[]> WrapAsync(string keyRef, byte[] dataKey, CancellationToken ct)
        => Task.FromResult(AesGcmWrap(LoadKey(keyRef), dataKey));

    public Task<byte[]> UnwrapAsync(string keyRef, byte[] wrappedKey, CancellationToken ct)
        => Task.FromResult(AesGcmUnwrap(LoadKey(keyRef), wrappedKey));

    public Task<bool> VerifyAsync(string keyRef, CancellationToken ct)
    {
        try { _ = LoadKey(keyRef); return Task.FromResult(true); }
        catch { return Task.FromResult(false); }
    }

    private static byte[] LoadKey(string keyRef)
    {
        // keyRef = "env:NAME"
        var name = keyRef[(keyRef.IndexOf(':') + 1)..].Trim();
        if (string.IsNullOrEmpty(name))
            throw new KmsUnavailableException("env KMS reference is missing the variable name.");
        var b64 = Environment.GetEnvironmentVariable(name);
        if (string.IsNullOrEmpty(b64))
            throw new KmsUnavailableException($"env KMS variable '{name}' is unset.");
        byte[] key;
        try { key = Convert.FromBase64String(b64); }
        catch (FormatException ex) { throw new KmsUnavailableException("env KMS key is not valid base64.", ex); }
        if (key.Length != 32)
            throw new KmsUnavailableException("env KMS key must decode to 32 bytes (AES-256).");
        return key;
    }

    private static byte[] AesGcmWrap(byte[] key, byte[] plain)
    {
        var nonce = RandomNumberGenerator.GetBytes(12);
        var cipher = new byte[plain.Length];
        var tag = new byte[16];
        using var aes = new AesGcm(key, 16);
        aes.Encrypt(nonce, plain, cipher, tag);
        var blob = new byte[12 + cipher.Length + 16];
        Buffer.BlockCopy(nonce, 0, blob, 0, 12);
        Buffer.BlockCopy(cipher, 0, blob, 12, cipher.Length);
        Buffer.BlockCopy(tag, 0, blob, 12 + cipher.Length, 16);
        return blob;
    }

    private static byte[] AesGcmUnwrap(byte[] key, byte[] blob)
    {
        if (blob.Length < 12 + 16) throw new KmsUnavailableException("Wrapped key is too short.");
        var nonce = new byte[12];
        var tag = new byte[16];
        var cipher = new byte[blob.Length - 12 - 16];
        Buffer.BlockCopy(blob, 0, nonce, 0, 12);
        Buffer.BlockCopy(blob, 12, cipher, 0, cipher.Length);
        Buffer.BlockCopy(blob, 12 + cipher.Length, tag, 0, 16);
        var plain = new byte[cipher.Length];
        using var aes = new AesGcm(key, 16);
        aes.Decrypt(nonce, cipher, tag, plain);
        return plain;
    }
}

/// <summary>
/// File-backed KMS for tests + on-prem appliances. Reference is
/// <c>"local:/abs/path/to/key.bin"</c>. The file must contain a raw 32-byte
/// AES-256 key. Same AES-GCM wrap/unwrap shape as <see cref="EnvKmsProvider"/>.
/// </summary>
public sealed class LocalKmsProvider : IKmsProvider
{
    public string Scheme => "local";

    public Task<byte[]> WrapAsync(string keyRef, byte[] dataKey, CancellationToken ct)
        => Task.FromResult(EnvKmsProviderWrap(LoadKey(keyRef), dataKey));

    public Task<byte[]> UnwrapAsync(string keyRef, byte[] wrappedKey, CancellationToken ct)
        => Task.FromResult(EnvKmsProviderUnwrap(LoadKey(keyRef), wrappedKey));

    public Task<bool> VerifyAsync(string keyRef, CancellationToken ct)
    {
        try { _ = LoadKey(keyRef); return Task.FromResult(true); }
        catch { return Task.FromResult(false); }
    }

    private static byte[] LoadKey(string keyRef)
    {
        var path = keyRef[(keyRef.IndexOf(':') + 1)..];
        if (!File.Exists(path)) throw new KmsUnavailableException($"local KMS key file not found: {path}");
        var key = File.ReadAllBytes(path);
        if (key.Length != 32) throw new KmsUnavailableException("local KMS key must be 32 bytes.");
        return key;
    }

    // Reuse AES-GCM helpers from EnvKmsProvider via private static delegation.
    private static byte[] EnvKmsProviderWrap(byte[] key, byte[] plain)
    {
        var nonce = RandomNumberGenerator.GetBytes(12);
        var cipher = new byte[plain.Length];
        var tag = new byte[16];
        using var aes = new AesGcm(key, 16);
        aes.Encrypt(nonce, plain, cipher, tag);
        var blob = new byte[12 + cipher.Length + 16];
        Buffer.BlockCopy(nonce, 0, blob, 0, 12);
        Buffer.BlockCopy(cipher, 0, blob, 12, cipher.Length);
        Buffer.BlockCopy(tag, 0, blob, 12 + cipher.Length, 16);
        return blob;
    }
    private static byte[] EnvKmsProviderUnwrap(byte[] key, byte[] blob)
    {
        if (blob.Length < 12 + 16) throw new KmsUnavailableException("Wrapped key is too short.");
        var nonce = new byte[12];
        var tag = new byte[16];
        var cipher = new byte[blob.Length - 12 - 16];
        Buffer.BlockCopy(blob, 0, nonce, 0, 12);
        Buffer.BlockCopy(blob, 12, cipher, 0, cipher.Length);
        Buffer.BlockCopy(blob, 12 + cipher.Length, tag, 0, 16);
        var plain = new byte[cipher.Length];
        using var aes = new AesGcm(key, 16);
        aes.Decrypt(nonce, cipher, tag, plain);
        return plain;
    }
}

// Iter-32 SEC-003 — real cloud-KMS adapters live under
// RadioPad.Infrastructure.Kms (`AwsKmsProvider`, `AzureKeyVaultKmsProvider`,
// `GcpKmsProvider`) so this Application layer stays free of cloud SDK
// dependencies. Their schemes are `aws:`, `azkv:`, `gcp:`.
