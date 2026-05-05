using System.Linq;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using RadioPad.Api.Tests.Integration;
using RadioPad.Domain.Enums;
using Xunit;

namespace RadioPad.Api.Tests.Iter34;

/// <summary>
/// Iter-34 PROV-009 — verifies that the operator-supplied
/// <c>retentionLabel</c> on a <c>ProviderConfig</c> is persisted by
/// <c>POST /api/providers</c> and round-tripped by
/// <c>GET /api/providers</c>. The label is informational; this test does
/// not assert anything about the PHI policy in <c>AiGateway</c>.
/// </summary>
public class Iter34ProviderRetentionTests : IClassFixture<RadioPadAppFactory>
{
    private readonly RadioPadAppFactory _factory;
    public Iter34ProviderRetentionTests(RadioPadAppFactory factory) => _factory = factory;

    [Fact]
    public async Task SaveProvider_PersistsRetentionLabel_RoundTripsOnList()
    {
        using var admin = _factory.CreateAdminClient();

        var name = "iter34-retention-" + Guid.NewGuid().ToString("N").Substring(0, 8);
        var save = await admin.PostAsJsonAsync("/api/providers", new
        {
            id = (Guid?)null,
            name,
            adapter = "mock",
            model = "echo",
            endpointUrl = "",
            apiKeySecretRef = "",
            compliance = (int)ProviderComplianceClass.LocalOnly,
            enabled = true,
            priority = 50,
            retentionLabel = "local-only-no-retention",
        });
        Assert.True(save.IsSuccessStatusCode, await save.Content.ReadAsStringAsync());

        using var listDoc = await JsonDocument.ParseAsync(
            await (await admin.GetAsync("/api/providers")).Content.ReadAsStreamAsync());

        var match = listDoc.RootElement.EnumerateArray()
            .FirstOrDefault(p => p.GetProperty("name").GetString() == name);
        Assert.NotEqual(JsonValueKind.Undefined, match.ValueKind);
        Assert.Equal("local-only-no-retention", match.GetProperty("retentionLabel").GetString());
    }

    [Fact]
    public async Task SaveProvider_DefaultsRetentionLabelToEmpty_WhenOmitted()
    {
        using var admin = _factory.CreateAdminClient();

        var name = "iter34-no-retention-" + Guid.NewGuid().ToString("N").Substring(0, 8);
        var save = await admin.PostAsJsonAsync("/api/providers", new
        {
            id = (Guid?)null,
            name,
            adapter = "mock",
            model = "echo",
            endpointUrl = "",
            apiKeySecretRef = "",
            compliance = (int)ProviderComplianceClass.Sandbox,
            enabled = true,
            priority = 50,
        });
        Assert.True(save.IsSuccessStatusCode, await save.Content.ReadAsStringAsync());

        using var listDoc = await JsonDocument.ParseAsync(
            await (await admin.GetAsync("/api/providers")).Content.ReadAsStreamAsync());

        var match = listDoc.RootElement.EnumerateArray()
            .FirstOrDefault(p => p.GetProperty("name").GetString() == name);
        Assert.NotEqual(JsonValueKind.Undefined, match.ValueKind);
        Assert.Equal(string.Empty, match.GetProperty("retentionLabel").GetString());
    }

    [Fact]
    public async Task SaveProvider_RejectsInlineApiKeySecretRef()
    {
        using var admin = _factory.CreateAdminClient();

        var save = await admin.PostAsJsonAsync("/api/providers", new
        {
            id = (Guid?)null,
            name = "inline-secret-" + Guid.NewGuid().ToString("N").Substring(0, 8),
            adapter = "openai",
            model = "gpt-4o-mini",
            endpointUrl = "https://api.openai.example/v1",
            apiKeySecretRef = "sk-not-allowed",
            compliance = (int)ProviderComplianceClass.Sandbox,
            enabled = true,
            priority = 50,
        });

        Assert.Equal(HttpStatusCode.BadRequest, save.StatusCode);
        var body = await save.Content.ReadAsStringAsync();
        Assert.Contains("env:", body);
    }
}
