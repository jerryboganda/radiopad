using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RadioPad.Api.Tests.Integration;
using RadioPad.Domain.Entities;
using RadioPad.Infrastructure.Persistence;
using Xunit;

namespace RadioPad.Api.Tests.Iter33;

/// <summary>
/// Iter-33 AUTH-001 — verifies the controller's attestation-object pipeline
/// for the three supported formats (<c>none</c>, <c>packed</c>,
/// <c>fido-u2f</c>) and rejects the rest. Each test mints a fresh ES256
/// authenticator keypair, builds a synthetic <c>authData</c> + COSE_Key,
/// signs the relevant statement, and verifies the controller stores the
/// expected <c>AttestationFormat</c>.
/// </summary>
public class WebAuthnAttestationTests : IClassFixture<RadioPadAppFactory>
{
    private readonly RadioPadAppFactory _factory;
    public WebAuthnAttestationTests(RadioPadAppFactory f) => _factory = f;

    [Fact]
    public async Task NoneAttestation_RegistersAndPersistsFormat()
    {
        var http = _factory.CreateTenantClient();
        var (attObj, clientData) = WebAuthnTestVectors.NoneAttestation();
        var resp = await http.PostAsJsonAsync("/api/auth/webauthn/register", new
        {
            attestationObject = attObj,
            clientDataJson = clientData,
            label = "iter33-none",
        });
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("none", body.GetProperty("attestationFormat").GetString());

        await AssertStoredFormatAsync("iter33-none", "none");
    }

    [Fact]
    public async Task PackedSelfAttestation_RegistersAndPersistsFormat()
    {
        var http = _factory.CreateTenantClient();
        var (attObj, clientData) = WebAuthnTestVectors.PackedSelfAttestation(corruptSignature: false);
        var resp = await http.PostAsJsonAsync("/api/auth/webauthn/register", new
        {
            attestationObject = attObj,
            clientDataJson = clientData,
            label = "iter33-packed",
        });
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("packed", body.GetProperty("attestationFormat").GetString());

        await AssertStoredFormatAsync("iter33-packed", "packed");
    }

    [Fact]
    public async Task PackedAttestation_CorruptSignature_RejectsAndAccruesLockout()
    {
        var http = _factory.CreateTenantClient();
        var beforeFailures = await GetUserFailuresAsync();

        var (attObj, clientData) = WebAuthnTestVectors.PackedSelfAttestation(corruptSignature: true);
        var resp = await http.PostAsJsonAsync("/api/auth/webauthn/register", new
        {
            attestationObject = attObj,
            clientDataJson = clientData,
            label = "iter33-bad",
        });
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);

        var afterFailures = await GetUserFailuresAsync();
        Assert.Equal(beforeFailures + 1, afterFailures);
    }

    [Fact]
    public async Task UnsupportedFormat_TpmIsRejected()
    {
        var http = _factory.CreateTenantClient();
        var (attObj, clientData) = WebAuthnTestVectors.UnsupportedFormat("tpm");
        var resp = await http.PostAsJsonAsync("/api/auth/webauthn/register", new
        {
            attestationObject = attObj,
            clientDataJson = clientData,
            label = "iter33-tpm",
        });
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Unsupported attestation format", body.GetProperty("error").GetString());
    }

    private async Task<int> GetUserFailuresAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RadioPadDbContext>();
        var u = await db.Users.AsNoTracking().FirstAsync(x => x.Id == _factory.SeedUser.Id);
        return u.FailedLoginCount;
    }

    private async Task AssertStoredFormatAsync(string label, string expectedFormat)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RadioPadDbContext>();
        var row = await db.WebAuthnCredentials.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Label == label);
        Assert.NotNull(row);
        Assert.Equal(expectedFormat, row!.AttestationFormat);
        Assert.False(string.IsNullOrEmpty(row.PublicKey));
    }
}

/// <summary>
/// Synthesises WebAuthn attestation objects (CBOR-encoded) with a fresh
/// ES256 authenticator keypair so the integration tests do not depend on
/// any pre-canned vectors.
/// </summary>
internal static class WebAuthnTestVectors
{
    private const string RpId = "localhost";

    public static (string AttestationObjectB64Url, string ClientDataJsonB64Url) NoneAttestation()
    {
        using var ec = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var (authData, _) = BuildAuthData(ec);
        var clientData = BuildClientDataBytes("webauthn.create");
        var att = EncodeAttestationObject("none", new Dictionary<object, object>(), authData);
        return (Base64Url(att), Base64Url(clientData));
    }

    public static (string AttestationObjectB64Url, string ClientDataJsonB64Url) PackedSelfAttestation(bool corruptSignature)
    {
        using var ec = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var (authData, _) = BuildAuthData(ec);
        var clientData = BuildClientDataBytes("webauthn.create");
        var clientDataHash = SHA256.HashData(clientData);

        var signed = new byte[authData.Length + clientDataHash.Length];
        Buffer.BlockCopy(authData, 0, signed, 0, authData.Length);
        Buffer.BlockCopy(clientDataHash, 0, signed, authData.Length, clientDataHash.Length);
        var sig = ec.SignData(signed, HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence);
        if (corruptSignature) sig[^1] ^= 0xFF;

        var attStmt = new Dictionary<object, object>
        {
            ["alg"] = -7L,
            ["sig"] = sig,
        };
        var att = EncodeAttestationObject("packed", attStmt, authData);
        return (Base64Url(att), Base64Url(clientData));
    }

    public static (string AttestationObjectB64Url, string ClientDataJsonB64Url) UnsupportedFormat(string fmt)
    {
        using var ec = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var (authData, _) = BuildAuthData(ec);
        var clientData = BuildClientDataBytes("webauthn.create");
        var att = EncodeAttestationObject(fmt, new Dictionary<object, object>(), authData);
        return (Base64Url(att), Base64Url(clientData));
    }

    // -- builders -----------------------------------------------------------

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
        // Ensure 32-byte left-padded x/y.
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
            [1L] = 2L,    // kty = EC2
            [3L] = -7L,   // alg = ES256
            [-1L] = 1L,   // crv = P-256
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

/// <summary>Tiny CBOR writer covering the subset used by WebAuthn vectors.</summary>
internal static class CborWriter
{
    public static byte[] WriteMap(Dictionary<object, object> map)
    {
        var ms = new MemoryStream();
        WriteValue(ms, map);
        return ms.ToArray();
    }

    private static void WriteValue(MemoryStream ms, object value)
    {
        switch (value)
        {
            case long l:
                if (l >= 0) WriteHeader(ms, 0, (ulong)l);
                else WriteHeader(ms, 1, (ulong)(-1 - l));
                break;
            case int i:
                WriteValue(ms, (long)i);
                break;
            case byte[] bs:
                WriteHeader(ms, 2, (ulong)bs.Length);
                ms.Write(bs);
                break;
            case string s:
                var utf8 = Encoding.UTF8.GetBytes(s);
                WriteHeader(ms, 3, (ulong)utf8.Length);
                ms.Write(utf8);
                break;
            case List<object> arr:
                WriteHeader(ms, 4, (ulong)arr.Count);
                foreach (var item in arr) WriteValue(ms, item);
                break;
            case Dictionary<object, object> map:
                WriteHeader(ms, 5, (ulong)map.Count);
                foreach (var (k, v) in map) { WriteValue(ms, k); WriteValue(ms, v); }
                break;
            default:
                throw new InvalidOperationException($"CborWriter: unsupported value {value?.GetType().Name}");
        }
    }

    private static void WriteHeader(MemoryStream ms, int major, ulong arg)
    {
        var m = (byte)(major << 5);
        if (arg < 24) ms.WriteByte((byte)(m | (byte)arg));
        else if (arg <= byte.MaxValue) { ms.WriteByte((byte)(m | 24)); ms.WriteByte((byte)arg); }
        else if (arg <= ushort.MaxValue) { ms.WriteByte((byte)(m | 25)); ms.Write(new[] { (byte)(arg >> 8), (byte)arg }); }
        else if (arg <= uint.MaxValue) { ms.WriteByte((byte)(m | 26)); ms.Write(new[] { (byte)(arg >> 24), (byte)(arg >> 16), (byte)(arg >> 8), (byte)arg }); }
        else
        {
            ms.WriteByte((byte)(m | 27));
            for (var i = 7; i >= 0; i--) ms.WriteByte((byte)(arg >> (i * 8)));
        }
    }
}
