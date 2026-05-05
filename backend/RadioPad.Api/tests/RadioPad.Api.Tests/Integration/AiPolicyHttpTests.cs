using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using RadioPad.Domain.Enums;
using RadioPad.Infrastructure.Persistence;
using Xunit;

namespace RadioPad.Api.Tests.Integration;

/// <summary>
/// Verifies the PHI policy is enforced end-to-end through the HTTP pipeline:
/// a sandbox-class provider must reject a request whose dictation contains
/// PHI-shaped tokens (e.g. MRN), and the failure must be audited.
/// </summary>
public class AiPolicyHttpTests : IClassFixture<RadioPadAppFactory>
{
    private readonly RadioPadAppFactory _factory;
    public AiPolicyHttpTests(RadioPadAppFactory factory) => _factory = factory;

    [Fact]
    public async Task Sandbox_Provider_Rejects_Phi_Bearing_Request()
    {
        using var client = _factory.CreateTenantClient();
        using var adminClient = _factory.CreateAdminClient();

        // 1. Create a sandbox-class provider via the public API (admin only).
        var providerResp = await adminClient.PostAsJsonAsync("/api/providers", new
        {
            id = (Guid?)null,
            name = "Sandbox Echo",
            adapter = "mock",
            model = "echo",
            endpointUrl = "",
            apiKeySecretRef = "",
            compliance = (int)ProviderComplianceClass.Sandbox,
            enabled = true,
            priority = 50,
        });
        Assert.True(providerResp.IsSuccessStatusCode, await providerResp.Content.ReadAsStringAsync());
        using var providerDoc = await JsonDocument.ParseAsync(await providerResp.Content.ReadAsStreamAsync());
        var providerId = providerDoc.RootElement.GetProperty("id").GetGuid();

        // 2. Create a report and inject PHI-shaped text into the dictation.
        var create = await client.PostAsJsonAsync("/api/reports", new
        {
            modality = "CT",
            bodyPart = "Chest",
            indication = "MRN: 123456 — left chest pain",
            accessionNumber = "ACC-PHI-1",
        });
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        using var reportDoc = await JsonDocument.ParseAsync(await create.Content.ReadAsStreamAsync());
        var reportId = reportDoc.RootElement.GetProperty("id").GetGuid();

        await client.PatchAsJsonAsync($"/api/reports/{reportId}", new
        {
            findings = "Patient MRN: 123456 with right lower lobe consolidation.",
            impression = "",
        });

        // 3. Ask the sandbox provider to draft an impression — must be refused.
        var ai = await client.PostAsJsonAsync($"/api/reports/{reportId}/ai", new
        {
            mode = "impression",
            providerId,
        });

        Assert.False(ai.IsSuccessStatusCode);
        // Controller returns 403; the global handler maps unhandled propagation
        // to 409. Either is acceptable proof that policy is enforced.
        Assert.True(
            ai.StatusCode == HttpStatusCode.Forbidden || ai.StatusCode == HttpStatusCode.Conflict,
            $"Expected 403 or 409, got {(int)ai.StatusCode}");

        // 4. Audit chain must record the policy block.
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RadioPadDbContext>();
        var hasBlock = db.AuditEvents.Any(a => a.Action == AuditAction.ProviderBlocked || a.Action == AuditAction.PolicyViolation);
        Assert.True(hasBlock, "Expected an audit event recording the policy block.");
    }
}
