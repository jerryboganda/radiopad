using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RadioPad.Infrastructure.Migrations
{
    /// <summary>
    /// Iter-32 MCP-001..007 — MCP tool registry hardening. Adds manifest +
    /// signature columns, lifecycle status, dangerous-scope tenant flag, and
    /// extended invocation ledger columns.
    /// </summary>
    public partial class McpRegistry : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // McpTools — manifest + signature + status.
            migrationBuilder.AddColumn<string>(
                name: "Version", table: "McpTools",
                type: "TEXT", nullable: false, defaultValue: "1.0.0");
            migrationBuilder.AddColumn<string>(
                name: "ScopeString", table: "McpTools",
                type: "TEXT", nullable: false, defaultValue: "");
            migrationBuilder.AddColumn<string>(
                name: "ManifestJson", table: "McpTools",
                type: "TEXT", nullable: false, defaultValue: "");
            migrationBuilder.AddColumn<string>(
                name: "ManifestSha256", table: "McpTools",
                type: "TEXT", nullable: false, defaultValue: "");
            migrationBuilder.AddColumn<string>(
                name: "ManifestSig", table: "McpTools",
                type: "TEXT", nullable: false, defaultValue: "");
            migrationBuilder.AddColumn<int>(
                name: "Status", table: "McpTools",
                type: "INTEGER", nullable: false, defaultValue: 0);
            migrationBuilder.AddColumn<bool>(
                name: "IsBuiltIn", table: "McpTools",
                type: "INTEGER", nullable: false, defaultValue: false);

            // McpToolCalls — denormalised invocation ledger columns.
            migrationBuilder.AddColumn<string>(
                name: "ToolName", table: "McpToolCalls",
                type: "TEXT", nullable: false, defaultValue: "");
            migrationBuilder.AddColumn<string>(
                name: "ScopeString", table: "McpToolCalls",
                type: "TEXT", nullable: false, defaultValue: "");
            migrationBuilder.AddColumn<int>(
                name: "LatencyMs", table: "McpToolCalls",
                type: "INTEGER", nullable: false, defaultValue: 0);

            // TenantSettings — operator-controlled dangerous-scope override.
            migrationBuilder.AddColumn<bool>(
                name: "AllowDangerousMcp", table: "TenantSettings",
                type: "INTEGER", nullable: false, defaultValue: false);

            // Backfill: any pre-existing row had Approved=true ⇒ Status=Approved (1).
            migrationBuilder.Sql(@"UPDATE McpTools SET Status = 1 WHERE Approved = 1;");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "Version", table: "McpTools");
            migrationBuilder.DropColumn(name: "ScopeString", table: "McpTools");
            migrationBuilder.DropColumn(name: "ManifestJson", table: "McpTools");
            migrationBuilder.DropColumn(name: "ManifestSha256", table: "McpTools");
            migrationBuilder.DropColumn(name: "ManifestSig", table: "McpTools");
            migrationBuilder.DropColumn(name: "Status", table: "McpTools");
            migrationBuilder.DropColumn(name: "IsBuiltIn", table: "McpTools");
            migrationBuilder.DropColumn(name: "ToolName", table: "McpToolCalls");
            migrationBuilder.DropColumn(name: "ScopeString", table: "McpToolCalls");
            migrationBuilder.DropColumn(name: "LatencyMs", table: "McpToolCalls");
            migrationBuilder.DropColumn(name: "AllowDangerousMcp", table: "TenantSettings");
        }
    }
}
