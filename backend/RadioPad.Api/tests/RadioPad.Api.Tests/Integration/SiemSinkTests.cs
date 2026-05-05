using System.Net;
using System.Text;
using System.Text.Json;
using RadioPad.Application.Services.Siem;
using Xunit;

namespace RadioPad.Api.Tests.Integration;

/// <summary>
/// Iter-32 INT-010 — unit tests for the four SIEM sinks. All HTTP traffic is
/// captured by a stubbed <see cref="HttpMessageHandler"/>; UDP traffic is
/// captured by an in-memory <see cref="IUdpSender"/>. Real endpoints are
/// never contacted.
/// </summary>
public class SiemSinkTests : IDisposable
{
    public SiemSinkTests()
    {
        // Ensure a clean env before each test.
        var keys = new[]
        {
            SplunkHecSink.Url, SplunkHecSink.Token,
            SentinelLogAnalyticsSink.Workspace, SentinelLogAnalyticsSink.SharedKey, SentinelLogAnalyticsSink.LogType,
            ElasticBulkSink.Url, ElasticBulkSink.Index, ElasticBulkSink.Bearer, ElasticBulkSink.Basic,
            SyslogUdpSink.Host, SyslogUdpSink.Port,
        };
        foreach (var k in keys) Environment.SetEnvironmentVariable(k, null);
    }

    public void Dispose()
    {
        // restore.
        var keys = new[]
        {
            SplunkHecSink.Url, SplunkHecSink.Token,
            SentinelLogAnalyticsSink.Workspace, SentinelLogAnalyticsSink.SharedKey, SentinelLogAnalyticsSink.LogType,
            ElasticBulkSink.Url, ElasticBulkSink.Index, ElasticBulkSink.Bearer, ElasticBulkSink.Basic,
            SyslogUdpSink.Host, SyslogUdpSink.Port,
        };
        foreach (var k in keys) Environment.SetEnvironmentVariable(k, null);
    }

    private static IReadOnlyList<SiemEvent> SampleBatch(int n = 2)
    {
        var now = DateTimeOffset.UtcNow;
        var list = new List<SiemEvent>();
        for (var i = 0; i < n; i++)
        {
            list.Add(new SiemEvent(
                Guid.NewGuid(), Guid.NewGuid(), null, null,
                ActionCode: 5, ActionName: "ProviderBlocked",
                CreatedAt: now.AddSeconds(i),
                IntegrityHash: "abc"));
        }
        return list;
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        public HttpRequestMessage? LastRequest;
        public string? LastBody;
        public HttpStatusCode StatusCode { get; set; } = HttpStatusCode.OK;
        public int Calls;
        public Func<HttpRequestMessage, Task<HttpResponseMessage>>? OnSend;

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken ct)
        {
            Calls++;
            LastRequest = request;
            LastBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(ct);
            if (OnSend is not null) return await OnSend(request);
            return new HttpResponseMessage(StatusCode);
        }
    }

    [Fact]
    public void Splunk_Configured_Reflects_EnvVars()
    {
        var sink = new SplunkHecSink(new HttpClient(new StubHandler()));
        Assert.False(sink.Configured);
        Environment.SetEnvironmentVariable(SplunkHecSink.Url, "https://splunk:8088");
        Environment.SetEnvironmentVariable(SplunkHecSink.Token, "abc");
        Assert.True(sink.Configured);
    }

    [Fact]
    public async Task Splunk_PushAsync_Posts_NDJSON_With_Splunk_Auth()
    {
        Environment.SetEnvironmentVariable(SplunkHecSink.Url, "https://splunk:8088");
        Environment.SetEnvironmentVariable(SplunkHecSink.Token, "secret-token");
        var handler = new StubHandler();
        var sink = new SplunkHecSink(new HttpClient(handler));
        await sink.PushAsync(SampleBatch(3), CancellationToken.None);

        Assert.Equal(1, handler.Calls);
        Assert.NotNull(handler.LastRequest);
        Assert.Equal(HttpMethod.Post, handler.LastRequest!.Method);
        Assert.EndsWith("/services/collector/event", handler.LastRequest.RequestUri!.AbsoluteUri);
        Assert.Equal("Splunk", handler.LastRequest.Headers.Authorization?.Scheme);
        Assert.Equal("secret-token", handler.LastRequest.Headers.Authorization?.Parameter);
        Assert.NotNull(handler.LastBody);
        // NDJSON: 3 lines.
        var lines = handler.LastBody!.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(3, lines.Length);
        // Each line is a Splunk HEC envelope with `event`.
        foreach (var line in lines)
        {
            using var d = JsonDocument.Parse(line);
            Assert.True(d.RootElement.TryGetProperty("event", out _));
            Assert.True(d.RootElement.TryGetProperty("time", out _));
        }
    }

    [Fact]
    public async Task Sentinel_PushAsync_Sets_HMAC_Authorization_And_Headers()
    {
        Environment.SetEnvironmentVariable(SentinelLogAnalyticsSink.Workspace, "11111111-2222-3333-4444-555555555555");
        // 32-byte arbitrary base64 (a Sentinel shared key).
        Environment.SetEnvironmentVariable(SentinelLogAnalyticsSink.SharedKey,
            Convert.ToBase64String(new byte[32]));
        Environment.SetEnvironmentVariable(SentinelLogAnalyticsSink.LogType, "RadioPadAudit");
        var handler = new StubHandler { StatusCode = HttpStatusCode.OK };
        var sink = new SentinelLogAnalyticsSink(new HttpClient(handler));
        await sink.PushAsync(SampleBatch(1), CancellationToken.None);

        Assert.Equal(1, handler.Calls);
        var req = handler.LastRequest!;
        Assert.Equal(HttpMethod.Post, req.Method);
        Assert.Contains("ods.opinsights.azure.com/api/logs", req.RequestUri!.AbsoluteUri);
        Assert.True(req.Headers.Contains("Authorization"));
        Assert.StartsWith("SharedKey 11111111-2222-3333-4444-555555555555:",
            string.Concat(req.Headers.GetValues("Authorization")));
        Assert.True(req.Headers.Contains("Log-Type"));
        Assert.Equal("RadioPadAudit", req.Headers.GetValues("Log-Type").Single());
        Assert.True(req.Headers.Contains("x-ms-date"));
    }

    [Fact]
    public async Task Elastic_PushAsync_Posts_NDJSON_Bulk_With_Bearer()
    {
        Environment.SetEnvironmentVariable(ElasticBulkSink.Url, "https://elastic:9200");
        Environment.SetEnvironmentVariable(ElasticBulkSink.Index, "radiopad-audit");
        Environment.SetEnvironmentVariable(ElasticBulkSink.Bearer, "tok");
        var handler = new StubHandler();
        var sink = new ElasticBulkSink(new HttpClient(handler));
        await sink.PushAsync(SampleBatch(2), CancellationToken.None);

        var req = handler.LastRequest!;
        Assert.EndsWith("/_bulk", req.RequestUri!.AbsoluteUri);
        Assert.Equal("Bearer", req.Headers.Authorization?.Scheme);
        Assert.Equal("tok", req.Headers.Authorization?.Parameter);
        Assert.Equal("application/x-ndjson", req.Content!.Headers.ContentType?.MediaType);
        // Bulk: 2 events × 2 lines (action + doc) = 4 NDJSON lines.
        var lines = handler.LastBody!.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(4, lines.Length);
    }

    [Fact]
    public async Task Elastic_Basic_Auth_When_Bearer_Absent()
    {
        Environment.SetEnvironmentVariable(ElasticBulkSink.Url, "https://elastic:9200");
        Environment.SetEnvironmentVariable(ElasticBulkSink.Basic, "alice:pw");
        var handler = new StubHandler();
        var sink = new ElasticBulkSink(new HttpClient(handler));
        await sink.PushAsync(SampleBatch(1), CancellationToken.None);
        var req = handler.LastRequest!;
        Assert.Equal("Basic", req.Headers.Authorization?.Scheme);
        Assert.Equal(Convert.ToBase64String(Encoding.UTF8.GetBytes("alice:pw")),
            req.Headers.Authorization?.Parameter);
    }

    private sealed class StubUdp : IUdpSender
    {
        public List<(string host, int port, byte[] payload)> Sent { get; } = new();
        public Task SendAsync(string host, int port, byte[] payload, CancellationToken ct)
        {
            Sent.Add((host, port, payload));
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task Syslog_Emits_RFC5424_Frames_To_UDP_Sender()
    {
        Environment.SetEnvironmentVariable(SyslogUdpSink.Host, "siem.local");
        Environment.SetEnvironmentVariable(SyslogUdpSink.Port, "5140");
        var udp = new StubUdp();
        var sink = new SyslogUdpSink(udp);
        await sink.PushAsync(SampleBatch(2), CancellationToken.None);

        Assert.Equal(2, udp.Sent.Count);
        foreach (var (host, port, bytes) in udp.Sent)
        {
            Assert.Equal("siem.local", host);
            Assert.Equal(5140, port);
            var msg = Encoding.UTF8.GetString(bytes);
            // RFC 5424: <PRI>VERSION TIMESTAMP HOSTNAME APP-NAME PROCID MSGID SD MSG
            Assert.StartsWith("<134>1 ", msg);
            Assert.Contains("radiopad audit", msg);
            Assert.Contains("ProviderBlocked", msg);
            // Structured-data block present.
            Assert.Contains("[radiopad@32473 ", msg);
        }
    }

    [Fact]
    public async Task Syslog_Skipped_When_Host_Unset()
    {
        var udp = new StubUdp();
        var sink = new SyslogUdpSink(udp);
        Assert.False(sink.Configured);
        // Even if PushAsync is invoked anyway it must not throw on empty envs.
        // (BackgroundService skips unconfigured sinks; this test guards the
        // guard.)
        await sink.PushAsync(SampleBatch(0), CancellationToken.None);
        Assert.Empty(udp.Sent);
    }

    [Fact]
    public async Task Sink_Failure_Surface_HttpRequest_Exception()
    {
        Environment.SetEnvironmentVariable(SplunkHecSink.Url, "https://splunk:8088");
        Environment.SetEnvironmentVariable(SplunkHecSink.Token, "tok");
        var handler = new StubHandler { StatusCode = HttpStatusCode.InternalServerError };
        var sink = new SplunkHecSink(new HttpClient(handler));
        await Assert.ThrowsAsync<HttpRequestException>(
            () => sink.PushAsync(SampleBatch(1), CancellationToken.None));
    }
}
