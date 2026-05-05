using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RadioPad.Infrastructure.Migrations
{
    /// <summary>
    /// Iter-32 Agent A — AUTH-001/004/006/INT-001/INT-002/SEC-007.
    ///
    /// Adds:
    ///   * <c>WebAuthnCredentials</c> table for FIDO2 / passkey enrolment.
    ///   * <c>Users.FailedLoginCount</c>, <c>FailedLoginWindowStart</c>,
    ///     <c>LockedUntil</c>, <c>SessionEpoch</c> for the AUTH-006 lockout
    ///     and revoke-sessions flows.
    ///
    /// The model snapshot was already in iter-32 state when this migration
    /// was added; this file backs the schema diff so
    /// <c>db.Database.Migrate()</c> in production picks up the new columns
    /// and table without manual SQL.
    /// </summary>
    public partial class Auth32 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "FailedLoginCount",
                table: "Users",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<long>(
                name: "FailedLoginWindowStart",
                table: "Users",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "LockedUntil",
                table: "Users",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SessionEpoch",
                table: "Users",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "WebAuthnCredentials",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    TenantId = table.Column<Guid>(type: "TEXT", nullable: false),
                    UserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CredentialId = table.Column<string>(type: "TEXT", nullable: false),
                    CredentialIdHash = table.Column<string>(type: "TEXT", nullable: false),
                    PublicKey = table.Column<string>(type: "TEXT", nullable: false),
                    SignCount = table.Column<uint>(type: "INTEGER", nullable: false),
                    Label = table.Column<string>(type: "TEXT", nullable: false, defaultValue: ""),
                    LastUsedAt = table.Column<long>(type: "INTEGER", nullable: true),
                    CreatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    UpdatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WebAuthnCredentials", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_WebAuthnCredentials_TenantId_CredentialIdHash",
                table: "WebAuthnCredentials",
                columns: new[] { "TenantId", "CredentialIdHash" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WebAuthnCredentials_TenantId_UserId",
                table: "WebAuthnCredentials",
                columns: new[] { "TenantId", "UserId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "WebAuthnCredentials");
            migrationBuilder.DropColumn(name: "FailedLoginCount", table: "Users");
            migrationBuilder.DropColumn(name: "FailedLoginWindowStart", table: "Users");
            migrationBuilder.DropColumn(name: "LockedUntil", table: "Users");
            migrationBuilder.DropColumn(name: "SessionEpoch", table: "Users");
        }
    }
}
