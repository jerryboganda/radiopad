using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using RadioPad.Infrastructure.Persistence;

#nullable disable

namespace RadioPad.Infrastructure.Migrations
{
    /// <summary>
    /// PR-N2 — the Hangfire cron platform's persistence surface:
    ///   • <c>TenantWebhookEndpoints</c> — outbound webhook targets fanned out by
    ///     <c>WebhookDispatchJob</c> (PHI-minimized, HMAC-signed payloads).
    ///   • <c>AiUsageRollups</c> — daily per-(tenant, provider, model) AI usage/token
    ///     aggregates from <c>AiCostRollupJob</c>, preserved past AiRequest retention.
    ///   • <c>AuditExportBundles</c> — signed daily audit-chain JSONL snapshots from
    ///     <c>AuditExportRollupJob</c>.
    ///   • <c>Reports.ArchivedAt</c> — soft-archive marker for <c>OrphanedDraftCleanupJob</c>.
    ///   • <c>Tenants.DraftAutoArchiveDays</c> / <c>Tenants.CriticalNotificationCategoriesCsv</c>.
    ///
    /// Hand-written per repo convention (the EF model snapshot is stale relative to the
    /// applied migrations, so <c>dotnet ef migrations add</c> cannot scaffold cleanly).
    /// SQLite store types (TEXT / INTEGER); DateTimeOffset columns persist as long ticks
    /// via the context's SQLite value converter, DateOnly persists as TEXT. Test suites
    /// build the same shape straight from the model via EnsureCreated; production applies
    /// this through MigrateAsync at startup.
    /// </summary>
    [DbContext(typeof(RadioPadDbContext))]
    [Migration("20260724090001_AddCronPlatformTables")]
    public partial class AddCronPlatformTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TenantWebhookEndpoints",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    TenantId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Url = table.Column<string>(type: "TEXT", nullable: false, defaultValue: ""),
                    Secret = table.Column<string>(type: "TEXT", nullable: false, defaultValue: ""),
                    EventsCsv = table.Column<string>(type: "TEXT", nullable: false, defaultValue: "audit"),
                    Active = table.Column<bool>(type: "INTEGER", nullable: false, defaultValue: true),
                    FailureCount = table.Column<int>(type: "INTEGER", nullable: false, defaultValue: 0),
                    DisabledAt = table.Column<long>(type: "INTEGER", nullable: true),
                    CreatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    UpdatedAt = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TenantWebhookEndpoints", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TenantWebhookEndpoints_TenantId",
                table: "TenantWebhookEndpoints",
                column: "TenantId");

            migrationBuilder.CreateTable(
                name: "AiUsageRollups",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    TenantId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Date = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    Provider = table.Column<string>(type: "TEXT", nullable: false, defaultValue: ""),
                    Model = table.Column<string>(type: "TEXT", nullable: false, defaultValue: ""),
                    RequestCount = table.Column<int>(type: "INTEGER", nullable: false, defaultValue: 0),
                    InputTokens = table.Column<long>(type: "INTEGER", nullable: false, defaultValue: 0L),
                    OutputTokens = table.Column<long>(type: "INTEGER", nullable: false, defaultValue: 0L),
                    CreatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    UpdatedAt = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AiUsageRollups", x => x.Id);
                });

            // Idempotency key: a daily rollup re-run upserts the same (tenant, day, provider, model).
            migrationBuilder.CreateIndex(
                name: "IX_AiUsageRollups_TenantId_Date_Provider_Model",
                table: "AiUsageRollups",
                columns: new[] { "TenantId", "Date", "Provider", "Model" },
                unique: true);

            migrationBuilder.CreateTable(
                name: "AuditExportBundles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    TenantId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Date = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    ContentJsonl = table.Column<string>(type: "TEXT", nullable: false, defaultValue: ""),
                    Signature = table.Column<string>(type: "TEXT", nullable: true),
                    EventCount = table.Column<int>(type: "INTEGER", nullable: false, defaultValue: 0),
                    CreatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    UpdatedAt = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuditExportBundles", x => x.Id);
                });

            // Idempotency key: one bundle per (tenant, day); a re-run replaces it.
            migrationBuilder.CreateIndex(
                name: "IX_AuditExportBundles_TenantId_Date",
                table: "AuditExportBundles",
                columns: new[] { "TenantId", "Date" },
                unique: true);

            // Soft-archive marker for the weekly orphaned-draft cleanup (nullable ticks).
            migrationBuilder.AddColumn<long>(
                name: "ArchivedAt",
                table: "Reports",
                type: "INTEGER",
                nullable: true);

            // Opt-in stale-draft auto-archive window (0 = disabled).
            migrationBuilder.AddColumn<int>(
                name: "DraftAutoArchiveDays",
                table: "Tenants",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            // Reserved for later notification producers; default treats CriticalResult as critical.
            migrationBuilder.AddColumn<string>(
                name: "CriticalNotificationCategoriesCsv",
                table: "Tenants",
                type: "TEXT",
                nullable: false,
                defaultValue: "CriticalResult");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "CriticalNotificationCategoriesCsv", table: "Tenants");
            migrationBuilder.DropColumn(name: "DraftAutoArchiveDays", table: "Tenants");
            migrationBuilder.DropColumn(name: "ArchivedAt", table: "Reports");
            migrationBuilder.DropTable(name: "AuditExportBundles");
            migrationBuilder.DropTable(name: "AiUsageRollups");
            migrationBuilder.DropTable(name: "TenantWebhookEndpoints");
        }
    }
}
