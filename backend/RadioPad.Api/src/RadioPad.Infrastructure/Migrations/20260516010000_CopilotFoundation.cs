using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RadioPad.Infrastructure.Migrations
{
    [Microsoft.EntityFrameworkCore.Infrastructure.DbContext(typeof(RadioPad.Infrastructure.Persistence.RadioPadDbContext))]
    [Microsoft.EntityFrameworkCore.Migrations.Migration("20260516010000_CopilotFoundation")]
    public partial class CopilotFoundation : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CopilotIntegrationSettings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    TenantId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Enabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    EmergencyDisabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    DefaultMode = table.Column<int>(type: "INTEGER", nullable: false),
                    AllowedModes = table.Column<string>(type: "TEXT", nullable: false),
                    GitHubEnterpriseSlug = table.Column<string>(type: "TEXT", nullable: false),
                    GitHubOrganization = table.Column<string>(type: "TEXT", nullable: false),
                    GitHubHost = table.Column<string>(type: "TEXT", nullable: false),
                    SdkRuntimeEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    CliRuntimeEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    AllowByoAccounts = table.Column<bool>(type: "INTEGER", nullable: false),
                    AllowEnvironmentTokenAuth = table.Column<bool>(type: "INTEGER", nullable: false),
                    RequireOsKeychainForCli = table.Column<bool>(type: "INTEGER", nullable: false),
                    PromptLoggingEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    ContextLoggingEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    RetentionPolicy = table.Column<string>(type: "TEXT", nullable: false),
                    PolicyJson = table.Column<string>(type: "TEXT", nullable: false),
                    GitHubAppId = table.Column<string>(type: "TEXT", nullable: false),
                    GitHubAppInstallationId = table.Column<string>(type: "TEXT", nullable: false),
                    GitHubAppPrivateKeySecretRef = table.Column<string>(type: "TEXT", nullable: false),
                    OAuthClientId = table.Column<string>(type: "TEXT", nullable: false),
                    OAuthClientSecretRef = table.Column<string>(type: "TEXT", nullable: false),
                    SecretsUpdatedAt = table.Column<long>(type: "INTEGER", nullable: true),
                    CreatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    UpdatedAt = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table => table.PrimaryKey("PK_CopilotIntegrationSettings", x => x.Id));

            migrationBuilder.CreateTable(
                name: "CopilotFeatureFlags",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    TenantId = table.Column<Guid>(type: "TEXT", nullable: false),
                    FeatureKey = table.Column<string>(type: "TEXT", nullable: false),
                    Enabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    RequiredRole = table.Column<string>(type: "TEXT", nullable: false),
                    PolicyJson = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    UpdatedAt = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table => table.PrimaryKey("PK_CopilotFeatureFlags", x => x.Id));

            migrationBuilder.CreateTable(
                name: "CopilotUserAccounts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    TenantId = table.Column<Guid>(type: "TEXT", nullable: false),
                    UserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Mode = table.Column<int>(type: "INTEGER", nullable: false),
                    GitHubLogin = table.Column<string>(type: "TEXT", nullable: false),
                    GitHubUserId = table.Column<long>(type: "INTEGER", nullable: true),
                    TokenStatus = table.Column<string>(type: "TEXT", nullable: false),
                    TokenSecretRef = table.Column<string>(type: "TEXT", nullable: false),
                    SsoStatus = table.Column<string>(type: "TEXT", nullable: false),
                    SeatStatus = table.Column<string>(type: "TEXT", nullable: false),
                    DenialReason = table.Column<string>(type: "TEXT", nullable: false),
                    LastAuthenticatedAt = table.Column<long>(type: "INTEGER", nullable: true),
                    RevokedAt = table.Column<long>(type: "INTEGER", nullable: true),
                    CreatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    UpdatedAt = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table => table.PrimaryKey("PK_CopilotUserAccounts", x => x.Id));

            migrationBuilder.CreateTable(
                name: "CopilotEntitlements",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    TenantId = table.Column<Guid>(type: "TEXT", nullable: false),
                    UserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Mode = table.Column<int>(type: "INTEGER", nullable: false),
                    Allowed = table.Column<bool>(type: "INTEGER", nullable: false),
                    Source = table.Column<string>(type: "TEXT", nullable: false),
                    GitHubLogin = table.Column<string>(type: "TEXT", nullable: false),
                    SsoStatus = table.Column<string>(type: "TEXT", nullable: false),
                    SeatStatus = table.Column<string>(type: "TEXT", nullable: false),
                    DenialReason = table.Column<string>(type: "TEXT", nullable: false),
                    CheckedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    ExpiresAt = table.Column<long>(type: "INTEGER", nullable: true),
                    CreatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    UpdatedAt = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table => table.PrimaryKey("PK_CopilotEntitlements", x => x.Id));

            migrationBuilder.CreateTable(
                name: "CopilotQuotaPolicies",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    TenantId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ScopeType = table.Column<string>(type: "TEXT", nullable: false),
                    ScopeKey = table.Column<string>(type: "TEXT", nullable: false),
                    Feature = table.Column<string>(type: "TEXT", nullable: false),
                    WindowSeconds = table.Column<int>(type: "INTEGER", nullable: false),
                    MaxRequests = table.Column<int>(type: "INTEGER", nullable: false),
                    MaxConcurrent = table.Column<int>(type: "INTEGER", nullable: false),
                    Enabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    UpdatedAt = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table => table.PrimaryKey("PK_CopilotQuotaPolicies", x => x.Id));

            migrationBuilder.CreateTable(
                name: "CopilotSessions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    TenantId = table.Column<Guid>(type: "TEXT", nullable: false),
                    UserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Mode = table.Column<int>(type: "INTEGER", nullable: false),
                    Feature = table.Column<string>(type: "TEXT", nullable: false),
                    ContextKind = table.Column<string>(type: "TEXT", nullable: false),
                    Status = table.Column<string>(type: "TEXT", nullable: false),
                    Runtime = table.Column<string>(type: "TEXT", nullable: false),
                    ContextHash = table.Column<string>(type: "TEXT", nullable: false),
                    LastErrorKind = table.Column<string>(type: "TEXT", nullable: false),
                    StartedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    CompletedAt = table.Column<long>(type: "INTEGER", nullable: true),
                    CancelledAt = table.Column<long>(type: "INTEGER", nullable: true),
                    CreatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    UpdatedAt = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table => table.PrimaryKey("PK_CopilotSessions", x => x.Id));

            migrationBuilder.CreateTable(
                name: "CopilotMessages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    TenantId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SessionId = table.Column<Guid>(type: "TEXT", nullable: false),
                    UserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Role = table.Column<string>(type: "TEXT", nullable: false),
                    Sequence = table.Column<int>(type: "INTEGER", nullable: false),
                    Status = table.Column<string>(type: "TEXT", nullable: false),
                    InputHash = table.Column<string>(type: "TEXT", nullable: false),
                    OutputHash = table.Column<string>(type: "TEXT", nullable: false),
                    Model = table.Column<string>(type: "TEXT", nullable: false),
                    LatencyMs = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    UpdatedAt = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table => table.PrimaryKey("PK_CopilotMessages", x => x.Id));

            migrationBuilder.CreateTable(
                name: "CopilotUsageEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    TenantId = table.Column<Guid>(type: "TEXT", nullable: false),
                    UserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    RequestId = table.Column<string>(type: "TEXT", nullable: false),
                    Feature = table.Column<string>(type: "TEXT", nullable: false),
                    Mode = table.Column<string>(type: "TEXT", nullable: false),
                    Status = table.Column<string>(type: "TEXT", nullable: false),
                    BlockKind = table.Column<string>(type: "TEXT", nullable: false),
                    LatencyMs = table.Column<int>(type: "INTEGER", nullable: false),
                    InputHash = table.Column<string>(type: "TEXT", nullable: false),
                    OutputHash = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    UpdatedAt = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table => table.PrimaryKey("PK_CopilotUsageEvents", x => x.Id));

            migrationBuilder.CreateTable(
                name: "CopilotDiagnosticRuns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    TenantId = table.Column<Guid>(type: "TEXT", nullable: false),
                    UserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Status = table.Column<string>(type: "TEXT", nullable: false),
                    ResultsJson = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    UpdatedAt = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table => table.PrimaryKey("PK_CopilotDiagnosticRuns", x => x.Id));

            migrationBuilder.CreateIndex("IX_CopilotIntegrationSettings_TenantId", "CopilotIntegrationSettings", "TenantId", unique: true);
            migrationBuilder.CreateIndex("IX_CopilotFeatureFlags_TenantId_FeatureKey", "CopilotFeatureFlags", new[] { "TenantId", "FeatureKey" }, unique: true);
            migrationBuilder.CreateIndex("IX_CopilotUserAccounts_TenantId_UserId", "CopilotUserAccounts", new[] { "TenantId", "UserId" }, unique: true);
            migrationBuilder.CreateIndex("IX_CopilotEntitlements_TenantId_UserId_Mode", "CopilotEntitlements", new[] { "TenantId", "UserId", "Mode" }, unique: true);
            migrationBuilder.CreateIndex("IX_CopilotQuotaPolicies_TenantId_ScopeType_ScopeKey_Feature", "CopilotQuotaPolicies", new[] { "TenantId", "ScopeType", "ScopeKey", "Feature" }, unique: true);
            migrationBuilder.CreateIndex("IX_CopilotSessions_TenantId_UserId_CreatedAt", "CopilotSessions", new[] { "TenantId", "UserId", "CreatedAt" });
            migrationBuilder.CreateIndex("IX_CopilotSessions_TenantId_Status_CreatedAt", "CopilotSessions", new[] { "TenantId", "Status", "CreatedAt" });
            migrationBuilder.CreateIndex("IX_CopilotMessages_TenantId_SessionId_Sequence", "CopilotMessages", new[] { "TenantId", "SessionId", "Sequence" }, unique: true);
            migrationBuilder.CreateIndex("IX_CopilotUsageEvents_TenantId_CreatedAt", "CopilotUsageEvents", new[] { "TenantId", "CreatedAt" });
            migrationBuilder.CreateIndex("IX_CopilotUsageEvents_TenantId_UserId_CreatedAt", "CopilotUsageEvents", new[] { "TenantId", "UserId", "CreatedAt" });
            migrationBuilder.CreateIndex("IX_CopilotDiagnosticRuns_TenantId_CreatedAt", "CopilotDiagnosticRuns", new[] { "TenantId", "CreatedAt" });
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "CopilotDiagnosticRuns");
            migrationBuilder.DropTable(name: "CopilotUsageEvents");
            migrationBuilder.DropTable(name: "CopilotMessages");
            migrationBuilder.DropTable(name: "CopilotSessions");
            migrationBuilder.DropTable(name: "CopilotQuotaPolicies");
            migrationBuilder.DropTable(name: "CopilotEntitlements");
            migrationBuilder.DropTable(name: "CopilotUserAccounts");
            migrationBuilder.DropTable(name: "CopilotFeatureFlags");
            migrationBuilder.DropTable(name: "CopilotIntegrationSettings");
        }
    }
}
