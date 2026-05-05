using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RadioPad.Api.Tests.Integration;
using RadioPad.Api.Tests.Iter33;
using RadioPad.Application.Services.WebAuthn;
using RadioPad.Domain.Enums;
using RadioPad.Infrastructure.Persistence;
using Xunit;

namespace RadioPad.Api.Tests.Iter35;

/// <summary>
/// Iter-35 AUTH-001 — packed-attestation x5c chains must terminate in a
/// FIDO MDS3 trusted root. We mint a synthetic CA + leaf certificate,
/// sign a packed attStmt with the leaf, and:
/// <list type="bullet">
/// <item>register the synthetic root with a test-only
/// <see cref="IFidoMdsMetadataSource"/> override → expect <c>200 OK</c>.</item>
/// <item>swap the override with an empty source (or a different unrelated
/// root) → expect <c>400 attestation_root</c> + a <c>PolicyViolation</c>
/// audit row tagged <c>kind:"webauthn_attestation_root"</c>.</item>
/// </list>
/// </summary>
public class Iter35WebAuthnRootPinTests
{
    [Fact]
    public async Task PackedX5c_ChainRootedInTrustedRoot_Succeeds()
    {
        using var ca = CreateSelfSignedCa("CN=RadioPad Test FIDO MDS3 Root");
        using var leaf = CreateLeaf(ca, "CN=RadioPad Test Authenticator");
        await using var f = new RootPinAppFactory(new[] { ca });
        await f.InitializeAsync();
        var http = f.CreateTenantClient();

        var (attObj, clientData) = Iter35WebAuthnVectorExtensions.PackedX5cAttestationFor(leaf, ca);
        var resp = await http.PostAsJsonAsync("/api/auth/webauthn/register", new
        {
            attestationObject = attObj,
            clientDataJson = clientData,
            label = "iter35-trusted",
        });
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("packed", body.GetProperty("attestationFormat").GetString());
    }

    [Fact]
    public async Task PackedX5c_ChainRootedInUnknownCa_Returns400AndAudits()
    {
        using var trusted = CreateSelfSignedCa("CN=RadioPad Test FIDO MDS3 Root (trusted)");
        using var attacker = CreateSelfSignedCa("CN=Untrusted Attacker Root");
        using var leaf = CreateLeaf(attacker, "CN=Untrusted Authenticator");
        await using var f = new RootPinAppFactory(new[] { trusted });
        await f.InitializeAsync();
        var http = f.CreateTenantClient();

        var (attObj, clientData) = Iter35WebAuthnVectorExtensions.PackedX5cAttestationFor(leaf, attacker);
        var resp = await http.PostAsJsonAsync("/api/auth/webauthn/register", new
        {
            attestationObject = attObj,
            clientDataJson = clientData,
            label = "iter35-untrusted",
        });
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("attestation_root", body.GetProperty("kind").GetString());

        // The PolicyViolation audit row must have been appended.
        using var scope = f.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RadioPadDbContext>();
        var audited = await db.AuditEvents.AsNoTracking()
            .AnyAsync(a => a.Action == AuditAction.PolicyViolation
                        && a.DetailsJson.Contains("webauthn_attestation_root"));
        Assert.True(audited, "Expected a PolicyViolation audit row tagged webauthn_attestation_root.");
    }

    // -- helpers ------------------------------------------------------------

    private static X509Certificate2 CreateSelfSignedCa(string subject)
    {
        using var ec = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var req = new CertificateRequest(subject, ec, HashAlgorithmName.SHA256);
        req.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
        req.CertificateExtensions.Add(new X509KeyUsageExtension(
            X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.CrlSign, true));
        var notBefore = DateTimeOffset.UtcNow.AddMinutes(-5);
        var notAfter = notBefore.AddYears(5);
        return req.CreateSelfSigned(notBefore, notAfter);
    }

    private static X509Certificate2 CreateLeaf(X509Certificate2 issuer, string subject)
    {
        using var ec = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var req = new CertificateRequest(subject, ec, HashAlgorithmName.SHA256);
        req.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
        var notBefore = DateTimeOffset.UtcNow.AddMinutes(-5);
        var notAfter = notBefore.AddYears(1);
        var serial = RandomNumberGenerator.GetBytes(16);
        using var unsigned = req.Create(issuer, notBefore, notAfter, serial);
        // Re-attach the leaf private key so the test can sign the attStmt.
        return unsigned.CopyWithPrivateKey(ec);
    }
}

internal sealed class RootPinAppFactory : RadioPadAppFactory
{
    private readonly IReadOnlyList<X509Certificate2> _roots;
    public RootPinAppFactory(IReadOnlyList<X509Certificate2> roots) { _roots = roots; }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureTestServices(services =>
        {
            var existing = services.Where(d => d.ServiceType == typeof(IFidoMdsMetadataSource)).ToList();
            foreach (var d in existing) services.Remove(d);
            services.AddSingleton<IFidoMdsMetadataSource>(new TestFidoMdsMetadataSource(_roots));
        });
    }

    private sealed class TestFidoMdsMetadataSource : IFidoMdsMetadataSource
    {
        private readonly IReadOnlyList<X509Certificate2> _roots;
        public TestFidoMdsMetadataSource(IReadOnlyList<X509Certificate2> roots) { _roots = roots; }
        public IReadOnlyList<X509Certificate2> GetTrustedRoots() => _roots;
    }
}

internal static class Iter35WebAuthnVectorExtensions
{
    private const string RpId = "localhost";

    public static (string AttestationObjectB64Url, string ClientDataJsonB64Url) PackedX5cAttestationFor(
        X509Certificate2 leafWithPrivateKey,
        X509Certificate2 issuer)
    {
        // Authenticator's *own* keypair (used for the credential) — separate
        // from the leaf cert's keypair (used to sign the attStmt).
        using var authnEc = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var (authData, _) = BuildAuthData(authnEc);
        var clientData = BuildClientDataBytes("webauthn.create");
        var clientDataHash = SHA256.HashData(clientData);

        var signed = new byte[authData.Length + clientDataHash.Length];
        Buffer.BlockCopy(authData, 0, signed, 0, authData.Length);
        Buffer.BlockCopy(clientDataHash, 0, signed, authData.Length, clientDataHash.Length);

        using var leafEc = leafWithPrivateKey.GetECDsaPrivateKey()
            ?? throw new InvalidOperationException("leaf cert must carry an ECDSA private key.");
        var sig = leafEc.SignData(signed, HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence);

        var attStmt = new Dictionary<object, object>
        {
            ["alg"] = -7L,
            ["sig"] = sig,
            ["x5c"] = new List<object>
            {
                leafWithPrivateKey.RawData,
                issuer.RawData,
            },
        };
        var att = EncodeAttestationObject("packed", attStmt, authData);
        return (Base64Url(att), Base64Url(clientData));
    }

    private static (byte[] AuthData, byte[] CredentialId) BuildAuthData(ECDsa ec)
    {
        var rpIdHash = SHA256.HashData(Encoding.UTF8.GetBytes(RpId));
        const byte flags = 0x41; // UP + AT
        uint signCount = 0;
        var aaguid = new byte[16];
        var credId = RandomNumberGenerator.GetBytes(32);
        var cose = EncodeCoseEs256(ec);

        var ms = new MemoryStream();
        ms.Write(rpIdHash);
        ms.WriteByte(flags);
        ms.Write(new byte[] { (byte)(signCount >> 24), (byte)(signCount >> 16), (byte)(signCount >> 8), (byte)signCount });
        ms.Write(aaguid);
        ms.Write(new byte[] { (byte)(credId.Length >> 8), (byte)credId.Length });
        ms.Write(credId);
        ms.Write(cose);
        return (ms.ToArray(), credId);
    }

    private static byte[] BuildClientDataBytes(string type)
    {
        var json = JsonSerializer.Serialize(new
        {
            type,
            challenge = Base64Url(RandomNumberGenerator.GetBytes(32)),
            origin = "https://" + RpId,
        });
        return Encoding.UTF8.GetBytes(json);
    }

    private static byte[] EncodeCoseEs256(ECDsa ec)
    {
        var p = ec.ExportParameters(false);
        static byte[] Pad32(byte[] v)
        {
            if (v.Length == 32) return v;
            var o = new byte[32];
            Buffer.BlockCopy(v, 0, o, 32 - v.Length, v.Length);
            return o;
        }
        var x = Pad32(p.Q.X!);
        var y = Pad32(p.Q.Y!);
        var map = new Dictionary<object, object>
        {
            [1L] = 2L,
            [3L] = -7L,
            [-1L] = 1L,
            [-2L] = x,
            [-3L] = y,
        };
        return CborWriter.WriteMap(map);
    }

    private static byte[] EncodeAttestationObject(string fmt, Dictionary<object, object> attStmt, byte[] authData)
    {
        var root = new Dictionary<object, object>
        {
            ["fmt"] = fmt,
            ["attStmt"] = attStmt,
            ["authData"] = authData,
        };
        return CborWriter.WriteMap(root);
    }

    private static string Base64Url(byte[] b) =>
        Convert.ToBase64String(b).Replace("+", "-").Replace("/", "_").TrimEnd('=');
}
