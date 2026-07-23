using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using RadioPad.Infrastructure.Persistence;

#nullable disable

namespace RadioPad.Infrastructure.Migrations
{
    /// <summary>
    /// PR-B5 — adds <c>AiJobs.InputJson</c>, the request payload for the input-carrying
    /// job kinds (cleanup raw dictation / cross-check text). Same clinical-text-at-rest
    /// class as <c>ResultJson</c>: nulled 24h after completion by the retention sweep and
    /// never returned by any API; persisted so <c>POST /api/jobs/{id}/retry</c> can re-run
    /// the job without the client re-supplying the input.
    ///
    /// Copies the hand-written <c>AddAiJobs</c> pattern. SQLite test suites build the same
    /// shape straight from the model via EnsureCreated (the string property is picked up
    /// automatically — no DbContext configuration needed); production applies this migration
    /// through MigrateAsync at startup. No model-snapshot edit (stale by repo convention).
    /// </summary>
    [DbContext(typeof(RadioPadDbContext))]
    [Migration("20260724100000_AddAiJobInput")]
    public partial class AddAiJobInput : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "InputJson",
                table: "AiJobs",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "InputJson",
                table: "AiJobs");
        }
    }
}
