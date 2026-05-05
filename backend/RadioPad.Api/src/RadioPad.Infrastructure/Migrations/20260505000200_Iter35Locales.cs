using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RadioPad.Infrastructure.Migrations
{
    /// <inheritdoc />
    [Microsoft.EntityFrameworkCore.Infrastructure.DbContext(typeof(RadioPad.Infrastructure.Persistence.RadioPadDbContext))]
    [Migration("20260505000200_Iter35Locales")]
    public partial class Iter35Locales : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Iter-35 i18n — tenant-level default UI locale (IETF tag).
            migrationBuilder.AddColumn<string>(
                name: "Locale",
                table: "TenantSettings",
                type: "TEXT",
                nullable: false,
                defaultValue: "en");

            // Iter-35 i18n — optional per-user locale override.
            migrationBuilder.AddColumn<string>(
                name: "PreferredLocale",
                table: "Users",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "Locale", table: "TenantSettings");
            migrationBuilder.DropColumn(name: "PreferredLocale", table: "Users");
        }
    }
}
