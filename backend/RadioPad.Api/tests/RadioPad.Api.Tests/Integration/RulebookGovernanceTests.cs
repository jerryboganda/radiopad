using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using RadioPad.Domain.Enums;
using RadioPad.Infrastructure.Persistence;
using Xunit;

namespace RadioPad.Api.Tests.Integration;

public class RulebookGovernanceTests : IClassFixture<RadioPadAppFactory>
{
    private const string SampleYaml = """
rulebook_id: integration_test_v1
name: Integration Test Rulebook
version: 0.1.0
owner: Integration Tests
status: draft
applies_to:
  modalities: [CT]
  body_parts: [Chest]
  report_types: [diagnostic]
style:
  tone: concise_clinical
  impression_max_bullets: 3
  avoid_terms: [unremarkable]
required_sections: [Indication, Findings, Impression]
rules:
  - id: laterality_consistency
    severity: blocker
    description: laterality
prompt_blocks:
  system: x
  findings_to_impression: y
  cleanup: z
""";

    private readonly RadioPadAppFactory _factory;
    public RulebookGovernanceTests(RadioPadAppFactory factory) => _factory = factory;

    [Fact]
    public async Task Validate_Yaml_Returns_Ok_For_Wellformed_Spec()
    {
        using var client = _factory.CreateTenantClient();
        var resp = await client.PostAsJsonAsync("/api/rulebooks/validate", new { yaml = SampleYaml });
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        using var doc = await JsonDocument.ParseAsync(await resp.Content.ReadAsStreamAsync());
        Assert.True(doc.RootElement.GetProperty("ok").GetBoolean());
    }

    [Fact]
    public async Task Save_Then_Approve_Then_Deprecate_Writes_Audit_Events()
    {
        using var client = _factory.CreateAdminClient();
        var save = await client.PostAsJsonAsync("/api/rulebooks", new { yaml = SampleYaml });
        Assert.True(save.IsSuccessStatusCode, await save.Content.ReadAsStringAsync());
        using var saved = await JsonDocument.ParseAsync(await save.Content.ReadAsStreamAsync());
        var id = saved.RootElement.GetProperty("id").GetGuid();

        var approve = await client.PostAsync($"/api/rulebooks/{id}/approve", null);
        Assert.Equal(HttpStatusCode.OK, approve.StatusCode);

        var deprecate = await client.PostAsync($"/api/rulebooks/{id}/deprecate", null);
        Assert.Equal(HttpStatusCode.OK, deprecate.StatusCode);

        // Verify audit chain wrote entries with the right actions.
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RadioPadDbContext>();
        var actions = db.AuditEvents.OrderBy(a => a.CreatedAt)
            .Select(a => a.Action)
            .ToList();
        Assert.Contains(AuditAction.RulebookApproved, actions);
        Assert.Contains(AuditAction.RulebookDeprecated, actions);
    }
}
