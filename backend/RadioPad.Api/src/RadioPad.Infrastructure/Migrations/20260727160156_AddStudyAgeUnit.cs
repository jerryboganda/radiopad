using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RadioPad.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddStudyAgeUnit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Study_AgeUnit",
                table: "Reports",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Study_AgeUnit",
                table: "Reports");
        }
    }
}
