using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RadioPad.Infrastructure.Migrations
{
    /// <summary>
    /// Iter-35 — versioned clinical validation packs (rulebook golden suites).
    /// Lifecycle: Draft → Approved (Medical Director / ItAdmin) → Deprecated.
    /// (TenantId, RulebookId, Version) is unique so re-importing the same
    /// pack version is rejected with a 409.
    /// </summary>
    [Microsoft.EntityFrameworkCore.Infrastructure.DbContext(typeof(RadioPad.Infrastructure.Persistence.RadioPadDbContext))]
    [Migration("20260505000300_Iter35ValidationPacks")]
    public partial class Iter35ValidationPacks : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ValidationPacks",
                columns: table => new
                {
                    Id = table.Column<System.Guid>(type: "TEXT", nullable: false),
                    TenantId = table.Column<System.Guid>(type: "TEXT", nullable: false),
                    RulebookId = table.Column<string>(type: "TEXT", nullable: false, defaultValue: ""),
                    Version = table.Column<string>(type: "TEXT", nullable: false, defaultValue: "0.1.0"),
                    Name = table.Column<string>(type: "TEXT", nullable: false, defaultValue: ""),
                    Status = table.Column<int>(type: "INTEGER", nullable: false, defaultValue: 0),
                    ApprovedAt = table.Column<long>(type: "INTEGER", nullable: true),
                    ApprovedBy = table.Column<System.Guid>(type: "TEXT", nullable: true),
                    CreatedBy = table.Column<System.Guid>(type: "TEXT", nullable: false),
                    GoldenCasesJson = table.Column<string>(type: "TEXT", nullable: false, defaultValue: "[]"),
                    CreatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    UpdatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ValidationPacks", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ValidationPacks_TenantId_RulebookId_Version",
                table: "ValidationPacks",
                columns: new[] { "TenantId", "RulebookId", "Version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ValidationPacks_TenantId_RulebookId",
                table: "ValidationPacks",
                columns: new[] { "TenantId", "RulebookId" });
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "ValidationPacks");
        }
    }
}
