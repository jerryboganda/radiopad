using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RadioPad.Domain.Entities;
using RadioPad.Domain.Enums;
using RadioPad.Infrastructure.Persistence;
using Xunit;

namespace RadioPad.Api.Tests.Integration;

/// <summary>
/// Manual binding overrides — a caller-selected template/rulebook pins the
/// binding so study-context changes never auto-rebind over it; clearing the
/// pin (reset-to-auto) re-resolves from the selection key; bindings lock once
/// a Primary signature exists.
/// </summary>
public class BindingPinTests : IClassFixture<RadioPadAppFactory>
{
    private readonly RadioPadAppFactory _factory;
    public BindingPinTests(RadioPadAppFactory factory) => _factory = factory;

    /// <summary>
    /// Seeds two Approved template/rulebook pairs keyed on synthetic body parts
    /// (unique per test run) so bundled content can never interfere.
    /// </summary>
    private async Task<(string partA, string partB, Guid tplA, Guid tplB, Guid rbA, Guid rbB)> SeedPairsAsync()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var partA = $"PinPartA{suffix}";
        var partB = $"PinPartB{suffix}";
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RadioPadDbContext>();
        var tplA = new ReportTemplate
        {
            TenantId = _factory.SeedTenant.Id,
            TemplateId = $"pin-a-{suffix}",
            Name = $"Pin A {suffix}",
            Modality = "CT",
            BodyPart = partA,
            Status = TemplateStatus.Approved,
        };
        var tplB = new ReportTemplate
        {
            TenantId = _factory.SeedTenant.Id,
            TemplateId = $"pin-b-{suffix}",
            Name = $"Pin B {suffix}",
            Modality = "CT",
            BodyPart = partB,
            Status = TemplateStatus.Approved,
        };
        var rbA = new Rulebook
        {
            TenantId = _factory.SeedTenant.Id,
            RulebookId = $"pin-rb-a-{suffix}",
            Name = $"Pin RB A {suffix}",
            Version = "1.0.0",
            AppliesToModalities = "CT",
            AppliesToBodyParts = partA,
            Status = RulebookStatus.Approved,
        };
        var rbB = new Rulebook
        {
            TenantId = _factory.SeedTenant.Id,
            RulebookId = $"pin-rb-b-{suffix}",
            Name = $"Pin RB B {suffix}",
            Version = "1.0.0",
            AppliesToModalities = "CT",
            AppliesToBodyParts = partB,
            Status = RulebookStatus.Approved,
        };
        db.AddRange(tplA, tplB, rbA, rbB);
        await db.SaveChangesAsync();
        return (partA, partB, tplA.Id, tplB.Id, rbA.Id, rbB.Id);
    }

    private static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage res) =>
        await JsonDocument.ParseAsync(await res.Content.ReadAsStreamAsync());

    private async Task<Guid> CreateReportAsync(HttpClient client, string bodyPart)
    {
        var create = await client.PostAsJsonAsync("/api/reports", new
        {
            modality = "CT",
            bodyPart,
            indication = "Pin test",
            accessionNumber = $"ACC-PIN-{Guid.NewGuid():N}"[..20],
        });
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        using var doc = await ReadJsonAsync(create);
        return doc.RootElement.GetProperty("id").GetGuid();
    }

    [Fact]
    public async Task ManualTemplate_Pins_And_Survives_ContextChange_Until_Reset()
    {
        var (partA, partB, tplA, tplB, _, _) = await SeedPairsAsync();
        using var client = _factory.CreateTenantClient();
        var id = await CreateReportAsync(client, partA);

        // Auto-bound to A, unpinned.
        var get = await ReadJsonAsync(await client.GetAsync($"/api/reports/{id}"));
        Assert.Equal(tplA, get.RootElement.GetProperty("templateId").GetGuid());
        Assert.False(get.RootElement.GetProperty("templatePinned").GetBoolean());

        // Manual override to B while the context still says part A → pinned.
        var pin = await client.PatchAsJsonAsync($"/api/reports/{id}", new { templateId = tplB });
        Assert.True(pin.IsSuccessStatusCode);
        using (var doc = await ReadJsonAsync(pin))
        {
            Assert.Equal(tplB, doc.RootElement.GetProperty("templateId").GetGuid());
            Assert.True(doc.RootElement.GetProperty("templatePinned").GetBoolean());
        }

        // Selection-key change must NOT rebind the pinned template.
        var ctx = await client.PatchAsJsonAsync($"/api/reports/{id}", new { contrast = "With" });
        Assert.True(ctx.IsSuccessStatusCode);
        using (var doc = await ReadJsonAsync(ctx))
        {
            Assert.Equal(tplB, doc.RootElement.GetProperty("templateId").GetGuid());
            Assert.True(doc.RootElement.GetProperty("templatePinned").GetBoolean());
        }

        // Reset-to-auto clears the pin and re-resolves from the selection key.
        var reset = await client.PatchAsJsonAsync($"/api/reports/{id}", new { templatePinned = false });
        Assert.True(reset.IsSuccessStatusCode);
        using (var doc = await ReadJsonAsync(reset))
        {
            Assert.Equal(tplA, doc.RootElement.GetProperty("templateId").GetGuid());
            Assert.False(doc.RootElement.GetProperty("templatePinned").GetBoolean());
        }
    }

    [Fact]
    public async Task ManualRulebook_Pins_Independently_Of_Template_Rebind()
    {
        var (partA, partB, tplA, tplB, rbA, rbB) = await SeedPairsAsync();
        using var client = _factory.CreateTenantClient();
        var id = await CreateReportAsync(client, partA);

        // Pin the off-context rulebook B while the study still says part A.
        var pin = await client.PatchAsJsonAsync($"/api/reports/{id}", new { rulebookId = rbB });
        Assert.True(pin.IsSuccessStatusCode);
        using (var doc = await ReadJsonAsync(pin))
        {
            Assert.True(doc.RootElement.GetProperty("rulebookPinned").GetBoolean());
        }

        // Context change: template rebinds A→B, pinned rulebook stays B.
        var ctx = await client.PatchAsJsonAsync($"/api/reports/{id}", new { bodyPart = partB });
        Assert.True(ctx.IsSuccessStatusCode);
        using (var doc = await ReadJsonAsync(ctx))
        {
            Assert.Equal(tplB, doc.RootElement.GetProperty("templateId").GetGuid());
            Assert.Equal(rbB, doc.RootElement.GetProperty("rulebookId").GetGuid());
            Assert.True(doc.RootElement.GetProperty("rulebookPinned").GetBoolean());
            Assert.False(doc.RootElement.GetProperty("templatePinned").GetBoolean());
        }

        // Move context back to part A, then reset the rulebook pin → re-resolves to A.
        var back = await client.PatchAsJsonAsync($"/api/reports/{id}", new { bodyPart = partA });
        Assert.True(back.IsSuccessStatusCode);
        var reset = await client.PatchAsJsonAsync($"/api/reports/{id}", new { rulebookPinned = false });
        Assert.True(reset.IsSuccessStatusCode);
        using (var doc = await ReadJsonAsync(reset))
        {
            Assert.Equal(rbA, doc.RootElement.GetProperty("rulebookId").GetGuid());
            Assert.False(doc.RootElement.GetProperty("rulebookPinned").GetBoolean());
        }
    }

    [Fact]
    public async Task Create_With_Explicit_Bindings_Pins_Them()
    {
        var (partA, _, _, tplB, _, rbB) = await SeedPairsAsync();
        using var client = _factory.CreateTenantClient();
        var create = await client.PostAsJsonAsync("/api/reports", new
        {
            modality = "CT",
            bodyPart = partA,
            templateId = tplB,
            rulebookId = rbB,
            accessionNumber = $"ACC-PIN-{Guid.NewGuid():N}"[..20],
        });
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        using var doc = await ReadJsonAsync(create);
        Assert.Equal(tplB, doc.RootElement.GetProperty("templateId").GetGuid());
        Assert.Equal(rbB, doc.RootElement.GetProperty("rulebookId").GetGuid());
        Assert.True(doc.RootElement.GetProperty("templatePinned").GetBoolean());
        Assert.True(doc.RootElement.GetProperty("rulebookPinned").GetBoolean());
    }

    [Fact]
    public async Task Patch_Rejects_Unknown_Or_NonApproved_Bindings()
    {
        var (partA, _, _, _, _, _) = await SeedPairsAsync();
        using var client = _factory.CreateTenantClient();
        var id = await CreateReportAsync(client, partA);

        // Unknown ids → 400 validation.
        var badTpl = await client.PatchAsJsonAsync($"/api/reports/{id}", new { templateId = Guid.NewGuid() });
        Assert.Equal(HttpStatusCode.BadRequest, badTpl.StatusCode);
        var badRb = await client.PatchAsJsonAsync($"/api/reports/{id}", new { rulebookId = Guid.NewGuid() });
        Assert.Equal(HttpStatusCode.BadRequest, badRb.StatusCode);

        // Draft (non-Approved) template in a tenant without sandbox access → 400.
        Guid draftTplId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RadioPadDbContext>();
            var draft = new ReportTemplate
            {
                TenantId = _factory.SeedTenant.Id,
                TemplateId = $"pin-draft-{Guid.NewGuid():N}"[..20],
                Name = "Draft pin target",
                Modality = "CT",
                BodyPart = partA,
                Status = TemplateStatus.Draft,
            };
            db.Add(draft);
            await db.SaveChangesAsync();
            draftTplId = draft.Id;
        }
        var draftRes = await client.PatchAsJsonAsync($"/api/reports/{id}", new { templateId = draftTplId });
        Assert.Equal(HttpStatusCode.BadRequest, draftRes.StatusCode);
    }

    [Fact]
    public async Task Bindings_Lock_After_Primary_Signature()
    {
        var (partA, _, _, tplB, _, _) = await SeedPairsAsync();
        using var client = _factory.CreateTenantClient();
        var id = await CreateReportAsync(client, partA);
        var populate = await client.PatchAsJsonAsync($"/api/reports/{id}", new
        {
            findings = "Findings for signing.",
            impression = "1. Impression for signing.",
        });
        Assert.True(populate.IsSuccessStatusCode);

        var sign = await client.PostAsJsonAsync($"/api/reports/{id}/sign", new { role = "Primary" });
        Assert.Equal(HttpStatusCode.OK, sign.StatusCode);

        // Binding + pin mutations are rejected once a Primary signature exists.
        var tpl = await client.PatchAsJsonAsync($"/api/reports/{id}", new { templateId = tplB });
        Assert.Equal(HttpStatusCode.Conflict, tpl.StatusCode);
        var unpin = await client.PatchAsJsonAsync($"/api/reports/{id}", new { templatePinned = false });
        Assert.Equal(HttpStatusCode.Conflict, unpin.StatusCode);

        // Section text is still editable (addendum flow governs post-sign content).
        var text = await client.PatchAsJsonAsync($"/api/reports/{id}", new { findings = "Post-sign edit." });
        Assert.True(text.IsSuccessStatusCode);
    }
}
