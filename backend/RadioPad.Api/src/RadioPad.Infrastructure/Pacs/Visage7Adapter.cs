using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using RadioPad.Application.Services.Pacs;

namespace RadioPad.Infrastructure.Pacs;

/// <summary>
/// Iter-33 INT-007 — Visage 7 vendor adapter.
///
/// Visage exposes a single GraphQL endpoint at <c>/graphql</c>. All four
/// operations are mapped onto GraphQL queries / mutations:
/// <list type="bullet">
///   <item>Probe: <c>query { ping }</c>.</item>
///   <item>Worklist: <c>query Worklist($input: WorklistInput!) { worklist(input: $input) { ... } }</c>.</item>
///   <item>Prior: <c>query Prior($accession: String!) { prior(accession: $accession) { ... } }</c>.</item>
///   <item>SendReport: <c>mutation Report($input: ReportInput!) { reportSend(input: $input) { ok } }</c>.</item>
/// </list>
/// </summary>
public sealed class Visage7Adapter : IPacsVendorAdapter
{
    public const string ClientName = "pacs.visage";
    public string Vendor => "visage";

    private readonly IHttpClientFactory _http;
    private readonly ILogger<Visage7Adapter> _log;

    public Visage7Adapter(IHttpClientFactory http, ILogger<Visage7Adapter> log)
    {
        _http = http;
        _log = log;
    }

    private HttpClient Client()
    {
        var c = _http.CreateClient(ClientName);
        if (c.BaseAddress is null)
        {
            var baseUrl = Environment.GetEnvironmentVariable("RADIOPAD_PACS_VISAGE_BASE")
                          ?? "https://visage.example.invalid";
            c.BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/");
        }
        var token = PacsSecretResolver.Resolve(
            Environment.GetEnvironmentVariable("RADIOPAD_PACS_VISAGE_TOKEN_REF"),
            fallbackEnv: "RADIOPAD_PACS_VISAGE_TOKEN");
        if (!string.IsNullOrEmpty(token))
            c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return c;
    }

    private async Task<JsonDocument?> PostGraphQLAsync(string query, object? variables, CancellationToken ct)
    {
        var body = new { query, variables };
        try
        {
            using var resp = await Client().PostAsJsonAsync("graphql", body, ct);
            if (!resp.IsSuccessStatusCode) return null;
            var stream = await resp.Content.ReadAsStreamAsync(ct);
            return await JsonDocument.ParseAsync(stream, cancellationToken: ct);
        }
        catch (HttpRequestException ex)
        {
            _log.LogWarning(ex, "Visage 7 GraphQL call failed");
            return null;
        }
    }

    public async Task<PacsWorklistEntry[]> FetchWorklistAsync(PacsWorklistQuery q, CancellationToken ct)
    {
        const string Query = "query Worklist($input: WorklistInput!) { worklist(input: $input) { accessionNumber patientId studyInstanceUid modality status description } }";
        var variables = new
        {
            input = new
            {
                tenantId = q.TenantId,
                modality = q.Modality,
                status = q.Status,
                scheduledFrom = q.ScheduledFrom,
                scheduledTo = q.ScheduledTo,
                limit = q.Limit,
            },
        };
        using var doc = await PostGraphQLAsync(Query, variables, ct);
        if (doc is null) return Array.Empty<PacsWorklistEntry>();
        if (!doc.RootElement.TryGetProperty("data", out var data)) return Array.Empty<PacsWorklistEntry>();
        if (!data.TryGetProperty("worklist", out var arr) || arr.ValueKind != JsonValueKind.Array)
            return Array.Empty<PacsWorklistEntry>();
        var list = new List<PacsWorklistEntry>(arr.GetArrayLength());
        foreach (var el in arr.EnumerateArray())
        {
            var e = ParseEntry(el);
            if (e is not null) list.Add(e);
        }
        return list.ToArray();
    }

    public async Task<PacsStudySummary?> FetchPriorAsync(string accessionNumber, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(accessionNumber)) return null;
        const string Query = "query Prior($accession: String!) { prior(accession: $accession) { accessionNumber patientId studyInstanceUid modality status description } }";
        using var doc = await PostGraphQLAsync(Query, new { accession = accessionNumber }, ct);
        if (doc is null) return null;
        if (!doc.RootElement.TryGetProperty("data", out var data)) return null;
        if (!data.TryGetProperty("prior", out var p) || p.ValueKind != JsonValueKind.Object) return null;
        var e = ParseEntry(p);
        return e is null ? null : new PacsStudySummary(
            e.AccessionNumber, e.PatientId, e.StudyInstanceUid, e.Modality, e.Status, e.Description);
    }

    public async Task<bool> SendReportAsync(PacsReportSendback report, CancellationToken ct)
    {
        const string Mutation = "mutation Report($input: ReportInput!) { reportSend(input: $input) { ok } }";
        var variables = new
        {
            input = new
            {
                tenantId = report.TenantId,
                accessionNumber = report.AccessionNumber,
                studyInstanceUid = report.StudyInstanceUid,
                status = report.Status,
                radiologistEmail = report.RadiologistEmail,
                reportText = report.ReportText,
            },
        };
        using var doc = await PostGraphQLAsync(Mutation, variables, ct);
        if (doc is null) return false;
        if (!doc.RootElement.TryGetProperty("data", out var data)) return false;
        if (!data.TryGetProperty("reportSend", out var rs) || rs.ValueKind != JsonValueKind.Object) return false;
        return rs.TryGetProperty("ok", out var ok) && ok.ValueKind == JsonValueKind.True;
    }

    public async Task<PacsAdapterHealth> ProbeAsync(CancellationToken ct)
    {
        const string Query = "query { ping }";
        try
        {
            using var resp = await Client().PostAsJsonAsync("graphql", new { query = Query }, ct);
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
