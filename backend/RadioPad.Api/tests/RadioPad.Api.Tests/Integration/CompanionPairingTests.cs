using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace RadioPad.Api.Tests.Integration;

/// <summary>
/// Companion QR-login. The desktop's session-create mints a short-lived bearer
/// that the phone adopts off the pairing QR, so the phone authenticates AND pairs
/// from a single scan — fixing the original "Pairing failed" bug where the phone
/// had no credential and every pair POST 401'd. Ending / unpairing the session
/// revokes that bearer immediately.
/// </summary>
public class CompanionPairingTests : IClassFixture<RadioPadAppFactory>
{
    private readonly RadioPadAppFactory _factory;
    public CompanionPairingTests(RadioPadAppFactory factory) => _factory = factory;

    private static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage res) =>
        await JsonDocument.ParseAsync(await res.Content.ReadAsStreamAsync());

    private async Task<(string sessionId, string code, string token)> AdvertiseAsync(HttpClient desktop)
    {
        var create = await desktop.PostAsJsonAsync("/api/companion/sessions", new { deviceName = "Desktop" });
        Assert.Equal(HttpStatusCode.OK, create.StatusCode);
        using var body = await ReadJsonAsync(create);
        var root = body.RootElement;
        return (
            root.GetProperty("sessionId").GetString()!,
            root.GetProperty("pairingCode").GetString()!,
            root.GetProperty("companionToken").GetString()!);
    }

    [Fact]
    public async Task Create_returns_a_companion_bearer_and_identity()
    {
        var desktop = _factory.CreateTenantClient();
        var create = await desktop.PostAsJsonAsync("/api/companion/sessions", new { deviceName = "Desktop" });
        Assert.Equal(HttpStatusCode.OK, create.StatusCode);

        using var body = await ReadJsonAsync(create);
        var root = body.RootElement;
        var token = root.GetProperty("companionToken").GetString();
        Assert.False(string.IsNullOrWhiteSpace(token));
        Assert.StartsWith("rp_", token);
        Assert.Equal(_factory.SeedTenant.Slug, root.GetProperty("tenantSlug").GetString());
        Assert.Equal(_factory.SeedUser.Email, root.GetProperty("userEmail").GetString());
    }

    [Fact]
    public async Task Qr_token_alone_authenticates_the_phone_pair_call()
    {
        var desktop = _factory.CreateTenantClient();
        var (_, code, token) = await AdvertiseAsync(desktop);

        // The phone presents ONLY the QR token — no dev tenant/user headers, exactly
        // like the sideloaded mobile app. It must pair successfully (the crux of the fix).
        var phone = _factory.CreateClient();
        phone.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var pair = await phone.PostAsJsonAsync("/api/companion/pair", new { pairingCode = code, deviceName = "Android phone" });
        Assert.Equal(HttpStatusCode.OK, pair.StatusCode);
        using var pairBody = await ReadJsonAsync(pair);
        Assert.False(string.IsNullOrWhiteSpace(pairBody.RootElement.GetProperty("sessionId").GetString()));
    }

    [Fact]
    public async Task Ending_the_session_revokes_the_qr_token()
    {
        var desktop = _factory.CreateTenantClient();
        var (sessionId, code, token) = await AdvertiseAsync(desktop);

        var phone = _factory.CreateClient();
        phone.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var pair = await phone.PostAsJsonAsync("/api/companion/pair", new { pairingCode = code, deviceName = "phone" });
        Assert.Equal(HttpStatusCode.OK, pair.StatusCode);

        // Desktop unpairs → the companion bearer is revoked server-side.
        var end = await desktop.DeleteAsync($"/api/companion/sessions/{sessionId}");
        Assert.Equal(HttpStatusCode.NoContent, end.StatusCode);

        // The phone's token no longer authenticates anything.
        var after = await phone.GetAsync($"/api/companion/sessions/{sessionId}");
        Assert.Equal(HttpStatusCode.Unauthorized, after.StatusCode);
    }
}
