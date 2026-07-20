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
// These assert on the engine being DISABLED, which is read from a process-global environment
// variable — so they cannot run in parallel with the STT smoke tests, which enable it. Sharing the
// non-parallel environment-variable collection serialises them.
[Collection(RadioPad.Api.Tests.Infrastructure.EnvironmentVariableCollection.Name)]
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
        Assert.Contains("radiology-tdt-v1-beta", ids);

        // Future kinds surface as disabled "coming soon" placeholders.
        Assert.Contains(
            models.EnumerateArray(),
            m => m.GetProperty("kind").GetString() == "Tts" && m.GetProperty("placeholder").GetBoolean());
    }

    /// <summary>
    /// The card must know which actions the backend will actually accept. "Primary" is a
    /// speech-to-text concept only — <see cref="RadioPad.Api.Controllers.LocalModelsController"/>
    /// rejects it for anything else — and the orchestrator card used to offer it anyway,
    /// producing a button that always failed with a 400.
    /// </summary>
    [Fact]
    public async Task List_Marks_Primary_As_Stt_Only()
    {
        using var client = _factory.CreateClient();
        using var doc = JsonDocument.Parse(await client.GetStringAsync("/api/local-models"));

        foreach (var m in doc.RootElement.GetProperty("models").EnumerateArray())
        {
            var kind = m.GetProperty("kind").GetString();
            var placeholder = m.GetProperty("placeholder").GetBoolean();
            Assert.True(m.TryGetProperty("supportsPrimary", out var supports),
                $"model '{m.GetProperty("id").GetString()}' must expose supportsPrimary");
            Assert.Equal(kind == "Stt" && !placeholder, supports.GetBoolean());
        }
    }

    /// <summary>
    /// An orchestrator model needs a llama.cpp runtime as well as its GGUF. Reporting only
    /// "downloaded" made a half-installed chain indistinguishable from a working one, so the
    /// runtime is surfaced separately — and only for the models it applies to.
    /// </summary>
    [Fact]
    public async Task List_Exposes_Runtime_State_For_Orchestrator_Models_Only()
    {
        using var client = _factory.CreateClient();
        using var doc = JsonDocument.Parse(await client.GetStringAsync("/api/local-models"));

        var orchestrators = doc.RootElement.GetProperty("models").EnumerateArray()
            .Where(m => m.GetProperty("kind").GetString() == "Orchestrator"
                        && !m.GetProperty("placeholder").GetBoolean())
            .ToList();
        Assert.NotEmpty(orchestrators);

        foreach (var m in orchestrators)
        {
            var runtime = m.GetProperty("runtime");
            Assert.Equal(JsonValueKind.Object, runtime.ValueKind);
            Assert.False(string.IsNullOrWhiteSpace(runtime.GetProperty("id").GetString()));
            // Presence, not value: the test host has no runtime installed.
            Assert.True(runtime.TryGetProperty("installed", out _));
            Assert.True(runtime.TryGetProperty("running", out _));
        }

        // The API serializes with DefaultIgnoreCondition = WhenWritingNull, so a model with
        // no runtime omits the key entirely rather than sending null.
        foreach (var m in doc.RootElement.GetProperty("models").EnumerateArray()
                     .Where(m => m.GetProperty("kind").GetString() == "Stt"))
            Assert.False(m.TryGetProperty("runtime", out _),
                $"'{m.GetProperty("id").GetString()}' is not an orchestrator and must not carry runtime state");
    }

    /// <summary>
    /// Re-download is the only in-app recovery for a corrupt model, so it must stay gated
    /// exactly like a first download rather than becoming a way to delete files on a build
    /// where the model manager is inert.
    /// </summary>
    [Fact]
    public async Task Forced_Download_While_Disabled_Returns_503_Unavailable()
    {
        using var client = _factory.CreateClient();
        var resp = await client.PostAsync("/api/local-models/radiology-tdt-v1-beta/download?force=true", null);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, resp.StatusCode);
        Assert.Contains("stt_unavailable", await resp.Content.ReadAsStringAsync());
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
        var resp = await client.PostAsync("/api/local-models/radiology-tdt-v1-beta/download", null);

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
        var resp = await client.GetAsync("/api/local-models/radiology-tdt-v1-beta/diagnostics");

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        var root = doc.RootElement;
        Assert.False(root.GetProperty("enabled").GetBoolean());
        // No server paths leaked off-desktop.
        Assert.False(root.TryGetProperty("paths", out _));
    }

    [Fact]
    public async Task SetPrimary_While_Disabled_Returns_503()
    {
        using var client = _factory.CreateClient();
        var resp = await client.PostAsync("/api/local-models/radiology-tdt-v1-beta/primary", null);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, resp.StatusCode);
        Assert.Contains("stt_unavailable", await resp.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task List_Items_Expose_IsPrimary()
    {
        using var client = _factory.CreateClient();
        var resp = await client.GetAsync("/api/local-models");
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        var first = doc.RootElement.GetProperty("models").EnumerateArray().First();
        Assert.True(first.TryGetProperty("isPrimary", out _));
    }
}
