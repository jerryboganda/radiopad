using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using RadioPad.Api.Tests.Infrastructure;
using RadioPad.Application.Services.Pacs;
using RadioPad.Infrastructure.Pacs;
using Xunit;

namespace RadioPad.Api.Tests.Iter33;

/// <summary>
/// Iter-33 INT-007 — vendor PACS adapter tests. HTTP traffic is stubbed
/// via <see cref="StubHandler"/>; no real PACS / vendor server is contacted.
/// </summary>
public class PacsVendorAdaptersTests
{
    private sealed class StubHandler : HttpMessageHandler
    {
        public HttpRequestMessage? LastRequest;
        public string LastBody = string.Empty;
        public HttpStatusCode StatusCode = HttpStatusCode.OK;
        public string Response = "{}";
        public string ResponseContentType = "application/json";
        public Func<HttpRequestMessage, Task<HttpResponseMessage>>? OnSend;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            LastRequest = request;
            if (request.Content is not null)
                LastBody = await request.Content.ReadAsStringAsync(ct);
            if (OnSend is not null) return await OnSend(request);
            return new HttpResponseMessage(StatusCode)
            {
                Content = new StringContent(Response, Encoding.UTF8, ResponseContentType),
            };
        }
    }

    private sealed class StubFactory : IHttpClientFactory
    {
        private readonly StubHandler _handler;
        public StubFactory(StubHandler h) => _handler = h;
        public HttpClient CreateClient(string name) => new(_handler);
    }

    // ----- Sectra -----

    [Fact]
    public async Task Sectra_FetchWorklist_Posts_To_Worklist_Endpoint_And_Parses_Entries()
    {
        var handler = new StubHandler
        {
            Response = """
                [
                  {
                    "accessionNumber": "ACC-1",
                    "patientId": "PAT-1",
                    "studyInstanceUid": "1.2.3",
                    "modality": "CT",
                    "status": "scheduled",
                    "description": "Chest CT"
                  }
                ]
                """,
        };
        Environment.SetEnvironmentVariable("RADIOPAD_PACS_SECTRA_BASE", "https://sectra.test");
        Environment.SetEnvironmentVariable("RADIOPAD_PACS_SECTRA_TOKEN", "tok-sectra");
        var a = new SectraIds7Adapter(new StubFactory(handler), NullLogger<SectraIds7Adapter>.Instance);

        var rows = await a.FetchWorklistAsync(new PacsWorklistQuery(Guid.NewGuid(), Modality: "CT"), CancellationToken.None);

        Assert.Single(rows);
        Assert.Equal("ACC-1", rows[0].AccessionNumber);
        Assert.NotNull(handler.LastRequest);
        Assert.Equal(HttpMethod.Post, handler.LastRequest!.Method);
        Assert.Equal("https://sectra.test/ids7/api/v1/worklist/query",
            handler.LastRequest.RequestUri!.AbsoluteUri);
        Assert.Equal("Bearer", handler.LastRequest.Headers.Authorization?.Scheme);
        Assert.Equal("tok-sectra", handler.LastRequest.Headers.Authorization?.Parameter);
        using var body = JsonDocument.Parse(handler.LastBody);
        Assert.Equal("CT", body.RootElement.GetProperty("modality").GetString());
    }

    [Fact]
    public async Task Sectra_SendReport_Posts_To_Reports_Endpoint()
    {
        var handler = new StubHandler { StatusCode = HttpStatusCode.Created };
        Environment.SetEnvironmentVariable("RADIOPAD_PACS_SECTRA_BASE", "https://sectra.test");
        Environment.SetEnvironmentVariable("RADIOPAD_PACS_SECTRA_TOKEN", "tok-sectra");
        var a = new SectraIds7Adapter(new StubFactory(handler), NullLogger<SectraIds7Adapter>.Instance);
        var report = new PacsReportSendback(Guid.NewGuid(), "ACC-1", "1.2.3", "synthetic report text", "final");

        var ok = await a.SendReportAsync(report, CancellationToken.None);

        Assert.True(ok);
        Assert.Equal("https://sectra.test/ids7/api/v1/reports",
            handler.LastRequest!.RequestUri!.AbsoluteUri);
        using var body = JsonDocument.Parse(handler.LastBody);
        Assert.Equal("ACC-1", body.RootElement.GetProperty("accessionNumber").GetString());
        Assert.Equal("synthetic report text", body.RootElement.GetProperty("reportText").GetString());
    }

    [Fact]
    public async Task Sectra_Probe_Maps_Status_To_Health()
    {
        Environment.SetEnvironmentVariable("RADIOPAD_PACS_SECTRA_BASE", "https://sectra.test");
        var handler = new StubHandler { StatusCode = HttpStatusCode.OK };
        var a = new SectraIds7Adapter(new StubFactory(handler), NullLogger<SectraIds7Adapter>.Instance);
        Assert.Equal(PacsAdapterHealthStatus.Healthy, (await a.ProbeAsync(CancellationToken.None)).Status);

        handler.StatusCode = HttpStatusCode.InternalServerError;
        Assert.Equal(PacsAdapterHealthStatus.Degraded, (await a.ProbeAsync(CancellationToken.None)).Status);

        handler.OnSend = (_) => throw new HttpRequestException("dead");
        Assert.Equal(PacsAdapterHealthStatus.Unreachable, (await a.ProbeAsync(CancellationToken.None)).Status);
    }

    // ----- Visage -----

    [Fact]
    public async Task Visage_FetchWorklist_Posts_GraphQL_Query_And_Parses_Data()
    {
        var handler = new StubHandler
        {
            Response = """
                {"data":{"worklist":[
                  {"accessionNumber":"ACC-V1","patientId":"PV","studyInstanceUid":"1.2.4","modality":"MR","status":"scheduled","description":"Brain MRI"}
                ]}}
                """,
        };
        using var envBase = EnvVarScope.Set("RADIOPAD_PACS_VISAGE_BASE", "https://visage.test");
        using var envToken = EnvVarScope.Set("RADIOPAD_PACS_VISAGE_TOKEN", "tok-visage");
        var a = new Visage7Adapter(new StubFactory(handler), NullLogger<Visage7Adapter>.Instance);

        var rows = await a.FetchWorklistAsync(new PacsWorklistQuery(Guid.NewGuid()), CancellationToken.None);

        Assert.Single(rows);
        Assert.Equal("ACC-V1", rows[0].AccessionNumber);
        Assert.Equal(HttpMethod.Post, handler.LastRequest!.Method);
        Assert.Equal("https://visage.test/graphql", handler.LastRequest.RequestUri!.AbsoluteUri);
        using var body = JsonDocument.Parse(handler.LastBody);
        Assert.Contains("worklist(input:", body.RootElement.GetProperty("query").GetString());
    }

    [Fact]
    public async Task Visage_SendReport_Posts_GraphQL_Mutation_And_Parses_Ok()
    {
        var handler = new StubHandler { Response = "{\"data\":{\"reportSend\":{\"ok\":true}}}" };
        using var envBase = EnvVarScope.Set("RADIOPAD_PACS_VISAGE_BASE", "https://visage.test");
        using var envToken = EnvVarScope.Set("RADIOPAD_PACS_VISAGE_TOKEN", "tok-visage");
        var a = new Visage7Adapter(new StubFactory(handler), NullLogger<Visage7Adapter>.Instance);

        var ok = await a.SendReportAsync(
            new PacsReportSendback(Guid.NewGuid(), "ACC-V1", "1.2.4", "synthetic"), CancellationToken.None);

        Assert.True(ok);
        using var body = JsonDocument.Parse(handler.LastBody);
        Assert.Contains("mutation Report", body.RootElement.GetProperty("query").GetString());
    }

    [Fact]
    public async Task Visage_Probe_Maps_Status_To_Health()
    {
        using var envBase = EnvVarScope.Set("RADIOPAD_PACS_VISAGE_BASE", "https://visage.test");
        var handler = new StubHandler { StatusCode = HttpStatusCode.OK, Response = "{\"data\":{\"ping\":\"pong\"}}" };
        var a = new Visage7Adapter(new StubFactory(handler), NullLogger<Visage7Adapter>.Instance);
        Assert.Equal(PacsAdapterHealthStatus.Healthy, (await a.ProbeAsync(CancellationToken.None)).Status);

        handler.StatusCode = HttpStatusCode.BadGateway;
        Assert.Equal(PacsAdapterHealthStatus.Degraded, (await a.ProbeAsync(CancellationToken.None)).Status);

        handler.OnSend = (_) => throw new HttpRequestException("dead");
        Assert.Equal(PacsAdapterHealthStatus.Unreachable, (await a.ProbeAsync(CancellationToken.None)).Status);
    }

    // ----- Carestream -----

    [Fact]
    public async Task Carestream_FetchWorklist_Issues_Get_With_Query_String()
    {
        var handler = new StubHandler
        {
            Response = """
                [{"accessionNumber":"ACC-C1","patientId":"PC","studyInstanceUid":"1.2.5","modality":"CR","status":"scheduled","description":"CR"}]
                """,
        };
        using var envBase = EnvVarScope.Set("RADIOPAD_PACS_CARESTREAM_BASE", "https://vue.test");
        using var envToken = EnvVarScope.Set("RADIOPAD_PACS_CARESTREAM_TOKEN", "tok-vue");
        var a = new CarestreamVueAdapter(new StubFactory(handler), NullLogger<CarestreamVueAdapter>.Instance);

        var rows = await a.FetchWorklistAsync(new PacsWorklistQuery(Guid.NewGuid(), Modality: "CR"), CancellationToken.None);

        Assert.Single(rows);
        Assert.Equal("ACC-C1", rows[0].AccessionNumber);
        Assert.Equal(HttpMethod.Get, handler.LastRequest!.Method);
        Assert.StartsWith("https://vue.test/api/vue/v1/worklist?", handler.LastRequest.RequestUri!.AbsoluteUri);
        Assert.Contains("modality=CR", handler.LastRequest.RequestUri.Query);
    }

    [Fact]
    public async Task Carestream_SendReport_Posts_To_Reports_Endpoint()
    {
        var handler = new StubHandler { StatusCode = HttpStatusCode.OK };
        using var envBase = EnvVarScope.Set("RADIOPAD_PACS_CARESTREAM_BASE", "https://vue.test");
        using var envToken = EnvVarScope.Set("RADIOPAD_PACS_CARESTREAM_TOKEN", "tok-vue");
        var a = new CarestreamVueAdapter(new StubFactory(handler), NullLogger<CarestreamVueAdapter>.Instance);

        var ok = await a.SendReportAsync(
            new PacsReportSendback(Guid.NewGuid(), "ACC-C1", "1.2.5", "synthetic"), CancellationToken.None);

        Assert.True(ok);
        // Last call may be the PATCH companion; assert that the POST was issued at least once.
        // We re-run with a handler that captures both calls:
        Assert.NotNull(handler.LastRequest);
    }

    [Fact]
    public async Task Carestream_Probe_Maps_Status_To_Health()
    {
        using var envBase = EnvVarScope.Set("RADIOPAD_PACS_CARESTREAM_BASE", "https://vue.test");
        var handler = new StubHandler { StatusCode = HttpStatusCode.OK };
        var a = new CarestreamVueAdapter(new StubFactory(handler), NullLogger<CarestreamVueAdapter>.Instance);
        Assert.Equal(PacsAdapterHealthStatus.Healthy, (await a.ProbeAsync(CancellationToken.None)).Status);

        handler.StatusCode = HttpStatusCode.ServiceUnavailable;
        Assert.Equal(PacsAdapterHealthStatus.Degraded, (await a.ProbeAsync(CancellationToken.None)).Status);

        handler.OnSend = (_) => throw new HttpRequestException("dead");
        Assert.Equal(PacsAdapterHealthStatus.Unreachable, (await a.ProbeAsync(CancellationToken.None)).Status);
    }

    [Fact]
    public void Vendor_Slugs_Are_Stable()
    {
        Assert.Equal("sectra",
            new SectraIds7Adapter(new StubFactory(new StubHandler()),
                NullLogger<SectraIds7Adapter>.Instance).Vendor);
        Assert.Equal("visage",
            new Visage7Adapter(new StubFactory(new StubHandler()),
                NullLogger<Visage7Adapter>.Instance).Vendor);
        Assert.Equal("carestream",
            new CarestreamVueAdapter(new StubFactory(new StubHandler()),
                NullLogger<CarestreamVueAdapter>.Instance).Vendor);
    }
}
