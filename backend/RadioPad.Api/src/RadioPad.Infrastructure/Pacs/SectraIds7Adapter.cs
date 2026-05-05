using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using RadioPad.Application.Services.Pacs;

namespace RadioPad.Infrastructure.Pacs;

/// <summary>
/// Iter-33 INT-007 — Sectra IDS7 vendor adapter.
///
/// Speaks the IDS7 REST surface (<c>/ids7/api/v1/...</c>):
/// <list type="bullet">
///   <item><c>GET /ids7/api/v1/health</c> — probe.</item>
///   <item><c>POST /ids7/api/v1/worklist/query</c> — worklist pull.</item>
///   <item><c>GET /ids7/api/v1/studies/{accession}/prior</c> — prior fetch.</item>
///   <item><c>POST /ids7/api/v1/reports</c> — report sendback.</item>
/// </list>
/// Bearer token is read from <c>RADIOPAD_PACS_SECTRA_TOKEN</c> (or the
/// caller-supplied secret reference). The base URL comes from
/// <c>RADIOPAD_PACS_SECTRA_BASE</c> and falls back to
/// <c>https://ids7.example.invalid</c> in dev (probes return Unreachable
/// in that case, which is what the tests assert).
/// </summary>
public sealed class SectraIds7Adapter : IPacsVendorAdapter
{
    public const string ClientName = "pacs.sectra";
    public string Vendor => "sectra";

    private readonly IHttpClientFactory _http;
    private readonly ILogger<SectraIds7Adapter> _log;

    public SectraIds7Adapter(IHttpClientFactory http, ILogger<SectraIds7Adapter> log)
    {
        _http = http;
        _log = log;
    }

    private HttpClient Client()
    {
        var c = _http.CreateClient(ClientName);
        if (c.BaseAddress is null)
        {
            var baseUrl = Environment.GetEnvironmentVariable("RADIOPAD_PACS_SECTRA_BASE")
                          ?? "https://ids7.example.invalid";
            c.BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/");
        }
        var token = PacsSecretResolver.Resolve(
            Environment.GetEnvironmentVariable("RADIOPAD_PACS_SECTRA_TOKEN_REF"),
            fallbackEnv: "RADIOPAD_PACS_SECTRA_TOKEN");
        if (!string.IsNullOrEmpty(token))
            c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return c;
    }

    public async Task<PacsWorklistEntry[]> FetchWorklistAsync(PacsWorklistQuery q, CancellationToken ct)
    {
        var c = Client();
        var payload = new
        {
            tenantId = q.TenantId,
            modality = q.Modality,
            status = q.Status,
            scheduledFrom = q.ScheduledFrom,
            scheduledTo = q.ScheduledTo,
            limit = q.Limit,
        };
        using var resp = await c.PostAsJsonAsync("ids7/api/v1/worklist/query", payload, ct);
        if (!resp.IsSuccessStatusCode) return Array.Empty<PacsWorklistEntry>();
        var stream = await resp.Content.ReadAsStreamAsync(ct);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
        return ParseEntries(doc.RootElement);
    }

    public async Task<PacsStudySummary?> FetchPriorAsync(string accessionNumber, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(accessionNumber)) return null;
        var c = Client();
        using var resp = await c.GetAsync(
            $"ids7/api/v1/studies/{Uri.EscapeDataString(accessionNumber)}/prior", ct);
        if (!resp.IsSuccessStatusCode) return null;
        var stream = await resp.Content.ReadAsStreamAsync(ct);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
        var e = ParseEntry(doc.RootElement);
        return e is null ? null : new PacsStudySummary(
            e.AccessionNumber, e.PatientId, e.StudyInstanceUid, e.Modality, e.Status, e.Description);
    }

    public async Task<bool> SendReportAsync(PacsReportSendback report, CancellationToken ct)
    {
        var c = Client();
        var payload = new
        {
            tenantId = report.TenantId,
            accessionNumber = report.AccessionNumber,
            studyInstanceUid = report.StudyInstanceUid,
            status = report.Status,
            radiologistEmail = report.RadiologistEmail,
            reportText = report.ReportText,
        };
        try
        {
            using var resp = await c.PostAsJsonAsync("ids7/api/v1/reports", payload, ct);
            return resp.IsSuccessStatusCode;
        }
        catch (HttpRequestException ex)
        {
            _log.LogWarning(ex, "Sectra IDS7 sendback failed for accession {Accession}", report.AccessionNumber);
            return false;
        }
    }

    public async Task<PacsAdapterHealth> ProbeAsync(CancellationToken ct)
    {
        try
        {
            using var resp = await Client().GetAsync("ids7/api/v1/health", ct);
            var code = (int)resp.StatusCode;
            if (code >= 200 && code < 300) return new PacsAdapterHealth(Vendor, PacsAdapterHealthStatus.Healthy);
            if (code >= 500) return new PacsAdapterHealth(Vendor, PacsAdapterHealthStatus.Degraded, $"HTTP {code}");
            return new PacsAdapterHealth(Vendor, PacsAdapterHealthStatus.Degraded, $"HTTP {code}");
        }
        catch (Exception ex)
        {
            return new PacsAdapterHealth(Vendor, PacsAdapterHealthStatus.Unreachable, ex.GetType().Name);
        }
    }

    private static PacsWorklistEntry[] ParseEntries(JsonElement root)
    {
        var arr = root.ValueKind == JsonValueKind.Array
            ? root
            : (root.TryGetProperty("entries", out var e) && e.ValueKind == JsonValueKind.Array ? e : default);
        if (arr.ValueKind != JsonValueKind.Array) return Array.Empty<PacsWorklistEntry>();
        var list = new List<PacsWorklistEntry>(arr.GetArrayLength());
        foreach (var el in arr.EnumerateArray())
        {
            var entry = ParseEntry(el);
            if (entry is not null) list.Add(entry);
        }
        return list.ToArray();
    }

    private static PacsWorklistEntry? ParseEntry(JsonElement el)
    {
        if (el.ValueKind != JsonValueKind.Object) return null;
        return new PacsWorklistEntry(
            AccessionNumber: el.TryGetProperty("accessionNumber", out var a) ? (a.GetString() ?? "") : "",
            PatientId: el.TryGetProperty("patientId", out var p) ? (p.GetString() ?? "") : "",
            StudyInstanceUid: el.TryGetProperty("studyInstanceUid", out var s) ? (s.GetString() ?? "") : "",
            Modality: el.TryGetProperty("modality", out var m) ? (m.GetString() ?? "") : "",
            Status: el.TryGetProperty("status", out var st) ? (st.GetString() ?? "") : "",
            Description: el.TryGetProperty("description", out var d) ? (d.GetString() ?? "") : "");
    }
}
