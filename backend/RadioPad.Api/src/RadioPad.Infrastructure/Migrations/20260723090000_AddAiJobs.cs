using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using RadioPad.Infrastructure.Persistence;

#nullable disable

namespace RadioPad.Infrastructure.Migrations
{
    /// <summary>
    /// Durable AI generation jobs — the restart-surviving counterpart to the
    /// in-memory <c>AiJobRegistry</c>. One row per generation attempt (impression/
    /// rewrite or whole-report generate) across local/on-device, UBAG, and hosted
    /// provider paths; <c>AiJobCoordinator</c> writes through to this table on every
    /// state transition so the top-right jobs widget can rehydrate after a reload
    /// or a server restart instead of silently losing track of a running job.
    ///
    /// SQLite test suites build the same shape straight from the model via
    /// EnsureCreated; production applies this migration through MigrateAsync at
    /// startup (repo convention — the EF model snapshot is stale relative to the
    /// applied hand-written migrations, so `dotnet ef migrations add` cannot be
    /// used to scaffold cleanly).
    /// </summary>
    [DbContext(typeof(RadioPadDbContext))]
    [Migration("20260723090000_AddAiJobs")]
    public partial class AddAiJobs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AiJobs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    TenantId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ReportId = table.Column<Guid>(type: "TEXT", nullable: false),
                    UserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Kind = table.Column<string>(type: "TEXT", nullable: false, defaultValue: ""),
                    Mode = table.Column<string>(type: "TEXT", nullable: false, defaultValue: ""),
                    ProviderId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Status = table.Column<string>(type: "TEXT", nullable: false, defaultValue: "queued"),
                    ResultJson = table.Column<string>(type: "TEXT", nullable: true),
                    Error = table.Column<string>(type: "TEXT", nullable: true),
                    ErrorKind = table.Column<string>(type: "TEXT", nullable: true),
                    ProviderJobId = table.Column<string>(type: "TEXT", nullable: true),
                    Attempt = table.Column<int>(type: "INTEGER", nullable: false, defaultValue: 1),
                    RetryOfJobId = table.Column<Guid>(type: "TEXT", nullable: true),
                    CancelRequested = table.Column<bool>(type: "INTEGER", nullable: false, defaultValue: false),
                    StartedAt = table.Column<long>(type: "INTEGER", nullable: true),
                    CompletedAt = table.Column<long>(type: "INTEGER", nullable: true),
                    CreatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    UpdatedAt = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AiJobs", x => x.Id);
                });

            // Widget rehydration list: (tenant, user) recency scan.
            migrationBuilder.CreateIndex(
                name: "IX_AiJobs_TenantId_UserId_CreatedAt",
                table: "AiJobs",
                columns: new[] { "TenantId", "UserId", "CreatedAt" });

            // Single-flight dedupe + per-report job history.
            migrationBuilder.CreateIndex(
                name: "IX_AiJobs_TenantId_ReportId_Status",
                table: "AiJobs",
                columns: new[] { "TenantId", "ReportId", "Status" });

            // Boot recovery sweep + retention cleanup.
            migrationBuilder.CreateIndex(
                name: "IX_AiJobs_Status_CompletedAt",
                table: "AiJobs",
                columns: new[] { "Status", "CompletedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AiJobs");
        }
    }
}
