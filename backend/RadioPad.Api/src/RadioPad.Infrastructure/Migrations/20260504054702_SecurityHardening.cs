using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RadioPad.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class SecurityHardening : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "AllowDangerousMcp",
                table: "TenantSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "IpAllowlistJson",
                table: "TenantSettings",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "ScimGroupRoleMapJson",
                table: "TenantSettings",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<bool>(
                name: "IsBuiltIn",
                table: "McpTools",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "ManifestJson",
                table: "McpTools",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "ManifestSha256",
                table: "McpTools",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "ManifestSig",
                table: "McpTools",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "ScopeString",
                table: "McpTools",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "Status",
                table: "McpTools",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "Version",
                table: "McpTools",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "LatencyMs",
                table: "McpToolCalls",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "ScopeString",
                table: "McpToolCalls",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "ToolName",
                table: "McpToolCalls",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateTable(
                name: "ScimGroups",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    TenantId = table.Column<Guid>(type: "TEXT", nullable: false),
                    DisplayName = table.Column<string>(type: "TEXT", nullable: false),
                    ExternalId = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    UpdatedAt = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ScimGroups", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ScimGroupMemberships",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    TenantId = table.Column<Guid>(type: "TEXT", nullable: false),
                    GroupId = table.Column<Guid>(type: "TEXT", nullable: false),
                    UserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    UpdatedAt = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ScimGroupMemberships", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ScimGroupMemberships_ScimGroups_GroupId",
                        column: x => x.GroupId,
                        principalTable: "ScimGroups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AuditEvents_Action_CreatedAt",
                table: "AuditEvents",
                columns: new[] { "Action", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ScimGroupMemberships_GroupId",
                table: "ScimGroupMemberships",
                column: "GroupId");

            migrationBuilder.CreateIndex(
                name: "IX_ScimGroupMemberships_TenantId_GroupId_UserId",
                table: "ScimGroupMemberships",
                columns: new[] { "TenantId", "GroupId", "UserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ScimGroups_TenantId_DisplayName",
                table: "ScimGroups",
                columns: new[] { "TenantId", "DisplayName" },
                unique: true);

        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ScimGroupMemberships");

            migrationBuilder.DropTable(
                name: "ScimGroups");

            migrationBuilder.DropIndex(
                name: "IX_AuditEvents_Action_CreatedAt",
                table: "AuditEvents");

            migrationBuilder.DropColumn(
                name: "AllowDangerousMcp",
                table: "TenantSettings");

            migrationBuilder.DropColumn(
                name: "IpAllowlistJson",
                table: "TenantSettings");

            migrationBuilder.DropColumn(
                name: "ScimGroupRoleMapJson",
                table: "TenantSettings");

            migrationBuilder.DropColumn(
                name: "IsBuiltIn",
                table: "McpTools");

            migrationBuilder.DropColumn(
                name: "ManifestJson",
                table: "McpTools");

            migrationBuilder.DropColumn(
                name: "ManifestSha256",
                table: "McpTools");

            migrationBuilder.DropColumn(
                name: "ManifestSig",
                table: "McpTools");

            migrationBuilder.DropColumn(
                name: "ScopeString",
                table: "McpTools");

            migrationBuilder.DropColumn(
                name: "Status",
                table: "McpTools");

            migrationBuilder.DropColumn(
                name: "Version",
                table: "McpTools");

            migrationBuilder.DropColumn(
                name: "LatencyMs",
                table: "McpToolCalls");

            migrationBuilder.DropColumn(
                name: "ScopeString",
                table: "McpToolCalls");

            migrationBuilder.DropColumn(
                name: "ToolName",
                table: "McpToolCalls");
        }
    }
}
