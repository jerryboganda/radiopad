using System.Net;
using System.Net.Http.Json;
using Xunit;

namespace RadioPad.Api.Tests.Integration;

public class HealthAndCorrelationTests : IClassFixture<RadioPadAppFactory>
{
    private readonly RadioPadAppFactory _factory;
    public HealthAndCorrelationTests(RadioPadAppFactory factory) => _factory = factory;

    [Fact]
    public async Task Health_Returns_Ok()
    {
        using var client = _factory.CreateClient();
        var r = await client.GetAsync("/api/health");
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var body = await r.Content.ReadFromJsonAsync<HealthResp>();
        Assert.Equal("ok", body!.Status);
    }

    [Fact]
    public async Task Response_Includes_Correlation_Header()
    {
        using var client = _factory.CreateClient();
        var r = await client.GetAsync("/api/health");
        Assert.True(r.Headers.Contains("X-RadioPad-RequestId") ||
                    r.Headers.TryGetValues("X-RadioPad-RequestId", out _));
    }

    [Fact]
    public async Task Echoes_Provided_Correlation_Header()
    {
        using var client = _factory.CreateClient();
        var req = new HttpRequestMessage(HttpMethod.Get, "/api/health");
        req.Headers.Add("X-RadioPad-RequestId", "req-int-123");
        var r = await client.SendAsync(req);
        Assert.Equal("req-int-123", string.Join(",", r.Headers.GetValues("X-RadioPad-RequestId")));
    }

    private record HealthResp(string Status, string Service);
}
