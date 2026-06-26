using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RadioPad.Infrastructure.Migrations
{
    /// <summary>
    /// AUTH-003 — adds <c>Tenants.RequireMfa</c> (default true) backing the
    /// mandatory-TOTP policy flag introduced with password sign-in. Hand-written
    /// to back only this schema delta, matching the repo convention (see
    /// <c>Auth32</c>): the canonical model snapshot is maintained separately and
    /// <c>db.Database.Migrate()</c> applies this column in production; the
    /// EnsureCreated test path builds it straight from the entity model.
    /// </summary>
    public partial class AddTenantRequireMfa : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "RequireMfa",
                table: "Tenants",
                type: "INTEGER",
                nullable: false,
                defaultValue: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "RequireMfa", table: "Tenants");
        }
    }
}
