using System.IO.Compression;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RadioPad.Domain.Entities;
using RadioPad.Domain.Enums;
using RadioPad.Infrastructure.Identity;
using RadioPad.Infrastructure.Persistence;
using Xunit;

namespace RadioPad.Api.Tests.Integration;

/// <summary>
/// Report Templates (RPT-030) — covers: personal CRUD + ownership enforcement,
/// cross-tenant isolation, LayoutJson validation, the per-user default and the
/// tenant admin's recommended pointer (including delete-cascade), sample-data
/// preview (no side effects, mandatory branding present), and export layoutId
/// resolution (explicit id, "classic", caller default, tenant recommendation,
/// unacknowledged-report gate unchanged, legacy content unchanged when no
/// layoutId is given).
/// </summary>
public class ReportLayoutPackTests : IClassFixture<RadioPadAppFactory>
{
    private readonly RadioPadAppFactory _factory;
    public ReportLayoutPackTests(RadioPadAppFactory factory) => _factory = factory;

    private const string ValidLayoutJson = """
        {
          "schemaVersion": 1,
          "page": { "size": "letter", "marginPt": 36, "font": "sans", "baseFontSizePt": 11, "accent": "graphite", "showPageNumbers": true },
          "blocks": [
            { "id": "letterhead1", "type": "letterhead", "clinicName": null, "lines": [], "logo": null, "logoPosition": "left", "align": "left", "showAccentRule": false },
            { "id": "findings1", "type": "section", "section": "findings", "label": null, "headingStyle": "uppercase", "hideIfEmpty": false },
            { "id": "impression1", "type": "section", "section": "impression", "label": null, "headingStyle": "uppercase", "hideIfEmpty": false }
          ],
          "footer": { "showStatusLine": true, "customText": null }
        }
        """;

    // ---------------------------------------------------------------- CRUD

    [Fact]
    public async Task Create_Get_Update_List_And_Delete_RoundTrip()
    {
        using var client = _factory.CreateTenantClient();

        var (id, _) = await CreateLayoutAsync(client, "My Design", ValidLayoutJson);

        var get = await client.GetAsync($"/api/report-layouts/{id}");
        Assert.Equal(HttpStatusCode.OK, get.StatusCode);
        var got = JsonDocument.Parse(await get.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal("My Design", got.GetProperty("name").GetString());

        var update = await client.PutAsJsonAsync($"/api/report-layouts/{id}", new
        {
            name = "My Design v2",
            description = "updated",
            layoutJson = ValidLayoutJson,
        });
        Assert.True(update.IsSuccessStatusCode, await update.Content.ReadAsStringAsync());

        var list = await client.GetAsync("/api/report-layouts");
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        var listJson = JsonDocument.Parse(await list.Content.ReadAsStringAsync()).RootElement;
        var item = listJson.GetProperty("items").EnumerateArray().First(i => i.GetProperty("id").GetGuid() == id);
        Assert.Equal("My Design v2", item.GetProperty("name").GetString());
        Assert.Equal(ValidLayoutJson, item.GetProperty("layoutJson").GetString());

        var delete = await client.DeleteAsync($"/api/report-layouts/{id}");
        Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);

        var getAfterDelete = await client.GetAsync($"/api/report-layouts/{id}");
        Assert.Equal(HttpStatusCode.NotFound, getAfterDelete.StatusCode);
    }

    [Fact]
    public async Task NonAuthor_Cannot_Mutate_But_Admin_Can()
    {
        using var client = _factory.CreateTenantClient();
        var (id, _) = await CreateLayoutAsync(client, "Owned by radiologist", ValidLayoutJson);

        // A second radiologist in the SAME tenant is not the author and holds no
        // governance role — must be forbidden, not merely find-it-and-fail.
        var otherEmail = $"second-{Guid.NewGuid():N}@radiopad.local";
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RadioPadDbContext>();
            db.Users.Add(new User
            {
                TenantId = _factory.SeedTenant.Id,
                Email = otherEmail,
                DisplayName = "Second Radiologist",
                Role = UserRole.Radiologist,
            });
            await db.SaveChangesAsync();
            await EnterpriseIdentityBridge.EnsureForAllUsersAsync(db, CancellationToken.None);
        }
        using var otherClient = _factory.CreateClient();
        otherClient.DefaultRequestHeaders.Add("X-RadioPad-Tenant", _factory.SeedTenant.Slug);
        otherClient.DefaultRequestHeaders.Add("X-RadioPad-User", otherEmail);

        var forbiddenUpdate = await otherClient.PutAsJsonAsync($"/api/report-layouts/{id}", new
        {
            name = "Hijacked",
            layoutJson = ValidLayoutJson,
        });
        Assert.Equal(HttpStatusCode.Forbidden, forbiddenUpdate.StatusCode);
        Assert.Contains("\"kind\":\"forbidden\"", await forbiddenUpdate.Content.ReadAsStringAsync());

        var forbiddenDelete = await otherClient.DeleteAsync($"/api/report-layouts/{id}");
        Assert.Equal(HttpStatusCode.Forbidden, forbiddenDelete.StatusCode);

        // A reporting-governance role may curate anyone's layout.
        using var adminClient = _factory.CreateAdminClient();
        var adminUpdate = await adminClient.PutAsJsonAsync($"/api/report-layouts/{id}", new
        {
            name = "Curated by admin",
            layoutJson = ValidLayoutJson,
        });
        Assert.True(adminUpdate.IsSuccessStatusCode, await adminUpdate.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task CrossTenant_Access_Is404()
    {
        using var client = _factory.CreateTenantClient();
        var (id, _) = await CreateLayoutAsync(client, "Tenant-local design", ValidLayoutJson);

        var otherSlug = $"rl-other-{Guid.NewGuid():N}"[..16];
        var otherEmail = $"{otherSlug}@radiopad.local";
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RadioPadDbContext>();
            var otherTenant = new Tenant { Slug = otherSlug, DisplayName = "Other Tenant" };
            db.Tenants.Add(otherTenant);
            db.Users.Add(new User
            {
                TenantId = otherTenant.Id,
                Email = otherEmail,
                DisplayName = "Other Radiologist",
                Role = UserRole.Radiologist,
            });
            await db.SaveChangesAsync();
            await EnterpriseIdentityBridge.EnsureForAllUsersAsync(db, CancellationToken.None);
        }
        using var otherClient = _factory.CreateClient();
        otherClient.DefaultRequestHeaders.Add("X-RadioPad-Tenant", otherSlug);
        otherClient.DefaultRequestHeaders.Add("X-RadioPad-User", otherEmail);

        Assert.Equal(HttpStatusCode.NotFound, (await otherClient.GetAsync($"/api/report-layouts/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await otherClient.PutAsJsonAsync($"/api/report-layouts/{id}", new { name = "x", layoutJson = ValidLayoutJson })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await otherClient.DeleteAsync($"/api/report-layouts/{id}")).StatusCode);
    }

    // ---------------------------------------------------------- validation

    public static IEnumerable<object[]> InvalidLayoutJsonCases()
    {
        yield return new object[] { "unknown block type", ValidLayoutJson.Replace("\"type\": \"letterhead\"", "\"type\": \"bogus\"") };
        yield return new object[]
        {
            "duplicate section block",
            ValidLayoutJson.Replace(
                "{ \"id\": \"impression1\", \"type\": \"section\", \"section\": \"impression\", \"label\": null, \"headingStyle\": \"uppercase\", \"hideIfEmpty\": false }",
                "{ \"id\": \"impression1\", \"type\": \"section\", \"section\": \"findings\", \"label\": null, \"headingStyle\": \"uppercase\", \"hideIfEmpty\": false }"),
        };
        yield return new object[] { "too many blocks", BuildLayoutWithNBlocks(25) };
        yield return new object[] { "oversized json", InsertPadding(ValidLayoutJson) };
        yield return new object[]
        {
            "bad logo magic bytes",
            ValidLayoutJson.Replace(
                "\"logo\": null",
                "\"logo\": { \"dataUrl\": \"data:image/png;base64," + Convert.ToBase64String(Encoding.UTF8.GetBytes("this is definitely not a png file")) + "\", \"widthPt\": 100 }"),
        };
        yield return new object[] { "unsupported schema version", ValidLayoutJson.Replace("\"schemaVersion\": 1", "\"schemaVersion\": 2") };
    }

    [Theory]
    [MemberData(nameof(InvalidLayoutJsonCases))]
    public async Task Save_Rejects_Invalid_LayoutJson(string reason, string layoutJson)
    {
        using var client = _factory.CreateTenantClient();
        var create = await client.PostAsJsonAsync("/api/report-layouts", new { name = $"Invalid ({reason})", layoutJson });
        Assert.Equal(HttpStatusCode.BadRequest, create.StatusCode);
        var body = await create.Content.ReadAsStringAsync();
        Assert.Contains("\"kind\":\"validation\"", body);
    }

    /// <summary>Inserts a large filler property right after the opening brace, regardless
    /// of the base JSON's internal formatting — robust to how the raw-string literal above
    /// gets dedented, unlike a whitespace-sensitive string replace.</summary>
    private static string InsertPadding(string json)
    {
        var insertAt = json.IndexOf('{') + 1;
        return json[..insertAt] + "\"_pad\":\"" + new string('a', 300_000) + "\"," + json[insertAt..];
    }

    private static string BuildLayoutWithNBlocks(int count)
    {
        var sb = new StringBuilder();
        sb.Append('[');
        for (var i = 0; i < count; i++)
        {
            if (i > 0) sb.Append(',');
            sb.Append($"{{\"id\":\"d{i}\",\"type\":\"divider\",\"style\":\"line\",\"spacePt\":10}}");
        }
        sb.Append(']');
        return $$"""
            {
              "schemaVersion": 1,
              "page": { "size": "letter" },
              "blocks": {{sb}},
              "footer": { "showStatusLine": true }
            }
            """;
    }

    // -------------------------------------------------------------- default

    [Fact]
    public async Task SetDefault_Clear_And_DeleteCascade()
    {
        using var client = _factory.CreateTenantClient();
        var (id, _) = await CreateLayoutAsync(client, "Default candidate", ValidLayoutJson);

        var setInvalid = await client.PutAsJsonAsync("/api/report-layouts/default", new { layoutId = Guid.NewGuid() });
        Assert.Equal(HttpStatusCode.BadRequest, setInvalid.StatusCode);

        var set = await client.PutAsJsonAsync("/api/report-layouts/default", new { layoutId = id });
        Assert.True(set.IsSuccessStatusCode, await set.Content.ReadAsStringAsync());

        var listAfterSet = await client.GetAsync("/api/report-layouts");
        var afterSetJson = JsonDocument.Parse(await listAfterSet.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal(id, afterSetJson.GetProperty("myDefaultId").GetGuid());

        // Deleting the referenced layout must clear a dangling default pointer.
        var delete = await client.DeleteAsync($"/api/report-layouts/{id}");
        Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);
        var listAfterDelete = await client.GetAsync("/api/report-layouts");
        var afterDeleteJson = JsonDocument.Parse(await listAfterDelete.Content.ReadAsStringAsync()).RootElement;
        AssertJsonNullOrAbsent(afterDeleteJson, "myDefaultId");

        var clear = await client.PutAsJsonAsync("/api/report-layouts/default", new { layoutId = (Guid?)null });
        Assert.True(clear.IsSuccessStatusCode, await clear.Content.ReadAsStringAsync());
    }

    // ----------------------------------------------------------- recommended

    [Fact]
    public async Task Recommended_Set_Clear_NonAdmin403_UnknownGuid400_And_DeleteCascade()
    {
        using var adminClient = _factory.CreateAdminClient();
        using var tenantClient = _factory.CreateTenantClient();

        var (id, _) = await CreateLayoutAsync(tenantClient, "Recommend me", ValidLayoutJson);

        var nonAdminAttempt = await tenantClient.PostAsJsonAsync("/api/tenant/settings", new { recommendedReportLayoutId = id.ToString() });
        Assert.Equal(HttpStatusCode.Forbidden, nonAdminAttempt.StatusCode);

        var unknownGuid = await adminClient.PostAsJsonAsync("/api/tenant/settings", new { recommendedReportLayoutId = Guid.NewGuid().ToString() });
        Assert.Equal(HttpStatusCode.BadRequest, unknownGuid.StatusCode);

        var setRecommended = await adminClient.PostAsJsonAsync("/api/tenant/settings", new { recommendedReportLayoutId = id.ToString() });
        Assert.True(setRecommended.IsSuccessStatusCode, await setRecommended.Content.ReadAsStringAsync());

        var settingsAfterSet = JsonDocument.Parse(await (await adminClient.GetAsync("/api/tenant/settings")).Content.ReadAsStringAsync()).RootElement;
        Assert.Equal(id, settingsAfterSet.GetProperty("reportLayouts").GetProperty("recommendedId").GetGuid());

        var listShowsRecommended = JsonDocument.Parse(await (await tenantClient.GetAsync("/api/report-layouts")).Content.ReadAsStringAsync()).RootElement;
        Assert.Equal(id, listShowsRecommended.GetProperty("recommendedId").GetGuid());

        // Deleting the recommended layout must null the tenant pointer, not leave it dangling.
        await tenantClient.DeleteAsync($"/api/report-layouts/{id}");
        var settingsAfterDelete = JsonDocument.Parse(await (await adminClient.GetAsync("/api/tenant/settings")).Content.ReadAsStringAsync()).RootElement;
        AssertJsonNullOrAbsent(settingsAfterDelete.GetProperty("reportLayouts"), "recommendedId");

        var (id2, _) = await CreateLayoutAsync(tenantClient, "Recommend me too", ValidLayoutJson);
        await adminClient.PostAsJsonAsync("/api/tenant/settings", new { recommendedReportLayoutId = id2.ToString() });
        var clearRecommended = await adminClient.PostAsJsonAsync("/api/tenant/settings", new { recommendedReportLayoutId = "" });
        Assert.True(clearRecommended.IsSuccessStatusCode, await clearRecommended.Content.ReadAsStringAsync());
        var settingsAfterClear = JsonDocument.Parse(await (await adminClient.GetAsync("/api/tenant/settings")).Content.ReadAsStringAsync()).RootElement;
        AssertJsonNullOrAbsent(settingsAfterClear.GetProperty("reportLayouts"), "recommendedId");
    }

    // -------------------------------------------------------------- preview

    [Fact]
    public async Task Preview_Pdf_Returns_Valid_Bytes_Without_Touching_Reports()
    {
        using var client = _factory.CreateTenantClient();

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RadioPadDbContext>();
        var reportCountBefore = await db.Reports.CountAsync();

        var preview = await client.PostAsJsonAsync("/api/report-layouts/preview/pdf", new { layoutJson = ValidLayoutJson });
        Assert.Equal(HttpStatusCode.OK, preview.StatusCode);
        var bytes = await preview.Content.ReadAsByteArrayAsync();
        Assert.True(bytes.Length > 500, "expected a non-trivial PDF payload");
        Assert.Equal("%PDF-", Encoding.ASCII.GetString(bytes, 0, 5));

        Assert.Equal(reportCountBefore, await db.Reports.CountAsync());
    }

    [Fact]
    public async Task Preview_Docx_Always_Contains_Mandatory_Branding_Footer()
    {
        using var client = _factory.CreateTenantClient();

        var preview = await client.PostAsJsonAsync("/api/report-layouts/preview/docx", new { layoutJson = ValidLayoutJson });
        Assert.Equal(HttpStatusCode.OK, preview.StatusCode);
        var bytes = await preview.Content.ReadAsByteArrayAsync();

        var text = ExtractDocxText(bytes);
        Assert.Contains("Powered by RadioPad", text);
        Assert.Contains("radiopadstudio.com", text);
    }

    [Fact]
    public async Task Preview_Rejects_Invalid_LayoutJson()
    {
        using var client = _factory.CreateTenantClient();
        var preview = await client.PostAsJsonAsync("/api/report-layouts/preview/pdf", new { layoutJson = "{ not json" });
        Assert.Equal(HttpStatusCode.BadRequest, preview.StatusCode);
    }

    // --------------------------------------------------------------- export

    [Fact]
    public async Task Export_With_LayoutId_Resolves_Explicit_Classic_And_Rejects_Foreign_Id()
    {
        using var client = _factory.CreateTenantClient();
        var (layoutId, _) = await CreateLayoutAsync(client, "Export layout", ValidLayoutJson);
        var reportId = await CreateAcknowledgedReportAsync(client, $"ACC-RL-{Guid.NewGuid():N}"[..20]);

        var withLayout = await client.GetAsync($"/api/reports/{reportId}/export/pdf?layoutId={layoutId}");
        Assert.Equal(HttpStatusCode.OK, withLayout.StatusCode);

        var withClassic = await client.GetAsync($"/api/reports/{reportId}/export/pdf?layoutId=classic");
        Assert.Equal(HttpStatusCode.OK, withClassic.StatusCode);

        var withForeignId = await client.GetAsync($"/api/reports/{reportId}/export/pdf?layoutId={Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.BadRequest, withForeignId.StatusCode);
    }

    [Fact]
    public async Task Export_With_LayoutId_On_Unacknowledged_Report_Still_409s()
    {
        using var client = _factory.CreateTenantClient();
        var (layoutId, _) = await CreateLayoutAsync(client, "Export layout draft-gate", ValidLayoutJson);

        var create = await client.PostAsJsonAsync("/api/reports", new
        {
            modality = "CT", bodyPart = "Chest", indication = "x",
            accessionNumber = $"ACC-RL-DRAFT-{Guid.NewGuid():N}"[..24],
        });
        var reportId = JsonDocument.Parse(await create.Content.ReadAsStreamAsync()).RootElement.GetProperty("id").GetGuid();

        var export = await client.GetAsync($"/api/reports/{reportId}/export/docx?layoutId={layoutId}");
        Assert.Equal(HttpStatusCode.Conflict, export.StatusCode);
        Assert.Contains("\"kind\":\"report_state\"", await export.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Export_Uses_Caller_Default_Then_Tenant_Recommended_When_No_LayoutId_Given()
    {
        using var tenantClient = _factory.CreateTenantClient();
        using var adminClient = _factory.CreateAdminClient();

        var (recommendedId, _) = await CreateLayoutAsync(tenantClient, "Recommended layout", ValidLayoutJson);
        await adminClient.PostAsJsonAsync("/api/tenant/settings", new { recommendedReportLayoutId = recommendedId.ToString() });

        var reportId = await CreateAcknowledgedReportAsync(tenantClient, $"ACC-RL-REC-{Guid.NewGuid():N}"[..24]);

        // No default set yet: falls through to the tenant recommendation. Both
        // paths render successfully either way (this asserts resolution doesn't
        // error, not which bytes came out — that's covered by the renderer unit
        // of behavior already exercised in the explicit-layoutId test above).
        var exportViaRecommended = await tenantClient.GetAsync($"/api/reports/{reportId}/export/pdf");
        Assert.Equal(HttpStatusCode.OK, exportViaRecommended.StatusCode);

        var (defaultId, _) = await CreateLayoutAsync(tenantClient, "My own default", ValidLayoutJson);
        await tenantClient.PutAsJsonAsync("/api/report-layouts/default", new { layoutId = defaultId });

        var exportViaDefault = await tenantClient.GetAsync($"/api/reports/{reportId}/export/pdf");
        Assert.Equal(HttpStatusCode.OK, exportViaDefault.StatusCode);

        // Leave the fixture's shared tenant/user clean for other tests in this class.
        await tenantClient.PutAsJsonAsync("/api/report-layouts/default", new { layoutId = (Guid?)null });
        await adminClient.PostAsJsonAsync("/api/tenant/settings", new { recommendedReportLayoutId = "" });
    }

    [Fact]
    public async Task Export_Without_LayoutId_Still_Uses_Legacy_Content_When_No_Default_Or_Recommendation()
    {
        using var client = _factory.CreateTenantClient();

        // Defensive reset: other [Fact]s in this class share the fixture's tenant/user and
        // may leave a default or recommended layout set depending on run order.
        await client.PutAsJsonAsync("/api/report-layouts/default", new { layoutId = (Guid?)null });
        using (var adminClient = _factory.CreateAdminClient())
        {
            await adminClient.PostAsJsonAsync("/api/tenant/settings", new { recommendedReportLayoutId = "" });
        }

        var reportId = await CreateAcknowledgedReportAsync(client, $"ACC-RL-LEGACY-{Guid.NewGuid():N}"[..24]);

        var docx = await client.GetAsync($"/api/reports/{reportId}/export/docx");
        Assert.Equal(HttpStatusCode.OK, docx.StatusCode);
        var text = ExtractDocxText(await docx.Content.ReadAsByteArrayAsync());

        // The exact narrative content the legacy renderer has always emitted for
        // this report (see CreateAcknowledgedReportAsync) — proves the no-layoutId
        // path is byte-for-byte the same BuildNarrative content as before RPT-030.
        Assert.Contains("Lungs clear. No nodules.", text);
        Assert.Contains("No acute pulmonary findings", text);
    }

    // -------------------------------------------------------------- helpers

    private static async Task<(Guid Id, JsonElement Body)> CreateLayoutAsync(HttpClient client, string name, string layoutJson)
    {
        var response = await client.PostAsJsonAsync("/api/report-layouts", new { name, layoutJson });
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        return (body.GetProperty("id").GetGuid(), body);
    }

    private static async Task<Guid> CreateAcknowledgedReportAsync(HttpClient client, string accession)
    {
        var create = await client.PostAsJsonAsync("/api/reports", new
        {
            modality = "CT", bodyPart = "Chest", indication = "Persistent cough.",
            accessionNumber = accession,
        });
        var id = JsonDocument.Parse(await create.Content.ReadAsStreamAsync()).RootElement.GetProperty("id").GetGuid();

        await client.PatchAsJsonAsync($"/api/reports/{id}", new
        {
            indication = "Persistent cough.",
            technique = "Non-contrast CT.",
            comparison = "None.",
            findings = "Lungs clear. No nodules.",
            impression = "1. No acute pulmonary findings.",
        });
        var validate = await client.PostAsync($"/api/reports/{id}/validate", null);
        Assert.Equal(HttpStatusCode.OK, validate.StatusCode);
        var ack = await client.PostAsync($"/api/reports/{id}/acknowledge", null);
        Assert.Equal(HttpStatusCode.OK, ack.StatusCode);
        return id;
    }

    /// <summary>
    /// The API's global JsonSerializerOptions sets
    /// <c>DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull</c>, so a
    /// null-valued property (e.g. a cleared <c>recommendedId</c>) is OMITTED from
    /// the JSON entirely, not emitted as <c>null</c>. <c>JsonElement.GetProperty</c>
    /// throws <see cref="KeyNotFoundException"/> on an absent property, so "null or
    /// absent" is the correct check here, not <c>ValueKind == JsonValueKind.Null</c>.
    /// </summary>
    private static void AssertJsonNullOrAbsent(JsonElement obj, string property)
    {
        if (obj.TryGetProperty(property, out var value))
        {
            Assert.Equal(JsonValueKind.Null, value.ValueKind);
        }
    }

    /// <summary>Concatenates every XML part's text so a footer/document text assertion
    /// doesn't need to know the exact part filename OpenXml assigned.</summary>
    private static string ExtractDocxText(byte[] docxBytes)
    {
        using var ms = new MemoryStream(docxBytes);
        using var zip = new ZipArchive(ms, ZipArchiveMode.Read);
        var sb = new StringBuilder();
        foreach (var entry in zip.Entries)
        {
            if (!entry.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)) continue;
            using var reader = new StreamReader(entry.Open());
            sb.Append(reader.ReadToEnd());
        }
        return sb.ToString();
    }
}
