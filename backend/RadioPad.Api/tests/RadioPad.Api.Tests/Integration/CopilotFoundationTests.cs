using System.Net;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using RadioPad.Api.Controllers;
using RadioPad.Api.Services;
using RadioPad.Application.Abstractions;
using RadioPad.Domain.Entities;
using RadioPad.Domain.Enums;
using RadioPad.Infrastructure.Persistence;
using RadioPad.Infrastructure.Providers.Cli;
using Xunit;

namespace RadioPad.Api.Tests.Integration;

public class CopilotFoundationTests
{
    private static readonly IServiceProvider DevHeaderServices = new ServiceCollection()
        .AddSingleton<IConfiguration>(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["RadioPad:DevHeaders"] = "true" })
            .Build())
        .BuildServiceProvider();

    [Fact]
    public async Task Admin_Settings_Are_Rbac_Guarded_And_Default_FailClosed()
    {
        await using var fixture = await CopilotFixture.CreateAsync();
        var audit = new RecordingAudit();
        var service = new CopilotService(fixture.Db);

        var radiologistController = new CopilotAdminController(fixture.Db, service, audit);
        SetHeaders(radiologistController, fixture.Tenant, fixture.Radiologist);
        var denied = await radiologistController.GetSettings(CancellationToken.None);
        var forbidden = Assert.IsType<ObjectResult>(denied);
        Assert.Equal((int)HttpStatusCode.Forbidden, forbidden.StatusCode);

        var adminController = new CopilotAdminController(fixture.Db, service, audit);
        SetHeaders(adminController, fixture.Tenant, fixture.Admin);
        var ok = await adminController.GetSettings(CancellationToken.None);
        var result = Assert.IsType<OkObjectResult>(ok);
        var dto = Assert.IsType<CopilotSettingsDto>(result.Value);
        Assert.False(dto.Enabled);
        Assert.True(dto.EmergencyDisabled);
        Assert.False(dto.GitHubAppPrivateKeyConfigured);
        Assert.Null(dto.GitHubAppPrivateKeySecretRef);
    }

    [Fact]
    public async Task Save_Settings_Accepts_Secret_Refs_But_Returns_Only_Configured_Booleans()
    {
        await using var fixture = await CopilotFixture.CreateAsync();
        var audit = new RecordingAudit();
        var service = new CopilotService(fixture.Db);
        var controller = new CopilotAdminController(fixture.Db, service, audit);
        SetHeaders(controller, fixture.Tenant, fixture.Admin);

        var dto = new CopilotSettingsDto(
            Enabled: true,
            EmergencyDisabled: false,
            DefaultMode: "EnterpriseManaged",
            AllowedModes: new[] { "Disabled", "EnterpriseManaged" },
            GitHubEnterpriseSlug: "example-enterprise",
            GitHubOrganization: "example-org",
            GitHubHost: "github.com",
            SdkRuntimeEnabled: true,
            CliRuntimeEnabled: false,
            AllowByoAccounts: false,
            AllowEnvironmentTokenAuth: false,
            RequireOsKeychainForCli: true,
            PromptLoggingEnabled: false,
            ContextLoggingEnabled: false,
            RetentionPolicy: "metadata_only",
            PolicyJson: "{\"phi\":\"blocked\"}",
            GitHubAppId: "12345",
            GitHubAppInstallationId: "67890",
            OAuthClientId: "Iv1.example",
            GitHubAppPrivateKeyConfigured: false,
            OAuthClientSecretConfigured: false,
            GitHubAppPrivateKeySecretRef: "vault:copilot/app-key",
            OAuthClientSecretRef: "env:RADIOPAD_COPILOT_OAUTH_SECRET");

        var action = await controller.SaveSettings(dto, CancellationToken.None);
        var result = Assert.IsType<OkObjectResult>(action);
        var saved = Assert.IsType<CopilotSettingsDto>(result.Value);
        Assert.True(saved.GitHubAppPrivateKeyConfigured);
        Assert.True(saved.OAuthClientSecretConfigured);
        Assert.Null(saved.GitHubAppPrivateKeySecretRef);
        Assert.Contains(audit.Events, a => a.Action == AuditAction.CopilotAdminSettingsChanged);
    }

    [Fact]
    public async Task Save_Quotas_Is_Restricted_To_Privileged_Admins()
    {
        await using var fixture = await CopilotFixture.CreateAsync();
        var audit = new RecordingAudit();
        var service = new CopilotService(fixture.Db);
        var controller = new CopilotAdminController(fixture.Db, service, audit);
        var quotas = new[]
        {
            new CopilotQuotaPolicyDto(null, "tenant", "", "chat", 3600, 10, 1, true),
        };

        SetHeaders(controller, fixture.Tenant, fixture.ReportingAdmin);
        var denied = await controller.SaveQuotas(quotas, CancellationToken.None);
        var forbidden = Assert.IsType<ObjectResult>(denied);
        Assert.Equal((int)HttpStatusCode.Forbidden, forbidden.StatusCode);

        SetHeaders(controller, fixture.Tenant, fixture.Admin);
        var allowed = await controller.SaveQuotas(quotas, CancellationToken.None);
        var ok = Assert.IsType<OkObjectResult>(allowed);
        Assert.Single(Assert.IsAssignableFrom<IReadOnlyList<CopilotQuotaPolicyDto>>(ok.Value));
    }

    [Fact]
    public async Task Secret_Refs_Are_Encrypted_At_Rest()
    {
        await using var fixture = await CopilotFixture.CreateAsync();
        var audit = new RecordingAudit();
        var service = new CopilotService(fixture.Db);
        var controller = new CopilotAdminController(fixture.Db, service, audit);
        SetHeaders(controller, fixture.Tenant, fixture.Admin);

        var dto = new CopilotSettingsDto(
            Enabled: true,
            EmergencyDisabled: false,
            DefaultMode: "EnterpriseManaged",
            AllowedModes: new[] { "Disabled", "EnterpriseManaged" },
            GitHubEnterpriseSlug: "example-enterprise",
            GitHubOrganization: "example-org",
            GitHubHost: "github.com",
            SdkRuntimeEnabled: false,
            CliRuntimeEnabled: false,
            AllowByoAccounts: false,
            AllowEnvironmentTokenAuth: false,
            RequireOsKeychainForCli: true,
            PromptLoggingEnabled: false,
            ContextLoggingEnabled: false,
            RetentionPolicy: "metadata_only",
            PolicyJson: "{\"phi\":\"blocked\"}",
            GitHubAppId: "12345",
            GitHubAppInstallationId: "67890",
            OAuthClientId: "Iv1.example",
            GitHubAppPrivateKeyConfigured: false,
            OAuthClientSecretConfigured: false,
            GitHubAppPrivateKeySecretRef: "vault:copilot/app-key",
            OAuthClientSecretRef: "env:RADIOPAD_COPILOT_OAUTH_SECRET");

        await controller.SaveSettings(dto, CancellationToken.None);
        fixture.Db.CopilotUserAccounts.Add(new CopilotUserAccount
        {
            TenantId = fixture.Tenant.Id,
            UserId = fixture.Radiologist.Id,
            Mode = CopilotMode.BringYourOwnAccount,
            GitHubLogin = "radiologist",
            TokenStatus = "configured",
            TokenSecretRef = "vault:copilot/user-token",
            DenialReason = "",
        });
        await fixture.Db.SaveChangesAsync();

        var rawSettings = await fixture.RawScalarAsync(
            "SELECT GitHubAppPrivateKeySecretRef || '|' || OAuthClientSecretRef FROM CopilotIntegrationSettings LIMIT 1");
        var rawUserToken = await fixture.RawScalarAsync(
            "SELECT TokenSecretRef FROM CopilotUserAccounts LIMIT 1");

        Assert.Contains("enc:v1:", rawSettings);
        Assert.Contains("enc:v1:", rawUserToken);
        Assert.DoesNotContain("vault:copilot/app-key", rawSettings);
        Assert.DoesNotContain("RADIOPAD_COPILOT_OAUTH_SECRET", rawSettings);
        Assert.DoesNotContain("vault:copilot/user-token", rawUserToken);
    }

    [Fact]
    public async Task Chat_Always_Fails_Closed_And_Records_Metadata_Only()
    {
        await using var fixture = await CopilotFixture.CreateAsync();
        var audit = new RecordingAudit();
        var service = new CopilotService(fixture.Db);
        var controller = new CopilotController(fixture.Db, service, audit);
        SetHeaders(controller, fixture.Tenant, fixture.Radiologist);

        var action = await controller.Chat(
            new CopilotChatRequest("hello copilot", null, null, "unit-test"),
            CancellationToken.None);

        var result = Assert.IsType<ObjectResult>(action);
        Assert.Equal((int)HttpStatusCode.Conflict, result.StatusCode);
        var error = Assert.IsType<CopilotErrorDto>(result.Value);
        Assert.Contains(error.Kind, new[] { "copilot_disabled", "runtime_not_enabled", "runtime_not_configured", "runtime_unavailable" });

        var evt = await fixture.Db.CopilotUsageEvents.SingleAsync(e => e.Feature == "unit-test");
        Assert.Equal("blocked", evt.Status);
        Assert.NotEmpty(evt.InputHash);
        Assert.DoesNotContain("hello copilot", evt.InputHash);
        Assert.Contains(audit.Events, a => a.Action == AuditAction.CopilotPolicyBlocked);
    }

    [Fact]
    public async Task LocalCli_Link_Enables_Entitlement_And_Session_Uses_Fixed_Cli()
    {
        await using var fixture = await CopilotFixture.CreateAsync();
        var audit = new RecordingAudit();
        var launcher = StubLauncher.Ok("Copilot says use the typed API client.\n");
        var service = new CopilotService(fixture.Db, launcher);
        var admin = new CopilotAdminController(fixture.Db, service, audit);
        SetHeaders(admin, fixture.Tenant, fixture.Admin);

        await admin.SaveSettings(new CopilotSettingsDto(
            Enabled: true,
            EmergencyDisabled: false,
            DefaultMode: "LocalCli",
            AllowedModes: new[] { "Disabled", "LocalCli" },
            GitHubEnterpriseSlug: "",
            GitHubOrganization: "",
            GitHubHost: "github.com",
            SdkRuntimeEnabled: false,
            CliRuntimeEnabled: true,
            AllowByoAccounts: false,
            AllowEnvironmentTokenAuth: false,
            RequireOsKeychainForCli: true,
            PromptLoggingEnabled: false,
            ContextLoggingEnabled: false,
            RetentionPolicy: "metadata_only",
            PolicyJson: "{}",
            GitHubAppId: "",
            GitHubAppInstallationId: "",
            OAuthClientId: "",
            GitHubAppPrivateKeyConfigured: false,
            OAuthClientSecretConfigured: false), CancellationToken.None);

        var user = new CopilotController(fixture.Db, service, audit);
        SetHeaders(user, fixture.Tenant, fixture.Radiologist);
        var linkedAction = await user.LinkLocalCli(
            new CopilotLocalCliAccountRequest("octocat", 123, "github.com", "local_cli", "cli_enforced"),
            CancellationToken.None);
        var linked = Assert.IsType<CopilotAccountDto>(Assert.IsType<OkObjectResult>(linkedAction).Value);
        Assert.True(linked.EntitlementAllowed);
        Assert.Equal("cli_keychain", linked.TokenStatus);

        var sessionAction = await user.StartSession(
            new CopilotSessionRequest("show how to use the API client", "LocalCli", "chat", null),
            CancellationToken.None);
        var sessionResult = Assert.IsType<ObjectResult>(sessionAction);
        Assert.Equal((int)HttpStatusCode.OK, sessionResult.StatusCode);
        var session = Assert.IsType<CopilotSessionDto>(sessionResult.Value);
        Assert.Equal("completed", session.Status);
        Assert.Equal("Copilot says use the typed API client.", session.Output);

        var spec = Assert.Single(launcher.Captured);
        Assert.Equal("gh", spec.FileName);
        Assert.Equal(new[] { "copilot", "suggest", "--type", "explain" }, spec.Arguments);
        Assert.Contains("show how to use the API client", spec.StandardInput);
        Assert.DoesNotContain(spec.Arguments, a => a.Contains("show how"));
        Assert.Contains(audit.Events, a => a.Action == AuditAction.CopilotRequestLifecycle);
    }

    [Fact]
    public async Task Quota_Blocks_Before_Starting_Cli_Process()
    {
        await using var fixture = await CopilotFixture.CreateAsync();
        var audit = new RecordingAudit();
        var launcher = StubLauncher.Ok("first response");
        var service = new CopilotService(fixture.Db, launcher);
        var settings = await service.GetOrCreateSettingsAsync(fixture.Tenant.Id, CancellationToken.None);
        service.Apply(settings, new CopilotSettingsDto(
            Enabled: true,
            EmergencyDisabled: false,
            DefaultMode: "LocalCli",
            AllowedModes: new[] { "Disabled", "LocalCli" },
            GitHubEnterpriseSlug: "",
            GitHubOrganization: "",
            GitHubHost: "github.com",
            SdkRuntimeEnabled: false,
            CliRuntimeEnabled: true,
            AllowByoAccounts: false,
            AllowEnvironmentTokenAuth: false,
            RequireOsKeychainForCli: true,
            PromptLoggingEnabled: false,
            ContextLoggingEnabled: false,
            RetentionPolicy: "metadata_only",
            PolicyJson: "{}",
            GitHubAppId: "",
            GitHubAppInstallationId: "",
            OAuthClientId: "",
            GitHubAppPrivateKeyConfigured: false,
            OAuthClientSecretConfigured: false));
        fixture.Db.CopilotUserAccounts.Add(new CopilotUserAccount
        {
            TenantId = fixture.Tenant.Id,
            UserId = fixture.Radiologist.Id,
            Mode = CopilotMode.LocalCli,
            GitHubLogin = "octocat",
            TokenStatus = "cli_keychain",
            SsoStatus = "local_cli",
            SeatStatus = "cli_enforced",
        });
        fixture.Db.CopilotQuotaPolicies.Add(new CopilotQuotaPolicy
        {
            TenantId = fixture.Tenant.Id,
            ScopeType = "user",
            ScopeKey = "",
            Feature = "chat",
            WindowSeconds = 3600,
            MaxRequests = 1,
            MaxConcurrent = 1,
            Enabled = true,
        });
        await fixture.Db.SaveChangesAsync();

        var controller = new CopilotController(fixture.Db, service, audit);
        SetHeaders(controller, fixture.Tenant, fixture.Radiologist);
        var first = await controller.StartSession(new CopilotSessionRequest("first", "LocalCli", "chat", null), CancellationToken.None);
        Assert.Equal((int)HttpStatusCode.OK, Assert.IsType<ObjectResult>(first).StatusCode);

        var second = await controller.StartSession(new CopilotSessionRequest("second", "LocalCli", "chat", null), CancellationToken.None);
        var blocked = Assert.IsType<ObjectResult>(second);
        Assert.Equal((int)HttpStatusCode.Conflict, blocked.StatusCode);
        Assert.Equal("quota_exceeded", Assert.IsType<CopilotErrorDto>(blocked.Value).Kind);
        Assert.Single(launcher.Captured);
    }

    private static void SetHeaders(ControllerBase controller, Tenant tenant, User user)
    {
        var context = new DefaultHttpContext();
        context.RequestServices = DevHeaderServices;
        context.Request.Headers["X-RadioPad-Tenant"] = tenant.Slug;
        context.Request.Headers["X-RadioPad-User"] = user.Email;
        context.TraceIdentifier = "copilot-test";
        controller.ControllerContext = new ControllerContext { HttpContext = context };
    }

    private sealed class RecordingAudit : IAuditLog
    {
        public List<AuditEvent> Events { get; } = new();
        public Task AppendAsync(AuditEvent evt, CancellationToken cancellationToken)
        {
            Events.Add(evt);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<AuditEvent>> QueryAsync(
            Guid tenantId,
            DateTimeOffset? from,
            DateTimeOffset? to,
            int take = 200,
            CancellationToken cancellationToken = default)
        {
            IReadOnlyList<AuditEvent> rows = Events
                .Where(e => e.TenantId == tenantId)
                .Where(e => from is null || e.CreatedAt >= from)
                .Where(e => to is null || e.CreatedAt <= to)
                .Take(take)
                .ToArray();
            return Task.FromResult(rows);
        }

        public Task<AuditChainVerification> VerifyChainAsync(
            Guid tenantId,
            CancellationToken cancellationToken = default)
        {
            var rows = Events.Where(e => e.TenantId == tenantId).ToArray();
            return Task.FromResult(new AuditChainVerification(
                rows.Length,
                Intact: true,
                FirstBrokenEventId: null,
                rows.LastOrDefault()?.CreatedAt));
        }
    }

    private sealed class StubLauncher : IProcessLauncher
    {
        private readonly Func<ProcessLaunchSpec, CancellationToken, Task<ProcessLaunchResult>> _responder;
        public List<ProcessLaunchSpec> Captured { get; } = new();

        private StubLauncher(Func<ProcessLaunchSpec, CancellationToken, Task<ProcessLaunchResult>> responder)
        {
            _responder = responder;
        }

        public Task<ProcessLaunchResult> RunAsync(ProcessLaunchSpec spec, CancellationToken ct)
        {
            Captured.Add(spec);
            return _responder(spec, ct);
        }

        public static StubLauncher Ok(string stdout) =>
            new((_, _) => Task.FromResult(new ProcessLaunchResult(0, stdout, "", 12)));
    }

    private sealed class CopilotFixture : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;

        private CopilotFixture(SqliteConnection connection, RadioPadDbContext db, Tenant tenant, User radiologist, User admin, User reportingAdmin)
        {
            _connection = connection;
            Db = db;
            Tenant = tenant;
            Radiologist = radiologist;
            Admin = admin;
            ReportingAdmin = reportingAdmin;
        }

        public RadioPadDbContext Db { get; }
        public Tenant Tenant { get; }
        public User Radiologist { get; }
        public User Admin { get; }
        public User ReportingAdmin { get; }

        public static async Task<CopilotFixture> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var options = new DbContextOptionsBuilder<RadioPadDbContext>()
                .UseSqlite(connection)
                .Options;
            var db = new RadioPadDbContext(options);
            await db.Database.EnsureCreatedAsync();

            var tenant = new Tenant { Slug = "it", DisplayName = "Integration", RequirePhiApprovedProvider = true };
            var radiologist = new User
            {
                TenantId = tenant.Id,
                Email = "it-radiologist@radiopad.local",
                DisplayName = "IT Radiologist",
                Role = UserRole.Radiologist,
            };
            var admin = new User
            {
                TenantId = tenant.Id,
                Email = "it-admin@radiopad.local",
                DisplayName = "IT Admin",
                Role = UserRole.ItAdmin,
            };
            var reportingAdmin = new User
            {
                TenantId = tenant.Id,
                Email = "reporting-admin@radiopad.local",
                DisplayName = "Reporting Admin",
                Role = UserRole.ReportingAdmin,
            };
            db.Tenants.Add(tenant);
            db.Users.AddRange(radiologist, admin, reportingAdmin);
            await db.SaveChangesAsync();
            return new CopilotFixture(connection, db, tenant, radiologist, admin, reportingAdmin);
        }

        public async Task<string> RawScalarAsync(string sql)
        {
            await using var command = _connection.CreateCommand();
            command.CommandText = sql;
            var value = await command.ExecuteScalarAsync();
            return Assert.IsType<string>(value);
        }

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            await _connection.DisposeAsync();
        }
    }
}
