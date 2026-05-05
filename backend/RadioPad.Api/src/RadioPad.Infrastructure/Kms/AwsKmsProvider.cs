using Amazon;
using Amazon.KeyManagementService;
using Amazon.KeyManagementService.Model;
using RadioPad.Application.Services.Kms;

namespace RadioPad.Infrastructure.Kms;

/// <summary>
/// Iter-32 SEC-003 — AWS KMS adapter. Reference format
/// <c>aws:arn:aws:kms:&lt;region&gt;:&lt;account&gt;:key/&lt;id&gt;</c>.
/// Wrap is <see cref="IAmazonKeyManagementService.EncryptAsync(EncryptRequest, CancellationToken)"/>;
/// unwrap is <see cref="IAmazonKeyManagementService.DecryptAsync(DecryptRequest, CancellationToken)"/>.
/// Tenant isolation is bound into the request via the <c>EncryptionContext</c>
/// dictionary <c>{ "tenantId": &lt;guid&gt; }</c>; AWS rejects a Decrypt that
/// supplies a different context, so a wrapped DEK from one tenant cannot be
/// unwrapped while masquerading as another.
///
/// Region is parsed from the ARN; credentials come from the SDK's default
/// chain (environment, EC2/ECS instance role, shared profile, SSO). The
/// adapter never reads or logs raw credential material.
///
/// Required IAM permissions on the CMK: <c>kms:Encrypt</c>, <c>kms:Decrypt</c>,
/// <c>kms:DescribeKey</c>.
/// </summary>
public sealed class AwsKmsProvider : IKmsProvider
{
    public const string SchemeName = "aws";

    private readonly Func<RegionEndpoint, IAmazonKeyManagementService> _clientFactory;
    private readonly Dictionary<string, IAmazonKeyManagementService> _clientsByRegion = new(StringComparer.OrdinalIgnoreCase);

    public AwsKmsProvider() : this(r => new AmazonKeyManagementServiceClient(r)) { }

    /// <summary>Test seam: inject a client factory keyed by region.</summary>
    public AwsKmsProvider(Func<RegionEndpoint, IAmazonKeyManagementService> clientFactory)
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
        var (client, keyId) = ResolveClient(keyRef);
        try
        {
            using var ms = new MemoryStream(dataKey, writable: false);
            var resp = await client.EncryptAsync(new EncryptRequest
            {
                KeyId = keyId,
                Plaintext = ms,
                EncryptionContext = BuildContext(tenantId),
            }, ct).ConfigureAwait(false);
            return resp.CiphertextBlob.ToArray();
        }
        catch (AmazonKeyManagementServiceException ex)
        {
            throw new KmsUnavailableException($"AWS KMS Encrypt failed: {ex.ErrorCode ?? ex.GetType().Name}.", ex);
        }
    }

    public async Task<byte[]> UnwrapAsync(string keyRef, byte[] wrappedKey, string? tenantId, CancellationToken ct)
    {
        var (client, keyId) = ResolveClient(keyRef);
        try
        {
            using var ms = new MemoryStream(wrappedKey, writable: false);
            var resp = await client.DecryptAsync(new DecryptRequest
            {
                KeyId = keyId,
                CiphertextBlob = ms,
                EncryptionContext = BuildContext(tenantId),
            }, ct).ConfigureAwait(false);
            return resp.Plaintext.ToArray();
        }
        catch (AmazonKeyManagementServiceException ex)
        {
            throw new KmsUnavailableException($"AWS KMS Decrypt failed: {ex.ErrorCode ?? ex.GetType().Name}.", ex);
        }
    }

    public async Task<bool> VerifyAsync(string keyRef, CancellationToken ct)
    {
        try
        {
            var (client, keyId) = ResolveClient(keyRef);
            await client.DescribeKeyAsync(new DescribeKeyRequest { KeyId = keyId }, ct).ConfigureAwait(false);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private (IAmazonKeyManagementService client, string keyId) ResolveClient(string keyRef)
    {
        var arn = ParseArn(keyRef);
        var region = ParseRegion(arn);
        if (!_clientsByRegion.TryGetValue(region.SystemName, out var client))
        {
            client = _clientFactory(region);
            _clientsByRegion[region.SystemName] = client;
        }
        return (client, arn);
    }

    public static string ParseArn(string keyRef)
    {
        // Accept "aws:arn:aws:kms:..." (preferred) and bare "arn:aws:kms:..." for resilience.
        if (keyRef is null) throw new ArgumentNullException(nameof(keyRef));
        var rest = keyRef.StartsWith("aws:", StringComparison.OrdinalIgnoreCase) ? keyRef[4..] : keyRef;
        if (!rest.StartsWith("arn:aws:kms:", StringComparison.OrdinalIgnoreCase))
            throw new KmsUnavailableException($"AWS KMS keyRef must be 'aws:arn:aws:kms:<region>:<acct>:key/<id>'. Got: {keyRef}");
        return rest;
    }

    public static RegionEndpoint ParseRegion(string arn)
    {
        // arn:aws:kms:<region>:<acct>:key/<id>
        var parts = arn.Split(':');
        if (parts.Length < 6 || string.IsNullOrEmpty(parts[3]))
            throw new KmsUnavailableException($"AWS KMS keyRef ARN missing region: {arn}");
        return RegionEndpoint.GetBySystemName(parts[3]);
    }

    private static Dictionary<string, string> BuildContext(string? tenantId)
        => new(StringComparer.Ordinal)
        {
            ["tenantId"] = tenantId ?? "",
        };
}
