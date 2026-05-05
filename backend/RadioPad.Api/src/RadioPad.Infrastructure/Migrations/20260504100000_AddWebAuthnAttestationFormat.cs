using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RadioPad.Infrastructure.Migrations
{
    /// <inheritdoc />
    [Microsoft.EntityFrameworkCore.Infrastructure.DbContext(typeof(RadioPad.Infrastructure.Persistence.RadioPadDbContext))]
    [Migration("20260504100000_AddWebAuthnAttestationFormat")]
    public partial class AddWebAuthnAttestationFormat : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // AUTH-001 — persist the verified WebAuthn attestation format
            // ("none" | "packed" | "fido-u2f") alongside the COSE_Key.
            migrationBuilder.AddColumn<string>(
                name: "AttestationFormat",
                table: "WebAuthnCredentials",
                type: "TEXT",
                maxLength: 32,
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AttestationFormat",
                table: "WebAuthnCredentials");
        }
    }
}
