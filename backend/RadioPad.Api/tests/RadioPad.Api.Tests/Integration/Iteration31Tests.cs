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
/// Iteration 31 (Agent D) closures — tests for validation strictness toggles
/// (RPT-012 / AI-007) and the dictation cleanup pipeline (AI-001).
/// </summary>
public class Iteration31Tests : IClassFixture<RadioPadAppFactory>
{
    private readonly RadioPadAppFactory _factory;
    public Iteration31Tests(RadioPadAppFactory f) => _factory = f;

    private async Task ResetSettingsAsync(bool? requireZero = null, bool? warnAsBlocker = null)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RadioPadDbContext>();
        var s = await db.TenantSettings.FirstOrDefaultAsync(x => x.TenantId == _factory.SeedTenant.Id);
        if (s is null)
        {
            s = new TenantSettings { TenantId = _factory.SeedTenant.Id };
            db.TenantSettings.Add(s);
        }
        if (requireZero is not null) s.RequireZeroBlockers = requireZero.Value;
        if (warnAsBlocker is not null) s.WarnAsBlocker = warnAsBlocker.Value;
        await db.SaveChangesAsync();
    }

    private async Task<Guid> EnsureRulebookAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RadioPadDbContext>();
        var existing = await db.Rulebooks.FirstOrDefaultAsync(
            r => r.TenantId == _factory.SeedTenant.Id && r.RulebookId == "iter31_test_rb");
        if (existing is not null) return existing.Id;
        var yaml = """
            rulebook_id: iter31_test_rb
            name: Iter31 Test
            version: 0.1.0
            owner: it
            status: approved
            applies_to:
              modalities: [CT]
              body_parts: [Chest]
            required_sections: [Findings, Impression]
            rules:
              - id: required.findings
                severity: blocker
                description: Findings section must be present
              - id: style.indication_present
                severity: warning
                description: Indication should be present
            prompt_blocks:
              system: "You are a test assistant."
            """;
        var rb = new Rulebook
        {
            TenantId = _factory.SeedTenant.Id,
            RulebookId = "iter31_test_rb",
            Name = "Iter31 Test",
            Version = "0.1.0",
            Owner = "it",
            Status = RulebookStatus.Approved,
            SourceYaml = yaml,
        };
        db.Rulebooks.Add(rb);
        await db.SaveChangesAsync();
        return rb.Id;
    }

    private async Task<Guid> CreateReportWithBlockerAsync(Guid rulebookId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RadioPadDbContext>();
        // Empty Findings → required.findings blocker fires; missing Indication → warning fires.
        var report = new Report
        {
            TenantId = _factory.SeedTenant.Id,
            CreatedByUserId = _factory.SeedUser.Id,
            RulebookId = rulebookId,
            Status = ReportStatus.Draft,
            Findings = "",
            Impression = "no acute findings",
            Study = new StudyContext
            {
                Modality = "CT",
                BodyPart = "Chest",
                AccessionNumber = $"ACC-IT31-{Guid.NewGuid():N}".Substring(0, 18),
            },
        };
        db.Reports.Add(report);
        await db.SaveChangesAsync();
        return report.Id;
    }

    private async Task<Guid> CreateValidReportAsync(Guid rulebookId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RadioPadDbContext>();
        var report = new Report
        {
            TenantId = _factory.SeedTenant.Id,
            CreatedByUserId = _factory.SeedUser.Id,
            RulebookId = rulebookId,
            Status = ReportStatus.Draft,
            Indication = "Routine follow-up",
            Findings = "lungs clear; no nodule",
            Impression = "no acute pulmonary findings",
            Study = new StudyContext
            {
                Modality = "CT",
                BodyPart = "Chest",
                AccessionNumber = $"ACC-IT31-OK-{Guid.NewGuid():N}".Substring(0, 18),
            },
        };
        db.Reports.Add(report);
        await db.SaveChangesAsync();
        return report.Id;
    }

    [Fact]
    public async Task Validate_Default_Toggles_Match_Today()
    {
        await ResetSettingsAsync(requireZero: true, warnAsBlocker: false);
        var rbId = await EnsureRulebookAsync();
        var reportId = await CreateReportWithBlockerAsync(rbId);

        using var client = _factory.CreateTenantClient();
        var resp = await client.PostAsync($"/api/reports/{reportId}/validate", content: null);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        using var doc = await JsonDocument.ParseAsync(await resp.Content.ReadAsStreamAsync());
        Assert.True(doc.RootElement.GetProperty("blockerPresent").GetBoolean());

        // Status must remain Draft (today's behaviour) when blockers exist.
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RadioPadDbContext>();
        var report = await db.Reports.FindAsync(reportId);
        Assert.Equal(ReportStatus.Draft, report!.Status);

        // Export is therefore blocked by report state.
        var export = await client.GetAsync($"/api/reports/{reportId}/export/text");
        Assert.Equal(HttpStatusCode.Conflict, export.StatusCode);
    }

    [Fact]
    public async Task WarnAsBlocker_Promotes_Warnings_To_Blockers()
    {
        await ResetSettingsAsync(requireZero: true, warnAsBlocker: true);
        try
        {
            var rbId = await EnsureRulebookAsync();
            // Valid report → no blocker by default, but missing-indication WARNING is promoted.
            var reportId = await CreateValidReportAsync(rbId);
            // Strip indication so the "indication_present" warning fires.
            using (var scope = _factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<RadioPadDbContext>();
                var r = await db.Reports.FindAsync(reportId);
                r!.Indication = "";
                r.Study.Indication = "";
                await db.SaveChangesAsync();
            }

            using var client = _factory.CreateTenantClient();
            var resp = await client.PostAsync($"/api/reports/{reportId}/validate", null);
            Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
            using var doc = await JsonDocument.ParseAsync(await resp.Content.ReadAsStreamAsync());
            Assert.True(doc.RootElement.GetProperty("blockerPresent").GetBoolean());
            // Every finding's severity should now be Blocker, none Warning.
            var findings = doc.RootElement.GetProperty("findings").EnumerateArray().ToList();
            Assert.NotEmpty(findings);
            Assert.DoesNotContain(findings, f => f.GetProperty("severity").GetString() == "Warning");
        }
        finally
        {
            await ResetSettingsAsync(requireZero: true, warnAsBlocker: false);
        }
    }

    [Fact]
    public async Task RequireZeroBlockers_Off_Allows_Export_With_Blockers()
    {
        await ResetSettingsAsync(requireZero: false, warnAsBlocker: false);
        try
        {
            var rbId = await EnsureRulebookAsync();
            var reportId = await CreateReportWithBlockerAsync(rbId);

            using var client = _factory.CreateTenantClient();
            var validate = await client.PostAsync($"/api/reports/{reportId}/validate", null);
            Assert.Equal(HttpStatusCode.OK, validate.StatusCode);
            using var vdoc = await JsonDocument.ParseAsync(await validate.Content.ReadAsStreamAsync());
            Assert.True(vdoc.RootElement.GetProperty("blockerPresent").GetBoolean());

            // Status must have advanced to Validated despite the blocker.
            using (var scope = _factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<RadioPadDbContext>();
                var r = await db.Reports.FindAsync(reportId);
                Assert.True(r!.Status >= ReportStatus.Validated);
            }

            var ack = await client.PostAsync($"/api/reports/{reportId}/acknowledge", null);
            Assert.Equal(HttpStatusCode.OK, ack.StatusCode);

            // Export must succeed once acknowledged.
            var export = await client.GetAsync($"/api/reports/{reportId}/export/text");
            Assert.Equal(HttpStatusCode.OK, export.StatusCode);
        }
        finally
        {
            await ResetSettingsAsync(requireZero: true, warnAsBlocker: false);
        }
    }

    [Fact]
    public async Task RequireZeroBlockers_On_Blocks_Acknowledge_With_Blockers()
    {
        await ResetSettingsAsync(requireZero: true, warnAsBlocker: false);
        var rbId = await EnsureRulebookAsync();
        var reportId = await CreateReportWithBlockerAsync(rbId);

        using var client = _factory.CreateTenantClient();
        var ack = await client.PostAsync($"/api/reports/{reportId}/acknowledge", null);
        Assert.Equal(HttpStatusCode.Conflict, ack.StatusCode);
        using var doc = await JsonDocument.ParseAsync(await ack.Content.ReadAsStreamAsync());
        Assert.Equal("validation_blockers", doc.RootElement.GetProperty("kind").GetString());

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RadioPadDbContext>();
        var report = await db.Reports.FindAsync(reportId);
        Assert.NotEqual(ReportStatus.Acknowledged, report!.Status);
        var acknowledgedAudit = await db.AuditEvents.AnyAsync(a =>
            a.ReportId == reportId && a.Action == AuditAction.ReportAcknowledged);
        Assert.False(acknowledgedAudit);
    }

    [Fact]
    public async Task TenantSettings_Toggles_Are_Admin_Only()
    {
        // Seed a non-admin user (Radiologist) for this assertion.
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RadioPadDbContext>();
            var radiologist = await db.Users.FirstOrDefaultAsync(
                u => u.TenantId == _factory.SeedTenant.Id
                  && u.Email == "iter31-rad@radiopad.local");
            if (radiologist is null)
            {
                db.Users.Add(new User
                {
                    TenantId = _factory.SeedTenant.Id,
                    Email = "iter31-rad@radiopad.local",
                    DisplayName = "Iter31 Radiologist",
                    Role = UserRole.Radiologist,
                });
                await db.SaveChangesAsync();
            }
        }

        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-RadioPad-Tenant", _factory.SeedTenant.Slug);
        client.DefaultRequestHeaders.Add("X-RadioPad-User", "iter31-rad@radiopad.local");
        var resp = await client.PostAsJsonAsync("/api/tenant/settings", new
        {
            hallucinationDetectionEnabled = true,
            hallucinationSeverity = "Warning",
            hallucinationAllowList = "",
            hallucinationMinSupport = 0.3,
            plan = 0,
            featureFlagsJson = "{}",
            requireZeroBlockers = false,
            warnAsBlocker = true,
        });
        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);

        // The admin path (Radiologist seed user is the default … but it's a
        // Radiologist too — the tenant settings admin RBAC requires
        // MedicalDirector/ReportingAdmin/ItAdmin). Promote to ReportingAdmin
        // and verify the toggles persist.
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RadioPadDbContext>();
            var admin = await db.Users.FirstOrDefaultAsync(
                u => u.TenantId == _factory.SeedTenant.Id
                  && u.Email == "iter31-admin@radiopad.local");
            if (admin is null)
            {
                db.Users.Add(new User
                {
                    TenantId = _factory.SeedTenant.Id,
                    Email = "iter31-admin@radiopad.local",
                    DisplayName = "Iter31 Admin",
                    Role = UserRole.ReportingAdmin,
                });
                await db.SaveChangesAsync();
            }
        }

        using var adminClient = _factory.CreateClient();
        adminClient.DefaultRequestHeaders.Add("X-RadioPad-Tenant", _factory.SeedTenant.Slug);
        adminClient.DefaultRequestHeaders.Add("X-RadioPad-User", "iter31-admin@radiopad.local");
        var ok = await adminClient.PostAsJsonAsync("/api/tenant/settings", new
        {
            hallucinationDetectionEnabled = true,
            hallucinationSeverity = "Warning",
            hallucinationAllowList = "",
            hallucinationMinSupport = 0.3,
            plan = 0,
            featureFlagsJson = "{}",
            requireZeroBlockers = false,
            warnAsBlocker = true,
        });
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RadioPadDbContext>();
            var s = await db.TenantSettings.FirstAsync(x => x.TenantId == _factory.SeedTenant.Id);
            Assert.False(s.RequireZeroBlockers);
            Assert.True(s.WarnAsBlocker);
        }

        await ResetSettingsAsync(requireZero: true, warnAsBlocker: false);
    }

    // ===== AI-001 dictation cleanup =====

    [Fact]
    public async Task Dictation_Cleanup_Returns_Section_Map()
    {
        var rbId = await EnsureRulebookAsync();
        var reportId = await CreateValidReportAsync(rbId);

        using var client = _factory.CreateTenantClient();
        var resp = await client.PostAsJsonAsync($"/api/reports/{reportId}/dictation/cleanup", new
        {
            rawDictation = "lungs clear; no nodules",
        });
        // Mock provider echoes the prompt; the response will not be valid JSON
        // for the schema, so the service routes free text into Findings.
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        using var doc = await JsonDocument.ParseAsync(await resp.Content.ReadAsStreamAsync());
        var sections = doc.RootElement.GetProperty("cleanedSections");
        Assert.True(sections.TryGetProperty("indication", out _));
        Assert.True(sections.TryGetProperty("technique", out _));
        Assert.True(sections.TryGetProperty("findings", out _));
        Assert.True(sections.TryGetProperty("impression", out _));
        Assert.True(sections.TryGetProperty("recommendations", out _));
        Assert.True(doc.RootElement.GetProperty("latencyMs").GetInt32() >= 0);

        // The gateway must have audited an AiResponse for this call.
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RadioPadDbContext>();
        var audited = await db.AuditEvents
            .Where(a => a.TenantId == _factory.SeedTenant.Id && a.Action == AuditAction.AiResponse)
            .AnyAsync();
        Assert.True(audited);
    }

    [Fact]
    public async Task Dictation_Cleanup_400_On_Empty_Body()
    {
        var rbId = await EnsureRulebookAsync();
        var reportId = await CreateValidReportAsync(rbId);

        using var client = _factory.CreateTenantClient();
        var resp = await client.PostAsJsonAsync($"/api/reports/{reportId}/dictation/cleanup", new
        {
            rawDictation = "",
        });
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }
}
