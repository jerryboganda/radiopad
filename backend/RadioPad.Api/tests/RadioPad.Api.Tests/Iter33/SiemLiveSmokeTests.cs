using System.Net.Sockets;
using System.Text;
using RadioPad.Api.Tests.Infrastructure;
using RadioPad.Application.Services.Siem;
using Xunit;

namespace RadioPad.Api.Tests.Iter33;

/// <summary>
/// Iter-33 INT-010 — live smoke tests for the four SIEM sinks. Each test is
/// gated by <c>RADIOPAD_RUN_SIEM_LIVE=1</c> AND the per-sink env vars used
/// at runtime. When the gate is not set the tests are skipped (xUnit shows
/// "skipped" rather than "failed"); when set, each test pushes a single
/// synthetic <see cref="SiemEvent"/> (NO PHI; tenant slug is the constant
/// <c>smoke</c>) and asserts a 2xx HTTP response or, for syslog, that the
/// expected number of UDP bytes were received on the local listener.
///
/// Pass criteria:
///   - Splunk / Sentinel / Elastic: <c>HttpResponseMessage.IsSuccessStatusCode</c>.
///   - Syslog: a UDP datagram of non-zero length lands on
///     <c>127.0.0.1:5514</c> within 2 s.
///
/// Synthetic event shape:
///   tenantId=00000000-0000-0000-0000-00000000beef, action=SystemTest (uses
///   the existing <see cref="Domain.Enums.AuditAction.UserLogin"/> code so
///   sink-side analysts see the well-known action label), message embedded
///   as the action name <c>radiopad-iter33-smoke</c>.
/// </summary>
[Collection(RadioPad.Api.Tests.Infrastructure.EnvironmentVariableCollection.Name)]
public sealed class SiemLiveSmokeTests
{
    private const string Gate = "RADIOPAD_RUN_SIEM_LIVE";

    private static SiemEvent BuildSmokeEvent() => new(
        Id: Guid.NewGuid(),
        TenantId: new Guid("00000000-0000-0000-0000-00000000beef"),
        UserId: null,
        ReportId: null,
        ActionCode: 8, // UserLogin
        ActionName: "radiopad-iter33-smoke",
        CreatedAt: DateTimeOffset.UtcNow,
        IntegrityHash: new string('0', 64));

    [EnvFact(Gate)]
    public async Task Splunk_Live_Push_Returns_2xx()
    {
        // Endpoint env vars are required and identical to production.
        if (!HasAll(SplunkHecSink.Url, SplunkHecSink.Token)) return;

        // Splunk dev containers ship with a self-signed cert. Allow it for
        // smoke runs ONLY (the production sink uses the default validator).
        var handler = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback =
                HttpClientHandler.DangerousAcceptAnyServerCertificateValidator,
        };
        var sink = new SplunkHecSink(new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(10) });
        Assert.True(sink.Configured);

        await sink.PushAsync(new[] { BuildSmokeEvent() }, CancellationToken.None);
        // Sink throws on non-2xx, so reaching here == success.
    }

    [EnvFact(Gate)]
    public async Task Sentinel_Live_Push_Returns_2xx()
    {
        if (!HasAll(SentinelLogAnalyticsSink.Workspace, SentinelLogAnalyticsSink.SharedKey)) return;

        var sink = new SentinelLogAnalyticsSink(new HttpClient { Timeout = TimeSpan.FromSeconds(10) });
        Assert.True(sink.Configured);
        await sink.PushAsync(new[] { BuildSmokeEvent() }, CancellationToken.None);
    }

    [EnvFact(Gate)]
    public async Task Elastic_Live_Push_Returns_2xx()
    {
        if (!HasAll(ElasticBulkSink.Url)) return;

        var handler = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback =
                HttpClientHandler.DangerousAcceptAnyServerCertificateValidator,
        };
        var sink = new ElasticBulkSink(new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(10) });
        Assert.True(sink.Configured);
        await sink.PushAsync(new[] { BuildSmokeEvent() }, CancellationToken.None);
    }

    [EnvFact(Gate)]
    public async Task Syslog_Live_Push_Lands_On_Local_Udp_Listener()
    {
        // For the live syslog test we always bind a local UDP socket on
        // 127.0.0.1:5514 and override the sink env vars to point at it.
        // This proves the RFC-5424 framing reaches a real socket without
        // requiring an out-of-process collector.
        const int port = 5514;
        var prevHost = Environment.GetEnvironmentVariable(SyslogUdpSink.Host);
        var prevPort = Environment.GetEnvironmentVariable(SyslogUdpSink.Port);
        Environment.SetEnvironmentVariable(SyslogUdpSink.Host, "127.0.0.1");
        Environment.SetEnvironmentVariable(SyslogUdpSink.Port, port.ToString());
        try
        {
            using var listener = new UdpClient(new System.Net.IPEndPoint(System.Net.IPAddress.Loopback, port));
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            var receiveTask = listener.ReceiveAsync(cts.Token).AsTask();

            var sink = new SyslogUdpSink(new DefaultUdpSender());
            await sink.PushAsync(new[] { BuildSmokeEvent() }, CancellationToken.None);

            var result = await receiveTask;
            Assert.True(result.Buffer.Length > 0);
            var msg = Encoding.UTF8.GetString(result.Buffer);
            Assert.StartsWith("<134>1 ", msg);
            Assert.Contains("radiopad-iter33-smoke", msg);
        }
        finally
        {
            Environment.SetEnvironmentVariable(SyslogUdpSink.Host, prevHost);
            Environment.SetEnvironmentVariable(SyslogUdpSink.Port, prevPort);
        }
    }

    private static bool HasAll(params string[] names) =>
        names.All(n => !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(n)));
}
