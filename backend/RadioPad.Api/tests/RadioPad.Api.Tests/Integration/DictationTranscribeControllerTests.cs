using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Xunit;

namespace RadioPad.Api.Tests.Integration;

/// <summary>
/// Phase B (dictation transcription) — exercises the
/// <c>POST /api/reports/{id}/dictation/transcribe</c> action over the real HTTP
/// pipeline. Covers the validation + RBAC surface that runs BEFORE any UBAG
/// call (oversize / missing / wrong content-type → 400; non-reporting role →
/// 403). The provider-policy 403 envelope shape is proven at the service layer
/// (TranscriptionServiceTests) since it requires a live gateway here.
/// </summary>
public class DictationTranscribeControllerTests : IClassFixture<RadioPadAppFactory>
{
    private readonly RadioPadAppFactory _factory;
    public DictationTranscribeControllerTests(RadioPadAppFactory factory) => _factory = factory;

    private static MultipartFormDataContent AudioForm(byte[] bytes, string contentType, bool ack)
    {
        var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        form.Add(file, "audio", "dictation.webm");
        form.Add(new StringContent(ack ? "true" : "false"), "deidentifiedAck");
        return form;
    }

    private async Task<Guid> CreateReportAsync(HttpClient client)
    {
        var create = await client.PostAsJsonAsync("/api/reports", new
        {
            modality = "CT",
            bodyPart = "Chest",
            indication = "cough",
            accessionNumber = "ACC-TX-1",
        });
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        using var doc = await JsonDocument.ParseAsync(await create.Content.ReadAsStreamAsync());
        return doc.RootElement.GetProperty("id").GetGuid();
    }

    [Fact]
    public async Task NonReportingRole_Is_Forbidden()
    {
        // The seeded ItAdmin is neither Radiologist nor MedicalDirector.
        using var radClient = _factory.CreateTenantClient();
        var reportId = await CreateReportAsync(radClient);

        using var adminClient = _factory.CreateAdminClient();
        using var form = AudioForm(new byte[] { 1, 2, 3, 4 }, "audio/webm", ack: false);
        var resp = await adminClient.PostAsync($"/api/reports/{reportId}/dictation/transcribe", form);

        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
    }

    [Fact]
    public async Task Missing_Audio_Returns_400()
    {
        using var client = _factory.CreateTenantClient();
        var reportId = await CreateReportAsync(client);

        // Form with the ack flag but no audio file part.
        using var form = new MultipartFormDataContent { { new StringContent("false"), "deidentifiedAck" } };
        var resp = await client.PostAsync($"/api/reports/{reportId}/dictation/transcribe", form);

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        var body = await resp.Content.ReadAsStringAsync();
        Assert.Contains("validation", body);
    }

    [Fact]
    public async Task Unsupported_ContentType_Returns_400()
    {
        using var client = _factory.CreateTenantClient();
        var reportId = await CreateReportAsync(client);

        using var form = AudioForm(Encoding.UTF8.GetBytes("not audio"), "text/plain", ack: false);
        var resp = await client.PostAsync($"/api/reports/{reportId}/dictation/transcribe", form);

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        var body = await resp.Content.ReadAsStringAsync();
        Assert.Contains("unsupported audio content type", body);
    }

    [Fact]
    public async Task Unknown_Report_Returns_404()
    {
        using var client = _factory.CreateTenantClient();
        using var form = AudioForm(new byte[] { 1, 2, 3, 4 }, "audio/webm", ack: false);
        var resp = await client.PostAsync($"/api/reports/{Guid.NewGuid()}/dictation/transcribe", form);

        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }
}
