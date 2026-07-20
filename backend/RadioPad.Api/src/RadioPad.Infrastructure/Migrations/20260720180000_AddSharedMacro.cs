using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using RadioPad.Infrastructure.Persistence;

#nullable disable

namespace RadioPad.Infrastructure.Migrations
{
    /// <summary>
    /// PRD RPT-021 — materialises the SharedMacros table for tenant- and
    /// subspecialty-scoped autotext. SQLite test suites build the same shape
    /// straight from the model via EnsureCreated; production applies this
    /// migration through MigrateAsync at startup.
    /// </summary>
    [DbContext(typeof(RadioPadDbContext))]
    [Migration("20260720180000_AddSharedMacro")]
    public partial class AddSharedMacro : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SharedMacros",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    TenantId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Scope = table.Column<int>(type: "INTEGER", nullable: false),
                    Subspecialty = table.Column<string>(type: "TEXT", nullable: false),
                    Trigger = table.Column<string>(type: "TEXT", nullable: false),
                    Body = table.Column<string>(type: "TEXT", nullable: false),
                    Description = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    UpdatedAt = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SharedMacros", x => x.Id);
                });

            // Expansion resolves by trigger within a scope, so a duplicate
            // (scope, subspecialty, trigger) inside one tenant would make the
            // expansion non-deterministic.
            migrationBuilder.CreateIndex(
                name: "IX_SharedMacros_TenantId_Scope_Subspecialty_Trigger",
                table: "SharedMacros",
                columns: new[] { "TenantId", "Scope", "Subspecialty", "Trigger" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SharedMacros");
        }
    }
}
