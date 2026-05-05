using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RadioPad.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Iter31BackendClosures : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "FhirWebhookSecret",
                table: "TenantSettings",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Hl7SendingFacility",
                table: "TenantSettings",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "IpAllowlistCidr",
                table: "TenantSettings",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<bool>(
                name: "RequireZeroBlockers",
                table: "TenantSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "WarnAsBlocker",
                table: "TenantSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "AllowExternalMcp",
                table: "Tenants",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "Status",
                table: "Templates",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "Variant",
                table: "Templates",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "DepartmentTag",
                table: "Rulebooks",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DepartmentTag",
                table: "Reports",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DeviceFingerprint",
                table: "DeviceAuth",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "McpToolCalls",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    TenantId = table.Column<Guid>(type: "TEXT", nullable: false),
                    UserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ToolId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ReportId = table.Column<Guid>(type: "TEXT", nullable: true),
                    InputHash = table.Column<string>(type: "TEXT", nullable: false),
                    OutputHash = table.Column<string>(type: "TEXT", nullable: false),
                    Status = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    UpdatedAt = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_McpToolCalls", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "McpTools",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    TenantId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    Kind = table.Column<int>(type: "INTEGER", nullable: false),
                    Scope = table.Column<int>(type: "INTEGER", nullable: false),
                    Approved = table.Column<bool>(type: "INTEGER", nullable: false),
                    ApprovedBy = table.Column<Guid>(type: "TEXT", nullable: true),
                    ApprovedAt = table.Column<long>(type: "INTEGER", nullable: true),
                    AllowedConnectorPaths = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    UpdatedAt = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_McpTools", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PromptOverrides",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    TenantId = table.Column<Guid>(type: "TEXT", nullable: false),
                    RulebookId = table.Column<string>(type: "TEXT", nullable: false),
                    BlockKey = table.Column<string>(type: "TEXT", nullable: false),
                    Body = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    UpdatedAt = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PromptOverrides", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_McpToolCalls_TenantId_CreatedAt",
                table: "McpToolCalls",
                columns: new[] { "TenantId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_McpTools_TenantId_Name",
                table: "McpTools",
                columns: new[] { "TenantId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PromptOverrides_TenantId_RulebookId_BlockKey",
                table: "PromptOverrides",
                columns: new[] { "TenantId", "RulebookId", "BlockKey" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "McpToolCalls");

            migrationBuilder.DropTable(
                name: "McpTools");

            migrationBuilder.DropTable(
                name: "PromptOverrides");

            migrationBuilder.DropColumn(
                name: "FhirWebhookSecret",
                table: "TenantSettings");

            migrationBuilder.DropColumn(
                name: "Hl7SendingFacility",
                table: "TenantSettings");

            migrationBuilder.DropColumn(
                name: "IpAllowlistCidr",
                table: "TenantSettings");

            migrationBuilder.DropColumn(
                name: "RequireZeroBlockers",
                table: "TenantSettings");

            migrationBuilder.DropColumn(
                name: "WarnAsBlocker",
                table: "TenantSettings");

            migrationBuilder.DropColumn(
                name: "AllowExternalMcp",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "Status",
                table: "Templates");

            migrationBuilder.DropColumn(
                name: "Variant",
                table: "Templates");

            migrationBuilder.DropColumn(
                name: "DepartmentTag",
                table: "Rulebooks");

            migrationBuilder.DropColumn(
                name: "DepartmentTag",
                table: "Reports");

            migrationBuilder.DropColumn(
                name: "DeviceFingerprint",
                table: "DeviceAuth");
        }
    }
}
