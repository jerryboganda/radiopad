using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using RadioPad.Api.Tests.Iter33;
using Xunit;

namespace RadioPad.Api.Tests.Integration;

/// <summary>
/// AUTH-001 — end-to-end WebAuthn sign-in assertion verification. Proves the
/// hardened controller actually checks the assertion signature, the single-use
/// challenge, and the ceremony binding — not just the credential lookup it did
/// before. Registers a credential with a known authenticator keypair, then
/// signs a real assertion and posts it.
/// </summary>
public class WebAuthnAssertionSignInTests : IClassFixture<RadioPadAppFactory>
{
    private readonly RadioPadAppFactory _factory;
    public WebAuthnAssertionSignInTests(RadioPadAppFactory f) => _factory = f;

    private async Task<(byte[] credId, byte[] pkcs8)> RegisterCredentialAsync(HttpClient http, string label)
    {
        var challenge = await WebAuthnTestVectors.FetchRegisterChallengeAsync(http, label);
        var (attObj, clientData, credId, pkcs8) = WebAuthnTestVectors.NoneAttestationWithKeyMaterial(challenge);
        var reg = await http.PostAsJsonAsync("/api/auth/webauthn/register", new
        {
            attestationObject = attObj,
            clientDataJson = clientData,
            label,
        });
        Assert.Equal(HttpStatusCode.OK, reg.StatusCode);
        return (credId, pkcs8);
    }

    private static async Task<string> SignInChallengeAsync(HttpClient http)
    {
        var resp = await http.PostAsJsonAsync("/api/auth/webauthn/signin-options", new { });
        resp.EnsureSuccessStatusCode();
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("challenge").GetString()!;
    }

    private static byte[] SignAssertion(byte[] pkcs8, byte[] authData, byte[] clientData, bool tamper)
    {
        var clientHash = SHA256.HashData(clientData);
        var signed = new byte[authData.Length + clientHash.Length];
        Buffer.BlockCopy(authData, 0, signed, 0, authData.Length);
        Buffer.BlockCopy(clientHash, 0, signed, authData.Length, clientHash.Length);
        using var ec = ECDsa.Create();
        ec.ImportPkcs8PrivateKey(pkcs8, out _);
        var sig = ec.SignData(signed, HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence);
        if (tamper) sig[^1] ^= 0xFF;
        return sig;
    }

    [Fact]
    public async Task ValidAssertion_MintsToken()
    {
        var http = _factory.CreateTenantClient();
        var (credId, pkcs8) = await RegisterCredentialAsync(http, "assert-valid");

        var challenge = await SignInChallengeAsync(http);
        var authData = WebAuthnTestVectors.BuildAssertionAuthData(signCount: 7, userVerified: true);
        var clientData = WebAuthnTestVectors.BuildGetClientData(challenge);
        var sig = SignAssertion(pkcs8, authData, clientData, tamper: false);

        var resp = await http.PostAsJsonAsync("/api/auth/webauthn/signin", new
        {
            credentialId = WebAuthnTestVectors.Base64UrlPublic(credId),
            clientDataJson = WebAuthnTestVectors.Base64UrlPublic(clientData),
            authenticatorData = WebAuthnTestVectors.Base64UrlPublic(authData),
            signature = WebAuthnTestVectors.Base64UrlPublic(sig),
            signCount = 7u,
        });

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(string.IsNullOrEmpty(body.GetProperty("token").GetString()));
    }

    [Fact]
    public async Task TamperedSignature_Rejected()
    {
        var http = _factory.CreateTenantClient();
        var (credId, pkcs8) = await RegisterCredentialAsync(http, "assert-tampered");

        var challenge = await SignInChallengeAsync(http);
        var authData = WebAuthnTestVectors.BuildAssertionAuthData(signCount: 3, userVerified: true);
        var clientData = WebAuthnTestVectors.BuildGetClientData(challenge);
        var sig = SignAssertion(pkcs8, authData, clientData, tamper: true);

        var resp = await http.PostAsJsonAsync("/api/auth/webauthn/signin", new
        {
            credentialId = WebAuthnTestVectors.Base64UrlPublic(credId),
            clientDataJson = WebAuthnTestVectors.Base64UrlPublic(clientData),
            authenticatorData = WebAuthnTestVectors.Base64UrlPublic(authData),
            signature = WebAuthnTestVectors.Base64UrlPublic(sig),
            signCount = 3u,
        });

        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    [Fact]
    public async Task PreAuthSignIn_WithTenantAndEmail_MintsToken()
    {
        // Enroll while authenticated (dev-header tenant client)...
        var enroll = _factory.CreateTenantClient();
        var (credId, pkcs8) = await RegisterCredentialAsync(enroll, "assert-preauth");

        // ...then sign in from a client with NO session context, identifying the
        // user only by tenant slug + email in the request body (the login screen).
        var anon = _factory.CreateClient();
        var slug = _factory.SeedTenant.Slug;
        var email = _factory.SeedUser.Email;

        var optResp = await anon.PostAsJsonAsync("/api/auth/webauthn/signin-options", new { tenant = slug, user = email });
        optResp.EnsureSuccessStatusCode();
        var opt = await optResp.Content.ReadFromJsonAsync<JsonElement>();
        var challenge = opt.GetProperty("challenge").GetString()!;

        var authData = WebAuthnTestVectors.BuildAssertionAuthData(signCount: 9, userVerified: true);
        var clientData = WebAuthnTestVectors.BuildGetClientData(challenge);
        var sig = SignAssertion(pkcs8, authData, clientData, tamper: false);

        var resp = await anon.PostAsJsonAsync("/api/auth/webauthn/signin", new
        {
            credentialId = WebAuthnTestVectors.Base64UrlPublic(credId),
            clientDataJson = WebAuthnTestVectors.Base64UrlPublic(clientData),
            authenticatorData = WebAuthnTestVectors.Base64UrlPublic(authData),
            signature = WebAuthnTestVectors.Base64UrlPublic(sig),
            signCount = 9u,
            tenant = slug,
            user = email,
        });

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(string.IsNullOrEmpty(body.GetProperty("token").GetString()));
    }

    [Fact]
    public async Task MissingChallenge_Rejected()
    {
        var http = _factory.CreateTenantClient();
        var (credId, pkcs8) = await RegisterCredentialAsync(http, "assert-no-challenge");

        // Deliberately skip signin-options so no challenge is stored. A valid
        // signature must still be refused because the ceremony was never started.
        var authData = WebAuthnTestVectors.BuildAssertionAuthData(signCount: 2, userVerified: true);
        var clientData = WebAuthnTestVectors.BuildGetClientData("not-a-real-challenge");
        var sig = SignAssertion(pkcs8, authData, clientData, tamper: false);

        var resp = await http.PostAsJsonAsync("/api/auth/webauthn/signin", new
        {
            credentialId = WebAuthnTestVectors.Base64UrlPublic(credId),
            clientDataJson = WebAuthnTestVectors.Base64UrlPublic(clientData),
            authenticatorData = WebAuthnTestVectors.Base64UrlPublic(authData),
            signature = WebAuthnTestVectors.Base64UrlPublic(sig),
            signCount = 2u,
        });

        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }
}
