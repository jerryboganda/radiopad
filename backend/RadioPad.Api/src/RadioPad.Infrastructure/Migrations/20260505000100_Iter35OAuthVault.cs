using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RadioPad.Infrastructure.Migrations
{
    /// <inheritdoc />
    [Microsoft.EntityFrameworkCore.Infrastructure.DbContext(typeof(RadioPad.Infrastructure.Persistence.RadioPadDbContext))]
    [Microsoft.EntityFrameworkCore.Migrations.Migration("20260505000100_Iter35OAuthVault")]
    public partial class Iter35OAuthVault : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // PROV-007 — per-provider OAuth refresh-token vault. Envelope
            // encryption: AES-256-GCM ciphertext + IV + tag, plus a per-token
            // DEK wrapped under the tenant's KMS-managed KEK. All columns are
            // nullable; legacy rows simply carry NULLs until an admin saves a
            // token.
            migrationBuilder.AddColumn<byte[]>(
                name: "OAuthRefreshTokenEnc",
                table: "Providers",
                type: "BLOB",
                nullable: true);
            migrationBuilder.AddColumn<byte[]>(
                name: "OAuthRefreshTokenIv",
                table: "Providers",
                type: "BLOB",
                nullable: true);
            migrationBuilder.AddColumn<byte[]>(
                name: "OAuthRefreshTokenTag",
                table: "Providers",
                type: "BLOB",
                nullable: true);
            migrationBuilder.AddColumn<byte[]>(
                name: "OAuthRefreshTokenWrappedDek",
                table: "Providers",
                type: "BLOB",
                nullable: true);
            migrationBuilder.AddColumn<long>(
                name: "OAuthRefreshTokenUpdatedAt",
                table: "Providers",
                type: "INTEGER",
                nullable: true);
            migrationBuilder.AddColumn<long>(
                name: "OAuthRefreshTokenExpiresAt",
                table: "Providers",
                type: "INTEGER",
                nullable: true);
            migrationBuilder.AddColumn<string>(
                name: "OAuthRefreshTokenRotationPolicy",
                table: "Providers",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "OAuthRefreshTokenEnc", table: "Providers");
            migrationBuilder.DropColumn(name: "OAuthRefreshTokenIv", table: "Providers");
            migrationBuilder.DropColumn(name: "OAuthRefreshTokenTag", table: "Providers");
            migrationBuilder.DropColumn(name: "OAuthRefreshTokenWrappedDek", table: "Providers");
            migrationBuilder.DropColumn(name: "OAuthRefreshTokenUpdatedAt", table: "Providers");
            migrationBuilder.DropColumn(name: "OAuthRefreshTokenExpiresAt", table: "Providers");
            migrationBuilder.DropColumn(name: "OAuthRefreshTokenRotationPolicy", table: "Providers");
        }
    }
}
