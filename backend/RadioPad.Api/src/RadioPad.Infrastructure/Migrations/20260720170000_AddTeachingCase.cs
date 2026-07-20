using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using RadioPad.Infrastructure.Persistence;

#nullable disable

namespace RadioPad.Infrastructure.Migrations
{
    /// <summary>
    /// PRD §14.14 (TF-001..008) — materialises the TeachingCases table for the
    /// teaching file &amp; education module. SQLite test suites build the same shape
    /// straight from the model via EnsureCreated; production applies this migration
    /// through MigrateAsync at startup.
    ///
    /// Note the columns that are deliberately ABSENT: no accession number, no
    /// patient reference, no MRN, no date of birth. A teaching case has nowhere
    /// to store a patient identifier even if the de-identifier were bypassed.
    /// </summary>
    [DbContext(typeof(RadioPadDbContext))]
    [Migration("20260720170000_AddTeachingCase")]
    public partial class AddTeachingCase : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TeachingCases",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    TenantId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Title = table.Column<string>(type: "TEXT", nullable: false),
                    Modality = table.Column<string>(type: "TEXT", nullable: false),
                    BodyPart = table.Column<string>(type: "TEXT", nullable: false),
                    Diagnosis = table.Column<string>(type: "TEXT", nullable: false),
                    TeachingPoints = table.Column<string>(type: "TEXT", nullable: false),
                    ClinicalHistory = table.Column<string>(type: "TEXT", nullable: false),
                    FindingsText = table.Column<string>(type: "TEXT", nullable: false),
                    ImpressionText = table.Column<string>(type: "TEXT", nullable: false),
                    Tags = table.Column<string>(type: "TEXT", nullable: false),
                    Difficulty = table.Column<int>(type: "INTEGER", nullable: false),
                    SourceReportId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Visibility = table.Column<int>(type: "INTEGER", nullable: false),
                    PublishedAt = table.Column<long>(type: "INTEGER", nullable: true),
                    ViewCount = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    UpdatedAt = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TeachingCases", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TeachingCases_TenantId_Modality_BodyPart",
                table: "TeachingCases",
                columns: new[] { "TenantId", "Modality", "BodyPart" });

            migrationBuilder.CreateIndex(
                name: "IX_TeachingCases_TenantId_CreatedByUserId",
                table: "TeachingCases",
                columns: new[] { "TenantId", "CreatedByUserId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TeachingCases");
        }
    }
}
