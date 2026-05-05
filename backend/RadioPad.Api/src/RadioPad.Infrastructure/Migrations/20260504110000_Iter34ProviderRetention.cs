using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RadioPad.Infrastructure.Migrations
{
    /// <inheritdoc />
    [Microsoft.EntityFrameworkCore.Infrastructure.DbContext(typeof(RadioPad.Infrastructure.Persistence.RadioPadDbContext))]
    [Migration("20260504110000_Iter34ProviderRetention")]
    public partial class Iter34ProviderRetention : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // PROV-009 — operator-supplied free-text retention label shown
            // alongside ProviderComplianceClass. Informational; the PHI
            // policy in AiGateway.EnforcePhiPolicy is unaffected.
            migrationBuilder.AddColumn<string>(
                name: "RetentionLabel",
                table: "Providers",
                type: "TEXT",
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RetentionLabel",
                table: "Providers");
        }
    }
}
