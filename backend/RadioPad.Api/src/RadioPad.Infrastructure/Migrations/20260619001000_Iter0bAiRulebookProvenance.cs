using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RadioPad.Infrastructure.Migrations
{
    /// <summary>
    /// Iter-0b (PRD RB-009 / AI-012) — record rulebook provenance on every AI
    /// usage-ledger row so the audit trail can prove which rulebook (id +
    /// version) governed each AI generation. Additive: nullable id + version
    /// defaulting to "" for legacy rows.
    /// </summary>
    [Microsoft.EntityFrameworkCore.Infrastructure.DbContext(typeof(RadioPad.Infrastructure.Persistence.RadioPadDbContext))]
    [Migration("20260619001000_Iter0bAiRulebookProvenance")]
    public partial class Iter0bAiRulebookProvenance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<System.Guid>(
                name: "RulebookId",
                table: "AiRequests",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RulebookVersion",
                table: "AiRequests",
                type: "TEXT",
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "RulebookId", table: "AiRequests");
            migrationBuilder.DropColumn(name: "RulebookVersion", table: "AiRequests");
        }
    }
}
