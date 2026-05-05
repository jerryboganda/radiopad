using System.Net.Http.Headers;
using System.Text.Json;
using RadioPad.Domain.Entities;

namespace RadioPad.Api.Services;

/// <summary>
/// PRD DCM-001..006 — minimal DICOMweb (QIDO-RS) client used to enrich the
/// editor's StudyContext panel with PACS-side metadata. Generic, vendor-neutral
/// HTTP only; no Orthanc / DCM4CHEE / vendor-specific code paths. Returns null
/// when the tenant has not configured a base URL.
/// </summary>
public interface IDicomWebClient
{
    Task<DicomStudyContext?> FetchStudyAsync(TenantSettings settings, string accessionNumber, CancellationToken ct);

    /// <summary>
    /// Iter-31 DCM-007 — WADO-RS instance metadata retrieval. Returns the
    /// parsed DICOM JSON Model array for
    /// <c>{base}/studies/{study}/series/{series}/instances/{instance}/metadata</c>.
    /// Returns <c>null</c> when DICOMweb is not configured for the tenant or
    /// the instance cannot be fetched.
    /// </summary>
    Task<JsonDocument?> RetrieveInstanceMetadataAsync(
        TenantSettings settings, string studyUid, string seriesUid, string instanceUid, CancellationToken ct);

    /// <summary>
    /// Iter-32 DESK-007 — generic QIDO-RS study search. Returns the raw
    /// DICOM JSON Model array exactly as the upstream PACS sent it (so the
    /// proxy is faithful), plus the upstream HTTP status code so callers
    /// can map non-200 responses cleanly. Returns
    /// <c>(null, 0)</c> when DICOMweb is not configured for the tenant.
    /// <paramref name="query"/> is forwarded as the URL query string
    /// (vendor-neutral DICOMweb query parameters, e.g.
    /// <c>AccessionNumber=ACC123</c>).
    /// </summary>
    Task<(JsonDocument? body, int statusCode)> SearchStudiesAsync(
        TenantSettings settings, string query, CancellationToken ct);

    /// <summary>
    /// Iter-32 DESK-007 — STOW-RS store proxy. Forwards DICOM bytes
    /// (multipart/related; type="application/dicom" or
    /// application/dicom+json) to <c>{base}/studies</c>.
    /// Returns the upstream status code and (optionally) a parsed JSON body.
    /// Returns <c>(0, null)</c> when DICOMweb is not configured.
    /// </summary>
    Task<(int statusCode, JsonDocument? body)> StoreInstancesAsync(
        TenantSettings settings, byte[] body, string contentType, CancellationToken ct);

    /// <summary>
    /// Iter-32 DESK-007 — generic Orthanc/DICOMweb health probe. Issues a
    /// HEAD/GET against the base URL and returns whether it responded
    /// inside the timeout. Never throws.
    /// </summary>
    Task<bool> HealthAsync(TenantSettings settings, CancellationToken ct);
}

public sealed record DicomStudyContext(
    string StudyInstanceUid,
    string Modality,
    string BodyPart,
    string StudyDate,
    int InstanceCount,
    string SourceUrl);

public sealed class DicomWebClient : IDicomWebClient
{
    private readonly IHttpClientFactory _http;
    private readonly ILogger<DicomWebClient> _log;
    public DicomWebClient(IHttpClientFactory http, ILogger<DicomWebClient> log)
    {
        _http = http;
        _log = log;
    }

    public async Task<DicomStudyContext?> FetchStudyAsync(
        TenantSettings settings, string accessionNumber, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(settings.DicomWebBaseUrl)) return null;
        if (string.IsNullOrWhiteSpace(accessionNumber)) return null;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
        var baseUrl = settings.DicomWebBaseUrl.TrimEnd('/');
        var url = $"{baseUrl}/studies?AccessionNumber={Uri.EscapeDataString(accessionNumber)}";
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/dicom+json"));
        if (!string.IsNullOrEmpty(settings.DicomWebBearerSecret))
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.DicomWebBearerSecret);

        try
        {
            using var client = _http.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(8);
            using var resp = await client.SendAsync(req, ct);
            if (!resp.IsSuccessStatusCode)
            {
                _log.LogWarning("DICOMweb QIDO returned {StatusCode}", resp.StatusCode);
                return null;
            }
            var json = await resp.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Array || doc.RootElement.GetArrayLength() == 0)
                return null;
            var first = doc.RootElement[0];
            return new DicomStudyContext(
                StudyInstanceUid: ReadDicom(first, "0020000D"),
                Modality: ReadDicom(first, "00080060"),
                BodyPart: ReadDicom(first, "00180015"),
                StudyDate: ReadDicom(first, "00080020"),
                InstanceCount: int.TryParse(ReadDicom(first, "00201208"), out var n) ? n : 0,
                SourceUrl: url);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "DICOMweb fetch failed");
            return null;
        }
        }
        finally
        {
            sw.Stop();
            PerfBudgets.DicomQidoDurationMs.Record(sw.Elapsed.TotalMilliseconds,
                new KeyValuePair<string, object?>("scope", "fetch"));
        }
    }

    /// <summary>Reads a DICOM JSON tag like <c>{ "00080060": { "vr": "CS", "Value": ["CT"] } }</c>.</summary>
    private static string ReadDicom(JsonElement obj, string tag)
    {
        if (!obj.TryGetProperty(tag, out var attr)) return "";
        if (!attr.TryGetProperty("Value", out var values)) return "";
        if (values.ValueKind != JsonValueKind.Array || values.GetArrayLength() == 0) return "";
        var v = values[0];
        return v.ValueKind switch
        {
            JsonValueKind.String => v.GetString() ?? "",
            JsonValueKind.Number => v.GetRawText(),
            _ => v.ToString(),
        };
    }

    public async Task<JsonDocument?> RetrieveInstanceMetadataAsync(
        TenantSettings settings, string studyUid, string seriesUid, string instanceUid, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(settings.DicomWebBaseUrl)) return null;
        if (string.IsNullOrWhiteSpace(studyUid) || string.IsNullOrWhiteSpace(seriesUid) || string.IsNullOrWhiteSpace(instanceUid))
            return null;
        var baseUrl = settings.DicomWebBaseUrl.TrimEnd('/');
        var url = $"{baseUrl}/studies/{Uri.EscapeDataString(studyUid)}" +
                  $"/series/{Uri.EscapeDataString(seriesUid)}" +
                  $"/instances/{Uri.EscapeDataString(instanceUid)}/metadata";
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/dicom+json"));
        if (!string.IsNullOrEmpty(settings.DicomWebBearerSecret))
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.DicomWebBearerSecret);
        try
        {
            using var client = _http.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(8);
            using var resp = await client.SendAsync(req, ct);
            if (!resp.IsSuccessStatusCode)
            {
                _log.LogWarning("DICOMweb WADO-RS instance metadata returned {StatusCode}", resp.StatusCode);
                return null;
            }
            var json = await resp.Content.ReadAsStringAsync(ct);
            return JsonDocument.Parse(json);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "DICOMweb WADO-RS instance metadata fetch failed");
            return null;
        }
    }

    public async Task<(JsonDocument? body, int statusCode)> SearchStudiesAsync(
        TenantSettings settings, string query, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(settings.DicomWebBaseUrl)) return (null, 0);
        // Iter-33 PERF-004 — record duration on the QIDO histogram regardless of outcome.
        var sw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            var baseUrl = settings.DicomWebBaseUrl.TrimEnd('/');
            var url = string.IsNullOrWhiteSpace(query) ? $"{baseUrl}/studies" : $"{baseUrl}/studies?{query}";
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/dicom+json"));
            if (!string.IsNullOrEmpty(settings.DicomWebBearerSecret))
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.DicomWebBearerSecret);
            try
            {
                using var client = _http.CreateClient();
                client.Timeout = TimeSpan.FromSeconds(8);
                using var resp = await client.SendAsync(req, ct);
                var json = await resp.Content.ReadAsStringAsync(ct);
                JsonDocument? body = null;
                if (!string.IsNullOrWhiteSpace(json))
                {
                    try { body = JsonDocument.Parse(json); }
                    catch { /* upstream returned non-JSON; surface status code only */ }
                }
                return (body, (int)resp.StatusCode);
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "DICOMweb QIDO-RS search failed");
                return (null, 0);
            }
        }
        finally
        {
            sw.Stop();
            PerfBudgets.DicomQidoDurationMs.Record(sw.Elapsed.TotalMilliseconds,
                new KeyValuePair<string, object?>("scope", "search"));
        }
    }

    public async Task<(int statusCode, JsonDocument? body)> StoreInstancesAsync(
        TenantSettings settings, byte[] body, string contentType, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(settings.DicomWebBaseUrl)) return (0, null);
        if (body is null || body.Length == 0) return (400, null);
        var baseUrl = settings.DicomWebBaseUrl.TrimEnd('/');
        var url = $"{baseUrl}/studies";
        using var content = new ByteArrayContent(body);
        // Default to multipart/related; type=application/dicom when caller did
        // not specify so the upstream knows we're forwarding raw DICOM bytes.
        var ct2 = string.IsNullOrWhiteSpace(contentType)
            ? "multipart/related; type=\"application/dicom\""
            : contentType;
        try { content.Headers.ContentType = MediaTypeHeaderValue.Parse(ct2); }
        catch { content.Headers.TryAddWithoutValidation("Content-Type", ct2); }
        using var req = new HttpRequestMessage(HttpMethod.Post, url) { Content = content };
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/dicom+json"));
        if (!string.IsNullOrEmpty(settings.DicomWebBearerSecret))
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.DicomWebBearerSecret);
        try
        {
            using var client = _http.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(30);
            using var resp = await client.SendAsync(req, ct);
            var json = await resp.Content.ReadAsStringAsync(ct);
            JsonDocument? doc = null;
            if (!string.IsNullOrWhiteSpace(json))
            {
                try { doc = JsonDocument.Parse(json); } catch { }
            }
            return ((int)resp.StatusCode, doc);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "DICOMweb STOW-RS store failed");
            return (0, null);
        }
    }

    public async Task<bool> HealthAsync(TenantSettings settings, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(settings.DicomWebBaseUrl)) return false;
        var baseUrl = settings.DicomWebBaseUrl.TrimEnd('/');
        // Hit /studies?limit=1 — every DICOMweb-compliant server (Orthanc,
        // DCM4CHEE, vendor) supports QIDO. HEAD support is inconsistent.
        using var req = new HttpRequestMessage(HttpMethod.Get, $"{baseUrl}/studies?limit=1");
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/dicom+json"));
        if (!string.IsNullOrEmpty(settings.DicomWebBearerSecret))
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.DicomWebBearerSecret);
        try
        {
            using var client = _http.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(3);
            using var resp = await client.SendAsync(req, ct);
            return (int)resp.StatusCode < 500;
        }
        catch
        {
            return false;
        }
    }
}
