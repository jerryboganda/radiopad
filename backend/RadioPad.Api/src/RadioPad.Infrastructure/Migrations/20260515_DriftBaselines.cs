using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RadioPad.Infrastructure.Migrations
{
    /// <summary>
    /// PRD §18.2 — drift detection baselines. Stores the last known-good
    /// golden-case regression result per (tenant, provider, rulebook) so the
    /// background <c>ModelDriftDetectionService</c> can compare subsequent
    /// runs and raise <c>SystemAlert</c> audit events on quality regressions.
    /// </summary>
    [Microsoft.EntityFrameworkCore.Infrastructure.DbContext(typeof(RadioPad.Infrastructure.Persistence.RadioPadDbContext))]
    [Migration("20260515000000_DriftBaselines")]
    public partial class DriftBaselines : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DriftBaselines",
                columns: table => new
                {
                    Id = table.Column<System.Guid>(type: "TEXT", nullable: false),
                    TenantId = table.Column<System.Guid>(type: "TEXT", nullable: false),
                    ProviderId = table.Column<string>(type: "TEXT", nullable: false, defaultValue: ""),
                    RulebookId = table.Column<string>(type: "TEXT", nullable: false, defaultValue: ""),
                    QualityScore = table.Column<int>(type: "INTEGER", nullable: false, defaultValue: 0),
                    FindingRuleIdsJson = table.Column<string>(type: "TEXT", nullable: false, defaultValue: "[]"),
                    CheckedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    UpdatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DriftBaselines", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DriftBaselines_TenantId_ProviderId_RulebookId",
                table: "DriftBaselines",
                columns: new[] { "TenantId", "ProviderId", "RulebookId" },
                unique: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "DriftBaselines");
        }
    }
}
