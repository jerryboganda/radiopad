using System.Net;
using System.Text.Json;
using Xunit;

namespace RadioPad.Api.Tests.Integration;

/// <summary>
/// Exercises the on-device model manager (<c>/api/local-models</c>) over the real
/// HTTP pipeline. In the test build the local engine is unconfigured
/// (<c>RADIOPAD_LOCAL_STT_ENABLED</c> unset), so the listing reports
/// <c>enabled:false</c> and the action endpoints are inert — exactly the web/server
/// behaviour. The endpoints are anonymous and not report-scoped, so an
/// unauthenticated client reaches them (no download is ever triggered here).
/// </summary>
public class LocalModelsControllerTests : IClassFixture<RadioPadAppFactory>
{
    private readonly RadioPadAppFactory _factory;
    public LocalModelsControllerTests(RadioPadAppFactory factory) => _factory = factory;

    [Fact]
    public async Task List_Returns_Catalog_With_Enabled_Flag_And_Placeholders()
    {
        using var client = _factory.CreateClient();
        var resp = await client.GetAsync("/api/local-models");

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        var root = doc.RootElement;

        // No local engine in the test build → inert.
        Assert.False(root.GetProperty("enabled").GetBoolean());

        var models = root.GetProperty("models");
        Assert.Equal(JsonValueKind.Array, models.ValueKind);
        Assert.True(models.GetArrayLength() >= 4);

        var ids = models.EnumerateArray().Select(m => m.GetProperty("id").GetString()).ToList();
        Assert.Contains("parakeet-tdt-0.6b-v3", ids);
        Assert.Contains("whisper-large-v3-turbo-q5_0", ids);

        // Future kinds surface as disabled "coming soon" placeholders.
        Assert.Contains(
            models.EnumerateArray(),
            m => m.GetProperty("kind").GetString() == "Tts" && m.GetProperty("placeholder").GetBoolean());
    }

    [Fact]
    public async Task Download_Placeholder_Returns_503_ComingSoon()
    {
        using var client = _factory.CreateClient();
        var resp = await client.PostAsync("/api/local-models/tts-coming-soon/download", null);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, resp.StatusCode);
        Assert.Contains("coming_soon", await resp.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Download_RealModel_While_Disabled_Returns_503_Unavailable()
    {
        using var client = _factory.CreateClient();
        var resp = await client.PostAsync("/api/local-models/parakeet-tdt-0.6b-v3/download", null);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, resp.StatusCode);
        Assert.Contains("stt_unavailable", await resp.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Progress_Unknown_Model_Returns_404()
    {
        using var client = _factory.CreateClient();
        var resp = await client.GetAsync("/api/local-models/not-a-model/progress");

        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    [Fact]
    public async Task Diagnostics_While_Disabled_Returns_Minimal_Without_Server_Paths()
    {
        using var client = _factory.CreateClient();
        var resp = await client.GetAsync("/api/local-models/parakeet-tdt-0.6b-v3/diagnostics");

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        var root = doc.RootElement;
        Assert.False(root.GetProperty("enabled").GetBoolean());
        // No server paths leaked off-desktop.
        Assert.False(root.TryGetProperty("paths", out _));
    }
}
