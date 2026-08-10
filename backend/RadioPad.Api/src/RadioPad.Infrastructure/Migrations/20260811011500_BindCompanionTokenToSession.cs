using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using RadioPad.Infrastructure.Persistence;

#nullable disable

namespace RadioPad.Infrastructure.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(RadioPadDbContext))]
    [Migration("20260811011500_BindCompanionTokenToSession")]
    public partial class BindCompanionTokenToSession : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CompanionTokenHash",
                table: "CompanionSessions",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_CompanionSessions_CompanionTokenHash",
                table: "CompanionSessions",
                column: "CompanionTokenHash");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_CompanionSessions_CompanionTokenHash",
                table: "CompanionSessions");

            migrationBuilder.DropColumn(
                name: "CompanionTokenHash",
                table: "CompanionSessions");
        }
    }
}
