using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using RadioPad.Domain.Enums;
using RadioPad.Infrastructure.Persistence;
using Xunit;

namespace RadioPad.Api.Tests.Integration;

/// <summary>
/// Verifies PHI routing end-to-end through the HTTP pipeline.
///
/// <para><b>Policy changed 2026-07-20 by operator instruction:</b> PHI gating was removed, so a
/// sandbox-class provider now ACCEPTS a request whose text contains PHI-shaped tokens. This test
/// was inverted rather than deleted — the suite should state the policy that exists, and the
/// audit trail it still depends on.</para>
/// </summary>
public class AiPolicyHttpTests : IClassFixture<RadioPadAppFactory>
{
    private readonly RadioPadAppFactory _factory;
    public AiPolicyHttpTests(RadioPadAppFactory factory) => _factory = factory;

    [Fact]
    public async Task Sandbox_Provider_Accepts_Phi_Bearing_Request()
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

        // 3. Ask the sandbox provider to draft an impression — served, not refused.
        var ai = await client.PostAsJsonAsync($"/api/reports/{reportId}/ai", new
        {
            mode = "impression",
            providerId,
        });

        Assert.True(
            ai.IsSuccessStatusCode,
            $"PHI gating is removed, so a sandbox provider must serve a PHI-bearing request; got {(int)ai.StatusCode}");

        // 4. Nothing blocks the routing any more, so the ledger is the ONLY remaining record
        //    that PHI went to a non-approved provider. If this ever stops being written, PHI
        //    routing becomes both unrestricted and invisible — which is the state to avoid.
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RadioPadDbContext>();
        var phiRun = db.AiRequests.Any(a => a.ContainsPhi);
        Assert.True(phiRun, "a PHI-bearing AI run must still be recorded with its PHI flag.");
    }
}
