using System.Net;
using System.Text.Json;
using Xunit;

namespace RadioPad.Api.Tests.Integration;

public class RadLexLookupTests : IClassFixture<RadioPadAppFactory>
{
    private readonly RadioPadAppFactory _factory;
    public RadLexLookupTests(RadioPadAppFactory factory) => _factory = factory;

    [Fact]
    public async Task Search_Returns_Hits()
    {
        using var client = _factory.CreateTenantClient();
        var resp = await client.GetAsync("/api/terminology/radlex/search?q=lung&take=5");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var doc = await JsonDocument.ParseAsync(await resp.Content.ReadAsStreamAsync());
        Assert.True(doc.RootElement.GetArrayLength() >= 1);
        var first = doc.RootElement[0];
        Assert.True(first.TryGetProperty("rid", out _));
        Assert.True(first.TryGetProperty("preferredLabel", out _));
    }

    [Fact]
    public async Task CodeSystem_Json_Validates_As_Fhir_Fragment()
    {
        using var client = _factory.CreateTenantClient();
        var resp = await client.GetAsync("/api/terminology/radlex/CodeSystem");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var doc = await JsonDocument.ParseAsync(await resp.Content.ReadAsStreamAsync());
        Assert.Equal("CodeSystem", doc.RootElement.GetProperty("resourceType").GetString());
        Assert.Equal("fragment", doc.RootElement.GetProperty("content").GetString());
        Assert.Equal("http://radlex.org", doc.RootElement.GetProperty("url").GetString());
        Assert.True(doc.RootElement.GetProperty("concept").GetArrayLength() >= 1);
    }
}
