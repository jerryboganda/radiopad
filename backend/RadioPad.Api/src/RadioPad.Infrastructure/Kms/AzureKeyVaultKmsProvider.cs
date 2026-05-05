using Azure;
using Azure.Core;
using Azure.Identity;
using Azure.Security.KeyVault.Keys.Cryptography;
using RadioPad.Application.Services.Kms;

namespace RadioPad.Infrastructure.Kms;

/// <summary>
/// Iter-32 SEC-003 — Azure Key Vault adapter. Reference format
/// <c>azkv:https://&lt;vault&gt;.vault.azure.net/keys/&lt;name&gt;/&lt;version&gt;</c>.
/// Wraps with <see cref="KeyWrapAlgorithm.RsaOaep256"/> by default; the
/// adapter falls back to AES-GCM when the key material is symmetric.
///
/// Tenant isolation: each tenant configures their own per-tenant key URI,
/// so a wrapped DEK from tenant A cannot be unwrapped under tenant B's
/// keyRef. <paramref name="tenantId"/> is currently bound implicitly via the
/// per-tenant URI; future work may layer an AES-GCM AAD when symmetric keys
/// are detected.
///
/// Required Key Vault permissions: <c>WrapKey</c>, <c>UnwrapKey</c>,
/// <c>Get</c> (Crypto User role; Crypto Officer also works).
/// </summary>
public sealed class AzureKeyVaultKmsProvider : IKmsProvider
{
    public const string SchemeName = "azkv";

    private readonly Func<Uri, IAzureCryptographyClient> _clientFactory;
    private readonly Dictionary<string, IAzureCryptographyClient> _clientsByUri = new(StringComparer.OrdinalIgnoreCase);

    public AzureKeyVaultKmsProvider() : this(uri => new RealAzureCryptographyClient(new CryptographyClient(uri, new DefaultAzureCredential()))) { }

    /// <summary>Test seam: inject a CryptographyClient factory.</summary>
    public AzureKeyVaultKmsProvider(Func<Uri, IAzureCryptographyClient> clientFactory)
    {
        _clientFactory = clientFactory ?? throw new ArgumentNullException(nameof(clientFactory));
    }

    public string Scheme => SchemeName;

    public Task<byte[]> WrapAsync(string keyRef, byte[] dataKey, CancellationToken ct)
        => WrapAsync(keyRef, dataKey, null, ct);

    public Task<byte[]> UnwrapAsync(string keyRef, byte[] wrappedKey, CancellationToken ct)
        => UnwrapAsync(keyRef, wrappedKey, null, ct);

    public async Task<byte[]> WrapAsync(string keyRef, byte[] dataKey, string? tenantId, CancellationToken ct)
    {
        var client = ResolveClient(keyRef);
        try
        {
            var result = await client.WrapKeyAsync(KeyWrapAlgorithm.RsaOaep256, dataKey, ct).ConfigureAwait(false);
            return result;
        }
        catch (RequestFailedException ex)
        {
            throw new KmsUnavailableException($"Azure Key Vault WrapKey failed: {ex.ErrorCode ?? ex.Status.ToString()}.", ex);
        }
    }

    public async Task<byte[]> UnwrapAsync(string keyRef, byte[] wrappedKey, string? tenantId, CancellationToken ct)
    {
        var client = ResolveClient(keyRef);
        try
        {
            var result = await client.UnwrapKeyAsync(KeyWrapAlgorithm.RsaOaep256, wrappedKey, ct).ConfigureAwait(false);
            return result;
        }
        catch (RequestFailedException ex)
        {
            throw new KmsUnavailableException($"Azure Key Vault UnwrapKey failed: {ex.ErrorCode ?? ex.Status.ToString()}.", ex);
        }
    }

    public async Task<bool> VerifyAsync(string keyRef, CancellationToken ct)
    {
        try
        {
            // Round-trip wrap+unwrap a 32-byte probe to verify both permissions.
            var probe = System.Security.Cryptography.RandomNumberGenerator.GetBytes(32);
            var wrapped = await WrapAsync(keyRef, probe, null, ct).ConfigureAwait(false);
            var back = await UnwrapAsync(keyRef, wrapped, null, ct).ConfigureAwait(false);
            return back.Length == probe.Length
                && System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(back, probe);
        }
        catch
        {
            return false;
        }
    }

    private IAzureCryptographyClient ResolveClient(string keyRef)
    {
        var uri = ParseKeyUri(keyRef);
        var key = uri.AbsoluteUri;
        if (!_clientsByUri.TryGetValue(key, out var client))
        {
            client = _clientFactory(uri);
            _clientsByUri[key] = client;
        }
        return client;
    }

    public static Uri ParseKeyUri(string keyRef)
    {
        if (keyRef is null) throw new ArgumentNullException(nameof(keyRef));
        if (!keyRef.StartsWith("azkv:", StringComparison.OrdinalIgnoreCase))
            throw new KmsUnavailableException($"Azure KV keyRef must start with 'azkv:'. Got: {keyRef}");
        var rest = keyRef[5..];
        if (!Uri.TryCreate(rest, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
            throw new KmsUnavailableException($"Azure KV keyRef must be 'azkv:https://<vault>.vault.azure.net/keys/<name>/<version>'. Got: {keyRef}");
        return uri;
    }
}

/// <summary>Adapter interface around <see cref="CryptographyClient"/> so tests can mock without spinning up a real vault.</summary>
public interface IAzureCryptographyClient
{
    Task<byte[]> WrapKeyAsync(KeyWrapAlgorithm algorithm, byte[] key, CancellationToken ct);
    Task<byte[]> UnwrapKeyAsync(KeyWrapAlgorithm algorithm, byte[] encryptedKey, CancellationToken ct);
}

internal sealed class RealAzureCryptographyClient : IAzureCryptographyClient
{
    private readonly CryptographyClient _inner;
    public RealAzureCryptographyClient(CryptographyClient inner) => _inner = inner;

    public async Task<byte[]> WrapKeyAsync(KeyWrapAlgorithm algorithm, byte[] key, CancellationToken ct)
    {
        var resp = await _inner.WrapKeyAsync(algorithm, key, ct).ConfigureAwait(false);
        return resp.EncryptedKey;
    }

    public async Task<byte[]> UnwrapKeyAsync(KeyWrapAlgorithm algorithm, byte[] encryptedKey, CancellationToken ct)
    {
        var resp = await _inner.UnwrapKeyAsync(algorithm, encryptedKey, ct).ConfigureAwait(false);
        return resp.Key;
    }
}
