using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RadioPad.Application.Security;
using RadioPad.Domain.Entities;
using RadioPad.Domain.Enums;
using RadioPad.Infrastructure.Persistence;
using Xunit;

namespace RadioPad.Api.Tests.Integration;

public class EndpointPermissionRbacTests : IClassFixture<RadioPadAppFactory>
{
    private readonly RadioPadAppFactory _factory;
    public EndpointPermissionRbacTests(RadioPadAppFactory factory) => _factory = factory;

    [Fact]
    public async Task ProviderSave_RequiresProvidersManage()
    {
        using var denied = _factory.CreateTenantClient();
        var deniedResp = await denied.PostAsJsonAsync("/api/providers", ProviderPayload("Denied Provider"));
        Assert.Equal(HttpStatusCode.Forbidden, deniedResp.StatusCode);

        using var allowed = _factory.CreateAdminClient();
        var allowedResp = await allowed.PostAsJsonAsync("/api/providers", ProviderPayload("Allowed Provider"));
        Assert.Equal(HttpStatusCode.OK, allowedResp.StatusCode);

        // Regression guard: a successful provider save audits as the routine
        // ProviderConfigChanged action, NOT PolicyViolation. It was previously
        // mis-tagged, which surfaced normal admin edits as "Policy violation" in the
        // activity log and inflated the governance policy-violation count.
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RadioPadDbContext>();
        var saveEvent = await db.AuditEvents
            .Where(a => a.TenantId == _factory.SeedTenant.Id && a.DetailsJson.Contains("provider_config_saved"))
            .OrderByDescending(a => a.CreatedAt)
            .FirstOrDefaultAsync();
        Assert.NotNull(saveEvent);
        Assert.Equal(AuditAction.ProviderConfigChanged, saveEvent!.Action);
    }

    [Fact]
    public async Task RulebookSave_RequiresRulebooksManage_BeforeYamlParsing()
    {
        using var denied = _factory.CreateTenantClient();
        var deniedResp = await denied.PostAsJsonAsync("/api/rulebooks", new { yaml = "not: valid: yaml:" });
        Assert.Equal(HttpStatusCode.Forbidden, deniedResp.StatusCode);

        using var reportingAdmin = await CreateRoleClientAsync(UserRole.ReportingAdmin);
        var allowedResp = await reportingAdmin.PostAsJsonAsync("/api/rulebooks", new { yaml = "not: valid: yaml:" });
        Assert.Equal(HttpStatusCode.BadRequest, allowedResp.StatusCode);
    }

    [Fact]
    public async Task TenantKmsVerify_RequiresSecurityManage()
    {
        using var denied = _factory.CreateTenantClient();
        var deniedResp = await denied.PostAsync("/api/tenant/settings/kms/verify", null);
        Assert.Equal(HttpStatusCode.Forbidden, deniedResp.StatusCode);

        using var allowed = _factory.CreateAdminClient();
        var allowedResp = await allowed.PostAsync("/api/tenant/settings/kms/verify", null);
        Assert.Equal(HttpStatusCode.BadRequest, allowedResp.StatusCode);
    }

    [Fact]
    public async Task AuditVerify_RequiresAuditVerify()
    {
        using var denied = _factory.CreateTenantClient();
        var deniedResp = await denied.GetAsync("/api/audit/verify");
        Assert.Equal(HttpStatusCode.Forbidden, deniedResp.StatusCode);

        using var compliance = _factory.CreateComplianceClient();
        var allowedResp = await compliance.GetAsync("/api/audit/verify");
        Assert.Equal(HttpStatusCode.OK, allowedResp.StatusCode);
    }

    [Fact]
    public async Task UserLockout_RequiresUsersManage()
    {
        using var billing = _factory.CreateBillingAdminClient();
        var deniedResp = await billing.PostAsync($"/api/users/{_factory.SeedUser.Id}/lockout", null);
        Assert.Equal(HttpStatusCode.Forbidden, deniedResp.StatusCode);

        using var admin = _factory.CreateAdminClient();
        var lockResp = await admin.PostAsync($"/api/users/{_factory.SeedUser.Id}/lockout", null);
        Assert.Equal(HttpStatusCode.OK, lockResp.StatusCode);

        var unlockResp = await admin.PostAsync($"/api/users/{_factory.SeedUser.Id}/unlock", null);
        Assert.Equal(HttpStatusCode.OK, unlockResp.StatusCode);
    }

    [Fact]
    public async Task ReportExport_RequiresReportsExport_BeforeReportLookup()
    {
        var missingReport = Guid.NewGuid();

        using var billing = _factory.CreateBillingAdminClient();
        var deniedResp = await billing.GetAsync($"/api/reports/{missingReport}/export/json");
        Assert.Equal(HttpStatusCode.Forbidden, deniedResp.StatusCode);

        using var admin = _factory.CreateAdminClient();
        var allowedResp = await admin.GetAsync($"/api/reports/{missingReport}/export/json");
        Assert.Equal(HttpStatusCode.NotFound, allowedResp.StatusCode);
    }

    [Fact]
    public async Task PromptOverrideSave_RequiresPromptOverrideManage()
    {
        using var denied = _factory.CreateTenantClient();
        var deniedResp = await denied.PostAsJsonAsync("/api/prompts/overrides", PromptPayload());
        Assert.Equal(HttpStatusCode.Forbidden, deniedResp.StatusCode);

        // Least-privilege (2026-06-23): PromptOverridesManage moved off ReportingAdmin to
        // ItAdmin to preserve manage/approve separation-of-duties. ReportingAdmin is now
        // denied; ItAdmin manages.
        using var reportingAdmin = await CreateRoleClientAsync(UserRole.ReportingAdmin);
        var reportingResp = await reportingAdmin.PostAsJsonAsync("/api/prompts/overrides", PromptPayload());
        Assert.Equal(HttpStatusCode.Forbidden, reportingResp.StatusCode);

        using var itAdmin = await CreateRoleClientAsync(UserRole.ItAdmin);
        var allowedResp = await itAdmin.PostAsJsonAsync("/api/prompts/overrides", PromptPayload());
        Assert.Equal(HttpStatusCode.OK, allowedResp.StatusCode);
    }

    [Fact]
    public async Task TenantMe_Surfaces_EffectivePermissionKeys_ForClientGating()
    {
        // The client builds its permission-accurate UI from /api/tenant/me's
        // permission keys; they must equal the server's RolePermissionMap for the role.
        using var radiologist = _factory.CreateTenantClient();
        var radDoc = await (await radiologist.GetAsync("/api/tenant/me"))
            .Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        var radPerms = radDoc.GetProperty("user").GetProperty("permissions")
            .EnumerateArray().Select(e => e.GetString()!).ToHashSet();
        var expectedRad = RolePermissionMap.ForRole(UserRole.Radiologist)
            .Select(p => PermissionCatalog.Get(p).Key).ToHashSet();
        Assert.Equal(expectedRad, radPerms);
        // A Radiologist must NOT be told it can manage providers/security.
        Assert.DoesNotContain("providers.manage", radPerms);
        Assert.DoesNotContain("security.manage", radPerms);
        // ...but DOES sign reports.
        Assert.Contains("reports.sign", radPerms);

        using var admin = _factory.CreateAdminClient();
        var adminDoc = await (await admin.GetAsync("/api/tenant/me"))
            .Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        var adminUser = adminDoc.GetProperty("user");
        var adminPerms = adminUser.GetProperty("permissions")
            .EnumerateArray().Select(e => e.GetString()!).ToHashSet();
        Assert.Contains("providers.manage", adminPerms);
        Assert.Contains("security.manage", adminPerms);
        Assert.False(string.IsNullOrWhiteSpace(adminUser.GetProperty("roleName").GetString()));
    }

    private async Task<HttpClient> CreateRoleClientAsync(UserRole role)
    {
        var email = $"{role.ToString().ToLowerInvariant()}-{Guid.NewGuid():N}@radiopad.local";
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RadioPadDbContext>();
            db.Users.Add(new User
            {
                TenantId = _factory.SeedTenant.Id,
                Email = email,
                DisplayName = role.ToString(),
                Role = role,
            });
            await db.SaveChangesAsync();
        }

        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-RadioPad-Tenant", _factory.SeedTenant.Slug);
        client.DefaultRequestHeaders.Add("X-RadioPad-User", email);
        return client;
    }

    private static object ProviderPayload(string name) => new
    {
        name,
        adapter = "mock",
        model = "mock-model",
        endpointUrl = "",
        apiKeySecretRef = "",
        compliance = ProviderComplianceClass.LocalOnly,
        enabled = true,
        priority = 10,
        costPerInputKToken = 0m,
        costPerOutputKToken = 0m,
        maxCostPerCallUsd = 0m,
        quality = 0.5m,
        retentionLabel = "",
    };

    private static object PromptPayload() => new
    {
        rulebookId = "rbac-test",
        blockKey = "impression",
        body = "Use concise language.",
    };
}
