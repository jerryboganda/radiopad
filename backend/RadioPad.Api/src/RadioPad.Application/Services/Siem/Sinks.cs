using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace RadioPad.Application.Services.Siem;

/// <summary>
/// Iter-32 INT-010 — Splunk HTTP Event Collector sink.
///
/// Env vars:
///   <c>RADIOPAD_SIEM_SPLUNK_URL</c> — base URL of HEC (e.g. https://splunk:8088).
///   <c>RADIOPAD_SIEM_SPLUNK_TOKEN</c> — HEC token (sent as Splunk &lt;token&gt;).
/// </summary>
public sealed class SplunkHecSink : ISiemSink
{
    public const string Url = "RADIOPAD_SIEM_SPLUNK_URL";
    public const string Token = "RADIOPAD_SIEM_SPLUNK_TOKEN";
    public string Name => "splunk";
    public bool Configured =>
        !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(Url)) &&
        !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(Token));

    private readonly HttpClient _http;
    public SplunkHecSink(HttpClient http) => _http = http;

    public async Task PushAsync(IReadOnlyList<SiemEvent> events, CancellationToken ct)
    {
        if (events.Count == 0) return;
        var url = Environment.GetEnvironmentVariable(Url)!.TrimEnd('/') + "/services/collector/event";
        var token = Environment.GetEnvironmentVariable(Token)!;
        var sb = new StringBuilder();
        foreach (var e in events)
        {
            sb.Append(JsonSerializer.Serialize(new
            {
                time = e.CreatedAt.ToUnixTimeSeconds(),
                source = "radiopad",
                sourcetype = "radiopad:audit",
                @event = new
                {
                    id = e.Id,
                    tenantId = e.TenantId,
                    userId = e.UserId,
                    reportId = e.ReportId,
                    actionCode = e.ActionCode,
                    action = e.ActionName,
                    integrityHash = e.IntegrityHash,
                },
            })).Append('\n');
        }
        using var req = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(sb.ToString(), Encoding.UTF8, "application/json"),
        };
        req.Headers.Authorization = new AuthenticationHeaderValue("Splunk", token);
        using var resp = await _http.SendAsync(req, ct);
        resp.EnsureSuccessStatusCode();
    }
}

/// <summary>
/// Iter-32 INT-010 — Microsoft Sentinel / Azure Monitor Log Analytics
/// data-collector sink. HMAC-SHA256-signed POST.
///
/// Env vars:
///   <c>RADIOPAD_SIEM_SENTINEL_WORKSPACE_ID</c>
///   <c>RADIOPAD_SIEM_SENTINEL_SHARED_KEY</c>  (base64 primary/secondary key)
///   <c>RADIOPAD_SIEM_SENTINEL_LOG_TYPE</c> (optional, default "RadioPadAudit")
/// </summary>
public sealed class SentinelLogAnalyticsSink : ISiemSink
{
    public const string Workspace = "RADIOPAD_SIEM_SENTINEL_WORKSPACE_ID";
    public const string SharedKey = "RADIOPAD_SIEM_SENTINEL_SHARED_KEY";
    public const string LogType = "RADIOPAD_SIEM_SENTINEL_LOG_TYPE";
    public string Name => "sentinel";
    public bool Configured =>
        !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(Workspace)) &&
        !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(SharedKey));

    private readonly HttpClient _http;
    public SentinelLogAnalyticsSink(HttpClient http) => _http = http;

    public async Task PushAsync(IReadOnlyList<SiemEvent> events, CancellationToken ct)
    {
        if (events.Count == 0) return;
        var workspaceId = Environment.GetEnvironmentVariable(Workspace)!;
        var sharedKeyB64 = Environment.GetEnvironmentVariable(SharedKey)!;
        var logType = Environment.GetEnvironmentVariable(LogType);
        if (string.IsNullOrWhiteSpace(logType)) logType = "RadioPadAudit";

        var json = JsonSerializer.Serialize(events.Select(e => new
        {
            id = e.Id,
            tenantId = e.TenantId,
            userId = e.UserId,
            reportId = e.ReportId,
            actionCode = e.ActionCode,
            action = e.ActionName,
            createdAt = e.CreatedAt,
            integrityHash = e.IntegrityHash,
        }));
        var bytes = Encoding.UTF8.GetBytes(json);
        var rfc1123 = DateTime.UtcNow.ToString("r", System.Globalization.CultureInfo.InvariantCulture);

        // Signature string per Azure Monitor Data Collector contract.
        var stringToHash = $"POST\n{bytes.Length}\napplication/json\nx-ms-date:{rfc1123}\n/api/logs";
        var hash = Encoding.UTF8.GetBytes(stringToHash);
        using var hmac = new HMACSHA256(Convert.FromBase64String(sharedKeyB64));
        var signature = Convert.ToBase64String(hmac.ComputeHash(hash));
        var authorization = $"SharedKey {workspaceId}:{signature}";

        var url = $"https://{workspaceId}.ods.opinsights.azure.com/api/logs?api-version=2016-04-01";
        using var req = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new ByteArrayContent(bytes),
        };
        req.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        req.Headers.TryAddWithoutValidation("Authorization", authorization);
        req.Headers.TryAddWithoutValidation("Log-Type", logType!);
        req.Headers.TryAddWithoutValidation("x-ms-date", rfc1123);
        req.Headers.TryAddWithoutValidation("time-generated-field", "createdAt");
        using var resp = await _http.SendAsync(req, ct);
        resp.EnsureSuccessStatusCode();
    }
}

/// <summary>
/// Iter-32 INT-010 — Elasticsearch <c>_bulk</c> sink.
///
/// Env vars:
///   <c>RADIOPAD_SIEM_ELASTIC_URL</c> — Elasticsearch base URL.
///   <c>RADIOPAD_SIEM_ELASTIC_INDEX</c> — index name (default <c>radiopad-audit</c>).
///   <c>RADIOPAD_SIEM_ELASTIC_BEARER</c> — Bearer / API key (optional).
///   <c>RADIOPAD_SIEM_ELASTIC_BASIC</c> — Basic auth in <c>user:pass</c> form (optional).
/// </summary>
public sealed class ElasticBulkSink : ISiemSink
{
    public const string Url = "RADIOPAD_SIEM_ELASTIC_URL";
    public const string Index = "RADIOPAD_SIEM_ELASTIC_INDEX";
    public const string Bearer = "RADIOPAD_SIEM_ELASTIC_BEARER";
    public const string Basic = "RADIOPAD_SIEM_ELASTIC_BASIC";
    public string Name => "elastic";
    public bool Configured => !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(Url));

    private readonly HttpClient _http;
    public ElasticBulkSink(HttpClient http) => _http = http;

    public async Task PushAsync(IReadOnlyList<SiemEvent> events, CancellationToken ct)
    {
        if (events.Count == 0) return;
        var url = Environment.GetEnvironmentVariable(Url)!.TrimEnd('/') + "/_bulk";
        var index = Environment.GetEnvironmentVariable(Index);
        if (string.IsNullOrWhiteSpace(index)) index = "radiopad-audit";

        var sb = new StringBuilder();
        foreach (var e in events)
        {
            sb.Append(JsonSerializer.Serialize(new { index = new { _index = index, _id = e.Id } })).Append('\n');
            sb.Append(JsonSerializer.Serialize(new
            {
                id = e.Id,
                tenantId = e.TenantId,
                userId = e.UserId,
                reportId = e.ReportId,
                actionCode = e.ActionCode,
                action = e.ActionName,
                createdAt = e.CreatedAt,
                integrityHash = e.IntegrityHash,
            })).Append('\n');
        }

        using var req = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(sb.ToString(), Encoding.UTF8, "application/x-ndjson"),
        };
        var bearer = Environment.GetEnvironmentVariable(Bearer);
        var basic = Environment.GetEnvironmentVariable(Basic);
        if (!string.IsNullOrWhiteSpace(bearer))
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
        else if (!string.IsNullOrWhiteSpace(basic))
            req.Headers.Authorization = new AuthenticationHeaderValue(
                "Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes(basic!)));
        using var resp = await _http.SendAsync(req, ct);
        resp.EnsureSuccessStatusCode();
    }
}

/// <summary>
/// Iter-32 INT-010 — RFC 5424 Syslog over UDP.
///
/// Env vars:
///   <c>RADIOPAD_SIEM_SYSLOG_HOST</c>
///   <c>RADIOPAD_SIEM_SYSLOG_PORT</c> (default 514)
/// </summary>
public sealed class SyslogUdpSink : ISiemSink
{
    public const string Host = "RADIOPAD_SIEM_SYSLOG_HOST";
    public const string Port = "RADIOPAD_SIEM_SYSLOG_PORT";
    public string Name => "syslog";
    public bool Configured => !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(Host));

    private readonly IUdpSender _udp;
    public SyslogUdpSink(IUdpSender udp) => _udp = udp;

    public async Task PushAsync(IReadOnlyList<SiemEvent> events, CancellationToken ct)
    {
        if (events.Count == 0) return;
        var host = Environment.GetEnvironmentVariable(Host)!;
        var port = int.TryParse(Environment.GetEnvironmentVariable(Port), out var p) && p > 0 ? p : 514;
        // RFC 5424: <PRI>VERSION TIMESTAMP HOSTNAME APP PROCID MSGID STRUCTURED-DATA MSG
        // PRI = facility*8 + severity. Local0 (16) * 8 + Informational (6) = 134.
        const int pri = 134;
        foreach (var e in events)
        {
            var sd = $"[radiopad@32473 tenantId=\"{e.TenantId}\" userId=\"{e.UserId?.ToString() ?? "-"}\" reportId=\"{e.ReportId?.ToString() ?? "-"}\" integrityHash=\"{e.IntegrityHash}\" actionCode=\"{e.ActionCode}\"]";
            var msg = $"<{pri}>1 {e.CreatedAt.ToString("o")} radiopad audit {Environment.ProcessId} {e.ActionName} {sd} {e.ActionName}";
            var bytes = Encoding.UTF8.GetBytes(msg);
            await _udp.SendAsync(host, port, bytes, ct);
        }
    }
}

/// <summary>UDP sender abstraction for unit tests.</summary>
public interface IUdpSender
{
    Task SendAsync(string host, int port, byte[] payload, CancellationToken ct);
}

public sealed class DefaultUdpSender : IUdpSender, IDisposable
{
    private readonly UdpClient _client = new();
    public async Task SendAsync(string host, int port, byte[] payload, CancellationToken ct)
    {
        await _client.SendAsync(payload, payload.Length, host, port).WaitAsync(ct);
    }
    public void Dispose() => _client.Dispose();
}
