using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using RadioPad.Infrastructure.Persistence;

#nullable disable

namespace RadioPad.Infrastructure.Migrations
{
    /// <summary>
    /// RPT-030 — Report Templates: radiologist-authored visual designs for the
    /// exported PDF/DOCX report document, a per-(tenant, user) chosen default,
    /// and the tenant admin's "recommended" layout pointer on TenantSettings.
    ///
    /// Hand-written, copying <c>20260724110000_AddNotifications</c>: TEXT for
    /// Guid/string, INTEGER for bool/int, and DateTimeOffset persisted as UTC
    /// ticks (INTEGER) — the SQLite value converter in <c>RadioPadDbContext</c>
    /// handles the CLR↔ticks round-trip. SQLite test suites build the same
    /// shape straight from the model via EnsureCreated; production applies
    /// this migration through MigrateAsync at startup.
    /// </summary>
    [DbContext(typeof(RadioPadDbContext))]
    [Migration("20260726120000_AddReportLayouts")]
    public partial class AddReportLayouts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "RecommendedReportLayoutId",
                table: "TenantSettings",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ReportLayouts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    TenantId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false, defaultValue: ""),
                    Description = table.Column<string>(type: "TEXT", nullable: true),
                    LayoutJson = table.Column<string>(type: "TEXT", nullable: false, defaultValue: "{}"),
                    SchemaVersion = table.Column<int>(type: "INTEGER", nullable: false, defaultValue: 1),
                    CreatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    UpdatedAt = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReportLayouts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ReportLayoutUserDefaults",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    TenantId = table.Column<Guid>(type: "TEXT", nullable: false),
                    UserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ReportLayoutId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    UpdatedAt = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReportLayoutUserDefaults", x => x.Id);
                });

            // Gallery list scan, ordered/filtered per tenant.
            migrationBuilder.CreateIndex(
                name: "IX_ReportLayouts_TenantId",
                table: "ReportLayouts",
                column: "TenantId");

            // One default layout per caller.
            migrationBuilder.CreateIndex(
                name: "IX_ReportLayoutUserDefaults_TenantId_UserId",
                table: "ReportLayoutUserDefaults",
                columns: new[] { "TenantId", "UserId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "ReportLayoutUserDefaults");
            migrationBuilder.DropTable(name: "ReportLayouts");

            migrationBuilder.DropColumn(
                name: "RecommendedReportLayoutId",
                table: "TenantSettings");
        }
    }
}
