using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using RadioPad.Application.Services.Pacs;

namespace RadioPad.Infrastructure.Pacs;

/// <summary>
/// Iter-33 INT-007 — Carestream Vue vendor adapter.
///
/// Vue speaks DICOMweb for retrieval (already covered by
/// <c>IDicomWebClient</c>) plus a thin REST surface at <c>/api/vue/v1</c>
/// for orchestration. This adapter targets the REST surface for
/// worklist/prior/sendback and reuses the standard Vue endpoints:
/// <list type="bullet">
///   <item><c>GET /api/vue/v1/health</c> — probe.</item>
///   <item><c>GET /api/vue/v1/worklist</c> — worklist pull (query string).</item>
///   <item><c>GET /api/vue/v1/studies/{accession}/prior</c> — prior fetch.</item>
///   <item><c>POST /api/vue/v1/reports</c> — report sendback.</item>
///   <item><c>PATCH /api/vue/v1/studies/{accession}/status</c> — study-status patch.</item>
/// </list>
/// </summary>
public sealed class CarestreamVueAdapter : IPacsVendorAdapter
{
    public const string ClientName = "pacs.carestream";
    public string Vendor => "carestream";

    private readonly IHttpClientFactory _http;
    private readonly ILogger<CarestreamVueAdapter> _log;

    public CarestreamVueAdapter(IHttpClientFactory http, ILogger<CarestreamVueAdapter> log)
    {
        _http = http;
        _log = log;
    }

    private HttpClient Client()
    {
        var c = _http.CreateClient(ClientName);
        if (c.BaseAddress is null)
        {
            var baseUrl = Environment.GetEnvironmentVariable("RADIOPAD_PACS_CARESTREAM_BASE")
                          ?? "https://vue.example.invalid";
            c.BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/");
        }
        var token = PacsSecretResolver.Resolve(
            Environment.GetEnvironmentVariable("RADIOPAD_PACS_CARESTREAM_TOKEN_REF"),
            fallbackEnv: "RADIOPAD_PACS_CARESTREAM_TOKEN");
        if (!string.IsNullOrEmpty(token))
            c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return c;
    }

    public async Task<PacsWorklistEntry[]> FetchWorklistAsync(PacsWorklistQuery q, CancellationToken ct)
    {
        var qs = new List<string>
        {
            $"tenantId={Uri.EscapeDataString(q.TenantId.ToString())}",
            $"limit={q.Limit}",
        };
        if (!string.IsNullOrEmpty(q.Modality)) qs.Add($"modality={Uri.EscapeDataString(q.Modality)}");
        if (!string.IsNullOrEmpty(q.Status)) qs.Add($"status={Uri.EscapeDataString(q.Status)}");
        if (q.ScheduledFrom is not null) qs.Add($"from={Uri.EscapeDataString(q.ScheduledFrom.Value.ToString("o"))}");
        if (q.ScheduledTo is not null) qs.Add($"to={Uri.EscapeDataString(q.ScheduledTo.Value.ToString("o"))}");
        var url = "api/vue/v1/worklist?" + string.Join("&", qs);

        try
        {
            using var resp = await Client().GetAsync(url, ct);
            if (!resp.IsSuccessStatusCode) return Array.Empty<PacsWorklistEntry>();
            var stream = await resp.Content.ReadAsStreamAsync(ct);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
            return ParseEntries(doc.RootElement);
        }
        catch (HttpRequestException ex)
        {
            _log.LogWarning(ex, "Carestream Vue worklist failed");
            return Array.Empty<PacsWorklistEntry>();
        }
    }

    public async Task<PacsStudySummary?> FetchPriorAsync(string accessionNumber, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(accessionNumber)) return null;
        try
        {
            using var resp = await Client().GetAsync(
                $"api/vue/v1/studies/{Uri.EscapeDataString(accessionNumber)}/prior", ct);
            if (!resp.IsSuccessStatusCode) return null;
            var stream = await resp.Content.ReadAsStreamAsync(ct);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
            var e = ParseEntry(doc.RootElement);
            return e is null ? null : new PacsStudySummary(
                e.AccessionNumber, e.PatientId, e.StudyInstanceUid, e.Modality, e.Status, e.Description);
        }
        catch (HttpRequestException ex)
        {
            _log.LogWarning(ex, "Carestream Vue prior fetch failed");
            return null;
        }
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
            using var resp = await c.PostAsJsonAsync("api/vue/v1/reports", payload, ct);
            if (!resp.IsSuccessStatusCode) return false;

            // Companion study-status patch — best-effort; failure here does
            // not roll back the sendback (report has already been accepted).
            var patch = new HttpRequestMessage(HttpMethod.Patch,
                $"api/vue/v1/studies/{Uri.EscapeDataString(report.AccessionNumber)}/status")
            {
                Content = JsonContent.Create(new { status = report.Status }),
            };
            try { using var _ = await c.SendAsync(patch, ct); }
            catch (HttpRequestException ex) { _log.LogDebug(ex, "Vue status patch failed"); }
            return true;
        }
        catch (HttpRequestException ex)
        {
            _log.LogWarning(ex, "Carestream Vue sendback failed for accession {Accession}", report.AccessionNumber);
            return false;
        }
    }

    public async Task<PacsAdapterHealth> ProbeAsync(CancellationToken ct)
    {
        try
        {
            using var resp = await Client().GetAsync("api/vue/v1/health", ct);
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
