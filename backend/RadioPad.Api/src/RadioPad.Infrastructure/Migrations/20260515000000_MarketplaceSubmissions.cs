using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RadioPad.Infrastructure.Migrations;

/// <summary>
/// PRD Enterprise GA #13 — adds marketplace submission & approval workflow
/// columns to MarketplaceListings: SourceRulebookId, SourceTemplateId,
/// Version, InstallCount, ReviewNotes.
/// </summary>
public partial class MarketplaceSubmissions : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "SourceRulebookId",
            table: "MarketplaceListings",
            type: "TEXT",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "SourceTemplateId",
            table: "MarketplaceListings",
            type: "TEXT",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "Version",
            table: "MarketplaceListings",
            type: "TEXT",
            nullable: false,
            defaultValue: "1.0.0");

        migrationBuilder.AddColumn<int>(
            name: "InstallCount",
            table: "MarketplaceListings",
            type: "INTEGER",
            nullable: false,
            defaultValue: 0);

        migrationBuilder.AddColumn<string>(
            name: "ReviewNotes",
            table: "MarketplaceListings",
            type: "TEXT",
            nullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "SourceRulebookId", table: "MarketplaceListings");
        migrationBuilder.DropColumn(name: "SourceTemplateId", table: "MarketplaceListings");
        migrationBuilder.DropColumn(name: "Version", table: "MarketplaceListings");
        migrationBuilder.DropColumn(name: "InstallCount", table: "MarketplaceListings");
        migrationBuilder.DropColumn(name: "ReviewNotes", table: "MarketplaceListings");
    }
}
