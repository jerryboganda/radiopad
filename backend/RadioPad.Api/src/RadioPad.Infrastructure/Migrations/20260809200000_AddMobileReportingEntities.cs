using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using RadioPad.Infrastructure.Persistence;

#nullable disable

namespace RadioPad.Infrastructure.Migrations
{
    /// <summary>
    /// Adds mobile reporting fields (RadiologyId, PatientName, PatientAge, PatientGender)
    /// to the Reports table and creates the DictationAudios table.
    /// </summary>
    [DbContext(typeof(RadioPadDbContext))]
    [Migration("20260809200000_AddMobileReportingEntities")]
    public partial class AddMobileReportingEntities : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "RadiologyId",
                table: "Reports",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "PatientName",
                table: "Reports",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "PatientAge",
                table: "Reports",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "PatientGender",
                table: "Reports",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateTable(
                name: "DictationAudios",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ReportId = table.Column<Guid>(type: "TEXT", nullable: false),
                    StoragePath = table.Column<string>(type: "TEXT", nullable: false, defaultValue: ""),
                    DurationSeconds = table.Column<double>(type: "REAL", nullable: false, defaultValue: 0.0),
                    TranscriptionEngine = table.Column<string>(type: "TEXT", nullable: false, defaultValue: "medASR-6gram"),
                    Status = table.Column<string>(type: "TEXT", nullable: false, defaultValue: "Pending"),
                    TranscribedText = table.Column<string>(type: "TEXT", nullable: true),
                    UploadedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    TranscribedAt = table.Column<long>(type: "INTEGER", nullable: true),
                    ErrorMessage = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    UpdatedAt = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DictationAudios", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DictationAudios_Reports_ReportId",
                        column: x => x.ReportId,
                        principalTable: "Reports",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DictationAudios_ReportId",
                table: "DictationAudios",
                column: "ReportId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "DictationAudios");

            migrationBuilder.DropColumn(name: "RadiologyId", table: "Reports");
            migrationBuilder.DropColumn(name: "PatientName", table: "Reports");
            migrationBuilder.DropColumn(name: "PatientAge", table: "Reports");
            migrationBuilder.DropColumn(name: "PatientGender", table: "Reports");
        }
    }
}
