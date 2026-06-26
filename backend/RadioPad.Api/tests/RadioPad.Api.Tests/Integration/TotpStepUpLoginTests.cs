using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace RadioPad.Api.Tests.Integration;

/// <summary>
/// AUTH-008 — TOTP step-up at sign-in. A single-factor sign-in (dev session)
/// must withhold the session token when the user has an authenticator app
/// enrolled, returning { mfaRequired: true }; the token is only minted after the
/// 6-digit code is verified at POST /api/auth/mfa/login.
/// </summary>
public class TotpStepUpLoginTests : IClassFixture<RadioPadAppFactory>
{
    private readonly RadioPadAppFactory _factory;
    public TotpStepUpLoginTests(RadioPadAppFactory f) => _factory = f;

    private static string CurrentCode(string secret)
    {
        var key = RadioPad.Api.Controllers.MfaController_TestAccess.B32Decode(secret);
        var counter = DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 30;
        return RadioPad.Api.Controllers.MfaController_TestAccess.Hotp(key, counter);
    }

    [Fact]
    public async Task DevSignIn_WithMfaEnrolled_RequiresTotpThenMintsToken()
    {
        var http = _factory.CreateTenantClient();
        var slug = _factory.SeedTenant.Slug;
        var email = _factory.SeedUser.Email;

        // Enroll + enable an authenticator app for the user.
        var enroll = await http.PostAsJsonAsync("/api/auth/mfa/enroll", new { tenant = slug, email });
        enroll.EnsureSuccessStatusCode();
        var secret = (await enroll.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("secret").GetString()!;
        var verify = await http.PostAsJsonAsync("/api/auth/mfa/verify", new { tenant = slug, email, code = CurrentCode(secret) });
        Assert.Equal(HttpStatusCode.OK, verify.StatusCode);

        // Single-factor dev sign-in must now be withheld pending the 6-digit code.
        var signin = await http.PostAsJsonAsync("/api/auth/signin", new { tenant = slug, user = email });
        Assert.Equal(HttpStatusCode.OK, signin.StatusCode);
        var signinBody = await signin.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(signinBody.TryGetProperty("mfaRequired", out var mr) && mr.GetBoolean());
        Assert.False(signinBody.TryGetProperty("token", out var tok) && tok.ValueKind == JsonValueKind.String,
            "single-factor sign-in must not return a token when MFA is enrolled");

        // The code turns into a session at the step-up endpoint.
        var login = await http.PostAsJsonAsync("/api/auth/mfa/login", new { tenant = slug, email, code = CurrentCode(secret) });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var loginBody = await login.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(string.IsNullOrEmpty(loginBody.GetProperty("token").GetString()));
    }

    [Fact]
    public async Task MfaLogin_WithWrongCode_IsRejected()
    {
        var http = _factory.CreateTenantClient();
        var slug = _factory.SeedTenant.Slug;
        var email = _factory.SeedUser.Email;

        var enroll = await http.PostAsJsonAsync("/api/auth/mfa/enroll", new { tenant = slug, email });
        enroll.EnsureSuccessStatusCode();
        var secret = (await enroll.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("secret").GetString()!;
        await http.PostAsJsonAsync("/api/auth/mfa/verify", new { tenant = slug, email, code = CurrentCode(secret) });

        var login = await http.PostAsJsonAsync("/api/auth/mfa/login", new { tenant = slug, email, code = "000000" });
        Assert.Equal(HttpStatusCode.Unauthorized, login.StatusCode);
    }
}
