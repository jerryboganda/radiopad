using System.Collections.Generic;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RadioPad.Api.Hubs;
using RadioPad.Domain.Entities;
using RadioPad.Domain.Enums;
using RadioPad.Infrastructure.Persistence;
using Xunit;

namespace RadioPad.Api.Tests.Integration;

/// <summary>
/// Mobile standalone reporting — <c>api/v1/reporting/reports</c> and its dictation
/// upload/transcribe/audio-stream sub-routes, exercised over the real HTTP pipeline via
/// <see cref="RadioPadAppFactory"/> (the same fixture every other tenant-scoped controller's
/// integration tests use). This controller previously had NO authentication or tenant
/// isolation at all — any unauthenticated caller could list every tenant's reports (including
/// patient name/age/gender) and upload audio against any report id. That is why every test
/// here goes through real tenant-header resolution instead of constructing the controller
/// directly with no HttpContext, and why tenant isolation + auth-required are covered
/// explicitly, not just the happy path.
/// </summary>
public class ReportingControllerTests : IClassFixture<RadioPadAppFactory>
{
    private readonly RadioPadAppFactory _factory;
    public ReportingControllerTests(RadioPadAppFactory factory) => _factory = factory;

    private static async Task<JsonElement> CreateReportAsync(
        HttpClient client, string radiologyId, string patientName, int age = 45, string gender = "Male",
        string? modality = "CT", string? bodyPart = "Chest")
    {
        var create = await client.PostAsJsonAsync("/api/v1/reporting/reports", new
        {
            radiologyId,
            patientName,
            patientAge = age,
            patientGender = gender,
            modality,
            bodyPart,
        });
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        using var doc = await JsonDocument.ParseAsync(await create.Content.ReadAsStreamAsync());
        return doc.RootElement.Clone();
    }

    [Fact]
    public async Task CreateReport_ReturnsCreatedAtAction_WithReportDto()
    {
        using var client = _factory.CreateTenantClient();
        var report = await CreateReportAsync(client, "RAD-2026-001", "John Doe");

        Assert.NotEqual(Guid.Empty, report.GetProperty("id").GetGuid());
        Assert.Equal("RAD-2026-001", report.GetProperty("radiologyId").GetString());
        Assert.Equal("John Doe", report.GetProperty("patientName").GetString());
        Assert.Equal(45, report.GetProperty("patientAge").GetInt32());
        Assert.Equal("Male", report.GetProperty("patientGender").GetString());
        Assert.Equal("Draft", report.GetProperty("status").GetString());
        Assert.Equal(0, report.GetProperty("dictations").GetArrayLength());
        // Modality/BodyPart must round-trip through Report.Study so template/rulebook
        // auto-resolution works for mobile-created reports too (Phase 2 requirement).
        Assert.Equal("CT", report.GetProperty("modality").GetString());
        Assert.Equal("Chest", report.GetProperty("bodyPart").GetString());
    }

    [Fact]
    public async Task CreateReport_PersistsTenantAndCreatedBy_SoDesktopWorklistCanSeeIt()
    {
        using var client = _factory.CreateTenantClient();
        var report = await CreateReportAsync(client, "RAD-TENANT-1", "Tenant Check");
        var id = report.GetProperty("id").GetGuid();

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RadioPadDbContext>();
        var stored = await db.Reports.FirstOrDefaultAsync(r => r.Id == id);

        Assert.NotNull(stored);
        // Previously TenantId/CreatedByUserId were never set on mobile-created reports, so
        // they were invisible to the desktop worklist's `WHERE TenantId == tenant.Id` filter —
        // the whole point of "push to desktop" was impossible. Guard that regression here.
        Assert.Equal(_factory.SeedTenant.Id, stored!.TenantId);
        Assert.Equal(_factory.SeedUser.Id, stored.CreatedByUserId);
    }

    [Fact]
    public async Task GetReports_ReturnsAllReports_AndFiltersBySearchAndStatus()
    {
        using var client = _factory.CreateTenantClient();
        await CreateReportAsync(client, "RAD-101", "Alice Smith", 30, "Female");
        await CreateReportAsync(client, "RAD-102", "Bob Johnson", 60, "Male");

        var all = await client.GetFromJsonAsync<JsonElement>("/api/v1/reporting/reports");
        Assert.True(all.GetArrayLength() >= 2);

        var search = await client.GetFromJsonAsync<JsonElement>("/api/v1/reporting/reports?search=Alice");
        Assert.Single(search.EnumerateArray());
        Assert.Equal("RAD-101", search[0].GetProperty("radiologyId").GetString());

        var byStatus = await client.GetFromJsonAsync<JsonElement>("/api/v1/reporting/reports?status=Draft");
        Assert.True(byStatus.GetArrayLength() >= 2);
    }

    [Fact]
    public async Task GetReportById_ReturnsOk_WhenReportExists()
    {
        using var client = _factory.CreateTenantClient();
        var created = await CreateReportAsync(client, "RAD-200", "Charlie Brown", 50, "Male");
        var id = created.GetProperty("id").GetGuid();

        var resp = await client.GetAsync($"/api/v1/reporting/reports/{id}");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        using var doc = await JsonDocument.ParseAsync(await resp.Content.ReadAsStreamAsync());
        Assert.Equal(id, doc.RootElement.GetProperty("id").GetGuid());
        Assert.Equal("RAD-200", doc.RootElement.GetProperty("radiologyId").GetString());
    }

    [Fact]
    public async Task GetReportById_ReturnsNotFound_WhenReportDoesNotExist()
    {
        using var client = _factory.CreateTenantClient();
        var resp = await client.GetAsync($"/api/v1/reporting/reports/{Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    [Fact]
    public async Task UploadDictation_ReturnsOkWithDictationAudioDto_WhenValid()
    {
        using var client = _factory.CreateTenantClient();
        var created = await CreateReportAsync(client, "RAD-300", "David Miller", 40, "Male");
        var id = created.GetProperty("id").GetGuid();

        using var form = new MultipartFormDataContent();
        var bytes = System.Text.Encoding.UTF8.GetBytes("fake audio content");
        var fileContent = new ByteArrayContent(bytes);
        fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("audio/wav");
        form.Add(fileContent, "file", "test_dictation.wav");
        form.Add(new StringContent("15.5"), "durationSeconds");

        var resp = await client.PostAsync($"/api/v1/reporting/reports/{id}/dictations", form);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        using var doc = await JsonDocument.ParseAsync(await resp.Content.ReadAsStreamAsync());
        var dictation = doc.RootElement;
        Assert.Equal(id, dictation.GetProperty("reportId").GetGuid());
        Assert.Equal(15.5, dictation.GetProperty("durationSeconds").GetDouble());
        Assert.Equal("medASR-6gram", dictation.GetProperty("transcriptionEngine").GetString());

        var storagePath = dictation.GetProperty("storagePath").GetString();
        Assert.True(File.Exists(storagePath));
        if (storagePath is not null && File.Exists(storagePath)) File.Delete(storagePath);
    }

    [Fact]
    public async Task UploadDictation_ReturnsBadRequest_WhenNoAudioFilePart()
    {
        using var client = _factory.CreateTenantClient();
        var created = await CreateReportAsync(client, "RAD-400", "Eva Green", 35, "Female");
        var id = created.GetProperty("id").GetGuid();

        using var form = new MultipartFormDataContent { { new StringContent("5.0"), "durationSeconds" } };
        var resp = await client.PostAsync($"/api/v1/reporting/reports/{id}/dictations", form);

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task UploadDictation_ReturnsNotFound_WhenReportDoesNotExist()
    {
        using var client = _factory.CreateTenantClient();
        using var form = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(System.Text.Encoding.UTF8.GetBytes("fake audio content"));
        form.Add(fileContent, "file", "test_dictation.wav");
        form.Add(new StringContent("5.0"), "durationSeconds");

        var resp = await client.PostAsync($"/api/v1/reporting/reports/{Guid.NewGuid()}/dictations", form);
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    [Fact]
    public async Task GetTranscriptionEngines_ReturnsMedAsrAsDefault()
    {
        using var client = _factory.CreateTenantClient();
        var engines = await client.GetFromJsonAsync<JsonElement>("/api/v1/reporting/reports/transcription-engines");

        Assert.True(engines.GetArrayLength() >= 1);
        var medAsr = engines.EnumerateArray()
            .First(e => e.GetProperty("engineId").GetString() == "medASR-6gram");
        Assert.True(medAsr.GetProperty("isDefault").GetBoolean());
        Assert.True(medAsr.GetProperty("isLocal").GetBoolean());
    }

    [Fact]
    public async Task TranscribeDictation_ReturnsNotFound_ForUnknownDictationId()
    {
        using var client = _factory.CreateTenantClient();
        var created = await CreateReportAsync(client, "RAD-TX-1", "Transcribe Test");
        var id = created.GetProperty("id").GetGuid();

        var resp = await client.PostAsJsonAsync(
            $"/api/v1/reporting/reports/{id}/dictations/{Guid.NewGuid()}/transcribe", new { engine = (string?)null });
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    [Fact]
    public async Task TranscribeDictation_ReturnsNotFound_WhenDictationBelongsToDifferentReport()
    {
        using var client = _factory.CreateTenantClient();
        var reportA = await CreateReportAsync(client, "RAD-TX-A", "Report A");
        var reportB = await CreateReportAsync(client, "RAD-TX-B", "Report B");
        var idA = reportA.GetProperty("id").GetGuid();
        var idB = reportB.GetProperty("id").GetGuid();

        using var form = new MultipartFormDataContent();
        form.Add(new ByteArrayContent(System.Text.Encoding.UTF8.GetBytes("audio")), "file", "d.wav");
        form.Add(new StringContent("3.0"), "durationSeconds");
        var upload = await client.PostAsync($"/api/v1/reporting/reports/{idA}/dictations", form);
        using var uploadDoc = await JsonDocument.ParseAsync(await upload.Content.ReadAsStreamAsync());
        var dictationId = uploadDoc.RootElement.GetProperty("id").GetGuid();

        // dictationId belongs to report A — asking to transcribe it via report B's route
        // must 404, not silently operate on the wrong report's dictation.
        var resp = await client.PostAsJsonAsync(
            $"/api/v1/reporting/reports/{idB}/dictations/{dictationId}/transcribe", new { engine = (string?)null });
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);

        var storagePath = uploadDoc.RootElement.GetProperty("storagePath").GetString();
        if (storagePath is not null && File.Exists(storagePath)) File.Delete(storagePath);
    }

    [Fact]
    public async Task GetDictationAudio_StreamsStoredBytes_WhenOwnedByTenant()
    {
        using var client = _factory.CreateTenantClient();
        var created = await CreateReportAsync(client, "RAD-AUDIO-1", "Audio Test");
        var id = created.GetProperty("id").GetGuid();

        using var form = new MultipartFormDataContent();
        var audioBytes = System.Text.Encoding.UTF8.GetBytes("fake wav bytes");
        var fileContent = new ByteArrayContent(audioBytes);
        fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("audio/wav");
        form.Add(fileContent, "file", "d.wav");
        form.Add(new StringContent("2.0"), "durationSeconds");
        var upload = await client.PostAsync($"/api/v1/reporting/reports/{id}/dictations", form);
        using var uploadDoc = await JsonDocument.ParseAsync(await upload.Content.ReadAsStreamAsync());
        var dictationId = uploadDoc.RootElement.GetProperty("id").GetGuid();
        var storagePath = uploadDoc.RootElement.GetProperty("storagePath").GetString();

        var resp = await client.GetAsync($"/api/v1/reporting/reports/{id}/dictations/{dictationId}/audio");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        Assert.Equal("audio/wav", resp.Content.Headers.ContentType?.MediaType);
        var returnedBytes = await resp.Content.ReadAsByteArrayAsync();
        Assert.Equal(audioBytes, returnedBytes);

        // Best-effort cleanup only: TestServer's FileStreamResult disposes its FileStream
        // asynchronously after the response is flushed, so an immediate File.Delete can race
        // a not-yet-released read handle (FileShare.Read blocks delete, not further reads).
        // That race is cleanup noise, not a product defect — swallow it.
        try
        {
            if (storagePath is not null && File.Exists(storagePath)) File.Delete(storagePath);
        }
        catch (IOException)
        {
        }
    }

    [Fact]
    public async Task GetDictationAudio_ReturnsNotFound_ForUnknownDictation()
    {
        using var client = _factory.CreateTenantClient();
        var created = await CreateReportAsync(client, "RAD-AUDIO-404", "Audio 404 Test");
        var id = created.GetProperty("id").GetGuid();

        var resp = await client.GetAsync($"/api/v1/reporting/reports/{id}/dictations/{Guid.NewGuid()}/audio");
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    // ---- Auth / tenant isolation --------------------------------------------------------
    // These are the "live PHI-exposure bug" tests: before this fix, ReportingController had
    // no TenantedController base and no permission checks at all.

    [Fact]
    public async Task NoAuthContext_ReturnsUnauthorized()
    {
        // Unknown tenant slug + no dev-header identity resolves to nothing — mirrors the
        // established "foreign tenant" pattern used by ReportsFlowTests for the sibling
        // ReportsController.
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-RadioPad-Tenant", "foreign-unknown-tenant");
        client.DefaultRequestHeaders.Add("X-RadioPad-User", "nobody@nowhere.invalid");

        var resp = await client.GetAsync("/api/v1/reporting/reports");
        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    [Fact]
    public async Task RoleWithoutReportsPermission_IsForbidden()
    {
        // BillingAdmin holds no Reports* permission in the RBAC matrix.
        using var client = _factory.CreateBillingAdminClient();
        var resp = await client.GetAsync("/api/v1/reporting/reports");
        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
    }

    [Fact]
    public async Task OtherTenant_CannotListOrReadFirstTenantsReports()
    {
        using var ownerClient = _factory.CreateTenantClient();
        var created = await CreateReportAsync(ownerClient, "RAD-ISO-1", "Isolation Subject");
        var reportId = created.GetProperty("id").GetGuid();

        using var otherClient = await CreateSecondTenantClientAsync();

        // Listing must never leak another tenant's report into the results.
        var list = await otherClient.GetFromJsonAsync<JsonElement>("/api/v1/reporting/reports");
        Assert.DoesNotContain(list.EnumerateArray(), r => r.GetProperty("id").GetGuid() == reportId);

        // Direct-by-id must look exactly like "does not exist" — never a distinguishable
        // "forbidden" that would leak that a report with that id exists elsewhere.
        var getResp = await otherClient.GetAsync($"/api/v1/reporting/reports/{reportId}");
        Assert.Equal(HttpStatusCode.NotFound, getResp.StatusCode);
    }

    [Fact]
    public async Task OtherTenant_CannotUploadDictationAgainstFirstTenantsReport()
    {
        using var ownerClient = _factory.CreateTenantClient();
        var created = await CreateReportAsync(ownerClient, "RAD-ISO-2", "Isolation Upload Subject");
        var reportId = created.GetProperty("id").GetGuid();

        using var otherClient = await CreateSecondTenantClientAsync();
        using var form = new MultipartFormDataContent();
        form.Add(new ByteArrayContent(System.Text.Encoding.UTF8.GetBytes("audio")), "file", "d.wav");
        form.Add(new StringContent("1.0"), "durationSeconds");

        var resp = await otherClient.PostAsync($"/api/v1/reporting/reports/{reportId}/dictations", form);
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    /// <summary>
    /// Seeds a genuinely separate tenant + radiologist user directly (the shared
    /// <see cref="RadioPadAppFactory"/> only seeds one tenant, "it") and returns a dev-header
    /// client authenticated as that new tenant/user, for tests that must prove cross-tenant
    /// isolation rather than just "no header at all".
    /// </summary>
    private async Task<HttpClient> CreateSecondTenantClientAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RadioPadDbContext>();

        var tenant = new Tenant { Slug = $"it2-{Guid.NewGuid():N}"[..12], DisplayName = "Integration Tenant 2" };
        db.Tenants.Add(tenant);
        var user = new User
        {
            TenantId = tenant.Id,
            Email = $"other-{Guid.NewGuid():N}@radiopad.local",
            DisplayName = "Other Tenant Radiologist",
            Role = UserRole.Radiologist,
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-RadioPad-Tenant", tenant.Slug);
        client.DefaultRequestHeaders.Add("X-RadioPad-User", user.Email);
        return client;
    }
}

/// <summary>
/// <see cref="ReportingHub"/> group join/leave is a trivial pass-through to
/// <see cref="IGroupManager"/> with no tenant/DB/HTTP involvement, so it stays on lightweight
/// hand-rolled test doubles rather than the full HTTP fixture used above.
/// </summary>
public class ReportingHubTests
{
    [Fact]
    public async Task ReportingHub_JoinAndLeaveGroup_Succeeds()
    {
        var hub = new ReportingHub();
        var mockContext = new TestHubCallerContext();
        hub.Context = mockContext;
        var mockGroups = new TestGroupManager();
        hub.Groups = mockGroups;

        await hub.JoinReportGroup("123");
        await hub.LeaveReportGroup("123");
    }

    private class TestGroupManager : IGroupManager
    {
        public Task AddToGroupAsync(string connectionId, string groupName, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task RemoveFromGroupAsync(string connectionId, string groupName, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private class TestHubCallerContext : HubCallerContext
    {
        public override string ConnectionId => "test-conn-id";
        public override string? UserIdentifier => "test-user-id";
        public override System.Security.Claims.ClaimsPrincipal User => new System.Security.Claims.ClaimsPrincipal();
        public override IDictionary<object, object?> Items { get; } = new Dictionary<object, object?>();
        public override IFeatureCollection Features => new TestFeatureCollection();
        public override CancellationToken ConnectionAborted => CancellationToken.None;
        public override void Abort() { }
    }

    private class TestFeatureCollection : IFeatureCollection
    {
        public bool IsReadOnly => false;
        public int Revision => 0;
        public object? this[Type key] { get => null; set { } }
        public TFeature? Get<TFeature>() => default;
        public void Set<TFeature>(TFeature? instance) { }
        public IEnumerator<KeyValuePair<Type, object>> GetEnumerator() => new List<KeyValuePair<Type, object>>().GetEnumerator();
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
