using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RadioPad.Infrastructure.Migrations
{
    /// <summary>
    /// Iter-0a (PRD v2 §14.12 / RPT-003 / RADS-001..008 / COMP-003/004) —
    /// structured report data model. Adds a flexible <c>StructuredFieldsJson</c>
    /// column to <c>Reports</c> for template-bound structured/table/numeric
    /// fields, plus two first-class queryable child tables — <c>RadsAssessments</c>
    /// and <c>ReportMeasurements</c> — that unblock the RADS engine, the RADS
    /// contradiction guard, longitudinal lesion tracking, and RADS analytics.
    ///
    /// Hand-written to match this repo's migration convention (the EF model
    /// snapshot is stale relative to the applied hand-written migrations, so
    /// <c>dotnet ef migrations add</c> cannot be used to scaffold cleanly).
    /// Additive only: existing rows are untouched; the new column defaults to
    /// <c>"{}"</c> so legacy reports remain valid.
    /// </summary>
    [Microsoft.EntityFrameworkCore.Infrastructure.DbContext(typeof(RadioPad.Infrastructure.Persistence.RadioPadDbContext))]
    [Migration("20260619000000_Iter0aStructuredReportModel")]
    public partial class Iter0aStructuredReportModel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "StructuredFieldsJson",
                table: "Reports",
                type: "TEXT",
                nullable: false,
                defaultValue: "{}");

            migrationBuilder.CreateTable(
                name: "RadsAssessments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    TenantId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ReportId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Family = table.Column<string>(type: "TEXT", nullable: false, defaultValue: ""),
                    Category = table.Column<string>(type: "TEXT", nullable: false, defaultValue: ""),
                    Score = table.Column<int>(type: "INTEGER", nullable: true),
                    IsDerived = table.Column<bool>(type: "INTEGER", nullable: false, defaultValue: false),
                    LesionKey = table.Column<string>(type: "TEXT", nullable: true),
                    Rationale = table.Column<string>(type: "TEXT", nullable: false, defaultValue: ""),
                    CreatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    UpdatedAt = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RadsAssessments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RadsAssessments_Reports_ReportId",
                        column: x => x.ReportId,
                        principalTable: "Reports",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ReportMeasurements",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    TenantId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ReportId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Label = table.Column<string>(type: "TEXT", nullable: false, defaultValue: ""),
                    Value = table.Column<double>(type: "REAL", nullable: false),
                    Unit = table.Column<string>(type: "TEXT", nullable: false, defaultValue: "mm"),
                    SecondValue = table.Column<double>(type: "REAL", nullable: true),
                    ThirdValue = table.Column<double>(type: "REAL", nullable: true),
                    AnatomicalLocation = table.Column<string>(type: "TEXT", nullable: false, defaultValue: ""),
                    Laterality = table.Column<string>(type: "TEXT", nullable: false, defaultValue: ""),
                    Section = table.Column<string>(type: "TEXT", nullable: false, defaultValue: ""),
                    LesionKey = table.Column<string>(type: "TEXT", nullable: true),
                    StudyReference = table.Column<string>(type: "TEXT", nullable: true),
                    Source = table.Column<string>(type: "TEXT", nullable: false, defaultValue: "manual"),
                    CreatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    UpdatedAt = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReportMeasurements", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ReportMeasurements_Reports_ReportId",
                        column: x => x.ReportId,
                        principalTable: "Reports",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RadsAssessments_ReportId",
                table: "RadsAssessments",
                column: "ReportId");
            migrationBuilder.CreateIndex(
                name: "IX_RadsAssessments_TenantId_ReportId",
                table: "RadsAssessments",
                columns: new[] { "TenantId", "ReportId" });
            migrationBuilder.CreateIndex(
                name: "IX_RadsAssessments_TenantId_Family_Category",
                table: "RadsAssessments",
                columns: new[] { "TenantId", "Family", "Category" });

            migrationBuilder.CreateIndex(
                name: "IX_ReportMeasurements_ReportId",
                table: "ReportMeasurements",
                column: "ReportId");
            migrationBuilder.CreateIndex(
                name: "IX_ReportMeasurements_TenantId_ReportId",
                table: "ReportMeasurements",
                columns: new[] { "TenantId", "ReportId" });
            migrationBuilder.CreateIndex(
                name: "IX_ReportMeasurements_TenantId_LesionKey",
                table: "ReportMeasurements",
                columns: new[] { "TenantId", "LesionKey" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "RadsAssessments");
            migrationBuilder.DropTable(name: "ReportMeasurements");
            migrationBuilder.DropColumn(name: "StructuredFieldsJson", table: "Reports");
        }
    }
}
