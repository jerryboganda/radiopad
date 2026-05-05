using System.Net;
using System.Text.Json;
using Xunit;

namespace RadioPad.Api.Tests.Integration;

public class RadsLookupTests : IClassFixture<RadioPadAppFactory>
{
    private readonly RadioPadAppFactory _factory;
    public RadsLookupTests(RadioPadAppFactory factory) => _factory = factory;

    [Fact]
    public async Task BiRads_Returns_Documented_Categories()
    {
        using var client = _factory.CreateTenantClient();
        var resp = await client.GetAsync("/api/terminology/rads?system=bi_rads");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var doc = await JsonDocument.ParseAsync(await resp.Content.ReadAsStreamAsync());
        Assert.Equal("bi_rads", doc.RootElement.GetProperty("system").GetString());
        var categories = doc.RootElement.GetProperty("categories");
        Assert.True(categories.GetArrayLength() >= 6);
        // Verify category 4 is present and labelled "Suspicious"
        var cat4 = categories.EnumerateArray()
            .FirstOrDefault(c => c.GetProperty("code").GetString() == "4");
        Assert.Equal(JsonValueKind.Object, cat4.ValueKind);
        var label = cat4.GetProperty("shortLabel").GetString() ?? "";
        Assert.Contains("Suspicious", label, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Unknown_System_Returns_NotFound()
    {
        using var client = _factory.CreateTenantClient();
        var resp = await client.GetAsync("/api/terminology/rads?system=fake_rads_v9");
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }
}
