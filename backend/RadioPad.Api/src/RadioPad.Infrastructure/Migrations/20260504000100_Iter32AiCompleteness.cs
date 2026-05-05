using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RadioPad.Infrastructure.Migrations
{
    /// <inheritdoc />
    [Microsoft.EntityFrameworkCore.Infrastructure.DbContext(typeof(RadioPad.Infrastructure.Persistence.RadioPadDbContext))]
    [Migration("20260504000100_Iter32AiCompleteness")]
    public partial class Iter32AiCompleteness : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Iter-32 AI-009 — approval gate on tenant prompt overrides.
            migrationBuilder.AddColumn<int>(
                name: "Status",
                table: "PromptOverrides",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<Guid>(
                name: "ApprovedByUserId",
                table: "PromptOverrides",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "ApprovedAt",
                table: "PromptOverrides",
                type: "INTEGER",
                nullable: true);

            // Iter-32 AI-010 — composite cost / quality / latency routing.
            migrationBuilder.AddColumn<string>(
                name: "Quality",
                table: "Providers",
                type: "TEXT",
                nullable: false,
                defaultValue: "0.5");

            migrationBuilder.AddColumn<string>(
                name: "RoutingWeightsJson",
                table: "TenantSettings",
                type: "TEXT",
                nullable: false,
                defaultValue: "{\"cost\":0.5,\"quality\":0.4,\"latency\":0.1}");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "Status", table: "PromptOverrides");
            migrationBuilder.DropColumn(name: "ApprovedByUserId", table: "PromptOverrides");
            migrationBuilder.DropColumn(name: "ApprovedAt", table: "PromptOverrides");
            migrationBuilder.DropColumn(name: "Quality", table: "Providers");
            migrationBuilder.DropColumn(name: "RoutingWeightsJson", table: "TenantSettings");
        }
    }
}
