using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using RadioPad.Api.Services;
using RadioPad.Domain.Entities;
using Xunit;

namespace RadioPad.Api.Tests.Integration;

/// <summary>
/// Iter-32 DESK-007 — DICOMweb client unit tests covering the new
/// QIDO-RS search, STOW-RS store, and health-probe surfaces. HTTP traffic
/// is stubbed; no real PACS / Orthanc instance is contacted.
/// </summary>
public class DicomWebClientUnitTests
{
    private sealed class StubHandler : HttpMessageHandler
    {
        public HttpRequestMessage? LastRequest;
        public string Response = "[]";
        public HttpStatusCode StatusCode = HttpStatusCode.OK;
        public Func<HttpRequestMessage, Task<HttpResponseMessage>>? OnSend;
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken ct)
        {
            LastRequest = request;
            if (OnSend is not null) return await OnSend(request);
            return new HttpResponseMessage(StatusCode)
            {
                Content = new StringContent(Response, Encoding.UTF8, "application/dicom+json"),
            };
        }
    }

    private sealed class StubFactory : IHttpClientFactory
    {
        private readonly StubHandler _handler;
        public StubFactory(StubHandler h) { _handler = h; }
        public HttpClient CreateClient(string name) => new(_handler);
    }

    private static TenantSettings Settings(string baseUrl = "https://pacs.example/dicom-web")
        => new() { TenantId = Guid.NewGuid(), DicomWebBaseUrl = baseUrl };

    [Fact]
    public async Task SearchStudies_Returns_NotConfigured_When_BaseUrl_Empty()
    {
        var c = new DicomWebClient(new StubFactory(new StubHandler()), NullLogger<DicomWebClient>.Instance);
        var (doc, status) = await c.SearchStudiesAsync(Settings(""), "AccessionNumber=A1", CancellationToken.None);
        Assert.Null(doc);
        Assert.Equal(0, status);
    }

    [Fact]
    public async Task SearchStudies_Forwards_Query_And_Returns_Body()
    {
        var handler = new StubHandler { Response = "[{\"00080050\":{\"vr\":\"SH\",\"Value\":[\"ACC1\"]}}]" };
        var c = new DicomWebClient(new StubFactory(handler), NullLogger<DicomWebClient>.Instance);
        var (doc, status) = await c.SearchStudiesAsync(Settings(), "AccessionNumber=ACC1&limit=1", CancellationToken.None);
        Assert.Equal(200, status);
        Assert.NotNull(doc);
        Assert.NotNull(handler.LastRequest);
        Assert.Equal("https://pacs.example/dicom-web/studies?AccessionNumber=ACC1&limit=1",
            handler.LastRequest!.RequestUri!.AbsoluteUri);
        doc!.Dispose();
    }

    [Fact]
    public async Task StoreInstances_Posts_Body_With_DICOM_Content_Type()
    {
        var handler = new StubHandler { StatusCode = HttpStatusCode.OK, Response = "{}" };
        var c = new DicomWebClient(new StubFactory(handler), NullLogger<DicomWebClient>.Instance);
        var bytes = new byte[] { 1, 2, 3, 4 };
        var (status, _) = await c.StoreInstancesAsync(Settings(), bytes, "multipart/related; type=\"application/dicom\"", CancellationToken.None);
        Assert.Equal(200, status);
        Assert.NotNull(handler.LastRequest);
        Assert.Equal(HttpMethod.Post, handler.LastRequest!.Method);
        Assert.Equal("https://pacs.example/dicom-web/studies", handler.LastRequest.RequestUri!.AbsoluteUri);
    }

    [Fact]
    public async Task Health_Returns_True_For_2xx_3xx_4xx_And_False_For_5xx()
    {
        var handler = new StubHandler { StatusCode = HttpStatusCode.OK };
        var c = new DicomWebClient(new StubFactory(handler), NullLogger<DicomWebClient>.Instance);
        Assert.True(await c.HealthAsync(Settings(), CancellationToken.None));

        handler.StatusCode = HttpStatusCode.NotFound;
        Assert.True(await c.HealthAsync(Settings(), CancellationToken.None));

        handler.StatusCode = HttpStatusCode.InternalServerError;
        Assert.False(await c.HealthAsync(Settings(), CancellationToken.None));

        // Network error → false.
        handler.OnSend = (_) => throw new HttpRequestException("dead");
        Assert.False(await c.HealthAsync(Settings(), CancellationToken.None));
    }

    [Fact]
    public async Task Health_Returns_False_When_BaseUrl_Empty()
    {
        var c = new DicomWebClient(new StubFactory(new StubHandler()), NullLogger<DicomWebClient>.Instance);
        Assert.False(await c.HealthAsync(Settings(""), CancellationToken.None));
    }

    [Fact]
    public async Task SearchStudies_Sends_Bearer_When_Configured()
    {
        var handler = new StubHandler();
        var c = new DicomWebClient(new StubFactory(handler), NullLogger<DicomWebClient>.Instance);
        var s = Settings();
        s.DicomWebBearerSecret = "tok";
        await c.SearchStudiesAsync(s, "AccessionNumber=A1", CancellationToken.None);
        Assert.Equal("Bearer", handler.LastRequest!.Headers.Authorization?.Scheme);
        Assert.Equal("tok", handler.LastRequest.Headers.Authorization?.Parameter);
    }
}
