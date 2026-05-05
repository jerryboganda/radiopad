using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RadioPad.Infrastructure.Migrations
{
    /// <summary>
    /// Iter-33 MCP-007 — trusted plugin publishers. Per-tenant ed25519 keys
    /// trusted to sign plugin <c>manifest.json</c> bodies. Append-only:
    /// revoke = set <c>RevokedAt</c>, never <c>DELETE</c>.
    /// </summary>
    [Microsoft.EntityFrameworkCore.Infrastructure.DbContext(typeof(RadioPad.Infrastructure.Persistence.RadioPadDbContext))]
    [Migration("20260504100001_TrustedPluginPublishers")]
    public partial class TrustedPluginPublishers : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TrustedPluginPublishers",
                columns: table => new
                {
                    Id = table.Column<System.Guid>(type: "TEXT", nullable: false),
                    TenantId = table.Column<System.Guid>(type: "TEXT", nullable: false),
                    PublisherName = table.Column<string>(type: "TEXT", nullable: false, defaultValue: ""),
                    Ed25519PublicKeyBase64 = table.Column<string>(type: "TEXT", nullable: false, defaultValue: ""),
                    RevokedAt = table.Column<long>(type: "INTEGER", nullable: true),
                    CreatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    UpdatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TrustedPluginPublishers", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TrustedPluginPublishers_TenantId_Ed25519PublicKeyBase64",
                table: "TrustedPluginPublishers",
                columns: new[] { "TenantId", "Ed25519PublicKeyBase64" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TrustedPluginPublishers_TenantId_PublisherName",
                table: "TrustedPluginPublishers",
                columns: new[] { "TenantId", "PublisherName" });
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "TrustedPluginPublishers");
        }
    }
}
