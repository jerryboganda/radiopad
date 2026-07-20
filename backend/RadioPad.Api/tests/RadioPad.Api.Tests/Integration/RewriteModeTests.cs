using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using RadioPad.Domain.Entities;
using RadioPad.Domain.Enums;
using RadioPad.Infrastructure.Persistence;
using Xunit;

namespace RadioPad.Api.Tests.Integration;

/// <summary>
/// Iter-30 RPT-007 — verify the rewrite endpoint round-trips for all four
/// modes against the deterministic mock adapter, and that PHI policy is
/// still honoured when a request is routed to a non-compliant provider.
/// </summary>
public class RewriteModeTests : IClassFixture<RadioPadAppFactory>
{
    private readonly RadioPadAppFactory _factory;
    public RewriteModeTests(RadioPadAppFactory factory) => _factory = factory;

    private async Task<Guid> CreateReportAsync(HttpClient client)
    {
        var create = await client.PostAsJsonAsync("/api/reports", new
        {
            modality = "CT",
            bodyPart = "Chest",
            indication = "Cough",
            comparison = "None",
            accessionNumber = $"ACC-RW-{Guid.NewGuid():N}",
        });
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var doc = await JsonDocument.ParseAsync(await create.Content.ReadAsStreamAsync());
        var id = doc.RootElement.GetProperty("id").GetGuid();
        var patch = await client.PatchAsJsonAsync($"/api/reports/{id}", new
        {
            findings = "Lungs clear. No nodules.",
            impression = "1. No acute pulmonary findings.",
        });
        Assert.True(patch.IsSuccessStatusCode);
        return id;
    }

    [Theory]
    [InlineData("concise")]
    [InlineData("formal")]
    [InlineData("patient_friendly")]
    [InlineData("referring_summary")]
    public async Task Rewrite_All_Four_Modes_RoundTrip(string mode)
    {
        using var client = _factory.CreateTenantClient();
        var id = await CreateReportAsync(client);
        var resp = await client.PostAsJsonAsync($"/api/reports/{id}/rewrite", new { mode });
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var doc = await JsonDocument.ParseAsync(await resp.Content.ReadAsStreamAsync());
        Assert.False(string.IsNullOrWhiteSpace(doc.RootElement.GetProperty("text").GetString()));
        Assert.Equal("rewrite-v1.iter30", doc.RootElement.GetProperty("promptVersion").GetString());
    }

    /// <summary>
    /// PHI gating was removed by operator instruction (2026-07-20): a PHI-bearing report may be
    /// rewritten by ANY enabled provider, including a Sandbox-class one. This test previously
    /// asserted the opposite; it is kept — inverted — rather than deleted, so the change of
    /// policy is explicit in the suite instead of being a silently missing guarantee.
    /// </summary>
    [Fact]
    public async Task Rewrite_Sends_Phi_To_A_NonCompliant_Provider()
    {
        // Demote the mock adapter to a non-PHI (Sandbox) provider, then mark the report
        // as containing PHI by leaving an MRN-like token in the indication.
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RadioPadDbContext>();
            var prov = await db.Providers.FindAsync(_factory.MockProvider.Id);
            prov!.Compliance = ProviderComplianceClass.Sandbox;
            await db.SaveChangesAsync();
        }
        try
        {
            using var client = _factory.CreateTenantClient();
            var id = await CreateReportAsync(client);
            // Patch a PHI-shaped value into the indication.
            var patch = await client.PatchAsJsonAsync($"/api/reports/{id}", new
            {
                indication = "MRN 12345678 follow-up.",
            });
            Assert.True(patch.IsSuccessStatusCode);
            var resp = await client.PostAsJsonAsync($"/api/reports/{id}/rewrite", new { mode = "concise" });
            Assert.True(
                resp.IsSuccessStatusCode,
                $"PHI gating is removed, so a Sandbox provider must serve this; got {(int)resp.StatusCode}");

            // The routing is no longer prevented, but it MUST still be reconstructable: the
            // request is recorded with its PHI flag, which is what an auditor asking "did PHI
            // leave the approved boundary, and to whom?" has to work from.
            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<RadioPadDbContext>();
            var phiRuns = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions
                .CountAsync(db.AiRequests, a => a.ContainsPhi);
            Assert.True(phiRuns >= 1, "a PHI-bearing AI run must still be recorded in the usage ledger");
        }
        finally
        {
            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<RadioPadDbContext>();
            var prov = await db.Providers.FindAsync(_factory.MockProvider.Id);
            prov!.Compliance = ProviderComplianceClass.LocalOnly;
            await db.SaveChangesAsync();
        }
    }
}
