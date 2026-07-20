using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using RadioPad.Infrastructure.Persistence;

#nullable disable

namespace RadioPad.Infrastructure.Migrations
{
    /// <summary>
    /// PRD §14.15 (CR-001..010) — materialises the CriticalResults table for the
    /// critical-results communication workflow. SQLite test suites build the same
    /// shape straight from the model via EnsureCreated; production applies this
    /// migration through MigrateAsync at startup.
    /// </summary>
    [DbContext(typeof(RadioPadDbContext))]
    [Migration("20260720120000_AddCriticalResult")]
    public partial class AddCriticalResult : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CriticalResults",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    TenantId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ReportId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Criticality = table.Column<int>(type: "INTEGER", nullable: false),
                    FindingSummary = table.Column<string>(type: "TEXT", nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    CommunicatedTo = table.Column<string>(type: "TEXT", nullable: true),
                    CommunicationMethod = table.Column<int>(type: "INTEGER", nullable: true),
                    CommunicatedAt = table.Column<long>(type: "INTEGER", nullable: true),
                    AcknowledgedBy = table.Column<string>(type: "TEXT", nullable: true),
                    AcknowledgedAt = table.Column<long>(type: "INTEGER", nullable: true),
                    DueAt = table.Column<long>(type: "INTEGER", nullable: false),
                    EscalatedAt = table.Column<long>(type: "INTEGER", nullable: true),
                    ClosedAt = table.Column<long>(type: "INTEGER", nullable: true),
                    Notes = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    UpdatedAt = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CriticalResults", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CriticalResults_TenantId_Status_DueAt",
                table: "CriticalResults",
                columns: new[] { "TenantId", "Status", "DueAt" });

            migrationBuilder.CreateIndex(
                name: "IX_CriticalResults_TenantId_ReportId",
                table: "CriticalResults",
                columns: new[] { "TenantId", "ReportId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CriticalResults");
        }
    }
}
