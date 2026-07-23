using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using RadioPad.Infrastructure.Persistence;

#nullable disable

namespace RadioPad.Infrastructure.Migrations
{
    /// <summary>
    /// NOTIF-001 — per-recipient in-app notification inbox + per-(tenant, user)
    /// notification preferences. Written by <c>NotificationProducer</c>; read/mutated
    /// by <c>NotificationsController</c>; pruned by <c>RetentionSweepJob</c>.
    ///
    /// Hand-written, copying <c>20260723090000_AddAiJobs</c>: TEXT for Guid/string,
    /// INTEGER for bool/int, and DateTimeOffset persisted as UTC ticks (INTEGER) — the
    /// SQLite value converter in <c>RadioPadDbContext</c> handles the CLR↔ticks round-trip.
    /// SQLite test suites build the same shape straight from the model via EnsureCreated;
    /// production applies this migration through MigrateAsync at startup.
    /// </summary>
    [DbContext(typeof(RadioPadDbContext))]
    [Migration("20260724110000_AddNotifications")]
    public partial class AddNotifications : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Notifications",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    TenantId = table.Column<Guid>(type: "TEXT", nullable: false),
                    UserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Category = table.Column<int>(type: "INTEGER", nullable: false),
                    Urgency = table.Column<int>(type: "INTEGER", nullable: false),
                    Title = table.Column<string>(type: "TEXT", nullable: false, defaultValue: ""),
                    Body = table.Column<string>(type: "TEXT", nullable: false, defaultValue: ""),
                    LinkHref = table.Column<string>(type: "TEXT", nullable: true),
                    SourceKind = table.Column<string>(type: "TEXT", nullable: true),
                    SourceId = table.Column<Guid>(type: "TEXT", nullable: true),
                    RequiresAck = table.Column<bool>(type: "INTEGER", nullable: false, defaultValue: false),
                    ReadAt = table.Column<long>(type: "INTEGER", nullable: true),
                    AcknowledgedAt = table.Column<long>(type: "INTEGER", nullable: true),
                    AcknowledgedByUserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    DedupeKey = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    UpdatedAt = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Notifications", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "NotificationPreferences",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    TenantId = table.Column<Guid>(type: "TEXT", nullable: false),
                    UserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    MutedCategoriesCsv = table.Column<string>(type: "TEXT", nullable: false, defaultValue: ""),
                    DndStartMinutes = table.Column<int>(type: "INTEGER", nullable: true),
                    DndEndMinutes = table.Column<int>(type: "INTEGER", nullable: true),
                    DndTimeZone = table.Column<string>(type: "TEXT", nullable: false, defaultValue: ""),
                    PushEnabled = table.Column<bool>(type: "INTEGER", nullable: false, defaultValue: true),
                    EmailEnabled = table.Column<bool>(type: "INTEGER", nullable: false, defaultValue: false),
                    CreatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    UpdatedAt = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NotificationPreferences", x => x.Id);
                });

            // Inbox unread scan + recency scan.
            migrationBuilder.CreateIndex(
                name: "IX_Notifications_TenantId_UserId_ReadAt",
                table: "Notifications",
                columns: new[] { "TenantId", "UserId", "ReadAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Notifications_TenantId_UserId_CreatedAt",
                table: "Notifications",
                columns: new[] { "TenantId", "UserId", "CreatedAt" });

            // Idempotency: a duplicate producer event with the same DedupeKey collapses into
            // one row. Filtered so rows without a DedupeKey are never constrained (the bare
            // "DedupeKey IS NOT NULL" predicate is portable across SQLite + Npgsql).
            migrationBuilder.CreateIndex(
                name: "IX_Notifications_TenantId_UserId_DedupeKey",
                table: "Notifications",
                columns: new[] { "TenantId", "UserId", "DedupeKey" },
                unique: true,
                filter: "DedupeKey IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_NotificationPreferences_TenantId_UserId",
                table: "NotificationPreferences",
                columns: new[] { "TenantId", "UserId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "Notifications");
            migrationBuilder.DropTable(name: "NotificationPreferences");
        }
    }
}
