using Google;
using Google.Cloud.Kms.V1;
using Google.Protobuf;
using RadioPad.Application.Services.Kms;

namespace RadioPad.Infrastructure.Kms;

/// <summary>
/// Iter-32 SEC-003 — Google Cloud KMS adapter. Reference format
/// <c>gcp:projects/&lt;p&gt;/locations/&lt;l&gt;/keyRings/&lt;r&gt;/cryptoKeys/&lt;k&gt;</c>.
/// Wraps with <see cref="KeyManagementServiceClient.EncryptAsync(EncryptRequest, Google.Api.Gax.Grpc.CallSettings)"/>
/// using <c>AdditionalAuthenticatedData = utf8(tenantId)</c> so a wrapped DEK
/// from one tenant cannot be unwrapped while masquerading as another.
///
/// Credentials come from the SDK's default chain (ADC: env var
/// <c>GOOGLE_APPLICATION_CREDENTIALS</c>, GCE/GKE workload identity, gcloud
/// CLI credentials). Required IAM role on the key:
/// <c>roles/cloudkms.cryptoKeyEncrypterDecrypter</c>.
/// </summary>
public sealed class GcpKmsProvider : IKmsProvider
{
    public const string SchemeName = "gcp";

    private readonly Func<IGcpKmsClient> _clientFactory;
    private IGcpKmsClient? _client;

    public GcpKmsProvider() : this(() => new RealGcpKmsClient(KeyManagementServiceClient.Create())) { }

    /// <summary>Test seam: inject a client factory.</summary>
    public GcpKmsProvider(Func<IGcpKmsClient> clientFactory)
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
        var name = ParseResource(keyRef);
        var client = ResolveClient();
        try
        {
            var resp = await client.EncryptAsync(new EncryptRequest
            {
                Name = name,
                Plaintext = ByteString.CopyFrom(dataKey),
                AdditionalAuthenticatedData = ByteString.CopyFromUtf8(tenantId ?? ""),
            }, ct).ConfigureAwait(false);
            return resp.Ciphertext.ToByteArray();
        }
        catch (Grpc.Core.RpcException ex)
        {
            throw new KmsUnavailableException($"GCP KMS Encrypt failed: {ex.StatusCode}.", ex);
        }
        catch (GoogleApiException ex)
        {
            throw new KmsUnavailableException($"GCP KMS Encrypt failed: {ex.HttpStatusCode}.", ex);
        }
    }

    public async Task<byte[]> UnwrapAsync(string keyRef, byte[] wrappedKey, string? tenantId, CancellationToken ct)
    {
        var name = ParseResource(keyRef);
        var client = ResolveClient();
        try
        {
            var resp = await client.DecryptAsync(new DecryptRequest
            {
                Name = name,
                Ciphertext = ByteString.CopyFrom(wrappedKey),
                AdditionalAuthenticatedData = ByteString.CopyFromUtf8(tenantId ?? ""),
            }, ct).ConfigureAwait(false);
            return resp.Plaintext.ToByteArray();
        }
        catch (Grpc.Core.RpcException ex)
        {
            throw new KmsUnavailableException($"GCP KMS Decrypt failed: {ex.StatusCode}.", ex);
        }
        catch (GoogleApiException ex)
        {
            throw new KmsUnavailableException($"GCP KMS Decrypt failed: {ex.HttpStatusCode}.", ex);
        }
    }

    public async Task<bool> VerifyAsync(string keyRef, CancellationToken ct)
    {
        try
        {
            // Round-trip wrap+unwrap a probe key to confirm both permissions.
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

    private IGcpKmsClient ResolveClient() => _client ??= _clientFactory();

    public static string ParseResource(string keyRef)
    {
        if (keyRef is null) throw new ArgumentNullException(nameof(keyRef));
        if (!keyRef.StartsWith("gcp:", StringComparison.OrdinalIgnoreCase))
            throw new KmsUnavailableException($"GCP KMS keyRef must start with 'gcp:'. Got: {keyRef}");
        var rest = keyRef[4..];
        if (!rest.StartsWith("projects/", StringComparison.Ordinal))
            throw new KmsUnavailableException($"GCP KMS keyRef must be 'gcp:projects/.../cryptoKeys/...'. Got: {keyRef}");
        return rest;
    }
}

/// <summary>Adapter interface around <see cref="KeyManagementServiceClient"/> so tests can mock without spinning up a real client.</summary>
public interface IGcpKmsClient
{
    Task<EncryptResponse> EncryptAsync(EncryptRequest request, CancellationToken ct);
    Task<DecryptResponse> DecryptAsync(DecryptRequest request, CancellationToken ct);
}

internal sealed class RealGcpKmsClient : IGcpKmsClient
{
    private readonly KeyManagementServiceClient _inner;
    public RealGcpKmsClient(KeyManagementServiceClient inner) => _inner = inner;

    public Task<EncryptResponse> EncryptAsync(EncryptRequest request, CancellationToken ct)
        => _inner.EncryptAsync(request, Google.Api.Gax.Grpc.CallSettings.FromCancellationToken(ct));

    public Task<DecryptResponse> DecryptAsync(DecryptRequest request, CancellationToken ct)
        => _inner.DecryptAsync(request, Google.Api.Gax.Grpc.CallSettings.FromCancellationToken(ct));
}
