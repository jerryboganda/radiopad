using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RadioPad.Infrastructure.Migrations
{
    /// <summary>
    /// Iter-0c (PRD §0.6 policy scaffold) — per-tenant security/clinical policy
    /// columns on <c>TenantSettings</c>: MFA-required, idle timeout, concurrent
    /// session cap, per-class retention map, and criticality classes. Additive;
    /// defaults preserve current (non-enforcing) behaviour until the Phase 2/3
    /// iterations that consume them land.
    /// </summary>
    [Microsoft.EntityFrameworkCore.Infrastructure.DbContext(typeof(RadioPad.Infrastructure.Persistence.RadioPadDbContext))]
    [Migration("20260619002000_Iter0cPolicyScaffold")]
    public partial class Iter0cPolicyScaffold : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "RequireMfa", table: "TenantSettings",
                type: "INTEGER", nullable: false, defaultValue: false);
            migrationBuilder.AddColumn<int>(
                name: "IdleTimeoutMinutes", table: "TenantSettings",
                type: "INTEGER", nullable: false, defaultValue: 0);
            migrationBuilder.AddColumn<int>(
                name: "MaxConcurrentSessions", table: "TenantSettings",
                type: "INTEGER", nullable: false, defaultValue: 0);
            migrationBuilder.AddColumn<string>(
                name: "RetentionByClassJson", table: "TenantSettings",
                type: "TEXT", nullable: false, defaultValue: "{}");
            migrationBuilder.AddColumn<string>(
                name: "CriticalityClassesJson", table: "TenantSettings",
                type: "TEXT", nullable: false, defaultValue: "[]");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "RequireMfa", table: "TenantSettings");
            migrationBuilder.DropColumn(name: "IdleTimeoutMinutes", table: "TenantSettings");
            migrationBuilder.DropColumn(name: "MaxConcurrentSessions", table: "TenantSettings");
            migrationBuilder.DropColumn(name: "RetentionByClassJson", table: "TenantSettings");
            migrationBuilder.DropColumn(name: "CriticalityClassesJson", table: "TenantSettings");
        }
    }
}
