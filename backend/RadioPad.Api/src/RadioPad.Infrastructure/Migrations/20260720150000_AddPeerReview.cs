using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using RadioPad.Infrastructure.Persistence;

#nullable disable

namespace RadioPad.Infrastructure.Migrations
{
    /// <summary>
    /// PRD §14.13 (PR-001..010) — materialises the PeerReviews table for the
    /// RADPEER-aligned peer-review workflow. SQLite test suites build the same
    /// shape straight from the model via EnsureCreated; production applies this
    /// migration through MigrateAsync at startup.
    /// </summary>
    [DbContext(typeof(RadioPadDbContext))]
    [Migration("20260720150000_AddPeerReview")]
    public partial class AddPeerReview : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PeerReviews",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    TenantId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ReportId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ReviewerUserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    OriginalAuthorUserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    AssignedByUserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ReviewType = table.Column<int>(type: "INTEGER", nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    Score = table.Column<int>(type: "INTEGER", nullable: false),
                    Complexity = table.Column<int>(type: "INTEGER", nullable: false),
                    DiscrepancyCategory = table.Column<int>(type: "INTEGER", nullable: false),
                    Comments = table.Column<string>(type: "TEXT", nullable: false),
                    Blinded = table.Column<bool>(type: "INTEGER", nullable: false),
                    StartedAt = table.Column<long>(type: "INTEGER", nullable: true),
                    CompletedAt = table.Column<long>(type: "INTEGER", nullable: true),
                    DisputeReason = table.Column<string>(type: "TEXT", nullable: true),
                    DisputedAt = table.Column<long>(type: "INTEGER", nullable: true),
                    CreatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    UpdatedAt = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PeerReviews", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PeerReviews_TenantId_ReviewerUserId_Status",
                table: "PeerReviews",
                columns: new[] { "TenantId", "ReviewerUserId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_PeerReviews_TenantId_ReportId",
                table: "PeerReviews",
                columns: new[] { "TenantId", "ReportId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PeerReviews");
        }
    }
}
