using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RadioPad.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class DropCopilotFeature : Migration
    {
        // Drops the GitHub Copilot integration tables created by
        // 20260516010000_CopilotFoundation. The feature was removed wholesale;
        // this migration only touches Copilot tables (the unrelated model
        // changes the scaffolder bundled in — pre-existing snapshot drift — were
        // intentionally excluded so applying this on prod is a clean Copilot drop).

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CopilotDiagnosticRuns");

            migrationBuilder.DropTable(
                name: "CopilotEntitlements");

            migrationBuilder.DropTable(
                name: "CopilotFeatureFlags");

            migrationBuilder.DropTable(
                name: "CopilotIntegrationSettings");

            migrationBuilder.DropTable(
                name: "CopilotMessages");

            migrationBuilder.DropTable(
                name: "CopilotQuotaPolicies");

            migrationBuilder.DropTable(
                name: "CopilotSessions");

            migrationBuilder.DropTable(
                name: "CopilotUsageEvents");

            migrationBuilder.DropTable(
                name: "CopilotUserAccounts");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CopilotDiagnosticRuns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    ResultsJson = table.Column<string>(type: "TEXT", nullable: false),
                    Status = table.Column<string>(type: "TEXT", nullable: false),
                    TenantId = table.Column<Guid>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    UserId = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CopilotDiagnosticRuns", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CopilotEntitlements",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Allowed = table.Column<bool>(type: "INTEGER", nullable: false),
                    CheckedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    DenialReason = table.Column<string>(type: "TEXT", nullable: false),
                    ExpiresAt = table.Column<long>(type: "INTEGER", nullable: true),
                    GitHubLogin = table.Column<string>(type: "TEXT", nullable: false),
                    Mode = table.Column<int>(type: "INTEGER", nullable: false),
                    SeatStatus = table.Column<string>(type: "TEXT", nullable: false),
                    Source = table.Column<string>(type: "TEXT", nullable: false),
                    SsoStatus = table.Column<string>(type: "TEXT", nullable: false),
                    TenantId = table.Column<Guid>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    UserId = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CopilotEntitlements", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CopilotFeatureFlags",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    Enabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    FeatureKey = table.Column<string>(type: "TEXT", nullable: false),
                    PolicyJson = table.Column<string>(type: "TEXT", nullable: false),
                    RequiredRole = table.Column<string>(type: "TEXT", nullable: false),
                    TenantId = table.Column<Guid>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CopilotFeatureFlags", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CopilotIntegrationSettings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    AllowByoAccounts = table.Column<bool>(type: "INTEGER", nullable: false),
                    AllowEnvironmentTokenAuth = table.Column<bool>(type: "INTEGER", nullable: false),
                    AllowedModes = table.Column<string>(type: "TEXT", nullable: false),
                    CliRuntimeEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    ContextLoggingEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    DefaultMode = table.Column<int>(type: "INTEGER", nullable: false),
                    EmergencyDisabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    Enabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    GitHubAppId = table.Column<string>(type: "TEXT", nullable: false),
                    GitHubAppInstallationId = table.Column<string>(type: "TEXT", nullable: false),
                    GitHubAppPrivateKeySecretRef = table.Column<string>(type: "TEXT", nullable: false),
                    GitHubEnterpriseSlug = table.Column<string>(type: "TEXT", nullable: false),
                    GitHubHost = table.Column<string>(type: "TEXT", nullable: false),
                    GitHubOrganization = table.Column<string>(type: "TEXT", nullable: false),
                    OAuthClientId = table.Column<string>(type: "TEXT", nullable: false),
                    OAuthClientSecretRef = table.Column<string>(type: "TEXT", nullable: false),
                    PolicyJson = table.Column<string>(type: "TEXT", nullable: false),
                    PromptLoggingEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    RequireOsKeychainForCli = table.Column<bool>(type: "INTEGER", nullable: false),
                    RetentionPolicy = table.Column<string>(type: "TEXT", nullable: false),
                    SdkRuntimeEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    SecretsUpdatedAt = table.Column<long>(type: "INTEGER", nullable: true),
                    TenantId = table.Column<Guid>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CopilotIntegrationSettings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CopilotMessages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    InputHash = table.Column<string>(type: "TEXT", nullable: false),
                    LatencyMs = table.Column<int>(type: "INTEGER", nullable: false),
                    Model = table.Column<string>(type: "TEXT", nullable: false),
                    OutputHash = table.Column<string>(type: "TEXT", nullable: false),
                    Role = table.Column<string>(type: "TEXT", nullable: false),
                    Sequence = table.Column<int>(type: "INTEGER", nullable: false),
                    SessionId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Status = table.Column<string>(type: "TEXT", nullable: false),
                    TenantId = table.Column<Guid>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    UserId = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CopilotMessages", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CopilotQuotaPolicies",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    Enabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    Feature = table.Column<string>(type: "TEXT", nullable: false),
                    MaxConcurrent = table.Column<int>(type: "INTEGER", nullable: false),
                    MaxRequests = table.Column<int>(type: "INTEGER", nullable: false),
                    ScopeKey = table.Column<string>(type: "TEXT", nullable: false),
                    ScopeType = table.Column<string>(type: "TEXT", nullable: false),
                    TenantId = table.Column<Guid>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    WindowSeconds = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CopilotQuotaPolicies", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CopilotSessions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CancelledAt = table.Column<long>(type: "INTEGER", nullable: true),
                    CompletedAt = table.Column<long>(type: "INTEGER", nullable: true),
                    ContextHash = table.Column<string>(type: "TEXT", nullable: false),
                    ContextKind = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    Feature = table.Column<string>(type: "TEXT", nullable: false),
                    LastErrorKind = table.Column<string>(type: "TEXT", nullable: false),
                    Mode = table.Column<int>(type: "INTEGER", nullable: false),
                    Runtime = table.Column<string>(type: "TEXT", nullable: false),
                    StartedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    Status = table.Column<string>(type: "TEXT", nullable: false),
                    TenantId = table.Column<Guid>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    UserId = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CopilotSessions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CopilotUsageEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    BlockKind = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    Feature = table.Column<string>(type: "TEXT", nullable: false),
                    InputHash = table.Column<string>(type: "TEXT", nullable: false),
                    LatencyMs = table.Column<int>(type: "INTEGER", nullable: false),
                    Mode = table.Column<string>(type: "TEXT", nullable: false),
                    OutputHash = table.Column<string>(type: "TEXT", nullable: false),
                    RequestId = table.Column<string>(type: "TEXT", nullable: false),
                    Status = table.Column<string>(type: "TEXT", nullable: false),
                    TenantId = table.Column<Guid>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    UserId = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CopilotUsageEvents", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CopilotUserAccounts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    DenialReason = table.Column<string>(type: "TEXT", nullable: false),
                    GitHubLogin = table.Column<string>(type: "TEXT", nullable: false),
                    GitHubUserId = table.Column<long>(type: "INTEGER", nullable: true),
                    LastAuthenticatedAt = table.Column<long>(type: "INTEGER", nullable: true),
                    Mode = table.Column<int>(type: "INTEGER", nullable: false),
                    RevokedAt = table.Column<long>(type: "INTEGER", nullable: true),
                    SeatStatus = table.Column<string>(type: "TEXT", nullable: false),
                    SsoStatus = table.Column<string>(type: "TEXT", nullable: false),
                    TenantId = table.Column<Guid>(type: "TEXT", nullable: false),
                    TokenSecretRef = table.Column<string>(type: "TEXT", nullable: false),
                    TokenStatus = table.Column<string>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    UserId = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CopilotUserAccounts", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CopilotDiagnosticRuns_TenantId_CreatedAt",
                table: "CopilotDiagnosticRuns",
                columns: new[] { "TenantId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_CopilotEntitlements_TenantId_UserId_Mode",
                table: "CopilotEntitlements",
                columns: new[] { "TenantId", "UserId", "Mode" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CopilotFeatureFlags_TenantId_FeatureKey",
                table: "CopilotFeatureFlags",
                columns: new[] { "TenantId", "FeatureKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CopilotIntegrationSettings_TenantId",
                table: "CopilotIntegrationSettings",
                column: "TenantId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CopilotMessages_TenantId_SessionId_Sequence",
                table: "CopilotMessages",
                columns: new[] { "TenantId", "SessionId", "Sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CopilotQuotaPolicies_TenantId_ScopeType_ScopeKey_Feature",
                table: "CopilotQuotaPolicies",
                columns: new[] { "TenantId", "ScopeType", "ScopeKey", "Feature" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CopilotSessions_TenantId_Status_CreatedAt",
                table: "CopilotSessions",
                columns: new[] { "TenantId", "Status", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_CopilotSessions_TenantId_UserId_CreatedAt",
                table: "CopilotSessions",
                columns: new[] { "TenantId", "UserId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_CopilotUsageEvents_TenantId_CreatedAt",
                table: "CopilotUsageEvents",
                columns: new[] { "TenantId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_CopilotUsageEvents_TenantId_UserId_CreatedAt",
                table: "CopilotUsageEvents",
                columns: new[] { "TenantId", "UserId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_CopilotUserAccounts_TenantId_UserId",
                table: "CopilotUserAccounts",
                columns: new[] { "TenantId", "UserId" },
                unique: true);
        }
    }
}
