using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using Amazon;
using Amazon.KeyManagementService;
using Amazon.KeyManagementService.Model;
using Amazon.Runtime;
using Azure.Security.KeyVault.Keys.Cryptography;
using Google.Cloud.Kms.V1;
using Google.Protobuf;
using RadioPad.Application.Services.Kms;
using RadioPad.Infrastructure.Kms;
using Xunit;

namespace RadioPad.Api.Tests.Kms;

public class KmsResolverDispatchTests
{
    [Fact]
    public void Resolves_All_Five_Schemes()
    {
        var resolver = new DefaultKmsResolver(new IKmsProvider[]
        {
            new EnvKmsProvider(),
            new LocalKmsProvider(),
            new AwsKmsProvider(_ => new FakeAwsKms(roundTrip: true)),
            new AzureKeyVaultKmsProvider(_ => new FakeAzureCrypto(roundTrip: true)),
            new GcpKmsProvider(() => new FakeGcpKms(roundTrip: true)),
        });

        Assert.Equal("env", resolver.Resolve("env:RADIOPAD_TEST_KEY").Scheme);
        Assert.Equal("local", resolver.Resolve("local:/tmp/key.bin").Scheme);
        Assert.Equal("aws", resolver.Resolve("aws:arn:aws:kms:us-east-1:111122223333:key/abcd").Scheme);
        Assert.Equal("azkv", resolver.Resolve("azkv:https://v.vault.azure.net/keys/k/v").Scheme);
        Assert.Equal("gcp", resolver.Resolve("gcp:projects/p/locations/l/keyRings/r/cryptoKeys/k").Scheme);
    }

    [Fact]
    public void Unknown_Scheme_Throws_KmsUnavailable()
    {
        var resolver = new DefaultKmsResolver(new IKmsProvider[] { new EnvKmsProvider() });
        Assert.Throws<KmsUnavailableException>(() => resolver.Resolve("unknown:foo"));
    }

    [Fact]
    public void Malformed_KeyRef_Throws_ArgumentException()
    {
        var resolver = new DefaultKmsResolver(new IKmsProvider[] { new EnvKmsProvider() });
        Assert.Throws<ArgumentException>(() => resolver.Resolve("no-scheme"));
    }
}

public class KmsAwsAdapterTests
{
    [Fact]
    public async Task Wrap_Unwrap_Round_Trip_Binds_TenantId_Into_EncryptionContext()
    {
        var fake = new FakeAwsKms(roundTrip: true);
        var sut = new AwsKmsProvider(_ => fake);
        var dek = RandomNumberGenerator.GetBytes(32);
        const string keyRef = "aws:arn:aws:kms:us-west-2:123456789012:key/aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee";
        var tenantId = Guid.NewGuid().ToString();

        var wrapped = await sut.WrapAsync(keyRef, dek, tenantId, CancellationToken.None);
        var back = await sut.UnwrapAsync(keyRef, wrapped, tenantId, CancellationToken.None);

        Assert.Equal(dek, back);
        Assert.Equal(tenantId, fake.LastEncryptContext["tenantId"]);
        Assert.Equal(tenantId, fake.LastDecryptContext["tenantId"]);
        Assert.Equal("arn:aws:kms:us-west-2:123456789012:key/aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee", fake.LastKeyId);
    }

    [Fact]
    public async Task Decrypt_With_Different_Tenant_Throws()
    {
        var fake = new FakeAwsKms(roundTrip: true);
        var sut = new AwsKmsProvider(_ => fake);
        const string keyRef = "aws:arn:aws:kms:us-west-2:123456789012:key/aaaa";
        var dek = RandomNumberGenerator.GetBytes(32);

        var wrapped = await sut.WrapAsync(keyRef, dek, "tenant-A", CancellationToken.None);
        await Assert.ThrowsAsync<KmsUnavailableException>(() =>
            sut.UnwrapAsync(keyRef, wrapped, "tenant-B", CancellationToken.None));
    }

    [Fact]
    public void ParseArn_Rejects_NonKms_Arn()
    {
        Assert.Throws<KmsUnavailableException>(() => AwsKmsProvider.ParseArn("aws:arn:aws:s3:::bucket"));
    }

    [Fact]
    public void ParseRegion_Extracts_Region_From_Arn()
    {
        var region = AwsKmsProvider.ParseRegion("arn:aws:kms:eu-central-1:111122223333:key/abcd");
        Assert.Equal(RegionEndpoint.EUCentral1, region);
    }

    [Fact(Skip = "Requires real AWS credentials. Set RADIOPAD_RUN_AWS_KMS_LIVE=1 + RADIOPAD_AWS_KMS_KEY_ARN to run.")]
    public async Task Live_Round_Trip_Against_Real_Aws_Kms()
    {
        if (Environment.GetEnvironmentVariable("RADIOPAD_RUN_AWS_KMS_LIVE") != "1") return;
        var keyRef = "aws:" + Environment.GetEnvironmentVariable("RADIOPAD_AWS_KMS_KEY_ARN");
        var sut = new AwsKmsProvider();
        var dek = RandomNumberGenerator.GetBytes(32);
        var wrapped = await sut.WrapAsync(keyRef, dek, "live-test-tenant", CancellationToken.None);
        var back = await sut.UnwrapAsync(keyRef, wrapped, "live-test-tenant", CancellationToken.None);
        Assert.Equal(dek, back);
    }
}

public class KmsAzureAdapterTests
{
    [Fact]
    public async Task Wrap_Unwrap_Round_Trip_Uses_RsaOaep256()
    {
        var fake = new FakeAzureCrypto(roundTrip: true);
        var sut = new AzureKeyVaultKmsProvider(_ => fake);
        var dek = RandomNumberGenerator.GetBytes(32);
        const string keyRef = "azkv:https://my-vault.vault.azure.net/keys/cmk/abcdef0123";
        var tenantId = Guid.NewGuid().ToString();

        var wrapped = await sut.WrapAsync(keyRef, dek, tenantId, CancellationToken.None);
        var back = await sut.UnwrapAsync(keyRef, wrapped, tenantId, CancellationToken.None);

        Assert.Equal(dek, back);
        Assert.Equal(KeyWrapAlgorithm.RsaOaep256, fake.LastWrapAlgorithm);
        Assert.Equal(KeyWrapAlgorithm.RsaOaep256, fake.LastUnwrapAlgorithm);
    }

    [Fact]
    public void ParseKeyUri_Rejects_NonHttps()
    {
        Assert.Throws<KmsUnavailableException>(() => AzureKeyVaultKmsProvider.ParseKeyUri("azkv:http://v/keys/k/v"));
        Assert.Throws<KmsUnavailableException>(() => AzureKeyVaultKmsProvider.ParseKeyUri("aws:arn:..."));
    }

    [Fact]
    public async Task VerifyAsync_Returns_True_On_Successful_Round_Trip()
    {
        var sut = new AzureKeyVaultKmsProvider(_ => new FakeAzureCrypto(roundTrip: true));
        Assert.True(await sut.VerifyAsync("azkv:https://v.vault.azure.net/keys/k/v", CancellationToken.None));
    }

    [Fact]
    public async Task VerifyAsync_Returns_False_When_RoundTrip_Fails()
    {
        var sut = new AzureKeyVaultKmsProvider(_ => new FakeAzureCrypto(roundTrip: false));
        Assert.False(await sut.VerifyAsync("azkv:https://v.vault.azure.net/keys/k/v", CancellationToken.None));
    }
}

public class KmsGcpAdapterTests
{
    [Fact]
    public async Task Wrap_Unwrap_Round_Trip_Binds_TenantId_Into_AAD()
    {
        var fake = new FakeGcpKms(roundTrip: true);
        var sut = new GcpKmsProvider(() => fake);
        var dek = RandomNumberGenerator.GetBytes(32);
        const string keyRef = "gcp:projects/proj/locations/global/keyRings/ring/cryptoKeys/cmk";
        var tenantId = Guid.NewGuid().ToString();

        var wrapped = await sut.WrapAsync(keyRef, dek, tenantId, CancellationToken.None);
        var back = await sut.UnwrapAsync(keyRef, wrapped, tenantId, CancellationToken.None);

        Assert.Equal(dek, back);
        Assert.Equal(tenantId, fake.LastEncryptAad.ToStringUtf8());
        Assert.Equal(tenantId, fake.LastDecryptAad.ToStringUtf8());
        Assert.Equal("projects/proj/locations/global/keyRings/ring/cryptoKeys/cmk", fake.LastEncryptName);
    }

    [Fact]
    public async Task Decrypt_With_Different_Tenant_AAD_Throws()
    {
        var fake = new FakeGcpKms(roundTrip: true);
        var sut = new GcpKmsProvider(() => fake);
        const string keyRef = "gcp:projects/p/locations/l/keyRings/r/cryptoKeys/k";
        var dek = RandomNumberGenerator.GetBytes(32);

        var wrapped = await sut.WrapAsync(keyRef, dek, "tenant-A", CancellationToken.None);
        await Assert.ThrowsAsync<KmsUnavailableException>(() =>
            sut.UnwrapAsync(keyRef, wrapped, "tenant-B", CancellationToken.None));
    }

    [Fact]
    public void ParseResource_Rejects_Bad_Path()
    {
        Assert.Throws<KmsUnavailableException>(() => GcpKmsProvider.ParseResource("gcp:locations/x"));
        Assert.Throws<KmsUnavailableException>(() => GcpKmsProvider.ParseResource("aws:arn:..."));
    }
}

[Collection(RadioPad.Api.Tests.Infrastructure.EnvironmentVariableCollection.Name)]
public class KmsEnvelopeRoundTripTests
{
    [Fact]
    public async Task TenantDekCache_Caches_Within_Ttl_And_Invalidates_On_Demand()
    {
        var keyB64 = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        Environment.SetEnvironmentVariable("RADIOPAD_TEST_TENANT_KEY", keyB64);
        try
        {
            var resolver = new DefaultKmsResolver(new IKmsProvider[] { new EnvKmsProvider() });
            var cache = new TenantDekCache(resolver);
            var tenantId = Guid.NewGuid();
            var keyRef = "env:RADIOPAD_TEST_TENANT_KEY";

            // Wrap a fresh DEK using the env provider (acting as the KEK).
            var dek = RandomNumberGenerator.GetBytes(32);
            var env = new EnvKmsProvider();
            var wrapped = await env.WrapAsync(keyRef, dek, CancellationToken.None);
            var wrappedB64 = Convert.ToBase64String(wrapped);

            var first = await cache.GetAsync(tenantId, keyRef, wrappedB64, CancellationToken.None);
            var second = await cache.GetAsync(tenantId, keyRef, wrappedB64, CancellationToken.None);
            Assert.Equal(dek, first);
            Assert.Equal(dek, second);

            cache.Invalidate(tenantId);
            var third = await cache.GetAsync(tenantId, keyRef, wrappedB64, CancellationToken.None);
            Assert.Equal(dek, third); // still correct after re-unwrap
        }
        finally
        {
            Environment.SetEnvironmentVariable("RADIOPAD_TEST_TENANT_KEY", null);
        }
    }

    [Fact]
    public async Task Aws_Adapter_Survives_Resolver_Round_Trip_Through_Default_Schemes()
    {
        var fake = new FakeAwsKms(roundTrip: true);
        var resolver = new DefaultKmsResolver(new IKmsProvider[]
        {
            new EnvKmsProvider(),
            new AwsKmsProvider(_ => fake),
        });
        var provider = resolver.Resolve("aws:arn:aws:kms:us-east-1:111111111111:key/abc");
        Assert.Equal("aws", provider.Scheme);
        var dek = RandomNumberGenerator.GetBytes(32);
        var wrapped = await provider.WrapAsync(
            "aws:arn:aws:kms:us-east-1:111111111111:key/abc", dek, "tenant-x", CancellationToken.None);
        var back = await provider.UnwrapAsync(
            "aws:arn:aws:kms:us-east-1:111111111111:key/abc", wrapped, "tenant-x", CancellationToken.None);
        Assert.Equal(dek, back);
    }
}

// ---------- in-memory fakes ----------

internal sealed class FakeAwsKms : AmazonKeyManagementServiceClient
{
    private readonly bool _roundTrip;
    private readonly Dictionary<string, (byte[] plain, Dictionary<string, string> ctx)> _vault = new();
    public Dictionary<string, string> LastEncryptContext { get; private set; } = new();
    public Dictionary<string, string> LastDecryptContext { get; private set; } = new();
    public string? LastKeyId { get; private set; }

    public FakeAwsKms(bool roundTrip)
        // Bogus credentials are fine because we never call out — base ctor needs them to construct.
        : base(new BasicAWSCredentials("AKIA_FAKE", "secret_fake"), RegionEndpoint.USEast1)
    {
        _roundTrip = roundTrip;
    }

    public override Task<Amazon.KeyManagementService.Model.EncryptResponse> EncryptAsync(Amazon.KeyManagementService.Model.EncryptRequest request, CancellationToken ct = default)
    {
        if (!_roundTrip) throw new AmazonKeyManagementServiceException("simulated failure");
        LastKeyId = request.KeyId;
        LastEncryptContext = new Dictionary<string, string>(request.EncryptionContext ?? new Dictionary<string, string>());
        var plain = request.Plaintext.ToArray();
        var token = Guid.NewGuid().ToString("N");
        _vault[token] = (plain, LastEncryptContext);
        var blob = Encoding.UTF8.GetBytes(token);
        return Task.FromResult(new Amazon.KeyManagementService.Model.EncryptResponse
        {
            KeyId = request.KeyId,
            CiphertextBlob = new MemoryStream(blob),
        });
    }

    public override Task<Amazon.KeyManagementService.Model.DecryptResponse> DecryptAsync(Amazon.KeyManagementService.Model.DecryptRequest request, CancellationToken ct = default)
    {
        if (!_roundTrip) throw new AmazonKeyManagementServiceException("simulated failure");
        LastKeyId = request.KeyId;
        LastDecryptContext = new Dictionary<string, string>(request.EncryptionContext ?? new Dictionary<string, string>());
        var token = Encoding.UTF8.GetString(request.CiphertextBlob.ToArray());
        if (!_vault.TryGetValue(token, out var entry))
            throw new AmazonKeyManagementServiceException("ciphertext not found");
        // Mirror AWS behaviour: encryption context must match exactly.
        if (!ContextEquals(entry.ctx, LastDecryptContext))
            throw new AmazonKeyManagementServiceException("InvalidCiphertextException: encryption context mismatch");
        return Task.FromResult(new Amazon.KeyManagementService.Model.DecryptResponse
        {
            KeyId = request.KeyId,
            Plaintext = new MemoryStream(entry.plain),
        });
    }

    public override Task<DescribeKeyResponse> DescribeKeyAsync(DescribeKeyRequest request, CancellationToken ct = default)
        => Task.FromResult(new DescribeKeyResponse { KeyMetadata = new KeyMetadata { KeyId = request.KeyId } });

    private static bool ContextEquals(Dictionary<string, string> a, Dictionary<string, string> b)
    {
        if (a.Count != b.Count) return false;
        foreach (var (k, v) in a)
        {
            if (!b.TryGetValue(k, out var bv) || bv != v) return false;
        }
        return true;
    }
}

internal sealed class FakeAzureCrypto : IAzureCryptographyClient
{
    private readonly bool _roundTrip;
    private readonly Dictionary<string, byte[]> _vault = new();
    public KeyWrapAlgorithm? LastWrapAlgorithm { get; private set; }
    public KeyWrapAlgorithm? LastUnwrapAlgorithm { get; private set; }

    public FakeAzureCrypto(bool roundTrip) { _roundTrip = roundTrip; }

    public Task<byte[]> WrapKeyAsync(KeyWrapAlgorithm algorithm, byte[] key, CancellationToken ct)
    {
        if (!_roundTrip) throw new Azure.RequestFailedException(403, "Forbidden");
        LastWrapAlgorithm = algorithm;
        var token = Guid.NewGuid().ToString("N");
        _vault[token] = (byte[])key.Clone();
        return Task.FromResult(Encoding.UTF8.GetBytes(token));
    }

    public Task<byte[]> UnwrapKeyAsync(KeyWrapAlgorithm algorithm, byte[] encryptedKey, CancellationToken ct)
    {
        if (!_roundTrip) throw new Azure.RequestFailedException(403, "Forbidden");
        LastUnwrapAlgorithm = algorithm;
        var token = Encoding.UTF8.GetString(encryptedKey);
        if (!_vault.TryGetValue(token, out var plain))
            throw new Azure.RequestFailedException(404, "NotFound");
        return Task.FromResult((byte[])plain.Clone());
    }
}

internal sealed class FakeGcpKms : IGcpKmsClient
{
    private readonly bool _roundTrip;
    private readonly Dictionary<string, (byte[] plain, ByteString aad)> _vault = new();
    public string? LastEncryptName { get; private set; }
    public ByteString LastEncryptAad { get; private set; } = ByteString.Empty;
    public ByteString LastDecryptAad { get; private set; } = ByteString.Empty;

    public FakeGcpKms(bool roundTrip) { _roundTrip = roundTrip; }

    public Task<Google.Cloud.Kms.V1.EncryptResponse> EncryptAsync(Google.Cloud.Kms.V1.EncryptRequest request, CancellationToken ct)
    {
        if (!_roundTrip) throw new Grpc.Core.RpcException(new Grpc.Core.Status(Grpc.Core.StatusCode.PermissionDenied, "denied"));
        LastEncryptName = request.Name;
        LastEncryptAad = request.AdditionalAuthenticatedData;
        var token = Guid.NewGuid().ToString("N");
        _vault[token] = (request.Plaintext.ToByteArray(), request.AdditionalAuthenticatedData);
        return Task.FromResult(new Google.Cloud.Kms.V1.EncryptResponse
        {
            Name = request.Name,
            Ciphertext = ByteString.CopyFromUtf8(token),
        });
    }

    public Task<Google.Cloud.Kms.V1.DecryptResponse> DecryptAsync(Google.Cloud.Kms.V1.DecryptRequest request, CancellationToken ct)
    {
        if (!_roundTrip) throw new Grpc.Core.RpcException(new Grpc.Core.Status(Grpc.Core.StatusCode.PermissionDenied, "denied"));
        LastDecryptAad = request.AdditionalAuthenticatedData;
        var token = request.Ciphertext.ToStringUtf8();
        if (!_vault.TryGetValue(token, out var entry))
            throw new Grpc.Core.RpcException(new Grpc.Core.Status(Grpc.Core.StatusCode.NotFound, "ciphertext not found"));
        if (!entry.aad.Equals(request.AdditionalAuthenticatedData))
            throw new Grpc.Core.RpcException(new Grpc.Core.Status(Grpc.Core.StatusCode.InvalidArgument, "aad mismatch"));
        return Task.FromResult(new Google.Cloud.Kms.V1.DecryptResponse
        {
            Plaintext = ByteString.CopyFrom(entry.plain),
        });
    }
}
